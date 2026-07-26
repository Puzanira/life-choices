using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// Arcade-integration contract guards (increment «arcade-packaging»): the game declares itself as a
    /// self-contained arcade package (package.json + game.json) and reads input ONLY through the shared
    /// arcade-controls layer — no raw device access anywhere in the game scripts (contract §2/§4 scan).
    /// </summary>
    public class ArcadePackagingTests
    {
        private static string UnityProjectRoot => Path.GetDirectoryName(Application.dataPath);      // .../unity-game
        private static string GameFolder => Path.Combine(Application.dataPath, "_Project");
        private static string ScriptsFolder => Path.Combine(GameFolder, "Scripts");

        [System.Serializable]
        private class GameManifest
        {
            public string id;
            public string name;
            public string displayName;
            public string version;
            public string entry;
            public string[] controls;
        }

        [Test]
        public void GameJson_Exists_Declares_Entry_And_Controls()
        {
            var path = Path.Combine(GameFolder, "game.json");
            Assert.IsTrue(File.Exists(path), "game.json must live in the game root (Assets/_Project)");

            var m = JsonUtility.FromJson<GameManifest>(File.ReadAllText(path));
            Assert.IsFalse(string.IsNullOrEmpty(m.id), "game.json declares an id");
            Assert.IsFalse(string.IsNullOrEmpty(m.version), "game.json declares a version");

            Assert.IsFalse(string.IsNullOrEmpty(m.entry), "game.json declares an entry scene");
            var entryScene = Path.Combine(UnityProjectRoot, m.entry);
            Assert.IsTrue(File.Exists(entryScene), $"the declared entry scene exists on disk: {m.entry}");

            Assert.IsNotNull(m.controls, "game.json lists the controls it uses");
            Assert.Greater(m.controls.Length, 0, "at least one control declared");
            // Founder Gate-2 core mapping: ДА / НЕТ / выход.
            CollectionAssert.Contains(m.controls, "GreenButton");
            CollectionAssert.Contains(m.controls, "RedButton");
            CollectionAssert.Contains(m.controls, "MenuButton");
        }

        [Test]
        public void PackageJson_Exists_As_A_Named_Upm_Package_Depending_On_ArcadeControls()
        {
            var path = Path.Combine(GameFolder, "package.json");
            Assert.IsTrue(File.Exists(path), "package.json must live in the game root (Assets/_Project)");
            var txt = File.ReadAllText(path);
            StringAssert.Contains("com.aigamestudio.game-life-choices", txt, "UPM package name declared");
            StringAssert.Contains("com.aigamestudio.arcade-controls", txt, "declares its arcade-controls dependency");
        }

        [Test]
        public void GameScripts_Never_Read_Raw_Input()
        {
            Assert.IsTrue(Directory.Exists(ScriptsFolder), "game Scripts folder exists");
            foreach (var file in Directory.GetFiles(ScriptsFolder, "*.cs", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                var name = Path.GetFileName(file);
                StringAssert.DoesNotContain("UnityEngine.InputSystem", text,
                    $"{name}: game code must not reference the Input System directly (read ArcadeInput instead)");
                StringAssert.DoesNotContain("Keyboard.current", text,
                    $"{name}: game code must not poll Keyboard.current (raw input lives behind arcade-controls)");
                StringAssert.DoesNotContain("Mouse.current", text,
                    $"{name}: game code must not poll Mouse.current (raw input lives behind arcade-controls)");
                StringAssert.DoesNotContain("Application.Quit(", text,
                    $"{name}: an arcade game must not CALL Application.Quit (contract §5)");
            }
        }
    }
}
