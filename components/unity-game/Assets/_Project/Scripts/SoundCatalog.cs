using System.Collections.Generic;

namespace ThanksNoThanks
{
    /// <summary>
    /// Одно звуковое СОБЫТИЕ игры — ровно строка манифеста дизайн-сессии
    /// (<c>docs/assets/audio-picks/manifest.md</c>). Имена событий, а не имена файлов: перекладка
    /// файла — правка одной строки <see cref="AudioCatalog"/>, разводка по коду не трогается.
    /// </summary>
    public enum SoundEvent
    {
        // 1. Сквозные
        MusicTheme,
        // 2. Карточки и ответы
        CardDeal,
        AnswerYes,
        AnswerNo,
        Timeout,
        DomeLastSecond,
        BlockMoney,
        // 3. Деньги
        CrankTick,
        CoinJar,
        // Общее семейство тревог — ОДИН файл, три высоты (манифест §«Общее семейство тревог»)
        AlarmMoney,
        AlarmEnergy,
        AlarmHealth,
        // 4. Энергия
        EnergyCharge,
        BurnoutIn,
        BurnoutOut,
        // 5. Отношения
        ZoneIn,
        ZoneOut,
        Breakup,
        SecondChance,
        // 6. Здоровье
        HealthOpen,
        Heal,
        // 7. Ребёнок (телефон)
        PhoneRing,
        PhonePickup,
        PhoneMissed,
        BadParent,
        // 8. Салют
        StarBurst,
        // 9. Ведущий и туториалы — шесть пиццикато по тонам реплик
        HostPositive,
        HostRisky,
        HostAbsurd,
        HostCautious,
        HostFatal,
        HostSkip,
        Tutorial,
        // 10. Кризис и депрессия
        BlitzStart,
        BlitzThought,
        Impulse,
        DepressionEnter,
        DepressionPulse,
        DepressionMiss,
        DepressionExit,
        // 11. Рамка жизни
        Opener,
        FinaleOldAge,
        FinaleBurnout,
        FinaleFatal,
        Restart
    }

    /// <summary>
    /// Куда событие играется. Три фильтруемых канала уходят под лоу-пасс депрессии;
    /// <see cref="Unfiltered"/> — единственное исключение манифеста («пульс остаётся наверху нетронутым»).
    /// </summary>
    public enum SoundBus
    {
        Sfx,          // пул одноразовых — фильтруется
        Music,        // луп-канал темы — фильтруется
        Loop,         // именованные луп-каналы (звонок, импульс) — фильтруются
        Unfiltered    // ВНЕ фильтра депрессии — только удар сердца
    }

    /// <summary>Именованные луп-каналы: у каждого свой <see cref="UnityEngine.AudioSource"/>, свой Stop.</summary>
    public enum LoopChannel
    {
        None,
        Music,        // цирковая шарманка (тема)
        PhoneRing,    // трубка звонит, пока окно открыто
        Impulse       // струнная «сирена» импульс-раунда
    }

    /// <summary>Одна строка манифеста, приведённая к машинному виду.</summary>
    public sealed class SoundDef
    {
        /// <summary>Путь ровно как в манифесте, с расширением: <c>«02-cards/answer-yes--q3-kalimba.wav»</c>.</summary>
        public string File;
        public SoundBus Bus = SoundBus.Sfx;
        public LoopChannel Channel = LoopChannel.None;
        /// <summary>ЧЕРНОВАЯ громкость — ТЮНИМО (ушной гейт основательницы).</summary>
        public float Volume = 1f;
        /// <summary>Базовый питч. Семейство тревог различается ТОЛЬКО им (−2 / 0 / +2 полутона).</summary>
        public float Pitch = 1f;
        /// <summary>Разброс питча ±доля. Манифест требует ±6 % на тик крутилки и монету, иначе дребезг.</summary>
        public float PitchJitter = 0f;

        public bool Loop => Channel != LoopChannel.None;

        /// <summary>Ключ для <c>Resources.Load</c>: путь манифеста без расширения под корнем Audio/.</summary>
        public string ResourceKey
        {
            get
            {
                int dot = File.LastIndexOf('.');
                string bare = dot < 0 ? File : File.Substring(0, dot);
                return AudioCatalog.ResourceRoot + bare;
            }
        }
    }

