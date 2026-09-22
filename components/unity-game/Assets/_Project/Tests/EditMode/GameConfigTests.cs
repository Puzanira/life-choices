using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// Гарды ЧТЕНИЯ конфига игры (<see cref="GameConfig"/>, r7 — полярность оси отношений).
    ///
    /// Зачем это вообще существует — в шапке <see cref="GameConfig"/>: ориентация оси джойстика есть
    /// свойство физической сборки автомата, пакетный <c>SerialTuning.InvertJoystickX</c> нам недоступен,
    /// и без этой ручки полярность на стойке нечем ни проверить, ни исправить без пересборки билда.
    /// Здесь проверяется ПАРСЕР и его умолчания; применение знака (и то, что он НЕ трогает клавиатуру) —
    /// в <c>ArcadeInputSourceMappingTests.RelationAxisSign_…</c>.
    /// </summary>
    public class GameConfigTests
    {
        [TearDown]
        public void Reset() => GameConfig.DebugReload();

        [Test]
        public void MissingKey_MeansPlusOne_SoTheGameWorksWithoutAnyConfigAtAll()
        {
            Assert.AreEqual(1f, GameConfig.SignFromJson("{\"id\": \"x\", \"controls\": [\"Joystick\"]}"),
                "ключа нет — ось не зеркалится");
            Assert.AreEqual(1f, GameConfig.SignFromJson(""), "пустой текст — тоже умолчание");
            Assert.AreEqual(1f, GameConfig.SignFromJson(null), "нет файла вовсе — тоже умолчание");
        }

        [Test]
        public void MinusOne_IsRead_InBothSpellings()
        {
            Assert.AreEqual(-1f, GameConfig.SignFromJson("{\"RelationAxisSign\": -1}"),
                "канон-написание ключа");
            Assert.AreEqual(-1f, GameConfig.SignFromJson("{\"relationAxisSign\": -1}"),
                "…и то же со строчной буквы: файл правит человек руками, регистр не должен решать");
            Assert.AreEqual(1f, GameConfig.SignFromJson("{\"RelationAxisSign\": 1}"),
                "явная единица читается как единица");
        }

        /// <summary>
        /// Это ПОЛЯРНОСТЬ, а не чувствительность: «−3» обязано стать «−1», иначе опечатка в конфиге
        /// втихую превратится в усилитель оси, которого никто не объявлял.
        /// </summary>
        [Test]
        public void AnyNonZeroNumber_NormalizesToPlusOrMinusOne()
        {
            Assert.AreEqual(-1f, GameConfig.SignFromJson("{\"RelationAxisSign\": -3}"));
            Assert.AreEqual(1f, GameConfig.SignFromJson("{\"RelationAxisSign\": 7.5}"));
            Assert.AreEqual(1f, GameConfig.SignFromJson("{\"RelationAxisSign\": 0}"),
                "ноль = «не задано», а не «ось мертва»");
        }

        /// <summary>Битый конфиг НЕ роняет игру: потерять управление из-за лишней запятой — хуже.</summary>
        [Test]
        public void BrokenJson_FallsBackToTheDefault_WithoutThrowing()
        {
            Assert.AreEqual(1f, GameConfig.SignFromJson("{это вообще не json"));
            Assert.AreEqual(1f, GameConfig.SignFromJson("[1,2,3]"));
        }

        /// <summary>
        /// Канон-манифест на диске реально содержит поле и реально читается игрой — иначе «правь
        /// game.json» было бы инструкцией в никуда. Путь к нему обязан быть среди тех, где игра ищет.
        /// </summary>
        [Test]
        public void TheShippedManifest_DeclaresTheKey_AndIsOneOfThePlacesTheGameLooks()
        {
            string manifest = Path.Combine(Application.dataPath, "_Project", GameConfig.FileName);
            Assert.IsTrue(File.Exists(manifest), "канон-манифест на месте: " + manifest);

            StringAssert.Contains(GameConfig.RelationAxisSignKey, File.ReadAllText(manifest),
                "поле объявлено в манифесте ЯВНО — человек у стойки должен увидеть, что тут можно крутить");
            Assert.AreEqual(1f, GameConfig.SignFromJson(File.ReadAllText(manifest)),
                "…и в репозитории оно стоит в нейтральном +1");

            Assert.IsTrue(GameConfig.CandidatePaths().Any(p => p == manifest),
                "манифест проекта — один из путей поиска (иначе в редакторе читался бы воздух)");
            Assert.AreEqual(1f, GameConfig.RelationAxisSign, "и живое чтение с диска даёт то же самое");
        }

        /// <summary>
        /// Порядок поиска: правка РЯДОМ С БИЛДОМ обязана бить канон-манифест из проекта — иначе обещание
        /// «поправил на стойке и перезапустил, без пересборки» не выполняется.
        /// </summary>
        [Test]
        public void SearchOrder_PutsTheOnDiskOverrides_BeforeTheInProjectManifest()
        {
            string[] paths = GameConfig.CandidatePaths();
            Assert.AreEqual(3, paths.Length, "три места поиска: StreamingAssets, рядом с плеером, проект");

            int project = System.Array.FindIndex(paths,
                p => p == Path.Combine(Application.dataPath, "_Project", GameConfig.FileName));
            Assert.AreEqual(paths.Length - 1, project, "канон-манифест проекта ищется ПОСЛЕДНИМ");
            Assert.IsTrue(paths.All(p => Path.GetFileName(p) == GameConfig.FileName),
                "во всех трёх местах это один и тот же файл, а не три разных формата");
        }
    }
}
