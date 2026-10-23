using System.Collections.Generic;
using System.Text;
using Resq.Sim;
using TMPro;
using UnityEngine;

namespace Resq.Game
{
    /// <summary>
    /// Tactile tool tray along the near edge of the board plus the operation board (mission, clock, rescues,
    /// objectives, warnings) on the far edge. Everything is a world-space object the same pointers can press.
    /// </summary>
    public sealed class OperationPanel : MonoBehaviour
    {
        public SimDriver Driver;
        public BoardInteraction Interaction;
        public Material CardMaterial;

        TextMeshPro _status, _objectives, _hint, _banner, _coach;
        Transform _ackButton;
        List<string> _missions;
        readonly List<(ToolKind tool, TextMeshPro label, Renderer card, Transform root)> _cards = new List<(ToolKind, TextMeshPro, Renderer, Transform)>();
        readonly StringBuilder _sb = new StringBuilder(512);
        MaterialPropertyBlock _mpb;
        string _lastWarning = "";
        float _warningUntil;
        MissionDef _def;
        Transform _tray, _board;

        static readonly int TintId = Shader.PropertyToID("_Tint");
        static readonly Color Paper = TerrainBuilder.Hex(0xF3EBDD), Evergreen = TerrainBuilder.Hex(0x132B2A);

        void Awake() { _mpb = new MaterialPropertyBlock(); }
        void OnEnable() { if (Driver != null) { Driver.OnRunLoaded += Rebuild; Driver.OnSimEvent += OnSimEvent; } }
        void OnDisable() { if (Driver != null) { Driver.OnRunLoaded -= Rebuild; Driver.OnSimEvent -= OnSimEvent; } }

