using System.Collections;
using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using NUnit.Framework;
using Resq.Game;
using Resq.XR;
using UnityEngine;
using UnityEngine.TestTools;

namespace Resq.Tests
{
    /// <summary>
    /// Table placement against MR Utility Kit's 50 sample rooms (living rooms, bedrooms, offices and awkward
    /// rooms). In each room, the head stands at the room centre at seated eye height. When placement picks a table,
    /// the board's footprint (frame included) must lie on that table's top, at its height, with its edges square
    /// to the table and its forward pointing away from the player. When it picks none, no table in the room may be
    /// able to hold even the compact board. Evidence kind: automated (Editor PlayMode, MRUK prefab rooms, no device).
    /// </summary>
    public sealed class XrPlacementTest
    {
        const string Rooms = "Packages/com.meta.xr.mrutilitykit/Core/Rooms/Prefabs/";
        static readonly Vector2 Wide = new Vector2(0.672f, 0.512f), Compact = new Vector2(0.64f * 5f / 6f + 0.032f, 0.48f * 5f / 6f + 0.032f);

        [UnityTest]
        public IEnumerator PlacesTheBoardOnTablesInSampleRooms()
        {
#if UNITY_EDITOR
            // the only expected error: MRUK warns that this headless test has no OVRCameraRig (it doesn't need one
            // to load prefab rooms); every other error still fails the test
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("OVRCameraRig is not present"));
            var go = new GameObject("MRUK (test)");
            go.SetActive(false);
            var mruk = go.AddComponent<MRUK>();
            mruk.SceneSettings = new MRUK.MRUKSettings { DataSource = MRUK.SceneDataSource.Prefab, LoadSceneOnStartup = false };
            go.SetActive(true);
            var head = new GameObject("TestHead").transform;
            int rooms = 0, placed = 0, wide = 0, compact = 0, none = 0;
            var report = new List<string>();
            foreach (var folder in new[] { "LivingRoom", "Bedroom", "Office", "Other" })
                foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { Rooms + folder }))
                {
                    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    var task = mruk.LoadSceneFromPrefab(prefab, true);
                    while (!task.IsCompleted) yield return null;
                    yield return null;
                    var room = mruk.GetCurrentRoom();
                    if (room == null) continue;
                    rooms++;
                    // the player sits at a table that fits, when the room has one
                    var centre = RoomCentre(room);
                    var table = FirstFittingTable(room);
                    if (table != null)
                    {
                        var top = table.transform.TransformPoint(new Vector3(table.PlaneRect.Value.center.x, table.PlaneRect.Value.center.y, 0f));
                        centre = new Vector3(top.x, centre.y, top.z - 0.9f);
                    }
                    head.SetPositionAndRotation(centre + Vector3.up * 1.2f, Quaternion.identity);
                    bool ok = TablePlacement.TryChooseTable(head, out var pose, out var layout, out var detail);
                    if (ok)
                    {
                        placed++; if (layout == "wide") wide++; else compact++;
                        AssertOnTable(room, pose, layout == "wide" ? Wide : Compact, head.position, prefab.name, detail);
                        var away = pose.position - head.position; away.y = 0;
                        Assert.LessOrEqual(away.magnitude, TablePlacement.MaxTableDistance, $"{prefab.name}: chosen table out of reach ({detail})");
                        // walk far from every table: no table is in reach, so placement must fall back to the virtual table
                        head.position += Vector3.right * 100f;
                        Assert.IsFalse(TablePlacement.TryChooseTable(head, out _, out _, out var farDetail), $"{prefab.name}: chose a table 100 m away");
                        StringAssert.Contains("out of reach", farDetail, prefab.name);
                    }
                    else
                    {
                        none++;
                        Assert.IsNull(table, $"{prefab.name}: placement found no table, but a table could hold the compact board ({detail})");
                    }
                    report.Add($"{prefab.name}: {(ok ? layout : "virtual")} ({detail})");
                }
            Object.Destroy(head.gameObject);
            Object.Destroy(go);
            Debug.Log($"[RESQ xr-placement] {rooms} rooms: {wide} wide, {compact} compact, {none} virtual table\n" + string.Join("\n", report));
            Assert.AreEqual(40, rooms, "MRUK sample rooms (LivingRoom, Bedroom, Office, Other: 10 each) not all loaded");
            Assert.Greater(placed, 5, "placement should find a usable table in several sample rooms");