    /// <summary>
    /// Тип конца жизни — три финала манифеста. <see cref="Game.Cause"/> — свободная русская строка,
    /// перечислимого типа в игре нет, поэтому классификация живёт ЗДЕСЬ, отдельно и тестируемо.
    /// </summary>
    public enum FinaleKind
    {
        OldAge,     // «…старость» → скрипка
        Burnout,    // «полное выгорание» → казу
        Fatal       // FATAL-карта, «здоровье не выдержало», «неведомая дичь» → обрыв пластинки
    }

    /// <summary>
    /// ЕДИНСТВЕННАЯ точка, где строка причины превращается в звук финала.
    /// Все «старости» (весёлая/одинокая/спокойная) — один конец; «полное выгорание» — второй;
    /// ВСЁ остальное, включая обрыв по здоровью и фатальные карточки, — обрыв пластинки.
    /// </summary>
    public static class FinaleSound
    {
        public const string OldAgeMarker = "старость";
        public const string BurnoutCause = "полное выгорание";

        public static FinaleKind Classify(string cause)
        {
            if (string.IsNullOrEmpty(cause)) return FinaleKind.Fatal;
            if (cause == BurnoutCause) return FinaleKind.Burnout;
            if (cause.Contains(OldAgeMarker)) return FinaleKind.OldAge;
            return FinaleKind.Fatal;
        }

        public static SoundEvent EventFor(string cause) => Classify(cause) switch
        {
            FinaleKind.OldAge => SoundEvent.FinaleOldAge,
            FinaleKind.Burnout => SoundEvent.FinaleBurnout,
            _ => SoundEvent.FinaleFatal
        };
    }

    /// <summary>
    /// МАППИНГ МАНИФЕСТА: событие → файл → канал/громкость/питч. Один источник истины;
    /// <c>AudioManifestTests</c> парсит сам <c>manifest.md</c> и падает, если таблицы разошлись.
    ///
    /// ⚠ ГРОМКОСТИ ЗДЕСЬ — ЧЕРНОВЫЕ, ПОМЕЧЕНЫ «ТЮНИМО». Уровни между источниками манифестом НЕ
    /// выровнены (его же предупреждение), сведение делает основательница на ушном гейте.
    /// </summary>
    public static class AudioCatalog
    {
        /// <summary>Все клипы лежат под <c>Assets/_Project/Audio/Resources/Audio/…</c> — группами манифеста.</summary>
        public const string ResourceRoot = "Audio/";

        // --- ЧЕРНОВЫЕ ГРОМКОСТИ (ТЮНИМО) --------------------------------------------------------
        // Сгруппированы по громкости-роли, а не по разделам, чтобы сведение читалось одним взглядом.
        public const float VolMusicBed = 0.35f;      // ТЮНИМО: тема — подложка, не солист
        public const float VolFrequentTick = 0.28f;  // ТЮНИМО: до 5/с (тик крутилки) — «ничего резкого»
        public const float VolFrequentSoft = 0.35f;  // ТЮНИМО: частое и мягкое (монета)
        public const float VolBlip = 0.40f;          // ТЮНИМО: блипы зон и тревоги
        public const float VolCard = 0.50f;          // ТЮНИМО: раздача карточки
        public const float VolDome = 0.45f;          // ТЮНИМО: последняя секунда купола
        public const float VolAnswer = 0.70f;        // ТЮНИМО: калимба ответа — голос игры
        public const float VolHostStinger = 0.50f;   // ТЮНИМО: пиццикато Ведущего
        public const float VolPhonePickup = 0.50f;   // ТЮНИМО: «алло» ИДЁТ ПОД САЛЮТОМ — тише салюта
        public const float VolReward = 0.70f;        // ТЮНИМО: салют/лечение/рестарт
        public const float VolEvent = 0.65f;         // ТЮНИМО: обычное заметное событие
        public const float VolBig = 0.85f;           // ТЮНИМО: крупные перебивки (депрессия, финалы)
        public const float VolLoud = 0.90f;          // ТЮНИМО: опенер, расставание, «плохой родитель»

