using System;
using System.Collections.Generic;
using System.Text;
using Resq.Sim;
using UnityEngine;

namespace Resq.Game
{
    /// <summary>Screen states from the UX inventory (Resq_Production_Pack/docs/04 §6). U11 is the operation itself.</summary>
    public enum FlowState
    {
        Boot,              // U01
        Welcome,           // U02
        Placement,         // U03–U05 (XR: permission, surface, calibration)
        Hub,               // U06
        Missions,          // U07
        Briefing,          // U08 + U09 (fixed default kit)
        Operation,         // U10 planning + U11 active
        Paused,            // U15
        Results,           // U18
        Debrief,           // U19
        Aftercare,         // U20
        Settings,          // U21
        Help,              // U22
        Credits,           // U23
        TrackingRecovery,  // U16
        AnchorRecovery,    // U17
        SaveRecovery,      // U24
        Confirm,           // destructive-action confirmation (reset, leave a run)
    }

    /// <summary>Player-facing mission text from content/briefings.json (extracted from the world bible).</summary>
    public static class Briefings
    {
        static JsonNode _doc;
        static JsonNode Doc => _doc ?? (_doc = Load());
        static JsonNode Load()
        {
            var a = Resources.Load<TextAsset>("ResqContent/briefings");
            return a == null ? JsonNode.NewObject() : JsonNode.Parse(a.text);
        }
        public static JsonNode Mission(string id) => Doc["missions"]?[id];
        public static JsonNode World(string id) => Doc["worlds"]?[id];
        public static string MissionText(string id, string key) => Mission(id)?.Str(key, "") ?? "";
        public static string WorldText(string id, string key) => World(id)?.Str(key, "") ?? "";
    }

    /// <summary>
    /// The game's screen flow: boot and save validation, first welcome, placement, hub, mission select, briefing,
    /// the operation, pause, results, causal debrief, aftercare, settings, help, credits/privacy and the recovery
    /// states. Menus float above the board as one <see cref="MenuPanel"/>; the board behind shows the selected
    /// mission as a paused diorama. Every screen has a safe way back, and nothing is erased without consent.
    /// </summary>
    public sealed class GameFlow : MonoBehaviour
    {
        public static readonly string[] Worlds = { "W1", "W2", "W3", "W4", "W5" };
        /// <summary>Dev/test: skip the flow and start straight into the operation (the pre-flow behaviour).</summary>
        public static bool Disabled;
        public static GameFlow Instance { get; private set; }

        public GameBootstrap Boot;
        public MenuPanel Menu { get; private set; }
        public FlowState State { get; private set; } = FlowState.Boot;
        public string SelectedWorld { get; private set; } = "W1";
        public string SelectedMission { get; private set; } = "W1-M1";
        /// <summary>Set by the XR assembly: hands currently tracked (null on the desktop).</summary>
        public static Func<bool> HandsTracked;
        /// <summary>Set by the XR assembly: re-run placement (move the board, or relocalise after anchor loss).</summary>
        public static Action<GameBootstrap> Replace;
        /// <summary>Set by the XR assembly: switch to the virtual table.</summary>
        public static Action<GameBootstrap> UseVirtualTable;
        /// <summary>Set by the XR assembly: the player confirmed the board's pose (save it as the spatial anchor).</summary>
        public static Action<GameBootstrap> PlacementConfirmed;

        SimDriver Driver => Boot.Driver;
        // placement controls exist only on the headset (on the desktop the board has a fixed place)
        bool CanReplace => Boot.XrActive && Replace != null;
        bool CanVirtual => Boot.XrActive && UseVirtualTable != null;
        bool _flowLoad;            // the next OnRunLoaded comes from the flow (preview or start), not from outside
        bool _previewRun;          // the loaded run is a menu backdrop, not a run in play
        bool _recorded;
        float _overAt = -1;
        FlowState _returnTo = FlowState.Hub;
        readonly List<(long tick, string text)> _timeline = new List<(long, string)>();
        readonly HashSet<int> _groupsSafe = new HashSet<int>();
        int _backups, _losses, _collapses;
        string _firstLoss = "";
        bool _improved;
        float _handsLostAt = -1;
        float _resumeAt = -1;

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        public void Init(GameBootstrap boot)
        {
            Boot = boot;
            var go = new GameObject("Menu");
            go.transform.SetParent(boot.BoardRoot, false);
            // over the board's middle, 0.67 m from the seated head (virtual table) and standing just clear of the terrain:
            // a tall card then reads at the natural resting gaze instead of rising above eye level, and sits beyond
            // the 0.5 m near limit for UI (FovTest; was 0.43 m away with a briefing card's top 18° above eye level)
            go.transform.localPosition = new Vector3(0, 0.08f, 0.12f);
            Menu = go.AddComponent<MenuPanel>();
            Driver.OnRunLoaded += OnRunLoaded;
            Driver.OnSimEvent += OnSimEvent;
        }

        // ------------------------------------------------------------------ entry points
        /// <summary>U01: validate the save, then welcome (first run), recovery, or the hub.</summary>
        public void Begin(bool xrPlacementPending)
        {
            var result = SaveStore.Load();
            ApplySettings();
            SelectedMission = SaveStore.Current.LastMission ?? "W1-M1";
            SelectedWorld = SelectedMission.Substring(0, 2);
            // an operation already started from outside the flow (tests, dev tools): leave it alone
            if (Driver.Sim != null && !_previewRun) { Enter(FlowState.Operation); return; }
            if (result == SaveStore.LoadResult.PrimaryDamaged) { ShowSaveRecovery(); return; }
            if (!SaveStore.Current.WelcomeDone) { ShowWelcome(xrPlacementPending); return; }
            if (xrPlacementPending) { ShowPlacementWait(); return; }
            ShowHub();
        }