        public void Rebuild()
        {
            _def = Driver.Def;
            foreach (Transform t in transform) Destroy(t.gameObject);
            _cards.Clear();
            var ext = BoardSpace.Extent(_def) * 0.5f;

            _tray = new GameObject("ToolTray").transform;
            _tray.SetParent(transform, false);
            _tray.localPosition = new Vector3(0, 0.012f, -ext.y - 0.055f);
            var tools = new List<ToolKind>();
            for (int t = 0; t < MissionDef.ToolCount; t++) if (_def.Permitted[t] && _def.MaxStock[t] > 0) tools.Add((ToolKind)t);
            float w = 0.07f, gap = 0.012f;
            float start = -(tools.Count - 1) * (w + gap) * 0.5f;
            for (int i = 0; i < tools.Count; i++)
            {
                var root = new GameObject("Card_" + tools[i]).transform;
                root.SetParent(_tray, false);
                root.localPosition = new Vector3(start + i * (w + gap), 0, 0);
                root.localRotation = Quaternion.Euler(-25, 0, 0); // tilted toward the seated player
                var card = new GameObject("Card");
                card.transform.SetParent(root, false);
                card.AddComponent<MeshFilter>().sharedMesh = new MeshKit().Box(Vector3.zero, new Vector3(w, 0.006f, 0.05f), Color.white).Build("Card");
                var r = card.AddComponent<MeshRenderer>(); r.sharedMaterial = CardMaterial;
                var col = root.gameObject.AddComponent<BoxCollider>(); col.size = new Vector3(w, 0.02f, 0.05f); col.isTrigger = true;
                var pick = root.gameObject.AddComponent<Pickable>(); pick.Kind = PickKind.Tool; pick.Index = (int)tools[i];
                var label = MakeText(root, "", new Vector3(0, 0.0045f, 0), 0.16f, Evergreen);
                label.transform.localRotation = Quaternion.Euler(90, 0, 0);
                label.rectTransform.sizeDelta = new Vector2(w * 0.95f, 0.045f);
                _cards.Add((tools[i], label, r, root));
            }

            _board = new GameObject("OperationBoard").transform;
            _board.SetParent(transform, false);
            _board.localPosition = new Vector3(0, 0.11f, ext.y + 0.04f);
            var bg = new GameObject("Backing");
            bg.transform.SetParent(_board, false);
            bg.transform.localPosition = new Vector3(0, 0, 0.004f);
            bg.AddComponent<MeshFilter>().sharedMesh = new MeshKit().Box(Vector3.zero, new Vector3(0.46f, 0.15f, 0.004f), Evergreen).Build("Backing");
            bg.AddComponent<MeshRenderer>().sharedMaterial = CardMaterial;
            _status = MakeText(_board, "", new Vector3(-0.115f, 0.035f, 0), 0.22f, Paper, 0.22f, 0.07f, TextAlignmentOptions.TopLeft);
            _objectives = MakeText(_board, "", new Vector3(0.115f, 0.035f, 0), 0.17f, Paper, 0.22f, 0.07f, TextAlignmentOptions.TopLeft);
            _hint = MakeText(_board, "", new Vector3(0, -0.045f, 0), 0.2f, TerrainBuilder.Hex(0xF2B134), 0.44f, 0.03f, TextAlignmentOptions.Center);
            _banner = MakeText(transform, "", new Vector3(0, 0.08f, 0), 0.5f, Paper, 0.5f, 0.08f, TextAlignmentOptions.Center);
            _banner.outlineWidth = 0.2f; _banner.outlineColor = Evergreen;
            _banner.gameObject.AddComponent<Billboard>();
            Button(_board, "Start", new Vector3(-0.17f, -0.058f, -0.006f), () => Driver.SetPaused(!Driver.Paused));
            Button(_board, "Restart", new Vector3(0.17f, -0.058f, -0.006f), () => Driver.LoadRun(Driver.MissionId, Driver.LayoutClass, Driver.Profile, Driver.Guided));
            // mission picker strip (development hub until the full hub lands): previous/next, layout, rules profile
            _missions = ContentLibrary.MissionIds();
            Button(_board, "Prev", new Vector3(-0.17f, 0.088f, -0.006f), () => Pick(-1));
            Button(_board, "Next", new Vector3(-0.09f, 0.088f, -0.006f), () => Pick(+1));
            Button(_board, Driver.LayoutClass == "wide" ? "Wide" : "Compact", new Vector3(0.01f, 0.088f, -0.006f),
                () => Driver.LoadRun(Driver.MissionId, Driver.LayoutClass == "wide" ? "compact" : "wide", Driver.Profile, Driver.Guided));
            Button(_board, Driver.Profile.ToString(), new Vector3(0.09f, 0.088f, -0.006f),
                () => Driver.LoadRun(Driver.MissionId, Driver.LayoutClass, (RuleProfile)(((int)Driver.Profile + 1) % 3), Driver.Guided));
            if (_def.GuidedFirstPlay)
                Button(_board, Driver.Guided ? "Guided" : "Free", new Vector3(0.17f, 0.088f, -0.006f),
                    () => Driver.LoadRun(Driver.MissionId, Driver.LayoutClass, Driver.Profile, !Driver.Guided));

            // guided first play coach card, above the tray, plus its acknowledge button
            _coach = MakeText(transform, "", new Vector3(0, 0.05f, -ext.y - 0.02f), 0.22f, Paper, 0.42f, 0.05f, TextAlignmentOptions.Center);
            _coach.outlineWidth = 0.22f; _coach.outlineColor = Evergreen;
            _coach.gameObject.AddComponent<Billboard>();
            _ackButton = Button(transform, "Got it", new Vector3(0, 0.02f, -ext.y - 0.02f), () => Driver.Submit(Command.AcknowledgeTutorial()));
            _ackButton.gameObject.SetActive(false);
            Interaction.ToolAllowed = ToolAllowedNow;
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
                    return "Barriers hold water back. Skiffs reach docks. Hover a spot with a tool to see the forecast.";
                return "";
            }
            bool bridge = sim.S.Deployed[(int)ToolKind.Bridge] > 0;
            if (!bridge) return Driver.Paused ? "Press Start, then tap Bridge and the glowing spot on the river" : "Tap Bridge, then the glowing spot on the river";
            if (!BusDelivered()) return "The bus is crossing — watch it reach the depot";
            if (sim.S.C.TutorialStep < 2) return "Everyone on the bus is safe. Upstream, the river is about to rise.";
            return "";
        }