        // --- Питч семейства тревог: ОДИН файл, три высоты (манифест) -----------------------------
        public const float AlarmPitchMoney = 0.8909f;    // −2 полутона
        public const float AlarmPitchEnergy = 1.0000f;   // базово
        public const float AlarmPitchHealth = 1.1225f;   // +2 полутона

        /// <summary>Манифест: тик и монета обязаны гулять питчем ±6 %, иначе очередь = дребезг.</summary>
        public const float RepeatPitchJitter = 0.06f;

        private static readonly Dictionary<SoundEvent, SoundDef> _map = new()
        {
            // 1. Сквозные — эмбиент шоу НАМЕРЕННО без звука (см. IntentionallySilent)
            [SoundEvent.MusicTheme] = new SoundDef
            {
                File = "01-ambient/theme--a-circus.mp3",
                Bus = SoundBus.Music, Channel = LoopChannel.Music, Volume = VolMusicBed
            },

            // 2. Карточки и ответы — калимба ДА/НЕТ есть голос игры
            [SoundEvent.CardDeal] = new SoundDef
            { File = "02-cards/card-deal--b-place.ogg", Volume = VolCard },
            [SoundEvent.AnswerYes] = new SoundDef
            { File = "02-cards/answer-yes--q3-kalimba.wav", Volume = VolAnswer },
            [SoundEvent.AnswerNo] = new SoundDef
            { File = "02-cards/answer-no--q3-kalimba.wav", Volume = VolAnswer },
            [SoundEvent.Timeout] = new SoundDef
            { File = "02-cards/timeout--c-slide-down.ogg", Volume = VolEvent },
            [SoundEvent.DomeLastSecond] = new SoundDef
            { File = "02-cards/dome-lastsec--r2b-kitchen-timer.wav", Volume = VolDome },
            [SoundEvent.BlockMoney] = new SoundDef
            { File = "02-cards/block-money--b-horn-fail.wav", Volume = VolAnswer },

            // 3. Деньги — оба частые, оба гуляют питчем
            [SoundEvent.CrankTick] = new SoundDef
            {
                File = "03-money/crank-tick--a-toy-crank.wav",
                Volume = VolFrequentTick, PitchJitter = RepeatPitchJitter
            },
            [SoundEvent.CoinJar] = new SoundDef
            {
                File = "03-money/coin-jar--c-coins-handle.wav",
                Volume = VolFrequentSoft, PitchJitter = RepeatPitchJitter
            },

            // Общее семейство тревог: ОДИН И ТОТ ЖЕ файл на трёх высотах
            [SoundEvent.AlarmMoney] = new SoundDef
            { File = "03-alarms/alarm--B-device.ogg", Volume = VolBlip, Pitch = AlarmPitchMoney },
            [SoundEvent.AlarmEnergy] = new SoundDef
            { File = "03-alarms/alarm--B-device.ogg", Volume = VolBlip, Pitch = AlarmPitchEnergy },
            [SoundEvent.AlarmHealth] = new SoundDef
            { File = "03-alarms/alarm--B-device.ogg", Volume = VolBlip, Pitch = AlarmPitchHealth },

            // 4. Энергия — зарядка ЦЕЛЫЙ ЖЕСТ, одним файлом (основательница отвергла разбиение)
            [SoundEvent.EnergyCharge] = new SoundDef
            { File = "04-energy/charge-scene--A-filling.wav", Volume = VolEvent },
            [SoundEvent.BurnoutIn] = new SoundDef
            { File = "04-energy/burnout-in--a-powerdown.wav", Volume = VolBig },
            [SoundEvent.BurnoutOut] = new SoundDef
            { File = "04-energy/burnout-out--a-powerup.wav", Volume = VolBig },

            // 5. Отношения
            [SoundEvent.ZoneIn] = new SoundDef
            { File = "05-relations/zone-in--A-synth.ogg", Volume = VolBlip },
            [SoundEvent.ZoneOut] = new SoundDef
            { File = "05-relations/zone-out--A-synth.ogg", Volume = VolBlip },
            [SoundEvent.Breakup] = new SoundDef
            { File = "05-relations/breakup--c-glass-plus-kalimba.wav", Volume = VolLoud },
            [SoundEvent.SecondChance] = new SoundDef
            { File = "05-relations/secondchance--a-kalimba-up.wav", Volume = VolBig },

            // 6. Здоровье
            [SoundEvent.HealthOpen] = new SoundDef
            { File = "06-health/health-open--a-deep-bell.wav", Volume = VolEvent },
            [SoundEvent.Heal] = new SoundDef
            { File = "06-health/heal--c-kalimba-warm.wav", Volume = VolReward },

            // 7. Ребёнок (телефон)
            [SoundEvent.PhoneRing] = new SoundDef
            {
                File = "07-phone/ring--a-toy-phone.wav",
                Bus = SoundBus.Loop, Channel = LoopChannel.PhoneRing, Volume = VolEvent
            },
            [SoundEvent.PhonePickup] = new SoundDef
            { File = "07-phone/pickup--a-kalimba-allo.wav", Volume = VolPhonePickup },
            [SoundEvent.PhoneMissed] = new SoundDef
            { File = "07-phone/missed--a-disconnect.wav", Volume = VolEvent },
            [SoundEvent.BadParent] = new SoundDef
            { File = "07-phone/badparent--a-braam.wav", Volume = VolLoud },

            // 8. Салют
            [SoundEvent.StarBurst] = new SoundDef
            { File = "08-starburst/starburst--c-sparkle.ogg", Volume = VolReward },

            // 9. Ведущий — шесть пиццикато по тонам пула, одно семейство
            [SoundEvent.HostPositive] = new SoundDef
            { File = "09-host/host-stinger--1-positive.wav", Volume = VolHostStinger },
            [SoundEvent.HostRisky] = new SoundDef
            { File = "09-host/host-stinger--2-risky.wav", Volume = VolHostStinger },
            [SoundEvent.HostAbsurd] = new SoundDef
            { File = "09-host/host-stinger--3-absurd.wav", Volume = VolHostStinger },
            [SoundEvent.HostCautious] = new SoundDef
            { File = "09-host/host-stinger--4-cautious.wav", Volume = VolHostStinger },
            [SoundEvent.HostFatal] = new SoundDef
            { File = "09-host/host-stinger--5-fatal.wav", Volume = VolHostStinger },
            [SoundEvent.HostSkip] = new SoundDef
            { File = "09-host/host-stinger--6-skip.wav", Volume = VolHostStinger },
            [SoundEvent.Tutorial] = new SoundDef
            { File = "09-host/tutorial--a-tusch.wav", Volume = VolEvent },

            // 10. Кризис и депрессия
            [SoundEvent.BlitzStart] = new SoundDef
            { File = "10-crisis/blitz-start--a-sting.wav", Volume = VolBig },
            [SoundEvent.BlitzThought] = new SoundDef
            { File = "10-crisis/blitz-thought--a-whoosh.wav", Volume = VolDome },
            [SoundEvent.Impulse] = new SoundDef
            {
                File = "10-crisis/impulse--r2a-strings.wav",
                Bus = SoundBus.Loop, Channel = LoopChannel.Impulse, Volume = VolEvent
            },
            [SoundEvent.DepressionEnter] = new SoundDef
            { File = "10-crisis/depr-enter--a-reverse-cymbal.wav", Volume = VolBig },
            // ⚠ ЕДИНСТВЕННЫЙ звук ВНЕ фильтра: «пульс остаётся наверху нетронутым» (манифест).
            [SoundEvent.DepressionPulse] = new SoundDef
            {
                File = "10-crisis/depr-pulse--b-heartbeat.wav",
                Bus = SoundBus.Unfiltered, Volume = VolReward
            },
            [SoundEvent.DepressionMiss] = new SoundDef
            { File = "10-crisis/depr-miss--a-muffled-thud.wav", Volume = VolHostStinger },
            [SoundEvent.DepressionExit] = new SoundDef
            { File = "10-crisis/depr-exit--a-riser.wav", Volume = VolBig },

            // 11. Рамка жизни — вехи возраста НАМЕРЕННО без звука (см. IntentionallySilent)
            [SoundEvent.Opener] = new SoundDef
            { File = "11-frame/opener--r2a-funny-fanfare.wav", Volume = VolLoud },
            [SoundEvent.FinaleOldAge] = new SoundDef
            { File = "11-frame/finale--1-oldage-violin.wav", Volume = VolBig },
            [SoundEvent.FinaleBurnout] = new SoundDef
            { File = "11-frame/finale--2-burnout-kazoo.wav", Volume = VolBig },
            [SoundEvent.FinaleFatal] = new SoundDef
            { File = "11-frame/finale--3-fatal-scratch.wav", Volume = VolBig },
            [SoundEvent.Restart] = new SoundDef
            { File = "11-frame/restart--a-tape-rewind.wav", Volume = VolReward }
        };