#else
            Assert.Ignore("Editor only");
            yield break;
#endif
        }

        static Vector3 RoomCentre(MRUKRoom room)
        {
            var b = new Bounds(room.transform.position, Vector3.zero);
            foreach (var a in room.Anchors) if (a != null) b.Encapsulate(a.transform.position);
            var c = b.center; c.y = room.transform.position.y;
            foreach (var a in room.Anchors) if (a != null && a.HasAnyLabel(MRUKAnchor.SceneLabels.FLOOR)) { c.y = a.transform.position.y; break; }
            return c;
        }

        /// <summary>All four footprint corners on the table top that placement chose, square to it, at its height.</summary>
        static void AssertOnTable(MRUKRoom room, Pose pose, Vector2 footprint, Vector3 headPos, string roomName, string detail)
        {
            var right = pose.rotation * Vector3.right; var fwd = pose.rotation * Vector3.forward;
            Assert.Greater(Vector3.Dot(pose.rotation * Vector3.up, Vector3.up), 0.999f, $"{roomName}: board not level");
            var toBoard = pose.position - headPos; toBoard.y = 0;
            Assert.Greater(Vector3.Dot(fwd, toBoard.normalized), 0f, $"{roomName}: board forward should point away from the player");
            MRUKAnchor host = null;
            foreach (var a in room.Anchors)
            {
                if (a == null || !a.HasAnyLabel(MRUKAnchor.SceneLabels.TABLE) || !a.PlaneRect.HasValue) continue;
                bool all = true;
                foreach (float sx in new[] { -0.5f, 0.5f })
                    foreach (float sz in new[] { -0.5f, 0.5f })
                    {
                        var corner = pose.position + right * (sx * footprint.x) + fwd * (sz * footprint.y);
                        var local = a.transform.InverseTransformPoint(corner);
                        var r = a.PlaneRect.Value;
                        all &= local.x >= r.xMin - 1e-3f && local.x <= r.xMax + 1e-3f && local.y >= r.yMin - 1e-3f && local.y <= r.yMax + 1e-3f;
                    }
                if (all) { host = a; break; }
            }
            Assert.IsNotNull(host, $"{roomName}: the board footprint isn't on a table top ({detail})");
            float topY = host.transform.position.y;
            Assert.AreEqual(topY + BoardSpace.BaseDepth, pose.position.y, 0.002f, $"{roomName}: the board's base should rest on the table top");
            float squareness = Mathf.Max(Mathf.Abs(Vector3.Dot(right, Flat(host.transform.right))), Mathf.Abs(Vector3.Dot(right, Flat(host.transform.up))));
            Assert.Greater(squareness, 0.999f, $"{roomName}: board edges should run square to the table's edges");
        }

        static MRUKAnchor FirstFittingTable(MRUKRoom room)
        {
            foreach (var a in room.Anchors)
            {
                if (a == null || !a.HasAnyLabel(MRUKAnchor.SceneLabels.TABLE) || !a.PlaneRect.HasValue) continue;
                if (Vector3.Dot(a.transform.forward, Vector3.up) < 0.9f) continue;
                var r = a.PlaneRect.Value;
                float w = r.width * a.transform.lossyScale.x, h = r.height * a.transform.lossyScale.y;
                float lo = Mathf.Min(w, h), hi = Mathf.Max(w, h);
                // either orientation, compact footprint plus margin
                if (hi >= Compact.x + 0.02f && lo >= Compact.y + 0.02f) return a;
            }
            return null;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v.normalized; }
    }
}