        /// <summary>Called by placement once the board sits on a surface (U04/U05): calibrate, then the hub.</summary>
        public void PlacementDone(string layout, string surface)
        {
            Boot.LayoutClass = layout;
            _placement = surface;
            // the first welcome (or a save decision) is on screen: calibrate once the player moves on
            if (State == FlowState.Welcome || State == FlowState.SaveRecovery || State == FlowState.Confirm) return;
            ShowCalibration(surface);
        }
        string _placement;

        // ------------------------------------------------------------------ run lifecycle
        void OnRunLoaded()
        {
            _timeline.Clear(); _groupsSafe.Clear();
            _backups = _losses = _collapses = 0; _firstLoss = ""; _recorded = false; _overAt = -1; _improved = false;
            if (_flowLoad) { _flowLoad = false; return; }
            // a run loaded from outside the flow (dev picker, tests): it's a real run
            _previewRun = false;
            if (State != FlowState.Operation) Enter(FlowState.Operation);
        }

        void LoadPreview(string missionId)
        {
            if (!ContentLibrary.HasMission(missionId)) return;
            if (Driver.Sim != null && _previewRun && Driver.MissionId == missionId && Driver.LayoutClass == Boot.LayoutClass) return;
            _flowLoad = true; _previewRun = true;
            Driver.LoadRun(missionId, Boot.LayoutClass, ProfileFromSettings(), false);
        }

        void StartMission(string missionId, bool guided, RuleProfile? profile = null, string challenge = null)
        {
            SelectedMission = missionId;
            Boot.MissionId = missionId;
            // challenges are defined for Standard rules only (spec 04 §6)
            Boot.Profile = challenge != null ? RuleProfile.Standard : profile ?? ProfileFromSettings();
            _flowLoad = true; _previewRun = false;
            Driver.LoadRun(missionId, Boot.LayoutClass, Boot.Profile, guided && challenge == null, challenge);
            SaveStore.Current.LastMission = missionId;
            SaveStore.Save();
            Enter(FlowState.Operation);
        }

        static RuleProfile ProfileFromSettings()
        {
            var s = SaveStore.Current.Settings;
            return s.HeadOnly ? RuleProfile.Command : s.Pacing == "relaxed" ? RuleProfile.Relaxed : RuleProfile.Standard;
        }

        void Enter(FlowState s)
        {
            State = s;
            if (s != FlowState.Aftercare && Boot != null && Boot.Board != null)
            {
                var playing = Boot.Board.GetComponent<RecoveryView>();   // leaving aftercare restores the live valley at once
                if (playing != null) playing.Stop();
            }
            bool operation = s == FlowState.Operation;
            Boot.Interaction.MenuOnly = !operation;
            if (operation) { Menu.Hide(); Boot.Interaction.Cancel(); }
            FrameDesktopCamera();
            SetHud(operation || s == FlowState.Paused || s == FlowState.TrackingRecovery || s == FlowState.AnchorRecovery);
            Debug.Log($"[RESQ flow] {s}{(Menu.Current != null && !operation ? " " + Menu.Current.Id : "")}");
        }

        /// <summary>Desktop/Editor only: frame the open card, or the board during play. (On the headset the player just looks.)</summary>
        void FrameDesktopCamera()
        {
            var orbit = Camera.main != null ? Camera.main.GetComponent<DesktopOrbit>() : null;
            if (orbit == null) return;
            if (Menu.Height > 0)
            {
                orbit.Target = Menu.transform.position + Vector3.up * (Menu.Height * 0.42f);
                orbit.Distance = Mathf.Clamp(Menu.Height * 1.9f, 0.42f, 0.7f);
                orbit.Pitch = 24f;
            }
            else { orbit.Target = Boot.BoardRoot.position; orbit.Distance = 0.62f; orbit.Pitch = 38f; }
        }

        void SetHud(bool on)
        {
            if (Boot.Panel != null && Boot.Panel.gameObject.activeSelf != on)
            {
                Boot.Panel.gameObject.SetActive(on);
                if (on && Driver.Def != null) Boot.Panel.Rebuild();
            }
            if (Boot.Hud != null && Boot.Hud.gameObject.activeSelf != on)
            {
                Boot.Hud.gameObject.SetActive(on);
                if (on && Driver.Def != null) Boot.Hud.Rebuild();
            }
        }

        void Show(FlowState s, MenuSpec spec)
        {
            Menu.Show(spec);
            Enter(s);
        }

        void Update()
        {
            if (Driver?.Sim == null) return;
            if (State == FlowState.Operation && !_previewRun)
            {
                if (Driver.Sim.IsOver)
                {
                    if (_overAt < 0) _overAt = Time.unscaledTime;
                    else if (Time.unscaledTime - _overAt > 2.2f) ShowResults();   // let the outcome banner land first
                }
                WatchHands();
            }
            if (_resumeAt > 0 && Time.unscaledTime >= _resumeAt) { _resumeAt = -1; Enter(FlowState.Operation); Driver.SetPaused(false); }
        }

        /// <summary>U16: the interacting hands vanished for more than a moment while the run is live → pause calmly.</summary>
        void WatchHands()
        {
            if (HandsTracked == null || SaveStore.Current.Settings.HeadOnly || Driver.Paused) { _handsLostAt = -1; return; }
            if (HandsTracked()) { _handsLostAt = -1; return; }
            if (_handsLostAt < 0) { _handsLostAt = Time.unscaledTime; return; }
            if (Time.unscaledTime - _handsLostAt > 1.2f) { _handsLostAt = -1; ShowTrackingRecovery(); }
        }

