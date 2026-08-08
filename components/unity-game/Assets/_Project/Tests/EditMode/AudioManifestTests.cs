using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// МАНИФЕСТ — ИСТОЧНИК ИСТИНЫ, И ЭТОТ ТЕСТ ЕГО ЧИТАЕТ. Не копия списка в коде, а РАЗБОР самого
    /// <c>docs/assets/audio-picks/manifest.md</c>: если дизайн-сессия однажды поменяет файл, добавит
    /// строку или передумает про «намеренно без звука», разъедется не игра, а этот тест.
    ///
    /// Художественную сторону тут не судят (ушной гейт — основательница). Проверяется структура:
    /// каждый шипящийся файл манифеста имеет событие, каждое событие — файл, файл лежит на диске.
    /// </summary>
    public class AudioManifestTests
    {
        private const string AudioRoot = "Assets/_Project/Audio/Resources/Audio/";

        private static string ProjectRoot()
        {
            // Application.dataPath = <repo>/components/unity-game/Assets
            var assets = new DirectoryInfo(Application.dataPath);
            return assets.Parent.Parent.Parent.FullName;   // → <repo>
        }

        private static string ManifestPath()
            => Path.Combine(ProjectRoot(), "docs", "assets", "audio-picks", "manifest.md");

        private static string ManifestText()
        {
            var p = ManifestPath();
            Assert.IsTrue(File.Exists(p), "манифест звуковой дизайн-сессии на месте: " + p);
            return File.ReadAllText(p);
        }

        /// <summary>Прослушки и демо — манифест прямо пишет «в сборку не идёт»/«в сборку не идут».</summary>
        private static bool ListenOnly(string file)
            => file.Contains("DEMO") || file.Contains("3voices");

        /// <summary>Все звуковые файлы, упомянутые манифестом в бэктиках, кроме прослушек.</summary>
        private static HashSet<string> ManifestShipFiles()
        {
            var rx = new Regex(@"`(\d\d-[a-z]+/[^`]+?\.(?:wav|ogg|mp3))`");
            var found = new HashSet<string>();
            foreach (Match m in rx.Matches(ManifestText()))
            {
                var file = m.Groups[1].Value;
                if (!ListenOnly(file)) found.Add(file);
            }
            return found;
        }

        [Test]
        public void Manifest_Parses_AndIsNotEmpty()
        {
            var files = ManifestShipFiles();
            // Голая защита от «регексп перестал совпадать и тест стал зелёным ни на чём».
            Assert.Greater(files.Count, 30, "манифест разобран: файлов в сборку заметно больше тридцати");
        }

        [Test]
        public void EveryManifestFile_HasCatalogEvent()
        {
            var manifest = ManifestShipFiles();
            var catalog = new HashSet<string>(AudioCatalog.BuildFiles());
            var missing = manifest.Except(catalog).OrderBy(x => x).ToList();
            CollectionAssert.IsEmpty(missing,
                "в манифесте есть файл, которому в AudioCatalog не назначено событие: "
                + string.Join(", ", missing));
        }

        [Test]
        public void EveryCatalogFile_IsInManifest()
        {
            var manifest = ManifestShipFiles();
            var catalog = new HashSet<string>(AudioCatalog.BuildFiles());
            var invented = catalog.Except(manifest).OrderBy(x => x).ToList();
            CollectionAssert.IsEmpty(invented,
                "AudioCatalog ссылается на файл, которого манифест не утверждал: "
                + string.Join(", ", invented));
        }

        [Test]
        public void EverySoundEvent_HasADefinition()
        {
            foreach (SoundEvent e in System.Enum.GetValues(typeof(SoundEvent)))
            {
                var def = AudioCatalog.Get(e);
                Assert.IsNotNull(def, "событие без строки каталога: " + e);
                Assert.IsFalse(string.IsNullOrEmpty(def.File), "событие без файла: " + e);
            }
        }

        [Test]
        public void EveryCatalogFile_ExistsOnDisk_UnderResources()
        {
            foreach (var file in AudioCatalog.BuildFiles())
            {
                var full = Path.Combine(ProjectRoot(), "components", "unity-game",
                    AudioRoot.Replace('/', Path.DirectorySeparatorChar) + file);
                Assert.IsTrue(File.Exists(full), "файл манифеста не импортирован в Assets: " + file);
            }
        }

        [Test]
        public void ListeningOnlyFiles_AreNotShipped()
        {
            // Прослушки (`--3voices`, `DEMO-6`, `DEMO--*`) манифест явно исключил из сборки — они не
            // должны ни попасть в каталог, ни оказаться под Resources.
            foreach (var file in AudioCatalog.BuildFiles())
                Assert.IsFalse(ListenOnly(file), "в сборку попала прослушка: " + file);

            var dir = Path.Combine(ProjectRoot(), "components", "unity-game",
                AudioRoot.Replace('/', Path.DirectorySeparatorChar));
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(f);
                if (name.EndsWith(".meta")) continue;
                Assert.IsFalse(ListenOnly(name), "прослушка лежит под Resources: " + name);
            }
        }

        /// <summary>
        /// «Намеренно без звука» — НЕ дыра, а решение основательницы. Список в коде обязан совпадать
        /// с разделом манифеста дословно: передумает — уронит тест, а не разъедется молча.
        /// </summary>
        [Test]
        public void IntentionallySilent_MatchesManifestSection()
        {
            var text = ManifestText();
            int start = text.IndexOf("## Намеренно без звука", System.StringComparison.Ordinal);
            Assert.Greater(start, 0, "в манифесте есть раздел «Намеренно без звука»");
            int end = text.IndexOf("\n## ", start + 3, System.StringComparison.Ordinal);
            var section = end > 0 ? text.Substring(start, end - start) : text.Substring(start);

            var rx = new Regex(@"^- \*\*(.+?)\*\*", RegexOptions.Multiline);
            var declared = rx.Matches(section).Select(m => m.Groups[1].Value.Trim()).ToList();

            CollectionAssert.AreEquivalent(declared, AudioCatalog.IntentionallySilent.ToList(),
                "список «намеренно без звука» в AudioCatalog разошёлся с манифестом");
            Assert.AreEqual(3, declared.Count, "их ровно три: эмбиент, вехи возраста, зал");
        }

        [Test]
        public void SilentDecisions_HaveNoSoundEvent()
        {
            // Прямая формулировка запрета: под эти три решения событий быть не должно.
            var names = System.Enum.GetNames(typeof(SoundEvent));
            foreach (var bad in new[] { "Ambient", "Milestone", "Applause", "Crowd", "Audience" })
                Assert.IsFalse(names.Any(n => n.Contains(bad)),
                    "появилось событие «" + bad + "», а основательница решила это НЕ озвучивать");
        }

        // --- Приёмы манифеста, которые легко потерять при правке -------------------------------

        [Test]
        public void AlarmFamily_IsOneFile_AtThreeHeights()
        {
            var money = AudioCatalog.Get(SoundEvent.AlarmMoney);
            var energy = AudioCatalog.Get(SoundEvent.AlarmEnergy);
            var health = AudioCatalog.Get(SoundEvent.AlarmHealth);

            Assert.AreEqual(money.File, energy.File, "тревоги — ОДИН файл на три шкалы");
            Assert.AreEqual(money.File, health.File, "тревоги — ОДИН файл на три шкалы");
            // −2 / 0 / +2 полутона: деньги ниже, здоровье выше.
            Assert.Less(money.Pitch, energy.Pitch, "деньги ниже базовой высоты");
            Assert.Less(energy.Pitch, health.Pitch, "здоровье выше базовой высоты");
            Assert.AreEqual(1f, energy.Pitch, 0.0001f, "энергия — базовая высота");
        }

        [Test]
        public void FrequentSounds_WanderInPitch()
        {
            // «Тик звучит до 5 раз/с — движок должен гулять питчем ±6 %, иначе дребезг». То же монете.
            foreach (var e in new[] { SoundEvent.CrankTick, SoundEvent.CoinJar })
                Assert.AreEqual(AudioCatalog.RepeatPitchJitter, AudioCatalog.Get(e).PitchJitter, 0.0001f,
                    "частое событие обязано гулять питчем: " + e);
        }

        [Test]
        public void PhonePickup_SitsUnderTheStarburst()
        {
            // Манифест: «Поднял трубку» играет ПОД салютом — держать тише салюта.
            Assert.Less(AudioCatalog.Get(SoundEvent.PhonePickup).Volume,
                        AudioCatalog.Get(SoundEvent.StarBurst).Volume,
                        "«алло» обязано быть тише салюта — они звучат одновременно");
        }

        [Test]
        public void LoopChannels_AreMarkedLooping_AndUnique()
        {
            var loops = AudioCatalog.Map.Where(p => p.Value.Loop).ToList();
            Assert.AreEqual(3, loops.Count, "луп-каналов три: тема, рингтон, сирена импульса");
            foreach (var p in loops)
                Assert.AreNotEqual(LoopChannel.None, p.Value.Channel, "у лупа обязан быть канал: " + p.Key);

            var channels = loops.Select(p => p.Value.Channel).ToList();
            CollectionAssert.AllItemsAreUnique(channels, "два события не могут делить один луп-канал");

            Assert.AreEqual(LoopChannel.Music, AudioCatalog.Get(SoundEvent.MusicTheme).Channel);
            Assert.AreEqual(LoopChannel.PhoneRing, AudioCatalog.Get(SoundEvent.PhoneRing).Channel);
            Assert.AreEqual(LoopChannel.Impulse, AudioCatalog.Get(SoundEvent.Impulse).Channel);
        }

        [Test]
        public void DepressionPulse_IsTheOnlyUnfilteredSound()
        {
            var unfiltered = AudioCatalog.Map.Where(p => p.Value.Bus == SoundBus.Unfiltered)
                                             .Select(p => p.Key).ToList();
            CollectionAssert.AreEquivalent(new[] { SoundEvent.DepressionPulse }, unfiltered,
                "мимо фильтра депрессии проходит РОВНО удар сердца — «остаётся наверху нетронутым»");
        }

        [Test]
        public void Volumes_AreDraftButSane()
        {
            // Черновые (ТЮНИМО) — но обязаны быть в разумных пределах, иначе ушной гейт сорвётся
            // не на балансе, а на клиппинге/тишине.
            foreach (var p in AudioCatalog.Map)
            {
                Assert.Greater(p.Value.Volume, 0f, "нулевая громкость = немое событие: " + p.Key);
                Assert.LessOrEqual(p.Value.Volume, 1f, "громкость выше 1 клиппит: " + p.Key);
            }
        }

        [Test]
        public void HostTones_AllSixPizzicati_AreDistinctFiles()
        {
            var tones = new[]
            {
                SoundEvent.HostPositive, SoundEvent.HostRisky, SoundEvent.HostAbsurd,
                SoundEvent.HostCautious, SoundEvent.HostFatal, SoundEvent.HostSkip
            };
            var files = tones.Select(t => AudioCatalog.Get(t).File).ToList();
            CollectionAssert.AllItemsAreUnique(files, "шесть тонов Ведущего — шесть разных стингеров");

            // Каждый тон пула HostTone обязан во что-то отобразиться (включая поздний `debt`).
            foreach (HostTone t in System.Enum.GetValues(typeof(HostTone)))
                Assert.Contains(AudioCatalog.ForHostTone(t), tones, "тон Ведущего без стингера: " + t);
        }

        [Test]
        public void FinaleKinds_MapToThreeDistinctEnds()
        {
            Assert.AreEqual(SoundEvent.FinaleOldAge, FinaleSound.EventFor("весёлая старость"));
            Assert.AreEqual(SoundEvent.FinaleOldAge, FinaleSound.EventFor("одинокая старость"));
            Assert.AreEqual(SoundEvent.FinaleOldAge, FinaleSound.EventFor("спокойная старость"));
            Assert.AreEqual(SoundEvent.FinaleBurnout, FinaleSound.EventFor("полное выгорание"));
            // Обрыв пластинки достаётся ВСЕМУ резкому: фатальным карточкам, здоровью и «дичи».
            Assert.AreEqual(SoundEvent.FinaleFatal, FinaleSound.EventFor("здоровье не выдержало"));
            Assert.AreEqual(SoundEvent.FinaleFatal, FinaleSound.EventFor("вы сунули палец в розетку"));
            Assert.AreEqual(SoundEvent.FinaleFatal, FinaleSound.EventFor("неведомая дичь"));
            Assert.AreEqual(SoundEvent.FinaleFatal, FinaleSound.EventFor(null));
        }

        [Test]
        public void ResourceKeys_DropExtension_AndKeepGroupFolders()
        {
            var def = AudioCatalog.Get(SoundEvent.AnswerYes);
            Assert.AreEqual("Audio/02-cards/answer-yes--q3-kalimba", def.ResourceKey);
            foreach (var p in AudioCatalog.Map)
                Assert.IsFalse(p.Value.ResourceKey.EndsWith(".wav")
                            || p.Value.ResourceKey.EndsWith(".ogg")
                            || p.Value.ResourceKey.EndsWith(".mp3"),
                    "Resources-ключ обязан быть без расширения: " + p.Key);
        }
    }
}
