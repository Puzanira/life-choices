using System;
using System.IO;
using UnityEngine;

namespace ThanksNoThanks
{
    /// <summary>
    /// Конфиг ЭТОЙ игры, читаемый с диска в рантайме (<c>game.json</c> — тот же манифест, в котором
    /// объявлены контролы). Сейчас в нём ровно одно поле, и появилось оно по конкретной боли стойки.
    ///
    /// ⚠ ЗАЧЕМ ЭТО ВООБЩЕ ЕСТЬ: ПОЛЯРНОСТЬ ГОРИЗОНТАЛЬНОЙ ОСИ ДЖОЙСТИКА.
    /// Ориентация осей — свойство ФИЗИЧЕСКОЙ СБОРКИ, а не кода (CONTROLS_BRIEF §5: «зеркало X уже
    /// дважды переворачивали из-за пере-подключения модуля»). В пакете arcade-controls для этого есть
    /// <c>SerialTuning.InvertJoystickX</c>, но его читает только <c>SerialBackend</c>, а раннер пакета
    /// жёстко строит <c>SerialTuning.Default</c> (где флаг всегда false) и не даёт его переопределить.
    /// Пакет нам трогать нельзя. Значит, у стойки не было НИ ОДНОГО способа перевернуть ось, кроме
    /// пересборки билда — а проверить полярность может только живой человек у автомата.
    ///
    /// Поэтому знак оси отношений живёт ЗДЕСЬ, на стороне игры: один множитель ±1, читаемый с диска.
    /// Перевернуть ось на стойке = поправить одну цифру в <c>game.json</c> и перезапустить игру.
    /// Пересборка не нужна.
    ///
    /// ⚠ ЗНАК ПРИМЕНЯЕТСЯ ТОЛЬКО НА ЖЕЛЕЗНОМ ПУТИ (см. <see cref="ArcadeInputSource"/>). Клавиатурная
    /// эмуляция однозначна: «←» — это влево, «→» — вправо, зеркалить там нечего и нельзя, иначе
    /// разработческая клавиатура начнёт врать ровно в тот момент, когда чинят проводку стойки.
    ///
    /// ⚠ ГДЕ ИГРА ИЩЕТ ФАЙЛ (первый найденный побеждает, см. <see cref="CandidatePaths"/>):
    ///   1. <c>StreamingAssets/game.json</c> — лежит в собранном билде ЛООСЕ-файлом, правится на месте;
    ///   2. <c>game.json</c> РЯДОМ С ПЛЕЕРОМ (папка билда) — то же самое, если класть снаружи;
    ///   3. <c>Assets/_Project/game.json</c> — канон-манифест в проекте (редактор, тесты, дев-прогон).
    /// Ни одного файла нет — берётся <see cref="DefaultRelationAxisSign"/> (+1), игра работает как
    /// работала. Отсутствие конфига НИКОГДА не ломает запуск: это тюнинг, а не зависимость.
    /// </summary>
    public static class GameConfig
    {
        /// <summary>Имя файла конфига — тот же манифест игры, что читает интеграционный кит аркады.</summary>
        public const string FileName = "game.json";

        /// <summary>Знак оси по умолчанию: +1 = «вправо двигает маркер вправо» (как нарисовано).</summary>
        public const float DefaultRelationAxisSign = 1f;

        /// <summary>Канон-написание ключа в JSON (принимается и вариант со строчной буквы).</summary>
        public const string RelationAxisSignKey = "RelationAxisSign";

        [Serializable]
        private sealed class Data
        {
            // ⚠ ДВА ПОЛЯ НА ОДИН КЛЮЧ — НАМЕРЕННО. JsonUtility сопоставляет имена ПОБУКВЕННО, а правит
            // этот файл человек у автомата, руками и, скорее всего, ночью. Канон — «RelationAxisSign»;
            // «relationAxisSign» (как у остальных ключей манифеста) принимается как синоним, чтобы
            // опечатка в регистре не читалась как «игра меня не слышит».
            public float RelationAxisSign;
            public float relationAxisSign;
        }

        private static bool _loaded;
        private static float _relationAxisSign = DefaultRelationAxisSign;

        /// <summary>Множитель горизонтальной оси джойстика НА СТОЙКЕ: +1 как есть, −1 — зеркало.</summary>
        public static float RelationAxisSign
        {
            get
            {
                if (!_loaded) Load();
                return _relationAxisSign;
            }
        }

        /// <summary>Путь, из которого реально прочитан конфиг, или пусто (взят умолчательный знак).</summary>
        public static string LoadedFrom { get; private set; } = "";

        /// <summary>
        /// Разбор знака из текста JSON. Без сторонних библиотек: <c>JsonUtility</c> — штатный
        /// сериализатор Unity, тот же, которым канон-манифест уже читает <c>ArcadePackagingTests</c>.
        ///
        /// Любое НЕнулевое число нормализуется до ±1: это ПОЛЯРНОСТЬ, а не чувствительность, и
        /// «RelationAxisSign: -3» не должно втихую становиться усилителем. Ноль и отсутствие ключа
        /// читаются одинаково — «не задано» → умолчание. Битый JSON не роняет игру: предупреждение
        /// в лог и умолчание (потерять ось из-за лишней запятой в конфиге — хуже, чем не зеркалить).
        /// </summary>
        public static float SignFromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return DefaultRelationAxisSign;

            Data d;
            try
            {
                d = JsonUtility.FromJson<Data>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GameConfig] {FileName} не разобран ({e.Message}); "
                               + $"{RelationAxisSignKey} = {DefaultRelationAxisSign}.");
                return DefaultRelationAxisSign;
            }

            if (d == null) return DefaultRelationAxisSign;

            float raw = d.RelationAxisSign != 0f ? d.RelationAxisSign : d.relationAxisSign;
            if (raw == 0f) return DefaultRelationAxisSign;
            return raw < 0f ? -1f : 1f;
        }

        /// <summary>Места, где игра ищет конфиг, в порядке приоритета. Первый существующий побеждает.</summary>
        public static string[] CandidatePaths()
        {
            string data = Application.dataPath;                  // редактор: <проект>/Assets; плеер: <билд>/<Имя>_Data
            string besidePlayer = Path.GetDirectoryName(data);   // редактор: <проект>;        плеер: <билд>
            return new[]
            {
                Path.Combine(Application.streamingAssetsPath, FileName),
                string.IsNullOrEmpty(besidePlayer) ? "" : Path.Combine(besidePlayer, FileName),
                Path.Combine(data, "_Project", FileName),
            };
        }

        private static void Load()
        {
            _loaded = true;
            _relationAxisSign = DefaultRelationAxisSign;
            LoadedFrom = "";

            foreach (string path in CandidatePaths())
            {
                if (string.IsNullOrEmpty(path)) continue;
                try
                {
                    if (!File.Exists(path)) continue;
                    _relationAxisSign = SignFromJson(File.ReadAllText(path));
                    LoadedFrom = path;
                    return;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[GameConfig] {path} не прочитан ({e.Message}); ищем дальше.");
                }
            }
        }

        /// <summary>Слой-2-шов: подсунуть конфиг текстом, не трогая диск (гарды знака оси).</summary>
        public static void DebugUseJson(string json)
        {
            _relationAxisSign = SignFromJson(json);
            _loaded = true;
            LoadedFrom = "<debug>";
        }

        /// <summary>Слой-2-шов: забыть прочитанное, следующий запрос снова пойдёт на диск.</summary>
        public static void DebugReload()
        {
            _loaded = false;
            _relationAxisSign = DefaultRelationAxisSign;
            LoadedFrom = "";
        }
    }
}