        // ------------------------------------------------------------------ events → debrief timeline
        void OnSimEvent(SimEvent e)
        {
            if (_previewRun || Driver.Def == null) return;
            var d = Driver.Def;
            switch (e.Kind)
            {
                case SimEventKind.Warning:
                    if (e.Subject >= 0 && e.Subject < d.Events.Length) Add(e.S, WarningLine(d.Events[e.Subject]));
                    break;
                case SimEventKind.ResidentEvacuated:
                {
                    int g = e.Subject >= 0 && e.Subject < d.ResidentGroup.Length ? d.ResidentGroup[e.Subject] : -1;
                    if (g >= 0 && !_groupsSafe.Contains(g) && GroupDone(g)) { _groupsSafe.Add(g); Add(e.S, $"The {BoardView.Pretty(d.GroupIds[g])} group reached safety"); }
                    break;
                }
                case SimEventKind.ResidentBackup:
                {
                    _backups++;
                    int g = e.Subject >= 0 && e.Subject < d.ResidentGroup.Length ? d.ResidentGroup[e.Subject] : -1;
                    string who = g >= 0 ? $"the {BoardView.Pretty(d.GroupIds[g])} group" : "a resident";
                    if (_firstLoss.Length == 0) _firstLoss = $"Backup crews had to collect {who} at {Clock(e.S)}.";
                    if (_backups == 1 || g >= 0) Add(e.S, $"Backup called for {who}");
                    break;
                }
                case SimEventKind.EquipmentBroken: _losses++; Add(e.S, "Equipment was lost to the hazard"); if (_firstLoss.Length == 0) _firstLoss = $"Equipment was lost at {Clock(e.S)}."; break;
                case SimEventKind.SupportCollapsed: _collapses++; Add(e.S, "A structure collapsed"); if (_firstLoss.Length == 0) _firstLoss = $"A structure collapsed at {Clock(e.S)}."; break;
                case SimEventKind.ShelterExhausted: Add(e.S, "A shelter ran out of protection"); break;
                case SimEventKind.GeneratorShutdown: Add(e.S, "A generator shut down"); break;
                case SimEventKind.LavaArrived: Add(e.S, "Lava reached a crossing"); break;
            }
        }

        bool GroupDone(int g)
        {
            var S = Driver.Sim.S;
            foreach (int r in Driver.Def.GroupResidents[g]) if (S.RState[r] != (byte)ResidentState.Evacuated) return false;
            return true;
        }

        void Add(long tick, string text)
        {
            if (_timeline.Count > 0 && _timeline[_timeline.Count - 1].text == text) return;
            _timeline.Add((tick, text));
        }

        static string Clock(long scenarioTick) { long s = Math.Max(0, scenarioTick / 10); return $"{s / 60}:{s % 60:00}"; }

        static string WarningLine(EventDef ev)
        {
            foreach (var op in ev.Ops)
                switch (op.Kind)
                {
                    case EventOpKind.SetSourceRate: return "Warning: water rising";
                    case EventOpKind.SetEdgeEnabled: return "Warning: a route closing";
                    case EventOpKind.ReleaseGroup: return "Warning: more residents arriving";
                    case EventOpKind.IgniteRegion: case EventOpKind.AddHeat: return "Warning: fire spreading";
                    case EventOpKind.SetWind: return "Warning: the wind turning";
                    case EventOpKind.Tremor: return "Warning: a tremor";
                    case EventOpKind.ReleaseAvalanche: case EventOpKind.WarnRunout: return "Warning: avalanche risk";
                    case EventOpKind.SetTideSegment: return "Warning: the tide turning";
                    case EventOpKind.AdvanceLava: return "Warning: lava advancing";
                    case EventOpKind.SetStabilityLoss: case EventOpKind.SetSupportLoad: case EventOpKind.WarnSupport: return "Warning: a structure weakening";
                }
            return "Warning";
        }

        // ------------------------------------------------------------------ U01 / U24 save recovery
        void ShowSaveRecovery()
        {
            var spec = new MenuSpec { Id = "U24", Title = "Your save needs attention", TitleIcon = Icon.Warning };
            bool backup = SaveStore.Recoverable != null;
            spec.Body = $"RESQ couldn't read your progress ({SaveStore.DamageReason})."
                + (backup ? " A backup from your last good session is available." : " No usable backup was found.")
                + " Nothing has been erased.";
            if (backup) spec.Items.Add(MenuOption.Button("Recover from backup", () => { SaveStore.Recover(); ApplySettings(); ShowHub(); }, true));
            spec.Items.Add(MenuOption.Button("Start new progress", () => Confirm("Start new progress?",
                "Your current progress will be replaced. The damaged file is kept aside, not deleted.", "Start new",
                () => { SaveStore.StartNew(); ApplySettings(); ShowWelcome(false); }, ShowSaveRecovery)));
            Show(FlowState.SaveRecovery, spec);
        }

        // ------------------------------------------------------------------ U02 welcome
        void ShowWelcome(bool placementPending)
        {
            if (!placementPending) LoadPreview("W1-M1");   // the first valley sits behind the welcome card
            var s = SaveStore.Current.Settings;
            var spec = new MenuSpec
            {
                Id = "U02", Title = "A little world needs a steady hand.", TitleIcon = Icon.People,
                Body = "Floods, fires, snow, tides and lava threaten five miniature valleys. Place equipment, plan routes and bring every resident home. You can change how you play at any time in Settings.",
                Columns = 2,
            };
            spec.Items.Add(new MenuOption { Label = "Play with hands", Detail = "pinch or poke", Selected = !s.HeadOnly, OnPress = () => { s.HeadOnly = false; ApplySettings(); ShowWelcome(placementPending); } });
            spec.Items.Add(new MenuOption { Label = "Hands-free", Detail = "look and hold", Selected = s.HeadOnly, OnPress = () => { s.HeadOnly = true; ApplySettings(); ShowWelcome(placementPending); } });
            spec.Items.Add(new MenuOption { Label = "Standard pace", Selected = s.Pacing != "relaxed", OnPress = () => { s.Pacing = "standard"; ShowWelcome(placementPending); } });
            spec.Items.Add(new MenuOption { Label = "Relaxed pace", Detail = "hazards move slower", Selected = s.Pacing == "relaxed", OnPress = () => { s.Pacing = "relaxed"; ShowWelcome(placementPending); } });
            spec.Footer.Add(MenuOption.Button("Begin", () =>
            {
                SaveStore.Current.WelcomeDone = true; SaveStore.Save();
                if (!placementPending) ShowHub();
                else if (_placement != null) ShowCalibration(_placement);
                else ShowPlacementWait();
            }, true));
            Show(FlowState.Welcome, spec);
        }

