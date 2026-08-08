using System.Collections.Generic;
using UnityEngine;

namespace ThanksNoThanks
{
    /// <summary>
    /// ЕДИНАЯ ТОЧКА ЗВУКА. Всё, что звучит в игре, проходит через <see cref="Play"/> или через
    /// именованный луп-канал (<see cref="PlayLoop"/> / <see cref="StopLoop"/>) — прямых
    /// <c>AudioSource.PlayOneShot</c> по драйверу нет и быть не должно: иначе фильтр депрессии
    /// («микс в вате») накрывал бы не весь микс, а то, что успели вспомнить.
    ///
    /// УСТРОЙСТВО (всё строится кодом, как и остальной проект — ни префабов, ни ссылок сцены):
    /// <list type="bullet">
    /// <item><b>SFX-пул</b> — <see cref="SfxVoices"/> голосов по кругу, ФИЛЬТРУЕТСЯ.</item>
    /// <item><b>Луп-каналы</b> — по одному источнику на <see cref="LoopChannel"/> (тема / звонок /
    /// импульс), <c>loop = true</c>, ФИЛЬТРУЮТСЯ.</item>
    /// <item><b>Нефильтруемый пул</b> — ровно для удара сердца депрессии: манифест требует, чтобы
    /// пульс остался «наверху нетронутым», пока весь остальной микс сидит в вате.</item>
    /// </list>
    ///
    /// ОЧЕРЕДИ НЕТ ПО УСТРОЙСТВУ: <see cref="Play"/> либо звучит сейчас, либо не звучит вовсе.
    /// Поэтому пауза туториала ничего не «копит» и на снятии паузы не выстреливает пачкой.
    ///
    /// ⚠ ГРОМКОСТИ ЧЕРНОВЫЕ (ТЮНИМО) — см. константы <see cref="AudioCatalog"/>. Художественный
    /// (ушной) гейт делает основательница; здесь только структура.
    /// </summary>
    public sealed class AudioLayer : MonoBehaviour
    {
        /// <summary>Голосов в SFX-пуле: хватает на «раздача + ответ + монета + тревога» внахлёст.</summary>
        public const int SfxVoices = 12;
        /// <summary>Пульс депрессии редкий (раз в ~1.5 с), но пусть переживает нахлёст с самим собой.</summary>
        public const int UnfilteredVoices = 2;

        /// <summary>Общий множитель мастера — ТЮНИМО.</summary>
        public float MasterVolume = 1f;

        private readonly Dictionary<SoundEvent, AudioClip> _clips = new();
        private readonly Dictionary<LoopChannel, AudioSource> _loops = new();
        /// <summary>Что слой СЧИТАЕТ запущенным на канале. Интент, а не состояние движка — см. LoopWanted.</summary>
        private readonly Dictionary<LoopChannel, SoundEvent> _loopWanted = new();
        private readonly List<AudioLowPassFilter> _filters = new();
        private AudioSource[] _sfx;
        private AudioSource[] _unfiltered;
        private int _sfxNext;
        private int _unfilteredNext;
        private bool _built;

        // --- Состояние фильтра депрессии ---------------------------------------------------------
        private bool _depActive;
        private int _depHits;

        /// <summary>Депрессия сейчас глушит микс.</summary>
        public bool DepressionActive => _depActive;
        /// <summary>Сколько попаданий пульса уже вернули миру звук (0…<see cref="DepressionMix.Steps"/>).</summary>
        public int DepressionHits => _depHits;
        /// <summary>Сколько каскадов лоу-пасса реально повесилось на каждый фильтруемый источник.</summary>
        public int CascadesPerSource { get; private set; }

        // --- Тестовые швы ------------------------------------------------------------------------
        /// <summary>Включает запись <see cref="DebugPlayed"/>. По умолчанию ВЫКЛ — в бою список не растёт.</summary>
        public bool DebugRecord;
        /// <summary>Что было сыграно с момента <see cref="DebugClear"/> — только при <see cref="DebugRecord"/>.</summary>
        public readonly List<SoundEvent> DebugPlayed = new();

