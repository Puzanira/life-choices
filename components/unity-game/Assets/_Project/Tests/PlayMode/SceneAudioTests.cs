using System.Collections;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ThanksNoThanks.Tests.PlayMode
{
    /// <summary>
    /// ГАРД СЛУШАТЕЛЯ. Найдено ЖИВЫМ ПЛЕЙТЕСТОМ 2026-08-08: весь звуковой слой был разведён,
    /// клипы грузились, тесты были зелёные — а игра молчала, потому что в сцене не оказалось
    /// <see cref="AudioListener"/>. Unity без слушателя не издаёт НИ ЗВУКА и даже не ругается.
    ///
    /// Это ровно тот класс дефекта, который headless не ловит: проигрывание «происходит», просто
    /// его некому услышать. Поэтому инвариант проверяется на ЗАГРУЖЕННОЙ СЦЕНЕ, а не на драйвере,
    /// собранном тестом: тест обязан падать, если слушателя снимут с камеры и сохранят сцену.
    /// </summary>
    public class SceneAudioTests
    {
        [UnityTest]
        public IEnumerator Scene_HasExactlyOneEnabledAudioListener()
        {
            SceneManager.LoadScene("ThanksNoThanks");
            yield return null;
            yield return null;

            var listeners = Object.FindObjectsByType<AudioListener>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            Assert.AreEqual(1, listeners.Length,
                "в сцене обязан быть РОВНО ОДИН AudioListener: ноль — игра молчит целиком (плейтест "
                + "2026-08-08), два и больше — Unity глушит лишние и сыплет предупреждениями");

            var listener = listeners[0];
            Assert.IsTrue(listener.enabled, "слушатель включён — выключенный молчит так же, как отсутствующий");
            Assert.IsTrue(listener.gameObject.activeInHierarchy, "объект слушателя жив в иерархии");
        }

        [UnityTest]
        public IEnumerator AudioListener_RidesTheCamera()
        {
            SceneManager.LoadScene("ThanksNoThanks");
            yield return null;
            yield return null;

            var listener = Object.FindAnyObjectByType<AudioListener>();
            Assert.IsNotNull(listener, "слушатель в сцене есть");
            Assert.IsNotNull(listener.GetComponent<Camera>(),
                "слушатель живёт на камере — канон Unity: уедет на случайный объект, и 2D-микс поедет");
        }

        /// <summary>
        /// Второй конец той же дыры: слой построился и клипы на месте — то есть «молчит» уже не может
        /// означать «нечего играть». Вместе с гардом слушателя это закрывает весь путь звука до колонок.
        /// </summary>
        [UnityTest]
        public IEnumerator Scene_Driver_BuildsTheAudioLayer_WithAllClips()
        {
            SceneManager.LoadScene("ThanksNoThanks");
            yield return null;
            yield return null;

            var driver = Object.FindAnyObjectByType<GameDriver>();
            Assert.IsNotNull(driver, "драйвер в сцене есть");
            Assert.IsNotNull(driver.Audio, "звуковой слой построен на боевой сцене, а не только в тестах");
            Assert.AreEqual(AudioCatalog.Map.Count, driver.Audio.LoadedClipCount,
                "на боевой сцене загружены все клипы манифеста");
        }
    }
}