        // ------------------------------------------------------------------ U03–U05 placement
        void ShowPlacementWait()
        {
            var spec = new MenuSpec
            {
                Id = "U03", Title = "Finding a surface", TitleIcon = Icon.Box,
                Body = "Use your room's surfaces to place the rescue board. RESQ only reads where tables are; it never records camera images. You can play on a virtual table instead.",
            };
            if (CanVirtual) spec.Footer.Add(MenuOption.Button("Use a virtual table", () => UseVirtualTable(Boot)));
            Show(FlowState.Placement, spec);
        }

        void ShowCalibration(string surface)
        {
            LoadPreview(SelectedMission);
            var spec = new MenuSpec
            {
                Id = "U05", Title = "Is the board comfortable?", TitleIcon = Icon.Check, Columns = 3,
                Subtitle = surface,
                Body = "Raise, lower or move the board until you can reach the near edge without leaning. Settings can change your hand and text size.",
            };
            var b = Boot.BoardRoot;
            spec.Items.Add(MenuOption.Button("Raise", () => b.position += Vector3.up * 0.03f));
            spec.Items.Add(MenuOption.Button("Nearer", () => b.position -= Flat(b.forward) * 0.04f));
            spec.Items.Add(MenuOption.Button("Turn left", () => b.Rotate(0, -15f, 0, Space.World)));
            spec.Items.Add(MenuOption.Button("Lower", () => b.position -= Vector3.up * 0.03f));
            spec.Items.Add(MenuOption.Button("Farther", () => b.position += Flat(b.forward) * 0.04f));
            spec.Items.Add(MenuOption.Button("Turn right", () => b.Rotate(0, 15f, 0, Space.World)));
            if (CanVirtual) spec.Footer.Add(MenuOption.Button("Virtual table", () => UseVirtualTable(Boot)));
            spec.Footer.Add(MenuOption.Button("Done", () => { PlacementConfirmed?.Invoke(Boot); ShowHub(); }, true));
            Show(FlowState.Placement, spec);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }

        // ------------------------------------------------------------------ U06 hub
        public void ShowHub()
        {
            Driver.SetPaused(true);
            LoadPreview(SelectedMission);
            var (stars, done, total) = Totals();
            string next = NextMission();
            var spec = new MenuSpec
            {
                Id = "U06", Title = "RESQ", TitleIcon = Icon.People,
                Subtitle = $"{done} of {total} missions complete   ·   {stars} of {total * 3} stars",
                Columns = 2,
            };
            spec.Items.Add(MenuOption.Button(done == 0 ? "Start" : "Continue", () => ShowBriefing(next), true, $"{next}  {Title(next)}"));
            spec.Items.Add(MenuOption.Button("Missions", () => ShowMissions(SelectedWorld), false, "five worlds"));
            spec.Items.Add(MenuOption.Button("Settings", () => ShowSettings(FlowState.Hub)));
            spec.Items.Add(MenuOption.Button("How to play", () => ShowHelp(FlowState.Hub)));
            spec.Items.Add(MenuOption.Button("Credits & privacy", () => ShowCredits(FlowState.Hub)));
            if (CanReplace) spec.Items.Add(MenuOption.Button("Move the board", () => Replace(Boot)));
            Show(FlowState.Hub, spec);
        }

        (int stars, int done, int total) Totals()
        {
            int stars = 0, done = 0;
            var ids = ContentLibrary.MissionIds();
            foreach (var id in ids)
            {
                var p = SaveStore.Current.Get(id);
                if (p == null) continue;
                stars += p.BestStars;
                if (p.Completed) done++;
            }
            return (stars, done, ids.Count);
        }

        /// <summary>The first unlocked, not-yet-completed mission in campaign order; else the last one played.</summary>
        string NextMission()
        {
            foreach (var id in ContentLibrary.MissionIds())
            {
                var p = SaveStore.Current.Get(id);
                if ((p == null || !p.Completed) && SaveStore.Unlocked(id)) return id;
            }
            return SaveStore.Current.LastMission ?? "W1-M1";
        }

        static string Title(string id) => ContentLibrary.Contract(id)?.Str("title", id) ?? id;

        // ------------------------------------------------------------------ U07 world / mission select
        void ShowMissions(string world)
        {
            SelectedWorld = world;
            string first = world + "-M1";
            if (SelectedMission.Substring(0, 2) != world) SelectedMission = first;
            LoadPreview(SelectedMission);
            var spec = new MenuSpec
            {
                Id = "U07", Title = HudView.WorldName(world), Columns = 5,
                Subtitle = Briefings.WorldText(world, "thesis"),
                Body = Briefings.WorldText(world, "identity"),
                TitleIcon = world == "W2" ? Icon.Tree : world == "W3" ? Icon.Snow : world == "W4" ? Icon.Wave : world == "W5" ? Icon.Volcano : Icon.Mountain,
            };
            foreach (var w in Worlds)
            {
                var wl = w;
                spec.Items.Add(new MenuOption { Label = HudView.WorldName(w), Selected = w == world, OnPress = () => ShowMissions(wl) });
            }
            for (int m = 1; m <= 5; m++)
            {
                string id = $"{world}-M{m}";
                if (!ContentLibrary.HasMission(id)) { spec.Items.Add(MenuOption.Locked($"M{m}", "in development")); continue; }
                if (!SaveStore.Unlocked(id)) { spec.Items.Add(MenuOption.Locked($"M{m}", "finish M" + (m - 1))); continue; }
                var p = SaveStore.Current.Get(id);
                int st = p?.BestStars ?? 0;
                string detail = (p != null && p.Completed ? Stars(st) : "new") + "\n" + Title(id);
                spec.Items.Add(new MenuOption { Label = $"M{m}", Detail = detail, Primary = m == 1 && p == null, OnPress = () => ShowBriefing(id) });
            }
            spec.Footer.Add(MenuOption.Button("Back", ShowHub));
            Show(FlowState.Missions, spec);
        }