        /// <summary>Сколько строк каталога реально нашли свой клип (done contract §1: битых путей нет).</summary>
        public int LoadedClipCount => _clips.Count;
        public bool ClipLoaded(SoundEvent e) => _clips.TryGetValue(e, out var c) && c != null;
        /// <summary>Источник луп-канала — тест проверяет по нему loop-флаг и назначенный клип.</summary>
        public AudioSource DebugLoopSource(LoopChannel ch)
            => ch != LoopChannel.None && _loops.TryGetValue(ch, out var s) ? s : null;

        /// <summary>Сколько лоу-пассов повешено всего (только на ФИЛЬТРУЕМЫХ источниках).</summary>
        public int DebugFilterCount => _filters.Count;
        /// <summary>Сколько из них включено прямо сейчас — 0, когда вата снята.</summary>
        public int DebugEnabledFilterCount
        {
            get
            {
                int n = 0;
                foreach (var f in _filters) if (f != null && f.enabled) n++;
                return n;
            }
        }

        public void DebugClear() => DebugPlayed.Clear();
        public bool DebugPlayedContains(SoundEvent e) => DebugPlayed.Contains(e);
        public int DebugCount(SoundEvent e)
        {
            int n = 0;
            foreach (var x in DebugPlayed) if (x == e) n++;
            return n;
        }

        private void Awake() => Build();

        /// <summary>
        /// Собрать источники и загрузить клипы. Идемпотентно — тест может позвать до <c>Awake</c>.
        /// Отсутствующий клип НЕ валит игру (done contract §1): громкая ошибка в консоль, событие немо.
        /// </summary>
        public void Build()
        {
            if (_built) return;
            _built = true;

            _sfx = new AudioSource[SfxVoices];
            for (int i = 0; i < SfxVoices; i++) _sfx[i] = NewSource("sfx-" + i, filtered: true, loop: false);

            _unfiltered = new AudioSource[UnfilteredVoices];
            for (int i = 0; i < UnfilteredVoices; i++)
                _unfiltered[i] = NewSource("unfiltered-" + i, filtered: false, loop: false);

            foreach (LoopChannel ch in System.Enum.GetValues(typeof(LoopChannel)))
            {
                if (ch == LoopChannel.None) continue;
                _loops[ch] = NewSource("loop-" + ch, filtered: true, loop: true);
            }

            foreach (var pair in AudioCatalog.Map)
            {
                var clip = Resources.Load<AudioClip>(pair.Value.ResourceKey);
                if (clip == null)
                {
                    // Громко, не тихо: немой звук — это дыра в манифесте или неимпортированный файл.
                    Debug.LogError("[ThanksNoThanks] audio clip not found: Resources/"
                        + pair.Value.ResourceKey + " (событие " + pair.Key + "). "
                        + "Проверь Assets/_Project/Audio/Resources/Audio/" + pair.Value.File);
                    continue;
                }
                _clips[pair.Key] = clip;
            }

            // Один раз на слой, а не на каждый из 15 источников (иначе лог suite тонет в 3000 строк).
            if (CascadesPerSource < DepressionMix.Cascades)
                Debug.Log("[ThanksNoThanks] лоу-пасс депрессии: " + CascadesPerSource + " каскад(а) вместо "
                    + DepressionMix.Cascades + " — Unity не вешает второй AudioLowPassFilter на объект. "
                    + "«Вата» мягче демо (2 полюса вместо 4); решение за ушным гейтом, эталон для сверки — "
                    + "docs/assets/audio-picks/10-crisis/DEMO--1-cotton.mp3");

            ApplyDepressionToSources();
        }

