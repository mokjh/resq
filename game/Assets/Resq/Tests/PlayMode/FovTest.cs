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
    /// FoV-aware UI (BUILD_PLAN P4.7, judging "UI in the field of view"): must-see UI stays inside the primary 30°
    /// zone around where the seated player is looking (Meta comfort guidelines, hz-immersive-designer). The head is
    /// where the game itself puts it for the virtual table (TablePlacement.InFrontOfHead: the board 0.55 m ahead of and
    /// 0.30 m below the head). Two gazes: during play the player watches the board centre (HUD); a menu card is read
    /// at the natural resting gaze, 15° below horizontal toward the board (Meta: 10–20° down is most comfortable).
    /// Measured on the live layout, not estimated: every visible mesh's oriented bounds corners, after the cards have
    /// turned to face that head. Must-see: the HUD (objectives, time, supplies) and the menu cards (briefing, pause).
    /// The tool tray and run controls are touch targets near the hands and are only reported. All five worlds' first
    /// missions on the wide layout (the larger board). Evidence: automated (Editor PlayMode).
    /// </summary>
    public sealed class FovTest
    {
        const string ScenePath = "Assets/Resq/Scenes/Operation_Desktop.unity";
        const float MustSeeDeg = 30f, RestingPitchDeg = 15f;
        enum Gaze { BoardCentre, Resting }

        sealed class Pointer : IPointerSource
        {
            public Ray Ray; public bool Valid, Pressed;
            public PointerSample Sample() => new PointerSample { Kind = PointerKind.Desktop, Valid = Valid, Ray = Ray, Pressed = Pressed };
            public void Feedback(Pickable hover, float progress) { }
        }

        string _dir;
        GameBootstrap _boot;
        GameFlow _flow;
        Pointer _pointer;

        [SetUp] public void SetUp() { _dir = Path.Combine(Path.GetTempPath(), "resq_fov_" + System.Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_dir); SaveStore.Folder = _dir; }
        [TearDown] public void TearDown() { SaveStore.Folder = null; try { Directory.Delete(_dir, true); } catch (IOException) { } }

        [UnityTest]
        public IEnumerator MustSeeUiStaysInsideTheCentralThirtyDegrees()
        {
            File.WriteAllText(SaveStore.PrimaryPath, JsonUtility.ToJson(new SaveData { WelcomeDone = true }));
#if UNITY_EDITOR
            var op = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            while (!op.isDone) yield return null;
#else
            Assert.Ignore("Editor only"); yield break;
#endif
            for (int i = 0; i < 6; i++) yield return null;
            _boot = Object.FindFirstObjectByType<GameBootstrap>();
            _flow = _boot.Flow;
            _boot.Interaction.ClearSources();
            _pointer = new Pointer();
            _boot.Interaction.AddSource(_pointer);
            SeatPlayer();

            Assert.AreEqual("U06", _flow.Menu.Current?.Id, "the hub opens after the welcome");
            yield return Press("Start");
            Assert.AreEqual("U08", _flow.Menu.Current?.Id, "briefing");
            yield return Settle();
            Check("briefing card", _flow.Menu.transform, true, Gaze.Resting);

            yield return Press("Skip the guide");
            Assert.AreEqual(FlowState.Operation, _flow.State);
            foreach (var mission in OperationSmokeTest.WorldFirstMissions)
            {
                if (mission != _boot.Driver.MissionId) { _boot.Driver.LoadRun(mission, "wide", RuleProfile.Standard, false); yield return null; }
                SeatPlayer();
                yield return Settle();
                Check($"{mission} HUD", Find("HudRoot"), true, Gaze.BoardCentre);
                Check($"{mission} run controls", Find("Controls"), false, Gaze.BoardCentre);
                Check($"{mission} tool tray", Find("ToolTray"), false, Gaze.BoardCentre);
            }

            _flow.Pause();
            Assert.AreEqual("U15", _flow.Menu.Current?.Id, "pause");
            yield return Settle();
            Check("pause card", _flow.Menu.transform, true, Gaze.Resting);
        }

        /// <summary>The camera becomes the seated player's head: where the virtual table puts it relative to the board.</summary>
        void SeatPlayer()
        {
            var cam = Camera.main;
            var orbit = cam.GetComponent<DesktopOrbit>();
            if (orbit != null) orbit.enabled = false;
            var board = _boot.BoardRoot;
            var fwd = board.forward; fwd.y = 0; fwd.Normalize();
            cam.transform.position = board.position - fwd * 0.55f + Vector3.up * 0.30f;
            cam.transform.LookAt(board.position);
        }

        /// <summary>Cards and the HUD ease round to face the head; give them time to arrive.</summary>
        static IEnumerator Settle()
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 2f) yield return null;
        }

        Transform Find(string name)
        {
            var t = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).FirstOrDefault(x => x.name == name && x.gameObject.activeInHierarchy);
            Assert.IsNotNull(t, $"{name} not found");
            return t;
        }

        /// <summary>Largest angle (degrees) between the gaze and any corner of any visible mesh under root.</summary>
        void Check(string what, Transform root, bool mustSee, Gaze kind)
        {
            var cam = Camera.main.transform;
            var toBoard = _boot.BoardRoot.position - cam.position;
            var flat = new Vector3(toBoard.x, 0f, toBoard.z).normalized;
            var gaze = kind == Gaze.BoardCentre ? toBoard.normalized
                                                : Quaternion.AngleAxis(RestingPitchDeg, Vector3.Cross(Vector3.up, flat)) * flat;
            float worst = 0f; string worstName = "";
            float top = -90f, bottom = 90f;
            int meshes = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                var r = mf.GetComponent<MeshRenderer>();
                if (r == null || !r.enabled || !mf.gameObject.activeInHierarchy || mf.sharedMesh == null) continue;
                meshes++;
                var b = mf.sharedMesh.bounds;
                for (int k = 0; k < 8; k++)
                {
                    var local = b.center + Vector3.Scale(b.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                    var dir = mf.transform.TransformPoint(local) - cam.position;
                    float a = Vector3.Angle(gaze, dir);
                    if (a > worst) { worst = a; worstName = mf.name; }
                    float pitch = Mathf.Asin(Mathf.Clamp(dir.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
                    top = Mathf.Max(top, pitch); bottom = Mathf.Min(bottom, pitch);
                }
            }
            Assert.Greater(meshes, 0, $"{what}: nothing visible to measure");
            Debug.Log($"[RESQ fov] {what}: {worst:F1}° from the {(kind == Gaze.BoardCentre ? "board-centre" : "resting")} gaze (widest part: {worstName}); spans {bottom:F0}° to {top:F0}° from horizontal{(mustSee ? $", limit {MustSeeDeg}°" : ", reach zone (reported)")}");
            if (mustSee) Assert.LessOrEqual(worst, MustSeeDeg, $"{what} reaches {worst:F1}° from the gaze at {worstName} (spans {bottom:F0}° to {top:F0}° from horizontal)");
        }

        IEnumerator Press(string label)
        {
            yield return null;
            var target = Object.FindObjectsByType<Pickable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                               .FirstOrDefault(p => p.Kind == PickKind.Button && p.Label == label && p.GetComponent<Collider>() != null);
            Assert.IsNotNull(target, $"no '{label}' button on {_flow.Menu.Current?.Id}");
            Physics.SyncTransforms();
            var c = target.GetComponent<Collider>().bounds.center;
            var cam = Camera.main.transform.position;
            _pointer.Ray = new Ray(cam, (c - cam).normalized);
            _pointer.Valid = true; _pointer.Pressed = false;
            yield return null;
            _pointer.Pressed = true;
            yield return null;
            _pointer.Pressed = false;
            yield return null;
            _pointer.Valid = false;
            yield return null;
        }
    }
}