        // Atkinson Hyperlegible has no star or check glyphs: say it in words (an icon mesh carries the star elsewhere)
        static string Stars(int n) => n == 1 ? "1 star" : $"{n} stars";

        // ------------------------------------------------------------------ U08/U09 briefing + kit
        void ShowBriefing(string id)
        {
            SelectedMission = id; SelectedWorld = id.Substring(0, 2);
            LoadPreview(id);
            var c = ContentLibrary.Contract(id);
            var sb = new StringBuilder();
            string decision = Briefings.MissionText(id, "decision");
            string layout = Briefings.MissionText(id, "layout");
            if (layout.Length > 0) sb.Append("<b>Who needs help</b>  ").Append(layout).Append('\n');
            var threats = Briefings.Mission(id)?["threats"];
            if (threats != null && threats.Count > 0)
            {
                sb.Append("<b>Expect</b>  ");
                int i = 0;
                foreach (var t in threats.Elements()) { if (i++ > 0) sb.Append("; "); sb.Append(t.Str("text", "")); }
                sb.Append('\n');
            }
            string req = Briefings.MissionText(id, "required");
            if (req.Length > 0) sb.Append("<b>Goal</b>  ").Append(req).Append('\n');
            string mastery = Briefings.MissionText(id, "mastery");
            if (mastery.Length > 0) sb.Append("<b>Three stars</b>  ").Append(mastery).Append('\n');
            if (c != null)
            {
                sb.Append("<b>Kit</b>  ");
                int n = 0;
                foreach (var kv in c["default_loadout"].Properties())
                {
                    if (n++ > 0) sb.Append(", ");
                    sb.Append(kv.Value.AsLong()).Append(" × ").Append(ToolName(kv.Key));
                }
                sb.Append($"   ·   {c.Long("supply_budget")} supplies");
            }
            var spec = new MenuSpec { Id = "U08", Title = $"{Title(id)}", Subtitle = $"{HudView.WorldName(SelectedWorld)}  ·  {id}  ·  {decision}", Body = sb.ToString().TrimEnd() };
            bool guidedAvailable = c != null && c.Str("tutorial_policy") == "guided_first_play_then_unguided";
            var p = SaveStore.Current.Get(id);
            bool firstPlay = p == null || p.Plays == 0;
            spec.Columns = guidedAvailable ? 2 : 1;
            if (guidedAvailable)
            {
                spec.Items.Add(MenuOption.Button(firstPlay ? "Start with a guide" : "Guided", () => StartMission(id, true), firstPlay));
                spec.Items.Add(MenuOption.Button(firstPlay ? "Skip the guide" : "Start", () => StartMission(id, false), !firstPlay));
            }
            else spec.Items.Add(MenuOption.Button("Start planning", () => StartMission(id, false), true, "the clock waits until you press Start"));
            // challenges open after the base mission is won; they never gate the campaign
            foreach (var ch in ContentLibrary.ChallengesFor(id))
            {
                var chId = ch.Str("id");
                if (p == null || !p.Completed) { spec.Items.Add(MenuOption.Locked("Challenge: " + ChallengeName(ch), "win the mission first")); continue; }
                var cp = SaveStore.Current.Get(chId);
                spec.Items.Add(MenuOption.Button("Challenge: " + ChallengeName(ch), () => ShowChallenge(id, ch), false, cp != null && cp.Completed ? Stars(cp.BestStars) : "new"));
            }
            spec.Footer.Add(MenuOption.Button("Back", () => ShowMissions(SelectedWorld)));
            Show(FlowState.Briefing, spec);
        }

        static string ChallengeName(JsonNode ch)
        {
            switch (ch.Str("modifier"))
            {
                case "early_surge": return "Early Surge";
                case "alternate_dock": return "Alternate Dock";
                case "crosswind": return "Crosswind";
                case "high_tide_start": return "High Tide Start";
                case "careful_operation": return "Careful Operation";
                case "careful_light_kit": return "Careful, Light Kit";
                default: return BoardView.Pretty(ch.Str("modifier"));
            }
        }

        static string ChallengeText(JsonNode ch)
        {
            var p = ch["parameters"];
            switch (ch.Str("modifier"))
            {
                case "early_surge": return $"The first surge comes {-(p?.Long("shift_scenario_ticks") ?? 0) / 10} seconds earlier. Same kit, same goals.";
                case "alternate_dock": return $"The {BoardView.Pretty(p?.Str("group_id") ?? "")} group boards at a second landing beside their dock: boats take longer to reach it.";
                case "crosswind": return "The wind blows the other way, so fire spreads toward different ground.";
                case "high_tide_start": return "The tide starts high: low causeways are under water from the first second.";
                case "careful_operation": return "Lose no equipment. Everything you place must survive to the end.";
                case "careful_light_kit": return "Lose no equipment, with one barrier fewer and a smaller budget.";
                default: return "";
            }
        }

        void ShowChallenge(string missionId, JsonNode ch)
        {
            string chId = ch.Str("id");
            var cp = SaveStore.Current.Get(chId);
            var spec = new MenuSpec
            {
                Id = "U08-challenge", Title = "Challenge: " + ChallengeName(ch), TitleIcon = Icon.Star,
                Subtitle = $"{Title(missionId)}  ·  Standard rules  ·  scored separately",
                Body = ChallengeText(ch) + (cp != null && cp.Completed ? $"\n\nBest: {Stars(cp.BestStars)}, score {cp.BestScore}." : ""),
            };
            spec.Items.Add(MenuOption.Button("Start planning", () => StartMission(missionId, false, RuleProfile.Standard, chId), true));
            spec.Footer.Add(MenuOption.Button("Back", () => ShowBriefing(missionId)));
            Show(FlowState.Briefing, spec);
        }

