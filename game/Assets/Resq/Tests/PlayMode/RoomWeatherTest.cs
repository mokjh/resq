using System.Collections;
using System.IO;
using NUnit.Framework;
using Resq.Game;
using Resq.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Resq.Tests
{
    /// <summary>
    /// Room weather (BUILD_PLAN P4.2): each world has its own weather, it starts at the real ceiling when one is known,
    /// ignores an impossible ceiling, clears once everyone is safe, and reduced effects removes the flashes. How it looks
    /// on passthrough is a device check for P5. Evidence: automated (Editor PlayMode).
    /// </summary>
    public sealed class RoomWeatherTest
    {
        string _dir;
        [SetUp] public void SetUp() { _dir = Path.Combine(Path.GetTempPath(), "resq_weather_" + System.Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_dir); SaveStore.Folder = _dir; }
        [TearDown] public void TearDown() { RoomWeather.CeilingWorldY = float.NaN; Comfort.ReducedEffects = false; SaveStore.Folder = null; try { Directory.Delete(_dir, true); } catch (IOException) { } }

        [UnityTest]
        public IEnumerator WeatherFollowsWorldCeilingAndOutcome()
        {
#if UNITY_EDITOR
            var op = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Resq/Scenes/Operation_Desktop.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!op.isDone) yield return null;
#else
            Assert.Ignore("Editor only"); yield break;
#endif
            for (int i = 0; i < 4; i++) yield return null;
            var boot = Object.FindFirstObjectByType<GameBootstrap>();
            var driver = boot.Driver;
            var weather = boot.Weather;
            Assert.IsNotNull(weather, "the operation has room weather");

            RoomWeather.CeilingWorldY = float.NaN;
            driver.LoadRun("W1-M1", "wide", RuleProfile.Standard, false);
            boot.Interaction.MenuOnly = false;
            yield return null;
            Assert.AreEqual(RoomWeather.Fall.Rain, weather.Kind, "Alder Valley rains");
            Assert.AreEqual(RoomWeather.AssumedCeiling, weather.CeilingAboveBoard, 1e-3f, "no room data: the assumed ceiling");

            // the real ceiling, 1.9 m above the board: the rain starts there
            RoomWeather.CeilingWorldY = driver.transform.position.y + 1.9f;
            driver.LoadRun("W1-M1", "wide", RuleProfile.Standard, false);
            yield return null;
            Assert.AreEqual(1.9f, weather.CeilingAboveBoard, 1e-3f, "rain starts at the real ceiling");
            Assert.IsTrue(weather.CloudVisible || weather.Level <= 0.05f, "a storm cloud gathers under the ceiling");
            float t0 = Time.realtimeSinceStartup;
            while (weather.Particles.particleCount == 0 && Time.realtimeSinceStartup - t0 < 5f) yield return null;
            Assert.Greater(weather.Particles.particleCount, 0, "it is raining");
            Assert.Greater(weather.Level, 0f);
            Assert.Greater(RoomWeather.RoomWeight, 0f, "the room takes the weather's colour");

            // a nonsense reading (a ceiling below the board, or far away) must not change anything
            RoomWeather.CeilingWorldY = driver.transform.position.y - 0.5f;
            driver.LoadRun("W1-M1", "wide", RuleProfile.Standard, false);
            yield return null;
            Assert.AreEqual(RoomWeather.AssumedCeiling, weather.CeilingAboveBoard, 1e-3f, "an impossible ceiling is ignored");
            RoomWeather.CeilingWorldY = float.NaN;

            // each world's own weather, and sparks fly up
            driver.LoadRun("W3-M1", "wide", RuleProfile.Standard, false);
            yield return null;
            Assert.AreEqual(RoomWeather.Fall.Snow, weather.Kind, "Frostline snows");
            Assert.IsFalse(weather.Strike(), "no lightning in the snow");
            driver.LoadRun("W2-M1", "wide", RuleProfile.Standard, false);
            yield return null;
            Assert.AreEqual(RoomWeather.Fall.Embers, weather.Kind, "Emberwood sparks");

            // lightning flashes the room, thunder follows; reduced effects has neither
            driver.LoadRun("W1-M1", "wide", RuleProfile.Standard, false);
            yield return null;
            bool thunder = false;
            System.Action heard = () => thunder = true;
            RoomWeather.Thunder += heard;
            try
            {
                Assert.IsTrue(weather.Strike());
                yield return null;
                Assert.Greater(weather.Flash, 0f, "the strike flashes");
                float t1 = Time.realtimeSinceStartup;
                while (!thunder && Time.realtimeSinceStartup - t1 < 3f) yield return null;
                Assert.IsTrue(thunder, "thunder follows the flash");
            }
            finally { RoomWeather.Thunder -= heard; }
            float sun = RenderSettings.sun.intensity;
            Assert.LessOrEqual(sun, WorldLighting.For("W1").Intensity * 2.5f);

            Comfort.ReducedEffects = true;
            Assert.IsFalse(weather.Strike(), "reduced effects: no lightning");
            yield return null;
            Assert.AreEqual(0f, weather.Flash, 1e-4f, "reduced effects: no flash");
            float t2 = Time.realtimeSinceStartup;
            while (weather.Level > 0.5f && Time.realtimeSinceStartup - t2 < 4f) yield return null;
            Assert.LessOrEqual(weather.Level, 0.5f, "reduced effects: less weather");
            Comfort.ReducedEffects = false;

            // everyone safe: the storm passes and the room clears
            driver.StartAutopilot("A");
            for (int guard = 0; guard < 400 && !driver.Sim.IsOver; guard++) driver.Advance(50);
            Assert.IsTrue(driver.Sim.IsOver && driver.Sim.S.C.Success, "the reference plan wins");
            float t3 = Time.realtimeSinceStartup;
            while ((weather.Level > 0f || RoomWeather.RoomWeight > 0f) && Time.realtimeSinceStartup - t3 < 5f) yield return null;
            Assert.AreEqual(0f, weather.Level, 1e-3f, "the storm has passed");
            Assert.AreEqual(0f, RoomWeather.RoomWeight, 1e-3f, "the room is itself again");
        }
    }
}
