using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Resq.Game;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Resq.Tests
{
    /// <summary>
    /// The screen flow (U01–U24 subset reachable on the desktop), driven only through pointer presses on menu
    /// buttons, the same path hands, head dwell and the mouse use. Saves go to a temporary folder, never the
    /// player's. Covers: first welcome → hub → missions → briefing → operation; pause → leave with confirmation;
    /// settings persist to disk; a won run is recorded and leads to results, debrief and aftercare; a damaged save
    /// offers the backup without erasing anything. Evidence: automated (Editor PlayMode).
    /// </summary>
    public sealed class GameFlowTest
    {
        const string ScenePath = "Assets/Resq/Scenes/Operation_Desktop.unity";
        string _dir;
        GameBootstrap _boot;
        GameFlow _flow;
        Pointer _pointer;

        sealed class Pointer : IPointerSource
        {
            public Ray Ray; public bool Valid, Pressed;
            public PointerSample Sample() => new PointerSample { Kind = PointerKind.Desktop, Valid = Valid, Ray = Ray, Pressed = Pressed };
            public void Feedback(Pickable hover, float progress) { }
        }

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "resq_flow_test_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SaveStore.Folder = _dir;
        }

        [TearDown]
        public void TearDown()
        {
            SaveStore.Folder = null;
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        [UnityTest]
        public IEnumerator WelcomeHubBriefingOperationPauseAndLeave()
        {
            yield return Boot();
            AssertScreen("U02");
            yield return Press("Hands-free");
            AssertScreen("U02");
            Assert.IsTrue(SaveStore.Current.Settings.HeadOnly, "comfort choice applies");
            yield return Press("Play with hands");
            yield return Press("Begin");
            AssertScreen("U06");
            Assert.IsTrue(SaveStore.Current.WelcomeDone && File.Exists(SaveStore.PrimaryPath), "welcome is remembered on disk");
            Assert.IsTrue(_boot.Interaction.MenuOnly, "the board can't be changed behind a menu");
            Assert.IsFalse(_boot.Panel.gameObject.activeSelf, "the tool tray is hidden in menus");

            yield return Press("Missions");
            AssertScreen("U07");
            yield return Press("Frostline");
            AssertScreen("U07");
            Assert.AreEqual("W3-M1", _boot.Driver.MissionId, "the board previews the selected world");
            Assert.IsNull(FindButton("M2"), "later missions are locked until the previous one is done");
            yield return Press("Alder Valley");
            yield return Press("M1");
            AssertScreen("U08");
            StringAssert.Contains("Kit", _flow.Menu.Current.Body, "the briefing lists the default kit");
            yield return Press("Skip the guide");
            Assert.AreEqual(FlowState.Operation, _flow.State);
            Assert.IsFalse(_boot.Interaction.MenuOnly);
            Assert.IsTrue(_boot.Panel.gameObject.activeSelf && _boot.Hud.gameObject.activeSelf, "HUD and tray come back for play");
            Assert.IsTrue(_boot.Driver.Paused, "a run starts paused for planning");
            Assert.IsFalse(_boot.Driver.Guided);

            yield return Press("Menu");
            AssertScreen("U15");
            yield return Press("Return to hub");
            AssertScreen("confirm");
            yield return Press("Cancel");
            AssertScreen("U15");
            yield return Press("Return to hub");
            yield return Press("Leave");
            AssertScreen("U06");
        }

        [UnityTest]
        public IEnumerator SettingsPersistAndApply()
        {
            File.WriteAllText(SaveStore.PrimaryPath, JsonUtility.ToJson(new SaveData { WelcomeDone = true }));
            yield return Boot();
            AssertScreen("U06");
            yield return Press("Settings");
            AssertScreen("U21");
            yield return Press("Text size");
            yield return Press("Contrast");
            yield return Press("Dwell time");
            var onDisk = JsonUtility.FromJson<SaveData>(File.ReadAllText(SaveStore.PrimaryPath));
            Assert.AreEqual(125, onDisk.Settings.TextScale);
            Assert.IsTrue(onDisk.Settings.HighContrast);
            Assert.AreEqual(1050, onDisk.Settings.DwellMs);
            Assert.AreEqual(1.25f, MenuPanel.TextScale, 1e-4f, "text scale applies to menus");
            Assert.AreEqual(1.05f, _boot.Interaction.DwellSeconds, 1e-4f, "dwell time applies to the pointer layer");
            Assert.AreEqual(1.25f, Comfort.UiScale, 1e-4f, "text size also scales the HUD and tool tray");
            Assert.IsTrue(Comfort.HighContrast, "high contrast applies to the HUD panels");
            yield return Press("Open play");
            yield return Press("Back");
            yield return Press("Missions");
            Assert.IsNotNull(FindButton("M5"), "open play unlocks every mission");
            yield return Press("Back");
            yield return Press("Settings");
            yield return Press("Text size");   // 150
            yield return Press("Text size");   // back to 100
            yield return Press("Contrast");
            Assert.AreEqual(1f, MenuPanel.TextScale, 1e-4f);
        }

        [UnityTest]
        public IEnumerator WonRunIsRecordedThenResultsDebriefAftercare()
        {
            File.WriteAllText(SaveStore.PrimaryPath, JsonUtility.ToJson(new SaveData { WelcomeDone = true }));
            yield return Boot();
            yield return Press("Start");          // hub: first mission
            AssertScreen("U08");
            yield return Press("Skip the guide");
            Assert.AreEqual(FlowState.Operation, _flow.State);
            var driver = _boot.Driver;
            // the validated strategy plays through the normal command path; time advances in command-mode steps
            driver.StartAutopilot("A");
            for (int i = 0; i < 20000 && !driver.Sim.IsOver; i++) driver.Advance(1);
            Assert.IsTrue(driver.Sim.S.C.Success, "strategy A wins W1-M1");
            float t0 = Time.realtimeSinceStartup;
            while (_flow.State != FlowState.Results && Time.realtimeSinceStartup - t0 < 6f) yield return null;
            AssertScreen("U18");
            var rec = JsonUtility.FromJson<SaveData>(File.ReadAllText(SaveStore.PrimaryPath)).Get("W1-M1");
            Assert.IsNotNull(rec, "the run is saved");
            Assert.IsTrue(rec.Completed);
            Assert.AreEqual(driver.Sim.S.C.Stars, rec.BestStars);
            Assert.IsTrue(SaveStore.Unlocked("W1-M2"), "completing M1 unlocks M2");
            yield return Press("What happened");
            AssertScreen("U19");
            StringAssert.Contains("Lesson", _flow.Menu.Current.Body);
            yield return Press("Aftercare");
            AssertScreen("U20");
            // the valley recovers on the table: water recedes, hazards fade, everyone home cheers (presentation only)
            var recovery = _boot.Board.GetComponent<RecoveryView>();
            Assert.IsNotNull(recovery, "aftercare plays the recovery");
            float r0 = Time.realtimeSinceStartup;
            while (recovery.Progress < 1f && Time.realtimeSinceStartup - r0 < recovery.Seconds + 3f) yield return null;
            Assert.AreEqual(1f, recovery.Progress, 1e-4f, "recovery completes");
            Assert.AreEqual(1f, _boot.Board.Water.Recede, 1e-4f, "the flood recedes to the starting water");
            Assert.AreEqual(1f, _boot.Board.Hazards.Fade, 1e-4f, "hazard overlays fade");
            Assert.IsTrue(_boot.Board.Actors.Celebrate, "everyone home cheers");
            Assert.IsTrue(driver.Sim.S.C.Success, "the simulation is untouched");
            yield return Press("Hub");
            AssertScreen("U06");
            Assert.AreEqual(0f, recovery.Progress, "leaving aftercare restores the live view");
            Assert.AreEqual(0f, _boot.Board.Water.Recede);
            Assert.IsFalse(_boot.Board.Actors.Celebrate);
        }

        [UnityTest]
        public IEnumerator CommandStepControlsAndRestartConfirmation()
        {
            File.WriteAllText(SaveStore.PrimaryPath, JsonUtility.ToJson(new SaveData { WelcomeDone = true }));
            yield return Boot();
            var driver = _boot.Driver;
            driver.LoadRun("W1-M1", "wide", Resq.Sim.RuleProfile.Command, false);
            yield return null; yield return null;
            Assert.AreEqual(FlowState.Operation, _flow.State, "a run started outside the menus is played, not previewed");
            yield return Press("+5 s");
            Assert.AreEqual(50, driver.Sim.S.C.O, "+5 s advances exactly 50 operation steps");
            Assert.IsTrue(driver.Paused, "stepping keeps the run paused");
            yield return Press("+1 s");
            Assert.AreEqual(60, driver.Sim.S.C.O);
            yield return Press("Restart");
            AssertScreen("confirm");
            Assert.AreEqual(60, driver.Sim.S.C.O, "nothing resets before confirming");
            yield return Press("Cancel");
            Assert.AreEqual(FlowState.Operation, _flow.State);
            Assert.AreEqual(60, driver.Sim.S.C.O);
            yield return Press("Restart");
            yield return Press("Restart");
            Assert.AreEqual(0, driver.Sim.S.C.O, "confirmed restart resets the run");
            Assert.AreEqual(FlowState.Operation, _flow.State);
        }

        [UnityTest]
        public IEnumerator ChallengeUnlocksAfterBaseWinAndIsScoredSeparately()
        {
            var save = new SaveData { WelcomeDone = true };
            yield return Boot();
            File.WriteAllText(SaveStore.PrimaryPath, JsonUtility.ToJson(save));
            // before the base win: listed but locked
            SaveStore.Load();
            _flow.ShowHub();
            yield return Press("Missions");
            yield return Press("M1");
            AssertScreen("U08");
            Assert.IsNull(FindButton("Challenge: Early Surge"), "a challenge stays locked until the mission is won");
            yield return Press("Back");
            yield return Press("Back");
            // after the base win
            var won = new SaveData { WelcomeDone = true };
            var w = won.Get("W1-M1", true); w.Completed = true; w.BestStars = 2; w.Plays = 1;
            File.WriteAllText(SaveStore.PrimaryPath, JsonUtility.ToJson(won));
            SaveStore.Load();
            _flow.ShowHub();
            yield return Press("Missions");
            yield return Press("M1");
            yield return Press("Challenge: Early Surge");
            AssertScreen("U08-challenge");
            yield return Press("Start planning");
            var driver = _boot.Driver;
            Assert.AreEqual("W1-M1-early-surge", driver.ChallengeId);
            Assert.AreEqual(Resq.Sim.RuleProfile.Standard, driver.Profile, "challenges use Standard rules");
            driver.StartAutopilot("A");
            for (int i = 0; i < 20000 && !driver.Sim.IsOver; i++) driver.Advance(1);
            Assert.IsTrue(driver.Sim.S.C.Success, "the validated plan wins the challenge in the game, as in SimLab");
            float t0 = Time.realtimeSinceStartup;
            while (_flow.State != FlowState.Results && Time.realtimeSinceStartup - t0 < 6f) yield return null;
            AssertScreen("U18");
            var disk = JsonUtility.FromJson<SaveData>(File.ReadAllText(SaveStore.PrimaryPath));
            Assert.IsTrue(disk.Get("W1-M1-early-surge").Completed, "the challenge result is saved under its own id");
            Assert.AreEqual(2, disk.Get("W1-M1").BestStars, "the base mission's best is untouched");
            Assert.AreEqual("W1-M1", disk.LastMission);
        }

        [UnityTest]
        public IEnumerator DamagedSaveOffersBackupWithoutErasing()
        {
            var good = new SaveData { WelcomeDone = true };
            good.Get("W1-M1", true).Completed = true;
            File.WriteAllText(SaveStore.BackupPath, JsonUtility.ToJson(good));
            File.WriteAllText(SaveStore.PrimaryPath, "{ this is not json");
            yield return Boot();
            AssertScreen("U24");
            Assert.AreEqual("{ this is not json", File.ReadAllText(SaveStore.PrimaryPath), "nothing is overwritten before the player chooses");
            yield return Press("Recover from backup");
            AssertScreen("U06");
            Assert.IsTrue(SaveStore.Current.Get("W1-M1").Completed, "progress came back from the backup");
            Assert.IsTrue(JsonUtility.FromJson<SaveData>(File.ReadAllText(SaveStore.PrimaryPath)).Valid(out _), "the recovered save is written back");
        }

        // ------------------------------------------------------------------ helpers
        IEnumerator Boot()
        {
#if UNITY_EDITOR
            var op = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            while (!op.isDone) yield return null;
#else
            Assert.Ignore("Editor only");
#endif
            for (int i = 0; i < 6; i++) yield return null;
            _boot = Object.FindFirstObjectByType<GameBootstrap>();
            _flow = _boot.Flow;
            Assert.IsNotNull(_flow, "the game flow is part of the operation scene");
            _boot.Interaction.ClearSources();
            _pointer = new Pointer();
            _boot.Interaction.AddSource(_pointer);
        }

        void AssertScreen(string id)
        {
            Assert.IsNotNull(_flow.Menu.Current, $"expected screen {id}, but no menu is showing (state {_flow.State})");
            Assert.AreEqual(id, _flow.Menu.Current.Id, $"screen (state {_flow.State}, title '{_flow.Menu.Current.Title}')");
            int visible = 0;
            foreach (Transform card in _flow.Menu.transform) if (card.gameObject.activeSelf) visible++;
            Assert.AreEqual(1, visible, "exactly one menu card is visible (an old card must never show through)");
        }

        static Pickable FindButton(string label) =>
            Object.FindObjectsByType<Pickable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                  .FirstOrDefault(p => p.Kind == PickKind.Button && p.Label == label && p.GetComponent<Collider>() != null);

        IEnumerator Press(string label)
        {
            yield return null;   // a rebuilt menu has its colliders in place after one frame
            var target = FindButton(label);
            Assert.IsNotNull(target, $"no '{label}' button on screen {_flow.Menu.Current?.Id ?? _flow.State.ToString()}");
            Physics.SyncTransforms();
            var c = target.GetComponent<Collider>().bounds.center;
            var cam = Camera.main.transform.position;
            _pointer.Ray = new Ray(cam, (c - cam).normalized);
            _pointer.Valid = true; _pointer.Pressed = false;
            yield return null;
            Assert.AreEqual(target, _boot.Interaction.Hover, $"'{label}' should be under the pointer (occluded?)");
            _pointer.Pressed = true;
            yield return null;
            _pointer.Pressed = false;
            yield return null;
            _pointer.Valid = false;
            yield return null;
        }
    }
}