        static string ToolName(string key)
        {
            foreach (ToolKind t in Enum.GetValues(typeof(ToolKind)))
                if (MapCompiler.ToolName(t) == key) return HudView.ToolLabel(t);
            return BoardView.Pretty(key);
        }

        // ------------------------------------------------------------------ U15 pause
        /// <summary>The persistent Menu control during play opens this.</summary>
        public void Pause()
        {
            if (State != FlowState.Operation) return;
            Driver.SetPaused(true);
            Boot.Interaction.Cancel();
            ShowPause();
        }

        /// <summary>The run controls' Restart: pause, then confirm.</summary>
        public void ConfirmRestart()
        {
            if (State != FlowState.Operation) return;
            bool wasPaused = Driver.Paused;
            Driver.SetPaused(true);
            Boot.Interaction.Cancel();
            Confirm("Restart this mission?", "The current attempt ends and the board resets.", "Restart",
                () => StartMission(Driver.MissionId, Driver.Guided, Driver.Profile, Driver.ChallengeId),   // a restart keeps the run's rules
                () => { Enter(FlowState.Operation); if (!wasPaused) Driver.SetPaused(false); });
        }

        void ShowPause()
        {
            var spec = new MenuSpec { Id = "U15", Title = "Paused", TitleIcon = Icon.Ring, Columns = 2, Body = "Your rescue is paused. Nothing moves until you resume." };
            spec.Items.Add(MenuOption.Button("Resume", Resume, true));
            spec.Items.Add(MenuOption.Button("Restart", () => Confirm("Restart this mission?", "The current attempt ends and the board resets.", "Restart",
                () => StartMission(Driver.MissionId, Driver.Guided, Driver.Profile, Driver.ChallengeId), ShowPause)));
            spec.Items.Add(MenuOption.Button("Settings", () => ShowSettings(FlowState.Paused)));
            spec.Items.Add(MenuOption.Button("How to play", () => ShowHelp(FlowState.Paused)));
            if (CanReplace) spec.Items.Add(MenuOption.Button("Move the board", () => Replace(Boot)));
            spec.Items.Add(MenuOption.Button("Return to hub", () => Confirm("Leave this mission?", "This attempt won't be saved.", "Leave", ShowHub, ShowPause)));
            Show(FlowState.Paused, spec);
        }

        /// <summary>Explicit resume; in hands-free play a short countdown first, so the next glance can't act by accident.</summary>
        void Resume()
        {
            if (SaveStore.Current.Settings.HeadOnly)
            {
                Show(FlowState.Paused, new MenuSpec { Id = "U15-countdown", Title = "Resuming…", Body = "Look at the board. The rescue continues in 3 seconds." });
                _resumeAt = Time.unscaledTime + 3f;
                return;
            }
            Enter(FlowState.Operation);
            // the run stays paused for planning until Start if it never started; otherwise it continues
            if (Driver.Sim.S.C.O > 0) Driver.SetPaused(false);
        }

        // ------------------------------------------------------------------ U16 / U17 recovery
        public void ShowTrackingRecovery()
        {
            Driver.SetPaused(true);
            Boot.Interaction.Cancel();
            var spec = new MenuSpec { Id = "U16", Title = "Hands out of view.", TitleIcon = Icon.Pinch, Body = "Your rescue is paused. Bring your hands back in front of you, then resume. Anything you were holding was put back." };
            spec.Items.Add(MenuOption.Button("Resume", Resume, true));
            spec.Items.Add(MenuOption.Button("Switch to hands-free", () => { SaveStore.Current.Settings.HeadOnly = true; ApplySettings(); SaveStore.Save(); Resume(); }));
            Show(FlowState.TrackingRecovery, spec);
        }

        /// <summary>Called by the XR assembly when the board's spatial anchor stops tracking.</summary>
        public void ShowAnchorRecovery()
        {
            Driver.SetPaused(true);
            Boot.Interaction.Cancel();
            var spec = new MenuSpec { Id = "U17", Title = "The board lost its place", TitleIcon = Icon.Warning, Body = "Your rescue is paused. Look around the room so the headset can find the table again, or use the virtual table." };
            if (CanReplace) spec.Items.Add(MenuOption.Button("Find the table again", () => Replace(Boot), true));
            if (CanVirtual) spec.Items.Add(MenuOption.Button("Use the virtual table", () => UseVirtualTable(Boot)));
            spec.Items.Add(MenuOption.Button("Resume here", Resume));
            Show(FlowState.AnchorRecovery, spec);
        }

        // ------------------------------------------------------------------ U18 results
        void ShowResults()
        {
            var S = Driver.Sim.S; var d = Driver.Def;
            if (!_recorded) { _recorded = true; _improved = SaveStore.RecordRun(Driver.MissionId, S.C.Success, S.C.Stars, S.C.ScoreValue, Driver.ChallengeId); }
            var sb = new StringBuilder();
            foreach (var (obj, label) in HudView.ObjectiveLines(d))
            {
                bool ok = obj < 0 ? S.C.Evacuated >= d.MinimumRescues : Driver.Sim.ObjectiveFulfilled(obj);
                sb.Append(ok ? "<color=#7BD389><b>Done</b></color>  " : "<color=#E5484D><b>Missed</b></color>  ").Append(label).Append('\n');
            }
            var best = SaveStore.Current.Get(Driver.ChallengeId ?? Driver.MissionId);
            var chNode = ContentLibrary.Challenge(Driver.ChallengeId);
            var spec = new MenuSpec
            {
                Id = "U18",
                Title = S.C.Success ? $"Rescue complete  {Stars(S.C.Stars)}" : "Backup crews took over",
                TitleIcon = S.C.Success ? Icon.Star : Icon.Warning,
                Subtitle = (chNode != null ? "Challenge: " + ChallengeName(chNode) + "   ·   " : "") + $"{S.C.Evacuated} of {d.TotalResidents} rescued   ·   score {S.C.ScoreValue}" + (_improved ? "   ·   new best" : best != null && best.Completed ? $"   ·   best {Stars(best.BestStars)}" : ""),
                Body = sb.ToString().TrimEnd(),
                Columns = 3,
            };
            spec.Items.Add(MenuOption.Button("What happened", ShowDebrief, !S.C.Success));
            spec.Items.Add(MenuOption.Button("Try again", () => StartMission(Driver.MissionId, false, Driver.Profile, Driver.ChallengeId)));
            string next = NextAfter(Driver.MissionId);
            if (S.C.Success) spec.Items.Add(MenuOption.Button("Aftercare", ShowAftercare, true));
            else if (next != null && SaveStore.Unlocked(next)) spec.Items.Add(MenuOption.Button("Next mission", () => ShowBriefing(next)));
            spec.Footer.Add(MenuOption.Button("Hub", ShowHub));
            Show(FlowState.Results, spec);
        }

