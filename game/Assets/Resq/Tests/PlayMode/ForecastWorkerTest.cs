using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Resq.Game;
using Resq.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Resq.Tests
{
    /// <summary>
    /// The forecast preview runs on a worker thread: hovering an armed tool over a legal socket paints the
    /// difference a frame or more later, the live run is untouched, and the result matches the synchronous path
    /// exactly (the simulator is deterministic). Evidence: automated (Editor PlayMode).
    /// </summary>
    public sealed class ForecastWorkerTest
    {
        sealed class IdlePointer : IPointerSource
        {
            public PointerSample Sample() => new PointerSample { Kind = PointerKind.Desktop, Valid = true, Ray = new Ray(new Vector3(0, 10, 0), Vector3.up) };
            public void Feedback(Pickable hover, float progress) { }
        }

        string _dir;
        [SetUp] public void SetUp() { _dir = Path.Combine(Path.GetTempPath(), "resq_fc_" + System.Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_dir); SaveStore.Folder = _dir; }
        [TearDown] public void TearDown() { SaveStore.Folder = null; ForecastView.Synchronous = false; try { Directory.Delete(_dir, true); } catch (IOException) { } }

        [UnityTest]
        public IEnumerator WorkerForecastMatchesSynchronousAndLeavesLiveRunAlone()
        {
#if UNITY_EDITOR
            var op = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Resq/Scenes/Operation_Desktop.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!op.isDone) yield return null;
#else
            Assert.Ignore("Editor only"); yield break;
#endif
            for (int i = 0; i < 4; i++) yield return null;
            var boot = Object.FindFirstObjectByType<GameBootstrap>();
            var driver = boot.Driver; var input = boot.Interaction;
            driver.LoadRun("W1-M1", "wide", RuleProfile.Standard, false);
            for (int i = 0; i < 3; i++) yield return null;
            input.ClearSources();
            input.AddSource(new IdlePointer());   // hover only updates while some pointer is active; this one never presses
            var forecast = boot.Board.Forecast;
            Assert.IsNotNull(forecast);

            input.DebugArm(ToolKind.Barrier);
            yield return null;
            var socket = Object.FindObjectsByType<Pickable>(FindObjectsSortMode.None)
                               .FirstOrDefault(p => p.Kind == PickKind.Socket && input.Evaluate(p) == Reject.OK);
            Assert.IsNotNull(socket, "a legal barrier socket");
            ulong liveHash = driver.Sim.S.Hash();

            ForecastView.Synchronous = false;
            input.DebugHover = socket;
            float t0 = Time.realtimeSinceStartup;
            while (!forecast.Showing && Time.realtimeSinceStartup - t0 < 5f) yield return null;
            Assert.IsTrue(forecast.Showing, "the worker forecast finished and painted");
            string workerSummary = forecast.Summary;
            Assert.IsNotEmpty(workerSummary);
            Assert.AreEqual(liveHash, driver.Sim.S.Hash(), "forecasting never touches the live run");

            // the same forecast on the main thread gives the same answer
            forecast.Preview(socket.Index, ToolKind.Barrier, pin: true);
            Assert.AreEqual(workerSummary, forecast.Summary, "worker and synchronous forecasts agree");
            input.DebugHover = null;
            input.Cancel();
        }
    }
}