        private AudioSource NewSource(string name, bool filtered, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = loop;
            src.spatialBlend = 0f;   // чистое 2D — шоу играет «в лоб», без панорамы
            if (!filtered) return src;

            // 2 каскада по 2 полюса — как в спеке манифеста. Unity считает фильтры источника по
            // порядку компонентов, поэтому каскад = просто второй такой же компонент.
            for (int c = 0; c < DepressionMix.Cascades; c++)
            {
                var f = go.AddComponent<AudioLowPassFilter>();
                if (f == null) break;
                f.cutoffFrequency = DepressionMix.CutoffOff;
                f.lowpassResonanceQ = 1f;   // без подъёма на срезе — «вата», а не «телефон»
                f.enabled = false;
                _filters.Add(f);
            }
            // ⚠ ФАКТ, ПРОВЕРЕННЫЙ ПРОГОНОМ (2026-08-08): Unity НЕ ДАЁТ повесить второй
            // AudioLowPassFilter на тот же объект — сколько ни добавляй, остаётся один (2 полюса).
            // Спека манифеста просит 2 каскада (4 полюса), поэтому «вата» сейчас МЯГЧЕ демо,
            // по которому основательница утверждала приём. Слой честно работает на том, что дали;
            // расхождение сообщается ОДИН раз на слой (см. Build) и вынесено на ушной гейт.
            if (CascadesPerSource == 0) CascadesPerSource = go.GetComponents<AudioLowPassFilter>().Length;
            return src;
        }

        // --- Проигрывание ------------------------------------------------------------------------

        /// <summary>
        /// Сыграть событие ОДИН раз. Луп-события сюда тоже можно отдать — они уйдут в свой канал,
        /// чтобы вызывающему не приходилось помнить, что есть луп, а что нет.
        /// </summary>
        public void Play(SoundEvent e)
        {
            var def = AudioCatalog.Get(e);
            if (def == null) return;
            if (def.Loop) { PlayLoop(e); return; }

            if (DebugRecord) DebugPlayed.Add(e);
            if (!_clips.TryGetValue(e, out var clip) || clip == null) return;   // немо, но не падаем

            var src = def.Bus == SoundBus.Unfiltered ? NextUnfiltered() : NextSfx();
            if (src == null) return;
            src.pitch = Jittered(def);
            src.PlayOneShot(clip, VolumeFor(def));
        }

        /// <summary>Запустить луп-канал события (повторный вызов на играющем канале — не рестарт).</summary>
        public void PlayLoop(SoundEvent e)
        {
            var def = AudioCatalog.Get(e);
            if (def == null || !def.Loop) return;
            if (!_loops.TryGetValue(def.Channel, out var src) || src == null) return;

            // «Уже крутится — не рвём фразу» спрашивается у СОБСТВЕННОГО интента, а не у
            // `src.isPlaying`: в headless-прогоне (-batchmode) звуковое устройство может быть
            // пустышкой, и движок вернёт «не играю» на честно запущенный луп. Интент детерминирован
            // и одинаков в бою и на suite — иначе луп рестартовал бы каждый кадр (щелчки на живом
            // железе, тысяча строк в журнале на suite).
            if (_loopWanted.TryGetValue(def.Channel, out var running) && running == e) return;
            if (!_clips.TryGetValue(e, out var clip) || clip == null) return;

            // ЖУРНАЛ ПИШЕТ СТАРТЫ, А НЕ ВЫЗОВЫ (шов для теста): луп сводится с состоянием игры и
            // поэтому переспрашивается каждый кадр.
            if (DebugRecord) DebugPlayed.Add(e);
            _loopWanted[def.Channel] = e;
            src.clip = clip;
            src.pitch = def.Pitch;
            src.volume = VolumeFor(def);
            src.loop = true;
            src.Play();
        }

        public void StopLoop(LoopChannel ch)
        {
            if (ch == LoopChannel.None) return;
            _loopWanted.Remove(ch);
            if (_loops.TryGetValue(ch, out var src) && src != null) src.Stop();
        }

        /// <summary>Состояние ДВИЖКА: источник канала действительно играет прямо сейчас.</summary>
        public bool LoopPlaying(LoopChannel ch)
            => ch != LoopChannel.None && _loops.TryGetValue(ch, out var s) && s != null && s.isPlaying;

        /// <summary>
        /// Состояние СЛОЯ: канал запущен и не остановлен. Это то, чем управляет игра, и то, что
        /// проверяет тест — <see cref="LoopPlaying"/> в headless зависит от наличия звукового
        /// устройства и потому годится только как «замолчал ли», но не как «звучит ли».
        /// </summary>
        public bool LoopWanted(LoopChannel ch) => ch != LoopChannel.None && _loopWanted.ContainsKey(ch);