        public static IReadOnlyDictionary<SoundEvent, SoundDef> Map => _map;

        public static SoundDef Get(SoundEvent e) => _map.TryGetValue(e, out var d) ? d : null;

        /// <summary>
        /// НАМЕРЕННО БЕЗ ЗВУКА — решения основательницы, а НЕ дыры в разводке. Список-исключение
        /// сверяется тестом с разделом «Намеренно без звука» манифеста: если она когда-нибудь передумает,
        /// правка манифеста уронит тест и заставит вернуться сюда, а не тихо разъехаться.
        /// </summary>
        public static readonly IReadOnlyList<string> IntentionallySilent = new[]
        {
            "Эмбиент шоу",        // фонового воздуха под музыкой нет
            "Вехи возраста",      // «будет какофония, если на каждую цифру делать звук»
            "Зал / аплодисменты"  // сняты отовсюду, включая опенер и финал
        };

        /// <summary>
        /// ПРАВИЛО ИМПОРТА, живущее в рантайме нарочно: его применяет редакторный
        /// <c>AudioImportSettings</c>, а проверяет EditMode-тест — иначе правило и настройки .meta
        /// разъезжаются молча. Лупы грузятся потоком, короткие SFX распаковываются в память.
        /// </summary>
        public static bool StreamingFor(string file)
        {
            foreach (var pair in _map)
                if (pair.Value.File == file) return pair.Value.Loop;
            return false;
        }

