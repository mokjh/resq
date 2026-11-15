using System.IO;
using NUnit.Framework;
using Resq.Game;
using Resq.XR;
using UnityEngine;

namespace Resq.Tests
{
    /// <summary>
    /// What a judge meets in the first minutes on a headset nobody on the team has worn: no permission dialog at
    /// launch, every mission open, and a recentre that brings the virtual table back in front of them without
    /// touching a real-table board or the run. Evidence kind: automated (Editor PlayMode; files and transforms only,
    /// no device).
    /// </summary>
    public sealed class JudgeProofTest
    {
        static string GamePath(string rel) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", rel));

        [Test]
        public void FirstLaunchAsksForNoPermission()
        {
            var manifest = File.ReadAllText(GamePath("Assets/Plugins/Android/AndroidManifest.xml"));
            StringAssert.Contains("android:name=\"unityplayer.SkipPermissionsDialog\" android:value=\"true\"", manifest,
                "Unity must not show its permission dialog at launch");
            var scene = File.ReadAllText(GamePath("Assets/Resq/Scenes/Operation_XR.unity"));
            StringAssert.Contains("propertyPath: requestScenePermissionOnStartup\n      value: 0", scene.Replace("\r\n", "\n"),
                "OVRManager must not ask for room data at startup");
            StringAssert.Contains("LoadSceneOnStartup: 0", scene, "MRUK must not read room data before the player picks their table");
            StringAssert.DoesNotContain("LoadSceneOnStartup: 1", scene);
            // without a shared room (or an Editor dev room) placement goes straight to the virtual table
            if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable(TablePlacement.DevRoomEnv)))
                Assert.IsFalse(TablePlacement.RoomDataAllowed());
        }

        [Test]
        public void EveryMissionIsOpenOnAFreshSave()
        {
            Assert.IsTrue(new Settings().OpenPlay, "Open play is on by default");
            Assert.IsTrue(new SaveData().Settings.OpenPlay, "a new save starts with Open play on");
        }

        [Test]
        public void RecentreBringsTheVirtualTableBackOnly()
        {
            var head = new GameObject("Head").transform;
            var virtualBoard = new GameObject("Board (virtual)").transform;
            new GameObject("VirtualTable").transform.SetParent(virtualBoard, false);
            var tableBoard = new GameObject("Board (table)").transform;
            try
            {
                virtualBoard.SetPositionAndRotation(new Vector3(0, 0.8f, 0.55f), Quaternion.identity);
                tableBoard.SetPositionAndRotation(new Vector3(0, 0.75f, 0.6f), Quaternion.identity);
                // the player turned round and recentred, seated somewhere else
                head.SetPositionAndRotation(new Vector3(1.5f, 1.2f, -0.4f), Quaternion.Euler(10f, 180f, 0f));

                Assert.IsTrue(TablePlacement.RecentreBoard(virtualBoard, head));
                var expected = head.position + Vector3.back * 0.55f + Vector3.down * 0.30f;
                Assert.Less(Vector3.Distance(virtualBoard.position, expected), 1e-3f, "virtual board 0.55 m in front, 0.30 m below the eyes");
                Assert.Less(Vector3.Angle(virtualBoard.forward, Vector3.back), 0.1f, "faces the way the player looks, level");
                Assert.Less(Vector3.Angle(virtualBoard.up, Vector3.up), 0.1f);

                Assert.IsFalse(TablePlacement.RecentreBoard(tableBoard, head), "a board on a real table stays on the table");
                Assert.Less(Vector3.Distance(tableBoard.position, new Vector3(0, 0.75f, 0.6f)), 1e-6f);
            }
            finally
            {
                Object.Destroy(head.gameObject); Object.Destroy(virtualBoard.gameObject); Object.Destroy(tableBoard.gameObject);
            }
        }
    }
}