        static string NextAfter(string id)
        {
            var ids = ContentLibrary.MissionIds();
            int i = ids.IndexOf(id);
            return i >= 0 && i + 1 < ids.Count ? ids[i + 1] : null;
        }

        // ------------------------------------------------------------------ U19 causal debrief
        void ShowDebrief()
        {
            var S = Driver.Sim.S;
            var sb = new StringBuilder();
            int shown = 0;
            foreach (var (tick, text) in _timeline)
            {
                if (shown++ >= 7) { sb.Append("…\n"); break; }
                sb.Append("<b>").Append(Clock(tick)).Append("</b>  ").Append(text).Append('\n');
            }
            if (_timeline.Count == 0) sb.Append("A quiet operation: no warnings fired before the end.\n");
            sb.Append('\n').Append("<b>Lesson</b>  ").Append(Lesson(S));
            var spec = new MenuSpec { Id = "U19", Title = "What happened", TitleIcon = Icon.Ring, Body = sb.ToString(), Columns = 2 };
            spec.Items.Add(MenuOption.Button("Try again", () => StartMission(Driver.MissionId, false, Driver.Profile, Driver.ChallengeId), true));
            if (S.C.Success) spec.Items.Add(MenuOption.Button("Aftercare", ShowAftercare));
            spec.Footer.Add(MenuOption.Button("Back", ShowResults));
            Show(FlowState.Debrief, spec);
        }

        /// <summary>One specific lesson: the first concrete loss, the mission's authored failure note, or the next star.</summary>
        string Lesson(SimState S)
        {
            string id = Driver.MissionId;
            if (!S.C.Success)
            {
                string note = Briefings.MissionText(id, "failure");
                return (_firstLoss.Length > 0 ? _firstLoss + " " : "") + (note.Length > 0 ? note : "Try protecting the route the stranded group needed.");
            }
            if (S.C.Stars < 3)
            {
                string mastery = Briefings.MissionText(id, "mastery");
                return (_firstLoss.Length > 0 ? _firstLoss + " " : "") + (mastery.Length > 0 ? "For three stars: " + mastery : "Finish the bonus goals for three stars.");
            }
            string payoff = Briefings.MissionText(id, "payoff");
            return payoff.Length > 0 ? payoff : "A clean rescue. Everyone is home.";
        }

        // ------------------------------------------------------------------ U20 aftercare
        void ShowAftercare()
        {
            string w = Driver.Def.WorldId;
            var spec = new MenuSpec
            {
                Id = "U20", Title = "Everyone you rescued is home.", TitleIcon = Icon.Check,
                Subtitle = HudView.WorldName(w) + "  ·  " + Driver.Def.Title,
                Body = "Take a look around. " + Briefings.WorldText(w, "aftercare"),
                Columns = 2,
            };
            spec.Items.Add(MenuOption.Button("Save a postcard", SavePostcard));
            var recovery = Recovery();
            string next = NextAfter(Driver.MissionId);
            if (next != null && SaveStore.Unlocked(next)) spec.Items.Add(MenuOption.Button("Next mission", () => ShowBriefing(next), true, Title(next)));
            else spec.Items.Add(MenuOption.Button("Missions", () => ShowMissions(SelectedWorld), true));
            spec.Footer.Add(MenuOption.Button("Hub", ShowHub));
            Show(FlowState.Aftercare, spec);
            recovery?.Play();   // the valley recovers on the table while the card is up
        }

        /// <summary>The board's aftercare view, created on first use (null without a board, e.g. some tests).</summary>
        RecoveryView Recovery()
        {
            if (Boot == null || Boot.Board == null) return null;
            var r = Boot.Board.GetComponent<RecoveryView>();   // Unity null: no ?? on components
            if (r == null) { r = Boot.Board.gameObject.AddComponent<RecoveryView>(); r.Board = Boot.Board; }
            return r;
        }

        void SavePostcard()
        {
            string dir = System.IO.Path.Combine(Application.persistentDataPath, "postcards");
            System.IO.Directory.CreateDirectory(dir);
            string file = System.IO.Path.Combine(dir, $"{Driver.MissionId}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            // hide the card for the frame being captured, so the postcard shows the valley
            Menu.gameObject.SetActive(false);
            StartCoroutine(CaptureThenRestore(file));
        }

        System.Collections.IEnumerator CaptureThenRestore(string file)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(file);
            yield return null;
            Menu.gameObject.SetActive(true);
            Debug.Log("[RESQ flow] postcard saved: " + file);
        }