        Transform Button(Transform parent, string text, Vector3 pos, System.Action onPress)
        {
            var root = new GameObject("Button_" + text).transform;
            root.SetParent(parent, false);
            root.localPosition = pos;
            var vis = new GameObject("Face");
            vis.transform.SetParent(root, false);
            vis.AddComponent<MeshFilter>().sharedMesh = new MeshKit().Box(Vector3.zero, new Vector3(0.07f, 0.024f, 0.006f), ActorsView.Rescue).Build("Btn");
            vis.AddComponent<MeshRenderer>().sharedMaterial = CardMaterial;
            var col = root.gameObject.AddComponent<BoxCollider>(); col.size = new Vector3(0.07f, 0.024f, 0.02f); col.isTrigger = true;
            var p = root.gameObject.AddComponent<Pickable>(); p.Kind = PickKind.Button; p.OnPress = onPress; p.Label = text;
            var label = MakeText(root, text, new Vector3(0, 0, -0.0035f), 0.16f, Paper, 0.07f, 0.024f, TextAlignmentOptions.Center);
            label.name = "Label";
            return root;
        }

        TextMeshPro MakeText(Transform parent, string text, Vector3 pos, float size, Color color, float w = 0.1f, float h = 0.03f,
                             TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text; tmp.fontSize = size; tmp.color = color; tmp.alignment = align;
            tmp.enableWordWrapping = true;
            tmp.rectTransform.sizeDelta = new Vector2(w, h);
            // long titles/tool names shrink to fit their card instead of spilling into neighbours
            tmp.enableAutoSizing = true;
            tmp.fontSizeMax = size;
            tmp.fontSizeMin = size * 0.45f;
            tmp.overflowMode = TextOverflowModes.Truncate;
            return tmp;
        }

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

        void LateUpdate()
        {
            if (Driver?.Sim == null || _def != Driver.Def || _status == null) return;
            var sim = Driver.Sim;
            var S = sim.S;
            long remain = System.Math.Max(0, (_def.DurationTicks - S.C.S) / 10);
            _sb.Clear();
            _sb.Append("<b>").Append(_def.Title).Append("</b>\n");
            _sb.Append("Time ").Append(remain / 60).Append(':').Append((remain % 60).ToString("00"));
            _sb.Append("   Rescued ").Append(S.C.Evacuated).Append('/').Append(_def.TotalResidents);
            _sb.Append("\nSupplies ").Append(sim.RemainingSupplies).Append('/').Append(_def.SupplyBudget);
            if (S.C.AwaitingBackup > 0) _sb.Append("   <color=#E5484D>Stranded ").Append(S.C.AwaitingBackup).Append("</color>");
            _status.text = _sb.ToString();

            _sb.Clear();
            _sb.Append("<b>Goals</b>\n");
            _sb.Append(S.C.Evacuated >= _def.MinimumRescues ? "✓ " : "• ").Append("Rescue ").Append(_def.MinimumRescues).Append('\n');
            for (int i = 0; i < _def.Objectives.Length; i++)
            {
                var o = _def.Objectives[i];
                if (o.Tier == Tier.Required && o.Condition.Kind == ConditionKind.RescueCount) continue;
                if (o.Tier == Tier.Critical) continue;
                _sb.Append(sim.ObjectiveFulfilled(i) ? "✓ " : o.Tier == Tier.Mastery ? "★ " : "• ").Append(Describe(o)).Append('\n');
            }
            _objectives.text = _sb.ToString();

            string hint = Interaction != null ? Interaction.Hint : "";
            var forecast = Interaction?.Board?.Forecast;
            if (forecast != null && forecast.Showing && forecast.Summary.Length > 0) hint = forecast.Summary;
            if (Time.unscaledTime < _warningUntil) hint = _lastWarning;
            else if (Driver.Paused && !sim.IsOver) hint = "Paused — plan your rescue, then press Start";
            _hint.text = hint;
            if (_coach != null)
            {
                _coach.text = CoachText();
                bool ask = TutorialActive && BusDelivered() && sim.S.C.TutorialStep < 2;
                if (_ackButton != null && _ackButton.gameObject.activeSelf != ask) _ackButton.gameObject.SetActive(ask);
            }

            _banner.text = sim.IsOver ? (S.C.Success ? $"Rescue complete  {new string('★', S.C.Stars)}{new string('☆', 3 - S.C.Stars)}\nScore {S.C.ScoreValue}" : "Operation failed — try another plan") : "";

            var startBtn = _board.Find("Button_Start/Label")?.GetComponent<TextMeshPro>();
            if (startBtn != null) startBtn.text = Driver.Paused ? "Start" : "Pause";

            foreach (var (tool, label, card, root) in _cards)
            {
                int left = sim.RemainingStock(tool);
                int cost = _def.ToolCost[(int)tool];
                label.text = $"<b>{BoardView.Pretty(MapCompiler.ToolName(tool))}</b>\n×{left}  ·  {cost} supply";
                bool armed = Interaction != null && Interaction.Mode == InteractMode.ToolArmed && Interaction.ArmedTool == tool;
                bool hovered = Interaction != null && Interaction.Hover != null && Interaction.Hover.transform == root;
                Color c = left <= 0 || sim.RemainingSupplies < cost || !ToolAllowedNow(tool) ? TerrainBuilder.Hex(0x9A9A92) : armed ? ActorsView.Rescue : Paper;
                if (hovered) c = Color.Lerp(c, Color.white, 0.3f);
                card.GetPropertyBlock(_mpb); _mpb.SetColor(TintId, c); card.SetPropertyBlock(_mpb);
                root.localPosition = new Vector3(root.localPosition.x, armed ? 0.008f : 0f, 0);
            }
        }

