using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Meta.XR.MRUtilityKit;
using Resq.Game;
using UnityEngine;

namespace Resq.XR
{
    /// <summary>
    /// Puts the diorama on the player's real table (BUILD_PLAN decision 7):
    ///   1. a saved spatial anchor from an earlier session: the valley comes back exactly where it was;
    ///   2. otherwise the nearest MRUK table that fits: the wide layout if the board fits, else compact;
    ///   3. otherwise a virtual table at seated height in front of the player.
    /// The board's long side faces the player. A fresh placement is saved as a spatial anchor, and the board is
    /// parented to it so it stays world-locked. Presentation and setup only: the simulation never reads any of this.
    /// </summary>
    public static class TablePlacement
    {
        public const string AnchorKey = "resq.board.anchor", LayoutKey = "resq.board.layout", SurfaceKey = "resq.board.surface";
        /// <summary>
        /// Editor-only dev hook: an MRUK sample-room prefab path in this environment variable is loaded when the
        /// device provides no room (the macOS XR Simulator has no room data). Never used in a build.
        /// </summary>
        public const string DevRoomEnv = "RESQ_DEV_ROOM";
        // board footprint including the 16 mm frame on each side (BoardSpace: wide cell 20 mm, 32 x 24 cells)
        static readonly Vector2 WideFootprint = new Vector2(0.64f + 0.032f, 0.48f + 0.032f);
        static readonly Vector2 CompactFootprint = new Vector2(0.64f * 5f / 6f + 0.032f, 0.48f * 5f / 6f + 0.032f);
        const float Margin = 0.02f, SceneTimeout = 8f, AnchorTimeout = 6f;
        /// <summary>A table further than this (head to its centre, flat) is out of a seated player's reach: use the virtual
        /// table in front of them instead (in the Simulator, the nearest table that fitted was 5 m away, 5 Nov).</summary>
        public const float MaxTableDistance = 1.6f;

        public enum Outcome { None, RestoredAnchor, Table, VirtualTable }
        /// <summary>What the last placement did (for the HUD, tests and logs).</summary>
        public static Outcome Last { get; private set; }
        public static string LastDetail { get; private set; } = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            GameBootstrap.XrPlacement = Run;
            GameBootstrap.XrPointers = XrPointers.Setup;
            GameFlow.Replace = Replace;
            GameFlow.UseVirtualTable = UseVirtual;
            GameFlow.PlacementConfirmed = Confirm;
            GameFlow.HandsTracked = XrPointers.AnyHandTracked;
        }

        static bool FlowActive(GameBootstrap boot) => boot.Flow != null && !GameFlow.Disabled;

        public static IEnumerator Run(GameBootstrap boot)
        {
            Last = Outcome.None; LastDetail = "";
            var head = WaitHead();
            while (head.MoveNext()) yield return head.Current;
            var headT = Camera.main != null ? Camera.main.transform : null;

            // 1. restore the anchored valley from an earlier session
            string saved = PlayerPrefs.GetString(AnchorKey, "");
            if (Guid.TryParse(saved, out var uuid))
            {
                var task = RestoreAnchor(uuid);
                float t0 = Time.realtimeSinceStartup;
                while (!task.IsCompleted && Time.realtimeSinceStartup - t0 < AnchorTimeout + 2f) yield return null;
                var anchor = task.IsCompleted && !task.IsFaulted ? task.Result : null;
                if (anchor != null)
                {
                    string layout = PlayerPrefs.GetString(LayoutKey, "wide");
                    Attach(boot, anchor.transform, anchor.transform.position, anchor.transform.rotation);
                    boot.BeginRun(layout, "Your valley is back where you left it");
                    if (PlayerPrefs.GetString(SurfaceKey, "") == "virtual")
                        VirtualTable.Build(boot, new Pose(anchor.transform.position, anchor.transform.rotation), layout == "wide" ? WideFootprint : CompactFootprint);
                    Report(Outcome.RestoredAnchor, $"restored anchor {uuid} ({layout})");
                    yield break;
                }
                // stale or not found here: forget it and place afresh
                PlayerPrefs.DeleteKey(AnchorKey);
                Debug.Log($"[RESQ XR] saved anchor {uuid} not restored; placing afresh");
            }

            // 2. the nearest real table that fits
            var scene = WaitScene();
            while (scene.MoveNext()) yield return scene.Current;
            if (TryChooseTable(headT, out var pose, out var layoutChoice, out var detail))
            {
                SetBoard(boot, pose);
                boot.BeginRun(layoutChoice, layoutChoice == "wide" ? "On your table" : "On your table (compact board)");
                yield return null;
                Report(Outcome.Table, detail);
            }
            else
            {
                // 3. a virtual table in front of the seated player (spec 05: eye − 300 mm, 550 mm out)
                var p = InFrontOfHead(headT);
                SetBoard(boot, p);
                VirtualTable.Build(boot, p, WideFootprint);
                boot.BeginRun("wide", "On a virtual table (no table that fits was found)");
                yield return null;
                Report(Outcome.VirtualTable, detail.Length > 0 ? detail : "no table found: virtual table");
            }
            // with the game flow the anchor is saved when the player confirms the calibrated pose (Confirm)
            if (boot.XrActive && !FlowActive(boot)) SaveAnchorAsync(boot, boot.LayoutClass, Last == Outcome.VirtualTable ? "virtual" : "table");
        }

