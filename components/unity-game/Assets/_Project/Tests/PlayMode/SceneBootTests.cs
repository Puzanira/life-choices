using System.Collections;
using NUnit.Framework;
using ThanksNoThanks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ThanksNoThanks.Tests.PlayMode
{
    public class SceneBootTests
    {
        [UnityTest]
        public IEnumerator Scene_Loads_And_Boots_In_Opener()
        {
            SceneManager.LoadScene("ThanksNoThanks");
            yield return null;
            yield return null;

            var driver = Object.FindAnyObjectByType<GameDriver>();
            Assert.IsNotNull(driver, "GameDriver present in the build scene");
            Assert.IsNotNull(driver.Game, "game constructed on boot");
            Assert.AreEqual(GameState.Opener, driver.Game.State,
                "scene boots into the opener (age timer not running yet)");

            var camera = Object.FindAnyObjectByType<Camera>();
            Assert.IsNotNull(camera, "scene has a camera");
        }
    }
}
