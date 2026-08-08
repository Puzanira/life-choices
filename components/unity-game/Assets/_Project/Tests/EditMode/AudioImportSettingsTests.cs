using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// Настройки импорта — часть поставки, а не «как Unity решит». Короткие SFX распакованы в память
    /// (ответ игрока не должен ждать декодера), лупы идут потоком. Правило одно и то же для
    /// постпроцессора и для этого теста — <see cref="AudioCatalog.StreamingFor"/>, поэтому .meta не
    /// могут молча разъехаться с манифестом.
    /// </summary>
    public class AudioImportSettingsTests
    {
        private const string Root = "Assets/_Project/Audio/Resources/Audio/";

        [Test]
        public void EveryClip_Imports_AsAnAudioClip()
        {
            foreach (var file in AudioCatalog.BuildFiles())
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + file);
                Assert.IsNotNull(clip, "файл не импортировался как AudioClip: " + file);
                Assert.Greater(clip.length, 0f, "нулевая длина клипа: " + file);
            }
        }

        [Test]
        public void LoadType_FollowsTheLoopRule()
        {
            foreach (var file in AudioCatalog.BuildFiles())
            {
                var importer = AssetImporter.GetAtPath(Root + file) as AudioImporter;
                Assert.IsNotNull(importer, "нет AudioImporter у " + file);

                bool streaming = AudioCatalog.StreamingFor(file);
                var expected = streaming ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
                Assert.AreEqual(expected, importer.defaultSampleSettings.loadType,
                    "режим загрузки не по правилу (" + (streaming ? "луп" : "короткий SFX") + "): " + file);
            }
        }

        [Test]
        public void ClipsAreMono_AsTheDesignSessionPreparedThem()
        {
            // Все файлы приведены дизайн-сессией к моно 44.1 кГц; импорт это фиксирует, чтобы
            // докачанный основательницей стерео-оригинал не поехал по панораме.
            foreach (var file in AudioCatalog.BuildFiles())
            {
                var importer = AssetImporter.GetAtPath(Root + file) as AudioImporter;
                Assert.IsNotNull(importer, "нет AudioImporter у " + file);
                Assert.IsTrue(importer.forceToMono, "клип обязан импортироваться в моно: " + file);
            }
        }

        [Test]
        public void EveryClip_LoadsThroughResources_ByItsCatalogKey()
        {
            // Ровно тот путь, которым слой грузит клип в бою: разъедется ключ — здесь и всплывёт.
            foreach (var pair in AudioCatalog.Map)
            {
                var clip = Resources.Load<AudioClip>(pair.Value.ResourceKey);
                Assert.IsNotNull(clip, "Resources.Load не нашёл клип события " + pair.Key
                    + " по ключу " + pair.Value.ResourceKey);
            }
        }
    }
}