        // ------------------------------------------------------------------ flow hooks (U03–U05, U17)
        /// <summary>Move the board / relocalise: drop the current anchor and place afresh.</summary>
        static void Replace(GameBootstrap boot)
        {
            Detach(boot);
            ForgetAnchor();
            boot.StartCoroutine(Run(boot));
        }

        /// <summary>Switch to the virtual table in front of the player.</summary>
        static void UseVirtual(GameBootstrap boot)
        {
            Detach(boot);
            ForgetAnchor();
            var p = InFrontOfHead(Camera.main != null ? Camera.main.transform : null);
            SetBoard(boot, p);
            VirtualTable.Build(boot, p, boot.LayoutClass == "compact" ? CompactFootprint : WideFootprint);
            Report(Outcome.VirtualTable, "chosen by the player");
            boot.BeginRun(boot.LayoutClass, "On a virtual table");
        }

        /// <summary>The player confirmed the calibrated pose: save it as the valley's spatial anchor.</summary>
        static void Confirm(GameBootstrap boot)
        {
            if (!boot.XrActive) return;
            var parent = boot.BoardRoot.parent;
            var old = parent != null ? parent.GetComponent<OVRSpatialAnchor>() : null;
            // restored and not moved: the saved anchor is still right
            if (old != null && boot.BoardRoot.localPosition.sqrMagnitude < 1e-6f && Quaternion.Angle(boot.BoardRoot.localRotation, Quaternion.identity) < 0.1f) return;
            Detach(boot, keepTable: true);
            SaveAnchorAsync(boot, boot.LayoutClass, Last == Outcome.VirtualTable || boot.BoardRoot.Find("VirtualTable") != null ? "virtual" : "table");
        }

        static void Detach(GameBootstrap boot, bool keepTable = false)
        {
            var root = boot.BoardRoot;
            var parent = root.parent;
            root.SetParent(null, true);
            var anchor = parent != null ? parent.GetComponent<OVRSpatialAnchor>() : null;
            if (anchor != null)
            {
                _ = anchor.EraseAnchorAsync();   // don't leave stale anchors in the headset's storage
                UnityEngine.Object.Destroy(parent.gameObject);
            }
            if (!keepTable)
            {
                var vt = root.Find("VirtualTable");
                if (vt != null) UnityEngine.Object.Destroy(vt.gameObject);
            }
        }

