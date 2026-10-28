using System.Collections.Generic;
using Resq.Sim;
using TMPro;
using UnityEngine;

namespace Resq.Game
{
    /// <summary>
    /// Hands-on controls in the concept style: the glass tool tray along the near edge (icon, name, stock and cost
    /// per card), the run controls and development mission picker as glass pills under the HUD chips, the guided
    /// first-play coach, and the end-of-run banner. Status and objectives live in <see cref="HudView"/>; this panel
    /// publishes the current warning/forecast line as <see cref="Toast"/>.
    /// </summary>
    public sealed class OperationPanel : MonoBehaviour
    {
        public SimDriver Driver;
        public BoardInteraction Interaction;
        public Material CardMaterial;

        /// <summary>Transient line for the HUD toast: warnings, rejections, forecast summary.</summary>
        public string Toast { get; private set; } = "";

        TextMeshPro _banner, _coach;
        Transform _ackButton, _coachPanel, _controls, _tray;
        readonly List<(int left, int cost, bool usable)> _cardState = new List<(int, int, bool)>();
        /// <summary>Set false to measure or ship without the dev picker (default: Editor and development builds only).</summary>
        public static bool? DevControlsOverride;
        static bool ShowDevControls => DevControlsOverride ?? (Application.isEditor || Debug.isDebugBuild);
        List<string> _missions;
        readonly List<(ToolKind tool, TextMeshPro name, TextMeshPro stock, Transform root, Transform glow, Transform icon)> _cards =
            new List<(ToolKind, TextMeshPro, TextMeshPro, Transform, Transform, Transform)>();
        string _lastWarning = "";
        float _warningUntil;
        MissionDef _def;
        Material _glass;

        void OnEnable() { if (Driver != null) { Driver.OnRunLoaded += Rebuild; Driver.OnSimEvent += OnSimEvent; } }
        void OnDisable() { if (Driver != null) { Driver.OnRunLoaded -= Rebuild; Driver.OnSimEvent -= OnSimEvent; } }

        public void Rebuild()
        {
            _def = Driver.Def;
            // a new run starts with no toast: a warning from the previous mission must not carry over (found in the
            // website captures, 24 Oct: "Lava advances" over Alder Valley)
            _lastWarning = ""; _warningUntil = 0;
            if (_glass == null) _glass = new Material(Shader.Find("Resq/Glass"));
            foreach (Transform t in transform) Destroy(t.gameObject);
            _cards.Clear();
            var ext = BoardSpace.Extent(_def) * 0.5f;
            BuildTray(ext);
            BuildControls(ext);
            // draw-call budget: tool cards and run controls draw their glass as one mesh per root
            if (_tray != null) GlassBatcher.Attach(_tray, _glass);   // direct reference: the old tray is still pending Destroy
            if (_controls != null) GlassBatcher.Attach(_controls, _glass);

            _banner = UiKit.Text(transform, "", new Vector3(0, 0.1f, 0), 0.5f, UiKit.Ink, true, 0.5f, 0.08f, TextAlignmentOptions.Center);
            _banner.outlineWidth = 0.2f; _banner.outlineColor = TerrainBuilder.Hex(0x132B2A);
            _banner.enableWordWrapping = true;
            _banner.gameObject.AddComponent<Billboard>();

            // guided first play coach: a glass card above the tray plus its acknowledge button
            _coachPanel = UiKit.Panel(transform, "Coach", new Vector3(0, 0.07f, -ext.y - 0.03f), 0.36f, 0.04f, _glass, 0.02f);
            _coachPanel.gameObject.AddComponent<Billboard>();
            _coach = UiKit.Text(_coachPanel, "", Vector3.zero, 0.12f, UiKit.Ink, false, 0.34f, 0.036f, TextAlignmentOptions.Center);
            _coach.enableWordWrapping = true;
            _coachPanel.gameObject.SetActive(false);
            _ackButton = Pill(transform, "Got it", new Vector3(0, 0.035f, -ext.y - 0.03f), 0.07f, () => Driver.Submit(Command.AcknowledgeTutorial()), true);
            _ackButton.gameObject.AddComponent<Billboard>();
            _ackButton.gameObject.SetActive(false);
            Interaction.ToolAllowed = ToolAllowedNow;
        }