        string Describe(ObjectiveDef o)
        {
            var c = o.Condition;
            switch (c.Kind)
            {
                case ConditionKind.GroupEvacuated: return $"Get the {BoardView.Pretty(_def.GroupIds[c.A])} group to safety";
                case ConditionKind.InfrastructureOperational: return $"Keep the {BoardView.Pretty(_def.InfraIds[c.A])} working";
                case ConditionKind.ExposureBelow: return $"Keep the {BoardView.Pretty(_def.RegionIds[c.A])} dry";
                case ConditionKind.RouteAvailable: return $"Keep the {BoardView.Pretty(_def.RouteIds[c.A])} road open";
                case ConditionKind.CounterAtMost when c.Metric == MetricKind.GeneratorShutdowns: return "No generator shutdowns";
                case ConditionKind.CounterAtLeast when c.Metric == MetricKind.EvacuatedByMode:
                    return $"Bring {c.Value} out {(c.MetricMode == 1 ? "by boat" : c.MetricMode == 2 ? "by cable" : "on foot")}";
                case ConditionKind.CounterAtMost when c.Metric == MetricKind.StationShutdowns: return "No station loses power";
                case ConditionKind.CounterAtMost when c.Metric == MetricKind.ShelterExhaustions: return "No shelter runs out";
                case ConditionKind.CounterAtMost when c.Metric == MetricKind.PeakNodeOccupancy: return $"No more than {c.Value} at the {BoardView.Pretty(_def.NodeIds[c.MetricTarget])}";
                case ConditionKind.CounterAtMost when c.Metric == MetricKind.MaxNodeDwell: return $"Nobody waits long at the {BoardView.Pretty(_def.NodeIds[c.MetricTarget])}";
                case ConditionKind.CounterAtMost when c.Metric == MetricKind.MaxDockQueue: return "Keep the dock queue short";
                case ConditionKind.CounterAtMost when c.Metric == MetricKind.LoadedClosedDockWait: return "No loaded boat waits at a closed dock";
                case ConditionKind.CounterAtMost when c.Metric == MetricKind.PumpedIntoRegion: return $"Don't pump into the {BoardView.Pretty(_def.RegionIds[c.MetricTarget])}";
                case ConditionKind.CounterAtLeast when c.Metric == MetricKind.EdgeTransfers: return "Use the bypass route";
                case ConditionKind.TemporaryShelterEmpty: return "Everyone leaves the shelters";
                case ConditionKind.TemporaryStagingEmpty: return "Nobody left at staging points";
                case ConditionKind.AllVehiclesUnloaded: return "Every vehicle unloaded";
                case ConditionKind.ResourceRemaining: return "Keep some in reserve";
                case ConditionKind.EquipmentLossesAtMost: return c.Value == 0 ? "Lose no equipment" : $"Lose at most {c.Value} tools";
                case ConditionKind.RescueCount: return $"Rescue {c.Value}";
                default: return o.Id;
            }
        }
    }
}