        // ------------------------------------------------------------------ tables
        /// <summary>
        /// Picks the nearest horizontal TABLE whose top holds the board (wide first, then compact), long side facing
        /// the player. MRUK planes: the anchor's forward is the surface normal and PlaneRect is in its local X/Y.
        /// </summary>
        public static bool TryChooseTable(Transform head, out Pose pose, out string layout, out string detail)
        {
            pose = default; layout = null; detail = "";
            var mruk = MRUK.Instance;
            var room = mruk != null ? mruk.GetCurrentRoom() : null;
            if (room == null) { detail = "no MRUK room"; return false; }
            Vector3 headPos = head != null ? head.position : Vector3.zero;
            float best = float.MaxValue; int tables = 0, tooFar = 0;
            foreach (var a in room.Anchors)
            {
                if (a == null || !a.HasAnyLabel(MRUKAnchor.SceneLabels.TABLE) || !a.PlaneRect.HasValue) continue;
                tables++;
                var t = a.transform;
                if (Vector3.Dot(t.forward, Vector3.up) < 0.9f) continue;   // not a flat top
                var rect = a.PlaneRect.Value;
                var centre = t.TransformPoint(new Vector3(rect.center.x, rect.center.y, 0f));
                if (Flat(centre - headPos).magnitude > MaxTableDistance) { tooFar++; continue; }
                Vector3 axisA = FlatDir(t.right), axisB = FlatDir(t.up);
                float lenA = rect.width * t.lossyScale.x, lenB = rect.height * t.lossyScale.y;
                // the player stands at the edge nearest them: board forward points from that edge across the table
                var toTable = Flat(centre - headPos);
                bool facingA = Mathf.Abs(Vector3.Dot(toTable, axisA)) > Mathf.Abs(Vector3.Dot(toTable, axisB));
                // first choice: the board's long side toward the player's edge; if it only fits turned 90°, turn it
                // (forward still points away from the player along the other axis)
                // preference: wide facing, wide turned, compact facing, compact turned
                string fit = null; Vector3 fwd = Vector3.forward; float across = 0, deep = 0;
                foreach (var footprint in new[] { WideFootprint, CompactFootprint })
                {
                    foreach (bool useA in new[] { facingA, !facingA })
                    {
                        var ax = useA ? axisA : axisB;
                        float acr = useA ? lenB : lenA, dp = useA ? lenA : lenB;   // board X runs across the player's view
                        if (!Fits(acr, dp, footprint)) continue;
                        fit = footprint == WideFootprint ? "wide" : "compact";
                        fwd = ax * (Vector3.Dot(toTable, ax) >= 0 ? 1f : -1f);
                        across = acr; deep = dp;
                        break;
                    }
                    if (fit != null) break;
                }
                if (fit == null) continue;
                float dist = Flat(centre - headPos).magnitude + (fit == "wide" ? 0f : 0.25f);   // prefer wide when equally near
                if (dist >= best) continue;
                best = dist; layout = fit;
                pose = new Pose(centre + Vector3.up * BoardSpace.BaseDepth, Quaternion.LookRotation(fwd, Vector3.up));
                detail = $"table {a.name} {across:F2}x{deep:F2} m -> {fit}";
            }
            if (layout == null) detail = tables == 0 ? "room has no table" : tooFar == tables ? $"{tables} table(s), all out of reach" : $"{tables} table(s), none in reach fits the board";
            return layout != null;
        }

        static bool Fits(float across, float deep, Vector2 footprint) => across >= footprint.x + Margin && deep >= footprint.y + Margin;
        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
        static Vector3 FlatDir(Vector3 v) { v.y = 0; return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.forward; }

        static Pose InFrontOfHead(Transform head)
        {
            if (head == null) return new Pose(new Vector3(0, 0.75f, 0.55f), Quaternion.identity);
            var fwd = head.forward; fwd.y = 0;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            return new Pose(head.position + fwd * 0.55f + Vector3.down * 0.30f, Quaternion.LookRotation(fwd, Vector3.up));
        }

        static void SetBoard(GameBootstrap boot, Pose p) => boot.BoardRoot.SetPositionAndRotation(p.position, p.rotation);

        static void Attach(GameBootstrap boot, Transform parent, Vector3 pos, Quaternion rot)
        {
            boot.BoardRoot.SetPositionAndRotation(pos, rot);
            boot.BoardRoot.SetParent(parent, true);
        }