        // ------------------------------------------------------------------ tool tray
        void BuildTray(Vector2 ext)
        {
            var tray = _tray = new GameObject("ToolTray").transform;
            tray.SetParent(transform, false);
            tray.localPosition = new Vector3(0, 0.012f, -ext.y - 0.06f);
            tray.localScale = Vector3.one * Comfort.UiScale;   // larger text means larger cards: bigger targets too
            var tools = new List<ToolKind>();
            for (int t = 0; t < MissionDef.ToolCount; t++) if (_def.Permitted[t] && _def.MaxStock[t] > 0) tools.Add((ToolKind)t);
            float w = 0.074f, h = 0.05f, gap = 0.01f;
            float start = -(tools.Count - 1) * (w + gap) * 0.5f;
            for (int i = 0; i < tools.Count; i++)
            {
                var root = new GameObject("Card_" + tools[i]).transform;
                root.SetParent(tray, false);
                root.localPosition = new Vector3(start + i * (w + gap), 0, 0);
                root.localRotation = Quaternion.Euler(62, 0, 0); // lying back toward the seated player
                var glow = UiKit.Panel(root, "Glow", new Vector3(0, 0, 0.0012f), w + 0.006f, h + 0.006f, _glass, 0.013f, new Color(0.37f, 0.9f, 0.95f, 0.75f));
                glow.gameObject.SetActive(false);
                UiKit.Panel(root, "Face", Vector3.zero, w, h, _glass, 0.01f);
                var icon = UiKit.IconObj(root, HudView.ToolIcon(tools[i]), new Vector3(0, 0.011f, 0), 0.02f, UiKit.Accent, _glass);
                // name and stock share one text object (one draw); its text changes only when stock or usability does
                var name = UiKit.Text(root, HudView.ToolLabel(tools[i]), new Vector3(0, -0.0115f, 0), 0.1f, UiKit.Ink, false, w - 0.006f, 0.027f, TextAlignmentOptions.Center);
                name.enableAutoSizing = true; name.fontSizeMax = 0.1f; name.fontSizeMin = 0.05f;
                name.lineSpacing = -10f;
                var stock = name;
                var col = root.gameObject.AddComponent<BoxCollider>(); col.size = new Vector3(w, h, 0.012f); col.isTrigger = true;
                var pick = root.gameObject.AddComponent<Pickable>(); pick.Kind = PickKind.Tool; pick.Index = (int)tools[i]; pick.Label = HudView.ToolLabel(tools[i]);
                _cards.Add((tools[i], name, stock, root, glow, icon));
            }
        }

        // ------------------------------------------------------------------ run controls + dev mission picker
        void BuildControls(Vector2 ext)
        {
            _controls = new GameObject("Controls").transform;
            _controls.SetParent(transform, false);
            _controls.localPosition = new Vector3((Comfort.LeftHanded ? -1f : 1f) * (ext.x - 0.14f), 0.13f, ext.y + 0.05f);   // mirrored for left-hand play
            _controls.localRotation = Quaternion.Euler(-8f, 0, 0);
            _controls.localScale = Vector3.one * Comfort.UiScale;
            if (GameFlow.Instance != null && !GameFlow.Disabled)
                Pill(_controls, "Menu", new Vector3(-0.165f, 0, 0), 0.06f, () => GameFlow.Instance.Pause());   // pause is never hidden
            Pill(_controls, "Start", new Vector3(-0.1f, 0, 0), 0.06f, () => Driver.SetPaused(!Driver.Paused), true);
            // destructive: with the game flow, Restart asks first (Command mode especially: a glance can't wipe a run)
            Pill(_controls, "Restart", new Vector3(-0.035f, 0, 0), 0.06f, () =>
            {
                if (GameFlow.Instance != null && !GameFlow.Disabled) GameFlow.Instance.ConfirmRestart();
                else Driver.LoadRun(Driver.MissionId, Driver.LayoutClass, Driver.Profile, Driver.Guided, Driver.ChallengeId);
            });
            if (Driver.Profile == RuleProfile.Command)
            {
                // Command profile: time can be stepped instead of run, so slower input never costs a rescue (spec 04 §5)
                Pill(_controls, "+1 s", new Vector3(-0.1f, 0.03f, 0), 0.06f, () => { Driver.SetPaused(true); Driver.Advance(10); });
                Pill(_controls, "+5 s", new Vector3(-0.035f, 0.03f, 0), 0.06f, () => { Driver.SetPaused(true); Driver.Advance(50); });
            }
            _missions = ContentLibrary.MissionIds();
            if (!ShowDevControls) return;   // the mission/layout/profile picker is a development aid
            Pill(_controls, "Prev", new Vector3(0.03f, 0, 0), 0.06f, () => Pick(-1));
            Pill(_controls, "Next", new Vector3(0.095f, 0, 0), 0.06f, () => Pick(+1));
            Pill(_controls, Driver.LayoutClass == "wide" ? "Wide" : "Compact", new Vector3(-0.1f, -0.03f, 0), 0.06f,
                () => Driver.LoadRun(Driver.MissionId, Driver.LayoutClass == "wide" ? "compact" : "wide", Driver.Profile, Driver.Guided));
            Pill(_controls, Driver.Profile.ToString(), new Vector3(-0.035f, -0.03f, 0), 0.06f,
                () => Driver.LoadRun(Driver.MissionId, Driver.LayoutClass, (RuleProfile)(((int)Driver.Profile + 1) % 3), Driver.Guided));
            if (_def.GuidedFirstPlay)
                Pill(_controls, Driver.Guided ? "Guided" : "Free", new Vector3(0.03f, -0.03f, 0), 0.06f,
                    () => Driver.LoadRun(Driver.MissionId, Driver.LayoutClass, Driver.Profile, !Driver.Guided));
        }

