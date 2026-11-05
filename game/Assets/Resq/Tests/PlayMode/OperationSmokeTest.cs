using System.Collections;
using System.Collections.Generic;
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
    /// PlayMode smoke gate (BUILD_PLAN "real gates"). For each world's first mission: boot the Operation scene, check
    /// the board and art came up, check the render budget at a mid-run moment, then win with the mission's validated
    /// strategy A, played entirely through the input → command layer. Every action is a real pointer gesture at the
    /// board's pickable targets; BoardInteraction turns it into a validated command. Nothing calls Submit and no
    /// debug hover is used.
    ///   • Desktop path: a scripted pointer source (ray + press), the same contract as mouse and hand rays.
    ///   • Command path: the game's own HeadDwellPointer fed by a test-driven head transform; selections fire only
    ///     after the real 0.8 s dwell.
    /// Time advances with the game's command-mode stepping (Advance), so each step lands on its strategy tick. Any
    /// error or exception logged during a run fails it (Unity Test Framework default). Evidence: automated (Editor
    /// PlayMode).
    /// </summary>
    public sealed class OperationSmokeTest
    {
        const string ScenePath = "Assets/Resq/Scenes/Operation_Desktop.unity";
        const int BudgetDraws = 150, BudgetTris = 300_000, PerfTick = 300;

        public static IEnumerable<string> WorldFirstMissions => new[] { "W1-M1", "W2-M1", "W3-M1", "W4-M1", "W5-M1" };
        public static IEnumerable<string> AllMissions
        {
            get
            {
                for (int w = 1; w <= 5; w++)
                    for (int m = 1; m <= 5; m++) yield return $"W{w}-M{m}";
            }
        }

        enum Path { Desktop, HeadDwell }

        /// <summary>Pointer adapter for tests: aims along a ray and holds the press for one frame.</summary>
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
        Path _path;
        ScriptedPointer _pointer;
        Transform _head;
        string _lastPress = "";
        readonly List<System.Action> _checks = new List<System.Action>();   // verified after the tick's step applies

        // the game flow records finished runs: keep them out of the player's real save
        string _saveDir;
        [SetUp] public void TempSave() { _saveDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "resq_smoke_" + System.Guid.NewGuid().ToString("N")); SaveStore.Folder = _saveDir; }
        [TearDown]
        public void RestoreSave()
        {
            Comfort.LeftHanded = false; Comfort.UiScale = 1f;
            SaveStore.Folder = null;
            try { System.IO.Directory.Delete(_saveDir, true); } catch (System.IO.IOException) { }
        }

        [UnityTest]
        public IEnumerator WinsWorldFirstMission([ValueSource(nameof(WorldFirstMissions))] string mission)
        {
            yield return Play(mission, RuleProfile.Standard, Path.Desktop, checkPerf: true);
        }

        [UnityTest]
        public IEnumerator CommandPathHeadDwellWinsFirstCrossing()
        {
            yield return Play("W1-M1", RuleProfile.Command, Path.HeadDwell, checkPerf: false);
        }

        // ------------------------------------------------------------------ the full input matrix (tools/unity/matrix.sh)
        // AGENTS.md: hands-only, one-hand and head-gaze Command paths must each complete every mission. Explicit, so the
        // everyday gate stays short. One pointer only in every case: what one hand can do, two can.

        /// <summary>A hand ray: wide board, strategy A.</summary>
        [UnityTest, Explicit, Category("Matrix"), Timeout(900000)]
        public IEnumerator MatrixHandRay([ValueSource(nameof(AllMissions))] string mission)
        {
            yield return Play(mission, RuleProfile.Standard, Path.Desktop, false);
        }

        /// <summary>One hand, left-handed mirrored controls and the largest text: compact board, strategy B.</summary>
        [UnityTest, Explicit, Category("Matrix"), Timeout(900000)]
        public IEnumerator MatrixOneHandMirroredLargeText([ValueSource(nameof(AllMissions))] string mission)
        {
            Comfort.LeftHanded = true; Comfort.UiScale = 1.5f;
            yield return Play(mission, RuleProfile.Standard, Path.Desktop, false, "compact", "B");
        }

        /// <summary>Head gaze only, under Command rules, with the real dwell time: wide board, strategy A.</summary>
        [UnityTest, Explicit, Category("Matrix"), Timeout(900000)]
        public IEnumerator MatrixHeadDwell([ValueSource(nameof(AllMissions))] string mission)
        {
            yield return Play(mission, RuleProfile.Command, Path.HeadDwell, false);
        }

        // ------------------------------------------------------------------ one run
        IEnumerator Play(string mission, RuleProfile profile, Path path, bool checkPerf, string layout = "wide", string strategy = "A")
        {
            yield return LoadOperationScene();
            _boot = Object.FindFirstObjectByType<GameBootstrap>();
            Assert.IsNotNull(_boot, "GameBootstrap missing from the Operation scene");
            _driver = _boot.Driver;
            _input = _boot.Interaction;
            Assert.IsNotNull(_driver, "SimDriver missing");
            Assert.IsNotNull(_input, "BoardInteraction missing");
            _path = path;

            // a clean, non-guided run on the wide layout
            _driver.LoadRun(mission, layout, profile, false);
            yield return null;
            yield return null;
            Assert.IsTrue(_driver.Paused, "a new run starts paused for planning");
            AssertBoardAndArt(mission);

            // the chosen adapter is the only input source
            _input.ClearSources();
            if (path == Path.Desktop) { _pointer = new ScriptedPointer(); _input.AddSource(_pointer); }
            else
            {
                _head = new GameObject("TestHead").transform;
                _input.AddSource(new HeadDwellPointer(_head));
                ParkHead();
            }

            var d = _driver.Def;
            var S = _driver.Sim.S;

            // Start ends the planning pause through the Start control; pressing it again pauses, and time then runs
            // in command-mode steps so every strategy tick is exact
            yield return Press(Find(PickKind.Button, label: "Start"));
            Assert.IsFalse(_driver.Paused, "Start control should unpause the run; " + _lastPress);
            yield return Press(Find(PickKind.Button, label: "Start"));   // the control now reads "Pause"
            Assert.IsTrue(_driver.Paused, "the same control pauses again; " + _lastPress);
            // the run ticked in real time between those presses (a dwell takes 0.8 s); restart so every strategy step
            // lands on its validated tick
            _driver.LoadRun(mission, layout, profile, false);
            yield return null;
            yield return null;
            Assert.AreEqual(0, _driver.Sim.S.C.O, "restarted run starts at operation step 0");
            // a restart rebuilds the tool tray: every card must show its stock and cost again, not just its name
            foreach (var card in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t => t.name.StartsWith("Card_") && t.gameObject.activeInHierarchy))
                StringAssert.Contains("supply", card.GetComponentInChildren<TMPro.TextMeshPro>().text, $"{card.name} after restart");
            S = _driver.Sim.S;

            var script = ContentLibrary.Strategy(mission, strategy);
            Assert.IsNotNull(script, $"{mission} has no strategy {strategy}");
            bool measured = !checkPerf;
            for (int i = 0; i < script.Steps.Count;)
            {
                long t = script.Steps[i].t;
                if (!measured && t > PerfTick) { yield return CheckPerf(mission); measured = true; }
                // the replay submits a step when it is due on the next operation step: the player acts at t-1, every
                // step of that tick in turn, then one step applies them all and each step's effect is checked
                AdvanceTo(t - 1);
                _checks.Clear();
                for (; i < script.Steps.Count && script.Steps[i].t == t; i++) yield return PerformStep(script.Steps[i].step, t);
                _driver.Advance(1);
                foreach (var check in _checks) check();
            }
            if (!measured) { yield return CheckPerf(mission); }

            // let the board render the live run for a moment (visual LateUpdates over a moving simulation)
            _driver.SetPaused(false);
            for (int i = 0; i < 30; i++) yield return null;
            _driver.SetPaused(true);

            for (int guard = 0; guard < 400 && !_driver.Sim.IsOver; guard++) _driver.Advance(50);
            Assert.IsTrue(_driver.Sim.IsOver, $"{mission}: the run should reach its outcome");
            Assert.IsTrue(S.C.Success, $"{mission} ({profile}) strategy {strategy} should win (evacuated {S.C.Evacuated}/{d.TotalResidents}, losses {S.C.Losses})");
            Assert.AreEqual(3, S.C.Stars, $"{mission} strategy {strategy} is a validated 3-star plan on {layout}/{profile}");
            if (_head != null) Object.Destroy(_head.gameObject);
            yield return null;
        }

        IEnumerator PerformStep(JsonNode step, long t)
        {
            var d = _driver.Def;
            var sim = _driver.Sim;
            if (step.Has("place"))
            {
                string socket = step.Str("place");
                int s = d.SocketIndex[socket];
                yield return Place(d.SocketKind[s], socket);
            }
            else if (step.Has("vehicle"))
            {
                int v = StrategyScript.VehicleIndex(sim, step.Str("vehicle"));
                Assert.GreaterOrEqual(v, 0, $"t={t}: no vehicle {step.Str("vehicle")}");
                yield return RouteVehicle(v, step.Str("group"), step.Str("to"));
            }
            else if (step.Has("group"))
                yield return RouteGroup(step.Str("group"), step.Str("to"));
            else if (step.Has("enable") || step.Has("disable") || step.Has("cool"))
            {
                string action = step.Has("cool") ? "cool" : step.Has("enable") ? "enable" : "disable";
                int inst = StrategyScript.InstanceAt(sim, step.Str(action));
                Assert.GreaterOrEqual(inst, 0, $"t={t}: no equipment at {step.Str(action)}");
                yield return TapEquipment(inst, action);
            }
            else Assert.Fail($"t={t}: strategy step kind not covered by the smoke player");
        }

        // ------------------------------------------------------------------ board, art and budget checks
        void AssertBoardAndArt(string mission)
        {
            var board = _boot.Board;
            Assert.IsNotNull(board, "BoardView missing");
            var terrain = GameObject.Find("Terrain");
            Assert.IsNotNull(terrain, "terrain not built");
            Assert.Greater(terrain.GetComponent<MeshFilter>().sharedMesh.vertexCount, 1000, "terrain heightfield too small");
            Assert.IsNotNull(GameObject.Find("TerrainShadow"), "terrain shadow proxy missing");
            Assert.IsNotNull(GameObject.Find("Frame"), "wooden frame missing");
            Assert.IsNotNull(board.Scatter, "scatter view missing");
            Assert.Greater(board.Scatter.InstanceCount, 50, $"{mission}: world dressing (nature kit scatter) missing");

            int homes = 0, crew = 0, residents = 0, rigged = 0;
            foreach (Transform t in board.Actors.transform)
            {
                if (t.name.StartsWith("Home_")) homes++;
                else if (t.name.StartsWith("Crew_")) crew++;
                else if (t.name.StartsWith("Resident_"))
                {
                    residents++;
                    if (t.GetComponentInChildren<SkinnedMeshRenderer>(true) != null) rigged++;
                }
            }
            Assert.AreEqual(_driver.Def.ResidentIds.Length, residents, $"{mission}: one figure per resident");
            Assert.AreEqual(residents, rigged, $"{mission}: every resident uses the rigged kit");
            Assert.Greater(homes, 0, $"{mission}: hero homes missing");
            Assert.Greater(crew, 0, $"{mission}: rescue crew at landings missing");

            var ring = GameObject.Find("WorldRing");
            Assert.IsNotNull(ring, "world ring missing");
            Assert.AreEqual(7, ring.transform.Cast<Transform>().Count(c => c.name.StartsWith("Backdrop_")), $"{mission}: backdrop tiles: 3 far + 2 per side");
            var sun = RenderSettings.sun;
            Assert.IsNotNull(sun, "no sun");
            Assert.Less(sun.transform.eulerAngles.x, 35f, "golden-hour sun should be low");
        }

        /// <summary>
        /// Render budget at a mid-run moment (operation step 300, as tools/unity/perf.sh): seated view, shipping HUD
        /// (no dev picker), 60 rendered frames of Unity's own counters, shadow pass included. Asserts the worst frame.
        /// </summary>
        IEnumerator CheckPerf(string mission)
        {
            AdvanceTo(PerfTick);
            OperationPanel.DevControlsOverride = false;
            _boot.Panel.Rebuild();
            var cam = Camera.main;
            var orbit = cam.GetComponent<DesktopOrbit>();
            if (orbit != null) { orbit.enabled = true; orbit.Pitch = 52; orbit.Distance = 0.72f; orbit.Yaw = 0; cam.fieldOfView = 60f; }
            var probe = _boot.gameObject.AddComponent<PerfProbe>();
            probe.Frames = 60;
            for (int i = 0; i < 600 && !PerfProbe.Done; i++) yield return null;
            Object.Destroy(probe);
            OperationPanel.DevControlsOverride = null;
            _boot.Panel.Rebuild();
            yield return null;
            yield return null;
            Assert.IsTrue(PerfProbe.Done, $"{mission}: perf probe did not finish");
            Assert.AreEqual(60, PerfProbe.RenderedFrames, $"{mission}: the Game view must render every frame to measure (focus the Editor): {PerfProbe.LastReport}");
            Debug.Log($"[RESQ smoke] {mission} perf (Editor, mono): {PerfProbe.LastReport}");
            Assert.LessOrEqual(PerfProbe.MaxDraw, BudgetDraws, $"{mission}: draw calls over budget: {PerfProbe.LastReport}");
            Assert.LessOrEqual(PerfProbe.MaxTris, BudgetTris, $"{mission}: triangles over budget: {PerfProbe.LastReport}");
        }

        // ------------------------------------------------------------------ actions
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
            Assert.AreEqual(InteractMode.Idle, _input.Mode, $"{tool} socket press should place it ({_input.Hint}); " + _lastPress);
            string press = _lastPress; int expect = before + 1;
            _checks.Add(() => Assert.GreaterOrEqual(_driver.Sim.S.C.EquipmentCount, expect, $"{tool} should be placed at {socketId}; " + press));
        }

        IEnumerator RouteVehicle(int vehicle, string groupId, string toNodeId)
        {
            yield return null;   // a vehicle placed this step gets its handle on the next rendered frame
            yield return Press(Find(PickKind.Vehicle, index: vehicle));
            Assert.AreEqual(InteractMode.VehiclePickup, _input.Mode, "vehicle press selects it; " + _lastPress);
            if (groupId != null)
            {
                yield return Press(Find(PickKind.Group, index: _driver.Def.GroupIndex[groupId]));
                Assert.AreEqual(InteractMode.VehicleDestination, _input.Mode, "group press picks who to collect; " + _lastPress);
            }
            yield return Press(Find(PickKind.Node, index: _driver.Def.NodeIndex[toNodeId]));
            Assert.AreEqual(InteractMode.Idle, _input.Mode, "destination press issues the route; " + _lastPress);
            string press = _lastPress;
            _checks.Add(() => Assert.AreNotEqual((byte)VehiclePhase.None, _driver.Sim.S.VPhase[vehicle], $"vehicle {vehicle} should be on its way to {groupId ?? toNodeId}; " + press));
        }

        IEnumerator RouteGroup(string groupId, string toNodeId)
        {
            yield return null;
            int g = _driver.Def.GroupIndex[groupId], node = _driver.Def.NodeIndex[toNodeId];
            yield return Press(Find(PickKind.Group, index: g));
            Assert.AreEqual(InteractMode.GroupRouting, _input.Mode, $"group {groupId} press selects it; " + _lastPress);
            yield return Press(Find(PickKind.Node, index: node));
            Assert.AreEqual(InteractMode.Idle, _input.Mode, "destination press issues the walk route; " + _lastPress);
            string press = _lastPress;
            _checks.Add(() => Assert.AreEqual(node, _driver.Sim.S.GroupTarget[g], $"group {groupId} should be routed to {toNodeId}; " + press));
        }

        IEnumerator TapEquipment(int inst, string action)
        {
            yield return null;
            var S = _driver.Sim.S;
            bool wasActive = S.EqActive[inst];
            int charges = S.EqCharges[inst];
            yield return Press(Find(PickKind.Equipment, index: inst));
            string press = _lastPress;
            if (action == "cool") _checks.Add(() => Assert.Less(S.EqCharges[inst], charges, $"cooler #{inst} should spend a charge; " + press));
            else _checks.Add(() => Assert.AreEqual(action == "enable", S.EqActive[inst], $"equipment #{inst} should be {action}d (was {(wasActive ? "on" : "off")}); " + press));
        }

        Pickable Find(PickKind kind, int index = -1, string label = null)
        {
            var all = Object.FindObjectsByType<Pickable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var p = all.FirstOrDefault(x => x.Kind == kind && (index < 0 || x.Index == index) && (label == null || x.Label == label)
                                            && x.GetComponentInChildren<Collider>() != null);
            Assert.IsNotNull(p, $"no visible {kind} target (index {index}, label {label})");
            return p;
        }

        // ------------------------------------------------------------------ gestures
        /// <summary>A ray that BoardInteraction's own pick (or the dwell pointer's cast) resolves to the target.</summary>
        Ray AimRay(Pickable target, Collider col, float radius)
        {
            Physics.SyncTransforms();
            var c = col.bounds.center;
            var cam = Camera.main;
            foreach (var origin in new[] { cam.transform.position, c + Vector3.up * 0.4f, c + (cam.transform.position - c).normalized * 0.3f })
            {
                var ray = new Ray(origin, (c - origin).normalized);
                if (Physics.SphereCast(ray, radius, out var hit, 1.9f) && hit.collider.GetComponentInParent<Pickable>() == target) return ray;
            }
            Assert.Fail($"{target.Kind} '{target.Label}' #{target.Index} can't be reached by a pointer ray (occluded)");
            return default;
        }

        IEnumerator Press(Pickable target) => _path == Path.Desktop ? RayPress(target) : DwellPress(target);

        /// <summary>Hover, press for one frame, release: the gesture a click, pinch or trigger produces.</summary>
        IEnumerator RayPress(Pickable target)
        {
            var col = target.GetComponentInChildren<Collider>();
            yield return Settle(col);
            _pointer.Ray = AimRay(target, col, 0.004f);
            _pointer.Valid = true;
            _pointer.Pressed = false;
            yield return null;                      // hover
            _pointer.Ray = AimRay(target, col, 0.004f);
            Assert.AreEqual(target, _input.Hover, $"pointer should hover {target.Kind} #{target.Index}");
            _pointer.Pressed = true;
            yield return null;                      // press
            _lastPress = $"pressed {target.Kind} #{target.Index}: hover at press = {(_input.Hover ? _input.Hover.Kind + " #" + _input.Hover.Index : "none")}, mode {_input.Mode}";
            _pointer.Pressed = false;
            yield return null;                      // release
            _pointer.Valid = false;
            yield return null;
        }

        /// <summary>
        /// Command path: turn the head onto the target and hold it there; the game's HeadDwellPointer fires the
        /// selection after its dwell time (0.8 s of real time). Then look away so it can't fire twice.
        /// </summary>
        IEnumerator DwellPress(Pickable target)
        {
            var col = target.GetComponentInChildren<Collider>();
            yield return Settle(col);
            var ray = AimRay(target, col, 0.006f);
            _head.SetPositionAndRotation(ray.origin, Quaternion.LookRotation(ray.direction, Vector3.up));
            var mode = _input.Mode;
            int seq = _driver.Sim.S.C.LastSequence;
            bool paused = _driver.Paused;
            float start = Time.realtimeSinceStartup;
            bool hovered = false;
            // the selection shows up as a mode change, the Start/Pause toggle or a submitted command
            while (Time.realtimeSinceStartup - start < 3f)
            {
                yield return null;
                ray = AimRay(target, col, 0.006f);   // follow the target if it shifts
                _head.SetPositionAndRotation(ray.origin, Quaternion.LookRotation(ray.direction, Vector3.up));
                if (_input.Hover == target) hovered = true;
                if (_input.Mode != mode || _driver.Paused != paused || _driver.Sim.S.C.LastSequence != seq) break;
            }
            float held = Time.realtimeSinceStartup - start;
            _lastPress = $"dwelt on {target.Kind} #{target.Index} for {held:F2}s (hovered {hovered}), mode {_input.Mode}";
            Assert.IsTrue(hovered, $"head ray should hover {target.Kind} #{target.Index}");
            Assert.GreaterOrEqual(held, 0.75f, $"a dwell selection fired too early ({held:F2}s): inspection must never activate");
            ParkHead();
            yield return null;
            yield return null;
        }

        void ParkHead() { if (_head != null) _head.SetPositionAndRotation(new Vector3(0, 10, 0), Quaternion.LookRotation(Vector3.up)); }

        /// <summary>A player aims at where they see the target: wait until it has settled (new handles snap into place).</summary>
        static IEnumerator Settle(Collider col)
        {
            var last = col.bounds.center;
            for (int i = 0; i < 20; i++)
            {
                yield return null;
                if ((col.bounds.center - last).sqrMagnitude < 1e-8f) break;
                last = col.bounds.center;
            }
        }
    }
}