        static void Report(Outcome o, string detail)
        {
            Last = o; LastDetail = detail;
            Debug.Log($"[RESQ XR] placement: {o} — {detail}");
        }

        // ------------------------------------------------------------------ waits
        static IEnumerator WaitHead()
        {
            for (int i = 0; i < 120; i++)
            {
                var cam = Camera.main;
                if (cam != null && cam.transform.position.sqrMagnitude > 1e-6f) break;
                yield return null;
            }
            yield return null;
        }

        static IEnumerator WaitScene()
        {
            var mruk = MRUK.Instance;
            if (mruk == null) yield break;
            bool loaded = mruk.IsInitialized;
            if (!loaded) mruk.RegisterSceneLoadedCallback(() => loaded = true);
            float t0 = Time.realtimeSinceStartup;
            while (!loaded && Time.realtimeSinceStartup - t0 < SceneTimeout) yield return null;
            if (!loaded) Debug.Log("[RESQ XR] MRUK scene did not load in time (permission denied or no Space Setup)");
#if UNITY_EDITOR
            string devRoom = Environment.GetEnvironmentVariable(DevRoomEnv);
            if (mruk.GetCurrentRoom() == null && !string.IsNullOrEmpty(devRoom))
            {
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(devRoom);
                if (prefab != null)
                {
                    var task = mruk.LoadSceneFromPrefab(prefab, true);
                    while (!task.IsCompleted) yield return null;
                    Debug.Log($"[RESQ XR] dev room loaded (Editor only, device had no room): {prefab.name}");
                }
            }
#endif
        }

        // ------------------------------------------------------------------ anchors
        static async Task<OVRSpatialAnchor> RestoreAnchor(Guid uuid)
        {
            try
            {
                var unbound = new List<OVRSpatialAnchor.UnboundAnchor>();
                var result = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(new[] { uuid }, unbound);
                if (!result.Success || unbound.Count == 0) return null;
                var u = unbound[0];
                if (!u.Localized && !await u.LocalizeAsync(AnchorTimeout)) return null;
                var go = new GameObject("BoardAnchor");
                var anchor = go.AddComponent<OVRSpatialAnchor>();
                u.BindTo(anchor);
                await anchor.WhenLocalizedAsync();
                return anchor;
            }
            catch (Exception e) { Debug.LogWarning("[RESQ XR] anchor restore failed: " + e.Message); return null; }
        }

        static async void SaveAnchorAsync(GameBootstrap boot, string layout, string surface)
        {
            try
            {
                var root = boot.BoardRoot;
                var go = new GameObject("BoardAnchor");
                go.transform.SetPositionAndRotation(root.position, root.rotation);
                var anchor = go.AddComponent<OVRSpatialAnchor>();
                if (!await anchor.WhenCreatedAsync()) { Debug.LogWarning("[RESQ XR] spatial anchor not created"); return; }
                root.SetParent(go.transform, true);   // world-locked from now on
                var saved = await anchor.SaveAnchorAsync();
                if (!saved.Success) { Debug.LogWarning("[RESQ XR] spatial anchor not saved: " + saved.Status); return; }
                PlayerPrefs.SetString(AnchorKey, anchor.Uuid.ToString());
                PlayerPrefs.SetString(LayoutKey, layout);
                PlayerPrefs.SetString(SurfaceKey, surface);
                PlayerPrefs.Save();
                Debug.Log($"[RESQ XR] board anchored and saved: {anchor.Uuid} ({layout})");
            }
            catch (Exception e) { Debug.LogWarning("[RESQ XR] anchor save failed: " + e.Message); }
        }

        /// <summary>Forget the saved valley (re-place from scratch next time).</summary>
        public static void ForgetAnchor() { PlayerPrefs.DeleteKey(AnchorKey); PlayerPrefs.DeleteKey(LayoutKey); PlayerPrefs.DeleteKey(SurfaceKey); PlayerPrefs.Save(); }
    }
}
