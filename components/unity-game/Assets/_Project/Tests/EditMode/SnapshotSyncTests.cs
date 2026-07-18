using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace ThanksNoThanks.Tests
{
    /// <summary>
    /// Drift guard: the runtime snapshot at Assets/_Project/Resources/scenes.csv MUST be a byte-for-byte
    /// (line-ending-normalized) copy of the canonical deck at docs/new_concept/scenes.csv. If the design
    /// agent edits the canon and the snapshot isn't refreshed (or vice-versa), this test goes red instead
    /// of the two silently diverging.
    /// </summary>
    public class SnapshotSyncTests
    {
        private static string Normalize(string s) =>
            s.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd('\n');

        private static string CanonPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "docs", "new_concept", "scenes.csv"));

        private static string SnapshotPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "_Project", "Resources", "scenes.csv"));

        [Test]
        public void ResourcesSnapshot_EqualsCanon_ByteForByte_LineEndingNormalized()
        {
            Assert.IsTrue(File.Exists(CanonPath), $"canon deck present at {CanonPath}");
            Assert.IsTrue(File.Exists(SnapshotPath), $"runtime snapshot present at {SnapshotPath}");

            string canon = Normalize(File.ReadAllText(CanonPath));
            string snapshot = Normalize(File.ReadAllText(SnapshotPath));

            Assert.AreEqual(canon, snapshot,
                "Resources/scenes.csv must equal docs/new_concept/scenes.csv — refresh the snapshot after canon edits.");
        }

        [Test]
        public void Snapshot_LoadedByResources_MatchesDiskContent()
        {
            // The copy Unity actually serves (Resources.Load) is the same bytes we guard on disk.
            var asset = Resources.Load<TextAsset>("scenes");
            Assert.IsNotNull(asset, "Resources/scenes present");
            Assert.AreEqual(Normalize(File.ReadAllText(SnapshotPath)), Normalize(asset.text),
                "TextAsset content matches the on-disk snapshot");
        }
    }
}
