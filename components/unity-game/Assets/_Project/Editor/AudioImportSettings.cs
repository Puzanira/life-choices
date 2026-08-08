using UnityEditor;
using UnityEngine;

namespace ThanksNoThanks.EditorTools
{
    /// <summary>
    /// Настройки импорта звука — КОДОМ, а не руками в инспекторе. Файлы кладутся в
    /// <c>Assets/_Project/Audio/Resources/Audio/…</c> группами манифеста, и любой заново
    /// импортированный (или докачанный основательницей в оригинале) файл автоматически получает
    /// правильный режим загрузки: короткие SFX распаковываются в память, лупы идут потоком.
    ///
    /// Правило намеренно живёт в рантайме (<see cref="AudioCatalog.StreamingFor"/>) — так его
    /// проверяет EditMode-тест, и .meta не могут молча разъехаться с манифестом.
    /// </summary>
    public sealed class AudioImportSettings : AssetPostprocessor
    {
        public const string Root = "Assets/_Project/Audio/Resources/Audio/";

        private void OnPreprocessAudio()
        {
            if (assetPath == null || !assetPath.StartsWith(Root)) return;
            if (assetImporter is not AudioImporter importer) return;

            string file = assetPath.Substring(Root.Length);   // «02-cards/answer-yes--q3-kalimba.wav»
            bool streaming = AudioCatalog.StreamingFor(file);

            var s = importer.defaultSampleSettings;
            s.loadType = streaming ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            // Лупы (тема 30 с + две трёхсекундные подкладки) жмутся Vorbis'ом, короткие события идут
            // PCM'ом: их суммарный вес мал, а распаковка на лету на каждый ответ игрока не нужна.
            s.compressionFormat = streaming ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.PCM;
            importer.defaultSampleSettings = s;

            // Все файлы манифеста приведены дизайн-сессией к МОНО 44.1 кГц — фиксируем это импортом,
            // чтобы докачанный стерео-оригинал не поехал по панораме.
            importer.forceToMono = true;
            importer.loadInBackground = streaming;
        }
    }
}