        Transform Pill(Transform parent, string text, Vector3 pos, float w, System.Action onPress, bool primary = false)
        {
            var root = UiKit.Panel(parent, "Button_" + text, pos, w, 0.024f, _glass, 0.012f,
                primary ? new Color(1f, 0.42f, 0.21f, 0.92f) : (Color?)null);
            var col = root.gameObject.AddComponent<BoxCollider>(); col.size = new Vector3(w, 0.024f, 0.012f); col.isTrigger = true;
            var p = root.gameObject.AddComponent<Pickable>(); p.Kind = PickKind.Button; p.OnPress = onPress; p.Label = text;
            var label = UiKit.Text(root, text, Vector3.zero, 0.1f, UiKit.Ink, true, w - 0.004f, 0.02f, TextAlignmentOptions.Center);
            label.name = "Label";
            return root;
        }

        void Pick(int delta)
        {
            if (_missions == null || _missions.Count == 0) return;
            int i = _missions.IndexOf(Driver.MissionId);
            i = ((i < 0 ? 0 : i) + delta + _missions.Count) % _missions.Count;
            string id = _missions[i];
            var contract = ContentLibrary.Contract(id);
            bool guided = contract != null && contract.Str("tutorial_policy") == "guided_first_play_then_unguided";
            Driver.LoadRun(id, Driver.LayoutClass, Driver.Profile, guided);
        }

        // ------------------------------------------------------------------ guided first play
        bool TutorialActive => Driver?.Sim != null && Driver.Sim.S.C.TutorialStep > 0 && !Driver.Sim.S.C.ScenarioEnabled;

        bool BusDelivered()
        {
            var sim = Driver.Sim; int g = _def.TutorialGroup;
            if (g < 0) return false;
            foreach (int r in _def.GroupResidents[g]) if (sim.S.RState[r] != (byte)ResidentState.Evacuated) return false;
            return true;
        }

        /// <summary>Guided first play: only the bridge until the bus is safe and the river warning is acknowledged.</summary>
        bool ToolAllowedNow(ToolKind tool) => !TutorialActive || tool == ToolKind.Bridge;

        string CoachText()
        {
            var sim = Driver.Sim;
            if (!TutorialActive)
            {
                if (sim.S.C.TutorialStep > 0 && sim.S.C.S < 150)
                    return "Barriers hold water back. Boats reach the docks. Hover a spot with a tool to see the forecast.";
                return "";
            }
            bool bridge = sim.S.Deployed[(int)ToolKind.Bridge] > 0;
            if (!bridge) return Driver.Paused ? "Press Start, then pinch the Emergency Bridge and the glowing spot on the river" : "Pinch the Emergency Bridge, then the glowing spot on the river";
            if (!BusDelivered()) return "The bus is crossing — watch it reach the depot";
            if (sim.S.C.TutorialStep < 2) return "Everyone on the bus is safe. Upstream, the river is about to rise.";
            return "";
        }

        // ------------------------------------------------------------------ warnings
        void OnSimEvent(SimEvent e)
        {
            if (_def == null) return;
            if (e.Kind == SimEventKind.Warning)
            {
                _lastWarning = WarningText(_def.Events[e.Subject]);
                _warningUntil = Time.unscaledTime + 8f;
            }
            else if (e.Kind == SimEventKind.ResidentEvacuated) { _lastWarning = "Residents safe!"; _warningUntil = Time.unscaledTime + 2.5f; }
            else if (e.Kind == SimEventKind.ResidentBackup) { _lastWarning = "Residents stranded — backup called"; _warningUntil = Time.unscaledTime + 5f; }
            else if (e.Kind == SimEventKind.CommandResolved && e.Code != Reject.OK)
            { _lastWarning = BoardInteraction.Explain(e.Code); _warningUntil = Time.unscaledTime + 3f; }
        }