        public void StopAllLoops()
        {
            _loopWanted.Clear();
            foreach (var pair in _loops) if (pair.Value != null) pair.Value.Stop();
        }

        private AudioSource NextSfx()
        {
            if (_sfx == null || _sfx.Length == 0) return null;
            var s = _sfx[_sfxNext];
            _sfxNext = (_sfxNext + 1) % _sfx.Length;
            return s;
        }

        private AudioSource NextUnfiltered()
        {
            if (_unfiltered == null || _unfiltered.Length == 0) return null;
            var s = _unfiltered[_unfilteredNext];
            _unfilteredNext = (_unfilteredNext + 1) % _unfiltered.Length;
            return s;
        }

        private static float Jittered(SoundDef def)
        {
            if (def.PitchJitter <= 0f) return def.Pitch;
            return def.Pitch * (1f + Random.Range(-def.PitchJitter, def.PitchJitter));
        }

        /// <summary>Итоговая громкость: мастер × строка манифеста × ослабление депрессии (мимо пульса).</summary>
        private float VolumeFor(SoundDef def)
        {
            float v = MasterVolume * def.Volume;
            if (def.Bus != SoundBus.Unfiltered && _depActive) v *= DepressionMix.GainFor(_depHits);
            return v;
        }

        // --- Фильтр депрессии --------------------------------------------------------------------

        /// <summary>Вход в депрессию: весь микс РАЗОМ уходит в вату (ступень 0 = 500 Гц / −9 дБ).</summary>
        public void EnterDepression()
        {
            _depActive = true;
            _depHits = 0;
            ApplyDepressionToSources();
        }

        /// <summary>Попадание пульса вернуло ступень. Пятое = выход, фильтр снимается полностью.</summary>
        public void SetDepressionHits(int hits)
        {
            if (!_depActive) return;
            _depHits = Mathf.Clamp(hits, 0, DepressionMix.Steps);
            ApplyDepressionToSources();
        }

        /// <summary>Выход: «мир снова включили» — фильтр снят, громкость мастера возвращена.</summary>
        public void ExitDepression()
        {
            _depActive = false;
            _depHits = DepressionMix.Steps;
            ApplyDepressionToSources();
        }

        /// <summary>Текущий срез (для теста и для отладки): «фильтра нет» = <see cref="DepressionMix.CutoffOff"/>.</summary>
        public float CurrentCutoff
            => _depActive && !DepressionMix.Transparent(_depHits)
                ? DepressionMix.CutoffFor(_depHits)
                : DepressionMix.CutoffOff;

        private void ApplyDepressionToSources()
        {
            bool on = _depActive && !DepressionMix.Transparent(_depHits);
            float cutoff = CurrentCutoff;
            foreach (var f in _filters)
            {
                if (f == null) continue;
                f.enabled = on;
                f.cutoffFrequency = cutoff;
            }
            // Играющие лупы обязаны просесть ВМЕСТЕ со всеми — иначе тема останется бодрой поверх ваты.
            foreach (var pair in _loops)
            {
                var src = pair.Value;
                if (src == null || !_loopWanted.ContainsKey(pair.Key)) continue;
                var def = DefForChannel(pair.Key);
                if (def != null) src.volume = VolumeFor(def);
            }
        }

        private static SoundDef DefForChannel(LoopChannel ch)
        {
            foreach (var pair in AudioCatalog.Map)
                if (pair.Value.Channel == ch) return pair.Value;
            return null;
        }

        // --- Рестарт -----------------------------------------------------------------------------

        /// <summary>
        /// НОВАЯ ЖИЗНЬ: глушим всё и снимаем вату. Без этого смерть В ДЕПРЕССИИ оставляла бы
        /// следующую жизнь играть под лоу-пассом — «застрявшая вата» (done contract §4).
        /// </summary>
        public void ResetAll()
        {
            StopAllLoops();
            if (_sfx != null) foreach (var s in _sfx) if (s != null) s.Stop();
            if (_unfiltered != null) foreach (var s in _unfiltered) if (s != null) s.Stop();
            _depActive = false;
            _depHits = DepressionMix.Steps;
            ApplyDepressionToSources();
        }
    }
}