        /// <summary>Все файлы манифеста, идущие в сборку (одна и та же дорожка тревог — один раз).</summary>
        public static IReadOnlyList<string> BuildFiles()
        {
            var seen = new List<string>();
            foreach (var pair in _map)
                if (!seen.Contains(pair.Value.File)) seen.Add(pair.Value.File);
            return seen;
        }

        /// <summary>Тон реплики Ведущего → его пиццикато. <see cref="HostTone.Debt"/> — см. комментарий.</summary>
        public static SoundEvent ForHostTone(HostTone tone) => tone switch
        {
            HostTone.Positive => SoundEvent.HostPositive,
            HostTone.Risky => SoundEvent.HostRisky,
            HostTone.Absurd => SoundEvent.HostAbsurd,
            HostTone.Cautious => SoundEvent.HostCautious,
            HostTone.Fatal => SoundEvent.HostFatal,
            HostTone.Skip => SoundEvent.HostSkip,
            // Манифест назвал ШЕСТЬ пиццикато по шести тонам пула, а в коде тонов семь: `debt` появился
            // позже манифеста (пул «счёт ушёл в минус»). Отдельного файла под него НЕ придумываем —
            // долг это предупреждение о деньгах, и он берёт голос «осторожность». Если основательница
            // захочет долгу собственный стингер — это строка манифеста, а не правка кода.
            HostTone.Debt => SoundEvent.HostCautious,
            _ => SoundEvent.HostSkip
        };

        /// <summary>Тревога шкалы → её высота в семействе тревог. Отношения НЕ входят: у них свои зон-блипы.</summary>
        public static SoundEvent ForAlarm(AlarmScale scale) => scale switch
        {
            AlarmScale.Money => SoundEvent.AlarmMoney,
            AlarmScale.Energy => SoundEvent.AlarmEnergy,
            _ => SoundEvent.AlarmHealth
        };
    }
}