        // ------------------------------------------------------------------ U21 settings
        void ShowSettings(FlowState back)
        {
            var s = SaveStore.Current.Settings;
            var spec = new MenuSpec { Id = "U21", Title = "Settings", TitleIcon = Icon.Box, Columns = 3 };
            void Add(string label, string value, Action change) =>
                spec.Items.Add(new MenuOption { Label = label, Detail = value, OnPress = () => { change(); SaveStore.Save(); ApplySettings(); ShowSettings(back); } });
            Add("Text size", s.TextScale + "%", () => s.TextScale = s.TextScale == 100 ? 125 : s.TextScale == 125 ? 150 : 100);
            Add("Hand", s.LeftHanded ? "left" : "right", () => s.LeftHanded = !s.LeftHanded);
            Add("Controls", s.HeadOnly ? "hands-free" : "hands", () => s.HeadOnly = !s.HeadOnly);
            Add("Dwell time", $"{s.DwellMs / 1000f:0.0} s", () => s.DwellMs = s.DwellMs >= 1500 ? 500 : s.DwellMs + 250);
            Add("Pace", s.Pacing == "relaxed" ? "relaxed" : "standard", () => s.Pacing = s.Pacing == "relaxed" ? "standard" : "relaxed");
            Add("Contrast", s.HighContrast ? "high" : "normal", () => s.HighContrast = !s.HighContrast);
            Add("Effects", s.ReducedEffects ? "reduced" : "full", () => s.ReducedEffects = !s.ReducedEffects);
            Add("Captions", s.Captions ? "on" : "off", () => s.Captions = !s.Captions);
            Add("Open play", s.OpenPlay ? "all missions" : "campaign", () => s.OpenPlay = !s.OpenPlay);
            Add("Music", s.Music + "%", () => s.Music = Step(s.Music));
            Add("Effects volume", s.Effects + "%", () => s.Effects = Step(s.Effects));
            Add("Warnings", s.Warnings + "%", () => s.Warnings = Step(s.Warnings));
            spec.Items.Add(MenuOption.Button("Reset progress", () => Confirm("Reset all progress?", "Stars and unlocked missions are cleared. Settings stay. This can't be undone.", "Reset",
                () => { SaveStore.ResetProgress(); ShowSettings(back); },
                () => ShowSettings(back))));
            spec.Footer.Add(MenuOption.Button("Back", () => Back(back)));
            spec.Body = s.Pacing == "relaxed" || s.HeadOnly ? "Pace and controls apply from the next mission you start." : "";
            Show(FlowState.Settings, spec);
        }

        static int Step(int v) => v >= 100 ? 0 : v + 25;

        void Back(FlowState to)
        {
            if (to == FlowState.Paused) ShowPause(); else ShowHub();
        }

        /// <summary>Applies persisted settings to the live game (text, contrast, dwell, pointers).</summary>
        public void ApplySettings()
        {
            var s = SaveStore.Current.Settings;
            MenuPanel.TextScale = s.TextScale / 100f;
            MenuPanel.HighContrast = s.HighContrast;
            Boot.Interaction.DwellSeconds = s.DwellMs / 1000f;
            Boot.Interaction.SetHeadOnly(s.HeadOnly);
            float scale = s.TextScale / 100f;
            bool relayout = Comfort.LeftHanded != s.LeftHanded || !Mathf.Approximately(Comfort.UiScale, scale) || Comfort.HighContrast != s.HighContrast;
            Comfort.ReducedEffects = s.ReducedEffects;
            Comfort.LeftHanded = s.LeftHanded;
            Comfort.UiScale = scale;
            Comfort.HighContrast = s.HighContrast;
            Comfort.Captions = s.Captions;
            if (relayout && Driver.Def != null)
            {
                if (Boot.Panel != null && Boot.Panel.gameObject.activeSelf) Boot.Panel.Rebuild();
                if (Boot.Hud != null && Boot.Hud.gameObject.activeSelf) Boot.Hud.Rebuild();
            }
        }

        // ------------------------------------------------------------------ U22 help
        void ShowHelp(FlowState back)
        {
            var spec = new MenuSpec
            {
                Id = "U22", Title = "How to play", TitleIcon = Icon.Pinch, Columns = 2,
                Body = "<b>Pick a tool</b>  pinch or poke a card on the tray, then a glowing spot on the board. Pinch, drag and let go works too.\n"
                     + "<b>Move people</b>  pick a group, then where they should go. For a vehicle: the vehicle, the group, then the drop-off.\n"
                     + "<b>Plan</b>  hold a tool over a spot to see the forecast: green stays dry, red floods.\n"
                     + "<b>Hands-free</b>  look at a control and hold your gaze until it fills.\n"
                     + "<b>Pause</b>  the Menu control is always there; the rescue never runs while you're reading.",
            };
            spec.Items.Add(MenuOption.Button("Practise a press", () => { _practice++; ShowHelp(back); }, false, _practice > 0 ? $"pressed {_practice}×, it works" : "pinch, poke or look"));
            spec.Footer.Add(MenuOption.Button("Back", () => Back(back)));
            Show(FlowState.Help, spec);
        }
        int _practice;

        // ------------------------------------------------------------------ U23 credits / privacy
        void ShowCredits(FlowState back)
        {
            var spec = new MenuSpec
            {
                Id = "U23", Title = "Credits & privacy", TitleIcon = Icon.Check,
                Body = "<b>Made by</b>  Amir Lotfy, for the Meta VR Start Developer Competition 2026.\n"
                     + "<b>Built with</b>  Unity 6, Meta XR SDK (Core, Interaction, MR Utility Kit), Blender.\n"
                     + "<b>Art</b>  scripted Blender kits; landmark buildings, vehicles and residents generated with Meshy from Higgsfield concept images, then cleaned and fitted by hand-written scripts.\n"
                     + "<b>Type</b>  Atkinson Hyperlegible (SIL Open Font License).\n"
                     + "<b>Privacy</b>  Your progress, settings and postcards stay on this headset. RESQ reads where your tables are only to place the board, never records camera images, and has no internet access.",
            };
            spec.Footer.Add(MenuOption.Button("Back", () => Back(back)));
            Show(FlowState.Credits, spec);
        }

        // ------------------------------------------------------------------ confirmation
        void Confirm(string title, string body, string yes, Action onYes, Action onNo)
        {
            var spec = new MenuSpec { Id = "confirm", Title = title, TitleIcon = Icon.Warning, Body = body, Columns = 2 };
            spec.Items.Add(MenuOption.Button("Cancel", onNo, true));
            spec.Items.Add(MenuOption.Button(yes, onYes));
            Show(FlowState.Confirm, spec);
        }
    }
}