        string WarningText(EventDef ev)
        {
            long lead = (ev.AtTick - Driver.Sim.S.C.S) / 10;
            foreach (var op in ev.Ops)
            {
                switch (op.Kind)
                {
                    case EventOpKind.SetSourceRate: return $"Water rising in {lead}s — check the forecast";
                    case EventOpKind.SetEdgeEnabled: return $"A route closes in {lead}s";
                    case EventOpKind.ReleaseGroup: return $"More residents arrive in {lead}s";
                    case EventOpKind.SetCurrentMultiplier: return $"The current strengthens in {lead}s";
                    case EventOpKind.IgniteRegion: return $"Fire breaks out in {lead}s";
                    case EventOpKind.AddHeat: return $"The fire flares in {lead}s";
                    case EventOpKind.SetWind: return $"The wind turns in {lead}s";
                    case EventOpKind.SetRegionCooling: return $"Cooler air arrives in {lead}s";
                    case EventOpKind.WarnResource: return "Water stores are running low";
                    case EventOpKind.SetStabilityLoss: return $"A structure starts to weaken in {lead}s";
                    case EventOpKind.SetSupportLoad: return $"Extra load on a structure in {lead}s";
                    case EventOpKind.Tremor: return $"Tremor in {lead}s — brace weak supports";
                    case EventOpKind.WarnRunout: return "Avalanche risk on the slope";
                    case EventOpKind.ReleaseAvalanche: return $"Avalanche in {lead}s — clear the runout";
                    case EventOpKind.WarnSupport: return "A support is under strain";
                    case EventOpKind.SetTideSegment: return $"The tide turns in {lead}s";
                    case EventOpKind.SetInfrastructureDemand: return $"A station needs power in {lead}s";
                    case EventOpKind.AdvanceLava: return $"Lava advances in {lead}s";
                    case EventOpKind.EnableCoolerSocket: return $"A new cooling point opens in {lead}s";
                    case EventOpKind.WarnReheat: return "A cooled crossing will reheat soon";
                    case EventOpKind.EnableShelterHazard: return $"Heat reaches a shelter in {lead}s";
                }
            }
            return $"Something changes in {lead}s";
        }

        // ------------------------------------------------------------------ live
        void LateUpdate()
        {
            if (Driver?.Sim == null || _def != Driver.Def || _banner == null) return;
            var sim = Driver.Sim;
            var S = sim.S;

            string toast = "";
            var forecast = Interaction?.Board?.Forecast;
            if (forecast != null && forecast.Showing && forecast.Summary.Length > 0) toast = forecast.Summary;
            if (Time.unscaledTime < _warningUntil) toast = _lastWarning;
            if (S.C.AwaitingBackup > 0 && toast.Length == 0) toast = $"{S.C.AwaitingBackup} stranded — backup called";
            Toast = toast;

            string coach = CoachText();
            bool showCoach = coach.Length > 0;
            if (_coachPanel.gameObject.activeSelf != showCoach) _coachPanel.gameObject.SetActive(showCoach);
            _coach.text = coach;
            bool ask = TutorialActive && BusDelivered() && sim.S.C.TutorialStep < 2;
            if (_ackButton.gameObject.activeSelf != ask) _ackButton.gameObject.SetActive(ask);

            _banner.text = sim.IsOver ? (S.C.Success ? $"Rescue complete  ·  {S.C.Stars} {(S.C.Stars == 1 ? "star" : "stars")}\nScore {S.C.ScoreValue}" : "Operation failed — try another plan") : "";

            var startBtn = _controls.Find("Button_Start/Label")?.GetComponent<TextMeshPro>();
            if (startBtn != null) startBtn.text = Driver.Paused ? "Start" : "Pause";

            if (_cardState.Count != _cards.Count) { _cardState.Clear(); for (int i = 0; i < _cards.Count; i++) _cardState.Add((-1, -1, false)); }
            for (int ci = 0; ci < _cards.Count; ci++)
            {
                var (tool, name, stock, root, glow, icon) = _cards[ci];
                int left = sim.RemainingStock(tool);
                int cost = _def.ToolCost[(int)tool];
                bool usable = left > 0 && sim.RemainingSupplies >= cost && ToolAllowedNow(tool);
                if (_cardState[ci] != (left, cost, usable))
                {
                    _cardState[ci] = (left, cost, usable);
                    name.text = $"<b>{HudView.ToolLabel(tool)}</b>\n<size=78%><color=#{(usable ? "CCDBDB" : "FFFFFF4D")}>×{left}  ·  {cost} supply</color></size>";
                }
                bool armed = Interaction != null && Interaction.Mode == InteractMode.ToolArmed && Interaction.ArmedTool == tool;
                bool hovered = Interaction != null && Interaction.Hover != null && Interaction.Hover.transform == root;
                if (glow.gameObject.activeSelf != (armed || hovered)) glow.gameObject.SetActive(armed || hovered);
                name.color = usable ? UiKit.Ink : new Color(1, 1, 1, 0.35f);
                icon.gameObject.SetActive(usable || armed);
                root.localPosition = new Vector3(root.localPosition.x, armed ? 0.008f : 0f, 0);
            }
        }
    }
}
