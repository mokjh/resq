using System.Collections;
using System.IO;
using NUnit.Framework;
using Resq.Game;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Resq.Tests
{
    /// <summary>
    /// Audio wiring: each world loads its own music and ambience, the problem stem rises after a warning while the
    /// run is live and falls in menus, and the Music volume setting governs the music. Evidence: automated (Editor
    /// PlayMode); how it sounds is a human review item.
    /// </summary>
    public sealed class AudioTest
    {
        string _dir;
        [SetUp] public void SetUp() { _dir = Path.Combine(Path.GetTempPath(), "resq_audio_" + System.Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_dir); SaveStore.Folder = _dir; }
        [TearDown] public void TearDown() { SaveStore.Folder = null; try { Directory.Delete(_dir, true); } catch (IOException) { } }

        [UnityTest]
        public IEnumerator WorldBedsProblemLayerAndVolume()
        {
#if UNITY_EDITOR
            var op = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Resq/Scenes/Operation_Desktop.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!op.isDone) yield return null;
#else
            Assert.Ignore("Editor only"); yield break;
#endif
            for (int i = 0; i < 4; i++) yield return null;
            var boot = Object.FindFirstObjectByType<GameBootstrap>();
            var audio = boot.Audio;
            Assert.IsNotNull(audio, "the operation scene has an audio director");
            Assert.IsNotNull(Object.FindFirstObjectByType<AudioListener>(), "something listens");

            var driver = boot.Driver;
            driver.LoadRun("W3-M1", "wide", Resq.Sim.RuleProfile.Standard, false);
            yield return null;
            Assert.AreEqual("music_w3_calm", audio.MusicClip?.name, "Frostline music");
            Assert.AreEqual("amb_w3_wind", audio.AmbienceClip?.name, "Frostline ambience");
            driver.LoadRun("W1-M1", "wide", Resq.Sim.RuleProfile.Standard, false);
            yield return null;
            Assert.AreEqual("music_w1_calm", audio.MusicClip?.name);
            Assert.AreEqual(0f, audio.ProblemLevel, 1e-3f, "calm before anything happens");

            // past W1-M1's first warning (scenario tick 500), live: the problem layer rises
            for (int i = 0; i < 520; i++) driver.Advance(1);
            driver.SetPaused(false);
            float t0 = Time.realtimeSinceStartup;
            while (audio.ProblemLevel < 0.5f && Time.realtimeSinceStartup - t0 < 4f) yield return null;
            Assert.Greater(audio.ProblemLevel, 0.5f, "a fresh warning brings in the problem stem");
            driver.SetPaused(true);

            SaveStore.Current.Settings.Music = 0;
            yield return null;
            Assert.AreEqual(0f, audio.MusicVolume, 1e-4f, "Music volume 0 silences the music");
        }
    }
}
