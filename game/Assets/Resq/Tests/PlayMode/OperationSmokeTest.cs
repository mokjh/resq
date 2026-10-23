using System.Collections;
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
    /// PlayMode smoke gate (BUILD_PLAN "real gates"): boot the Operation scene, check the board and art came up, then
    /// win W1-M1 with strategy A through the input → command layer. Every action is a real pointer press: a scripted
    /// pointer source casts rays at the board's pickable targets exactly as the mouse, hand and head-gaze adapters do,
    /// and BoardInteraction turns the presses into validated commands. Nothing calls Submit directly and no debug
    /// hover is used. Time advances with the game's own command-mode stepping (Advance), so the run is deterministic.
    /// Any error or exception logged during the run fails the test (Unity Test Framework default).
    /// Evidence kind: automated (Editor PlayMode).
    /// </summary>
    public sealed class OperationSmokeTest
    {
        const string ScenePath = "Assets/Resq/Scenes/Operation_Desktop.unity";

        /// <summary>Pointer adapter for tests: aims at a world point and holds the press for a set number of frames.</summary>
        sealed class ScriptedPointer : IPointerSource
        {
            public Ray Ray;
            public bool Valid, Pressed;
            public PointerSample Sample() => new PointerSample { Kind = PointerKind.Desktop, Valid = Valid, Ray = Ray, Pressed = Pressed };
            public void Feedback(Pickable hover, float progress) { }
        }

        GameBootstrap _boot;
        SimDriver _driver;
        BoardInteraction _input;
        ScriptedPointer _pointer;
        string _lastPress = "";

        [UnityTest]
        public IEnumerator BootPlaceAndWinFirstCrossing()
        {
            yield return LoadOperationScene();
            _boot = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(_boot, "GameBootstrap missing from the Operation scene");
            _driver = _boot.Driver;
            _input = _boot.Interaction;
            Assert.IsNotNull(_driver, "SimDriver missing");
            Assert.IsNotNull(_input, "BoardInteraction missing");

            // a clean, non-guided run of First Crossing (wide layout, Standard rules)
            _driver.LoadRun("W1-M1", "wide", RuleProfile.Standard, false);
            yield return null;
            yield return null;
            Assert.IsTrue(_driver.Paused, "a new run starts paused for planning");

            AssertBoardAndArt();

            // the scripted pointer is the only input source
            _input.ClearSources();
            _pointer = new ScriptedPointer();
            _input.AddSource(_pointer);

            var d = _driver.Def;
            var S = _driver.Sim.S;

            // Start (the planning pause ends through the Start control), then pause again: time below runs in
            // command-mode steps so the strategy's tick timing is exact
            yield return Press(Find(PickKind.Button, label: "Start"));
            Assert.IsFalse(_driver.Paused, "Start control should unpause the run");
            yield return Press(Find(PickKind.Button, label: "Start"));   // the control now reads "Pause"
            Assert.IsTrue(_driver.Paused, "the same control pauses again");
            long t0 = S.C.O;

            // strategy A (content/w1/m1/strategies/A.json), operation ticks from the run start
            AdvanceTo(t0 + 5);
            yield return Place(ToolKind.Bridge, "bridge:01");
            AdvanceTo(t0 + 6);
            yield return RouteVehicle(VehicleOf(VehicleKind.FixedBus), "bus", "depot");
            AdvanceTo(t0 + 60);
            yield return Place(ToolKind.Barrier, "barrier:01");
            AdvanceTo(t0 + 80);
            yield return Place(ToolKind.Skiff, "skiff:01");
            AdvanceTo(t0 + 81);
            yield return RouteVehicle(VehicleOf(VehicleKind.Skiff), "low_dock", "high_landing");

            // let the board render the live run for a moment (visual LateUpdates over a moving simulation)
            _driver.SetPaused(false);
            for (int i = 0; i < 30; i++) yield return null;
            _driver.SetPaused(true);

            for (int guard = 0; guard < 400 && !_driver.Sim.IsOver; guard++) _driver.Advance(50);
            Assert.IsTrue(_driver.Sim.IsOver, "the run should reach its outcome");
            Assert.IsTrue(S.C.Success, $"W1-M1 strategy A should win (evacuated {S.C.Evacuated}/{d.TotalResidents}, losses {S.C.Losses})");
            Assert.AreEqual(3, S.C.Stars, "strategy A is a validated 3-star plan on wide/Standard");
            Assert.AreEqual(d.TotalResidents, S.C.Evacuated, "every resident evacuated");
            yield return null;
        }

        // ------------------------------------------------------------------ board + art checks
        void AssertBoardAndArt()
        {
            var board = _boot.Board;
            Assert.IsNotNull(board, "BoardView missing");
            var terrain = GameObject.Find("Terrain");
            Assert.IsNotNull(terrain, "terrain not built");
            Assert.Greater(terrain.GetComponent<MeshFilter>().sharedMesh.vertexCount, 1000, "terrain heightfield too small");
            Assert.IsNotNull(GameObject.Find("Frame"), "wooden frame missing");
            Assert.IsNotNull(board.Scatter, "scatter view missing");
            Assert.Greater(board.Scatter.InstanceCount, 100, "world dressing (nature kit scatter) missing");

            var actors = board.Actors.transform;
            int homes = 0, landmarks = 0, crew = 0, residents = 0, rigged = 0;
            foreach (Transform t in actors)
            {
                if (t.name.StartsWith("Home_")) homes++;
                else if (t.name.StartsWith("Landmark_")) landmarks++;
                else if (t.name.StartsWith("Crew_")) crew++;
                else if (t.name.StartsWith("Resident_"))
                {
                    residents++;
                    if (t.GetComponentInChildren<Animator>() != null && t.GetComponentInChildren<SkinnedMeshRenderer>() != null) rigged++;
                }
            }
            Assert.AreEqual(_driver.Def.ResidentIds.Length, residents, "one figure per resident");
            Assert.AreEqual(residents, rigged, "every resident uses the rigged kit");
            Assert.Greater(homes, 0, "hero homes missing");
            Assert.Greater(landmarks, 0, "scenic landmark (church) missing");
            Assert.Greater(crew, 0, "rescue crew at landings missing");

            var ring = GameObject.Find("WorldRing");
            Assert.IsNotNull(ring, "world ring missing");
            Assert.AreEqual(7, ring.transform.Cast<Transform>().Count(c => c.name.StartsWith("Backdrop_")), "backdrop tiles: 3 far + 2 per side");

            var sun = RenderSettings.sun;
            Assert.IsNotNull(sun, "no sun");
            Assert.Less(sun.transform.eulerAngles.x, 35f, "golden-hour sun should be low");
        }

        // ------------------------------------------------------------------ input helpers
        IEnumerator LoadOperationScene()
        {
#if UNITY_EDITOR
            var op = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            while (!op.isDone) yield return null;
#else
            Assert.Ignore("the smoke gate runs in the Editor");
#endif
            for (int i = 0; i < 5; i++) yield return null;
        }

        void AdvanceTo(long tick)
        {
            var S = _driver.Sim.S;
            while (S.C.O < tick && !_driver.Sim.IsOver) _driver.Advance(1);
            Assert.AreEqual(tick, S.C.O, "operation clock");
        }

        IEnumerator Place(ToolKind tool, string socketId)
        {
            yield return null;   // the board refreshes its handles once per rendered frame, as a player would see them
            int before = _driver.Sim.S.C.EquipmentCount;
            yield return Press(Find(PickKind.Tool, index: (int)tool));
            Assert.AreEqual(InteractMode.ToolArmed, _input.Mode, $"{tool} card should arm the tool; " + _lastPress);
            yield return Press(Find(PickKind.Socket, index: _driver.Def.SocketIndex[socketId]));
            _driver.Advance(1); // commands apply on the next step
            Assert.AreEqual(before + 1, _driver.Sim.S.C.EquipmentCount, $"{tool} should be placed at {socketId} ({_input.Hint})");
        }

        IEnumerator RouteVehicle(int vehicle, string groupId, string toNodeId)
        {
            yield return null;   // a vehicle placed this step gets its handle on the next rendered frame
            var S0 = _driver.Sim.S;
            var kinds = string.Join(",", Enumerable.Range(0, S0.C.VehicleCount).Select(v => ((VehicleKind)S0.VKind[v]).ToString()));
            var handles = string.Join(",", Object.FindObjectsByType<Pickable>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(x => x.Kind == PickKind.Vehicle).Select(x => x.Index + (x.gameObject.activeInHierarchy ? "" : "(off)")));
            Assert.IsTrue(Object.FindObjectsByType<Pickable>(FindObjectsSortMode.None).Any(x => x.Kind == PickKind.Vehicle && x.Index == vehicle),
                          $"vehicle #{vehicle} has no handle: vehicles [{kinds}] handles [{handles}]");
            yield return Press(Find(PickKind.Vehicle, index: vehicle));
            Assert.AreEqual(InteractMode.VehiclePickup, _input.Mode, "vehicle press selects it; " + _lastPress);
            yield return Press(Find(PickKind.Group, index: _driver.Def.GroupIndex[groupId]));
            Assert.AreEqual(InteractMode.VehicleDestination, _input.Mode, "group press picks who to collect; " + _lastPress);
            yield return Press(Find(PickKind.Node, index: _driver.Def.NodeIndex[toNodeId]));
            Assert.AreEqual(InteractMode.Idle, _input.Mode, "destination press issues the route; " + _lastPress);
            _driver.Advance(1);
            Assert.AreNotEqual((byte)VehiclePhase.None, _driver.Sim.S.VPhase[vehicle], $"vehicle {vehicle} should be on its way to {groupId}");
        }

        int VehicleOf(VehicleKind kind)
        {
            var S = _driver.Sim.S;
            for (int v = S.C.VehicleCount - 1; v >= 0; v--) if ((VehicleKind)S.VKind[v] == kind) return v;
            Assert.Fail($"no {kind} vehicle on the board");
            return -1;
        }

        Pickable Find(PickKind kind, int index = -1, string label = null)
        {
            var all = Object.FindObjectsByType<Pickable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var p = all.FirstOrDefault(x => x.Kind == kind && (index < 0 || x.Index == index) && (label == null || x.Label == label)
                                            && x.GetComponentInChildren<Collider>() != null);
            Assert.IsNotNull(p, $"no visible {kind} target (index {index}, label {label})");
            return p;
        }

        /// <summary>
        /// Aims the scripted pointer so BoardInteraction's own pick resolves to <paramref name="target"/>: from the
        /// main camera if nothing is in the way, else straight down onto it.
        /// </summary>
        void Aim(Pickable target, Collider col)
        {
            Physics.SyncTransforms();
            var c = col.bounds.center;
            var cam = Camera.main;
            foreach (var origin in new[] { cam.transform.position, c + Vector3.up * 0.4f, c + (cam.transform.position - c).normalized * 0.3f })
            {
                var ray = new Ray(origin, (c - origin).normalized);
                if (Physics.SphereCast(ray, 0.004f, out var hit, 3f) && hit.collider.GetComponentInParent<Pickable>() == target) { _pointer.Ray = ray; return; }
            }
            Assert.Fail($"{target.Kind} '{target.Label}' #{target.Index} can't be reached by a pointer ray (occluded)");
        }

        /// <summary>Hover, press for one frame, release: the same gesture a click, pinch or dwell produces.</summary>
        IEnumerator Press(Pickable target)
        {
            var col = target.GetComponentInChildren<Collider>();
            // a player aims at where they see the target: wait until it has settled (new handles snap to their
            // vehicle on their first frames), then aim, and re-aim on the frame before pressing
            var last = col.bounds.center;
            for (int i = 0; i < 20; i++)
            {
                yield return null;
                if ((col.bounds.center - last).sqrMagnitude < 1e-8f) break;
                last = col.bounds.center;
            }
            Aim(target, col);
            _pointer.Valid = true;
            _pointer.Pressed = false;
            yield return null;                      // hover
            Aim(target, col);
            Assert.AreEqual(target, _input.Hover, $"pointer should hover {target.Kind} #{target.Index}");
            _pointer.Pressed = true;
            yield return null;                      // press
            _lastPress = $"pressed {target.Kind} #{target.Index}: hover at press = {(_input.Hover ? _input.Hover.Kind + " #" + _input.Hover.Index : "none")}, mode {_input.Mode}, frame {Time.frameCount}";
            _pointer.Pressed = false;
            yield return null;                      // release
            _pointer.Valid = false;
            yield return null;
        }
    }
}
