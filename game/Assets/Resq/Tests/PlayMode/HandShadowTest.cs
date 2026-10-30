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
    /// Hands cast shadows on the miniature: a point above the valley puts a shadow on the terrain directly below,
    /// fainter and wider as the hand rises, none above the cut-off height; and the terrain collider that catches
    /// the shadows is invisible to pointer picking (default raycast layers). Evidence: automated (Editor PlayMode);
    /// the XR feeder (real hand joints) is checked in the Meta XR Simulator.
    /// </summary>
    public sealed class HandShadowTest
    {
        string _dir;
        [SetUp] public void SetUp() { _dir = Path.Combine(Path.GetTempPath(), "resq_hs_" + System.Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_dir); SaveStore.Folder = _dir; }
        [TearDown] public void TearDown() { SaveStore.Folder = null; try { Directory.Delete(_dir, true); } catch (IOException) { } }

        [UnityTest]
        public IEnumerator ShadowsLandOnTerrainAndFadeWithHeight()
        {
#if UNITY_EDITOR
            var op = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Resq/Scenes/Operation_Desktop.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!op.isDone) yield return null;
#else
            Assert.Ignore("Editor only"); yield break;
#endif
            for (int i = 0; i < 4; i++) yield return null;
            var boot = Object.FindFirstObjectByType<GameBootstrap>();
            boot.Driver.LoadRun("W1-M1", "wide", RuleProfile.Standard, false);
            for (int i = 0; i < 3; i++) yield return null;

            var terrain = GameObject.Find("BoardRoot").transform.Find("Terrain");
            Assert.AreEqual(HandShadowView.TerrainLayer, terrain.gameObject.layer, "terrain collider must sit on Ignore Raycast");
            var centre = terrain.GetComponent<MeshCollider>().bounds.center;
            bool pickHitsTerrain = Physics.Raycast(centre + Vector3.up, Vector3.down, out var pick, 2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide)
                                   && pick.collider.gameObject == terrain.gameObject;
            Assert.IsFalse(pickHitsTerrain, "pointer picks (default layers) must never hit the terrain collider");
            Assert.IsTrue(Physics.Raycast(centre + Vector3.up, Vector3.down, out var surface, 2f, 1 << HandShadowView.TerrainLayer), "terrain collider present");

            var view = boot.Board.gameObject.AddComponent<HandShadowView>();
            yield return null;
            var pts = new Vector3[HandShadowView.Slots]; var radii = new float[HandShadowView.Slots];

            pts[0] = surface.point + Vector3.up * 0.04f; radii[0] = 0.03f;
            view.Frame(1, pts, radii);
            Assert.IsTrue(view.Blob(0).gameObject.activeSelf, "a hand 4 cm up casts a shadow");
            Assert.AreEqual(surface.point.y, view.Blob(0).position.y, 0.005f, "the shadow lies on the terrain below");
            float nearOpacity = view.Opacity(0), nearSize = view.Blob(0).lossyScale.x;
            Assert.AreEqual(nearOpacity, view.Blob(0).GetComponent<Renderer>().sharedMaterial.GetFloat("_Opacity"), 1e-4f,
                "the drawn material carries the opacity");

            pts[0] = surface.point + Vector3.up * 0.20f;
            view.Frame(1, pts, radii);
            Assert.Less(view.Opacity(0), nearOpacity, "a higher hand casts a fainter shadow");
            Assert.Greater(view.Blob(0).lossyScale.x, nearSize, "and a wider one");

            pts[0] = surface.point + Vector3.up * 0.45f;
            view.Frame(1, pts, radii);
            Assert.IsFalse(view.Blob(0).gameObject.activeSelf, "above the cut-off there is no shadow");

            view.Frame(0, pts, radii);
            for (int i = 0; i < HandShadowView.Slots; i++) Assert.IsFalse(view.Blob(i).gameObject.activeSelf);
        }
    }
}
