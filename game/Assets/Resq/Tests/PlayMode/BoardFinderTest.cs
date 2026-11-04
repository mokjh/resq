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
    /// The board can end up beside or behind the player (MRUK picks the nearest table that fits). Looking away brings
    /// up a hint inside the comfortable zone with an arrow the right way; looking back hides it; it never takes input.
    /// Evidence: automated (Editor PlayMode); the board-behind case was first seen in the Meta XR Simulator.
    /// </summary>
    public sealed class BoardFinderTest
    {
        string _dir;
        Transform _head;
        Transform _board;
        [SetUp] public void SetUp() { _dir = Path.Combine(Path.GetTempPath(), "resq_bf_" + System.Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_dir); SaveStore.Folder = _dir; }
        [TearDown] public void TearDown() { SaveStore.Folder = null; try { Directory.Delete(_dir, true); } catch (IOException) { } }

        [UnityTest]
        public IEnumerator HintAppearsWhenTheBoardIsOutOfViewAndPointsToIt()
        {
#if UNITY_EDITOR
            var op = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Resq/Scenes/Operation_Desktop.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!op.isDone) yield return null;
#else
            Assert.Ignore("Editor only"); yield break;
#endif
            for (int i = 0; i < 6; i++) yield return null;
            var boot = Object.FindFirstObjectByType<GameBootstrap>();
            var finder = Object.FindFirstObjectByType<BoardFinderView>();
            Assert.IsNotNull(finder, "the bootstrap creates the finder");
            _board = boot.BoardRoot;
            _head = Camera.main.transform;
            var orbit = _head.GetComponent<DesktopOrbit>();
            if (orbit != null) orbit.enabled = false;
            var fwd = _board.forward; fwd.y = 0; fwd.Normalize();
            _head.position = _board.position - fwd * 0.55f + Vector3.up * 0.30f;

            _head.LookAt(_board.position);
            yield return Wait(1f);
            Assert.IsFalse(finder.Visible, "looking at the board: no hint");

            // a quick glance away does not trigger it
            Face(-fwd);
            yield return Wait(0.2f);
            Assert.IsFalse(finder.Visible, "a glance shorter than the delay shows nothing");

            // the board behind the player
            yield return Wait(1f);
            Assert.IsTrue(finder.Visible, "looking away: the hint appears");
            StringAssert.Contains("behind", finder.Message);
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(finder.ArrowDegrees, 0f)) < 1f || Mathf.Abs(Mathf.DeltaAngle(finder.ArrowDegrees, 180f)) < 1f, "behind: arrow points to one side");
            AssertComfortable(finder);
            Assert.IsEmpty(finder.GetComponentsInChildren<Collider>(true), "the hint takes no input");

            // the board off to the player's left: face along +right, so the board lies to the left
            var right = Vector3.Cross(Vector3.up, fwd);
            Face(right);
            yield return Wait(1f);
            Assert.IsTrue(finder.Visible);
            StringAssert.Contains("this way", finder.Message);
            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(finder.ArrowDegrees, 0f)), 90f, $"board on the left: arrow points left (was {finder.ArrowDegrees:F0}°)");
            AssertComfortable(finder);

            // and to the player's right
            Face(-right);
            yield return Wait(1f);
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(finder.ArrowDegrees, 0f)), 90f, $"board on the right: arrow points right (was {finder.ArrowDegrees:F0}°)");

            _head.LookAt(_board.position);
            yield return Wait(0.3f);
            Assert.IsFalse(finder.Visible, "looking back: the hint hides");
        }

        void Face(Vector3 dir) => _head.rotation = Quaternion.LookRotation(dir, Vector3.up);

        void AssertComfortable(BoardFinderView finder)
        {
            float a = Vector3.Angle(_head.forward, finder.Root.position - _head.position);
            Assert.Less(a, 30f, $"hint centre is {a:F1}° from the gaze");
            float d = Vector3.Distance(_head.position, finder.Root.position);
            Assert.GreaterOrEqual(d, 0.5f, "beyond the 0.5 m near limit");
        }

        static IEnumerator Wait(float seconds)
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < seconds) yield return null;
        }
    }
}
