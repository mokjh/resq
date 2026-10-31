using System.Collections.Generic;
using System.Text;
using Resq.Sim;
using TMPro;
using UnityEngine;

namespace Resq.Game
{
    /// <summary>
    /// Concept-style HUD (art direction 23 Oct): a glass objectives card and stat chips floating above the board's
    /// far edge (world-locked, never head-locked), a contextual tooltip at the hovered target, an input-hint bar
    /// above the tool tray, "Evacuating…" bubbles over moving rescues, and the world signboard on the board corner.
    /// Read-only over simulation state; buttons and tools still go through <see cref="BoardInteraction"/>.
    /// </summary>
    public sealed class HudView : MonoBehaviour
    {
        public SimDriver Driver;
        public BoardInteraction Interaction;
        public OperationPanel Panel;
        public bool HandsActive;

        MissionDef _def;
        Material _glass;
        Transform _root, _card, _tooltip, _hintBar, _toast;
        TextMeshPro _progress, _tipTitle, _tipSub, _hintText, _toastText;
        readonly List<(int objective, string label, TextMeshPro text, MeshFilter icon)> _rows = new List<(int, string, TextMeshPro, MeshFilter)>();
        readonly List<(string kind, TextMeshPro value, string label)> _chips = new List<(string, TextMeshPro, string)>();
        readonly List<Transform> _bubbles = new List<Transform>();
        readonly StringBuilder _sb = new StringBuilder(128);
        MeshFilter _tipIcon;

        static readonly Dictionary<string, string> WorldNames = new Dictionary<string, string>
        { { "W1", "Alder Valley" }, { "W2", "Emberwood" }, { "W3", "Frostline" }, { "W4", "Tidemark" }, { "W5", "Cinder Reach" } };
        static readonly Dictionary<string, string> Taglines = new Dictionary<string, string>
        {
            { "W1", "Hold back the flood. Get everyone to safety." }, { "W2", "Contain the fire. Protect homes and people." },
            { "W3", "Steady the mountain. Bring everyone down." }, { "W4", "Read the tide. Get everyone across." },
            { "W5", "Cool the path. Shelter, then evacuate." },
        };

        public static string WorldName(string id) => WorldNames.TryGetValue(id ?? "", out var n) ? n : id;

        static Icon WorldIcon(string id) => id == "W2" ? Icon.Tree : id == "W3" ? Icon.Snow : id == "W4" ? Icon.Wave : id == "W5" ? Icon.Volcano : Icon.Mountain;

        public static string ToolLabel(ToolKind t)
        {
            switch (t)
            {
                case ToolKind.Barrier: return "Flood Barrier";
                case ToolKind.Bridge: return "Emergency Bridge";
                case ToolKind.Skiff: return "Rescue Boat";
                case ToolKind.Pump: return "Pump";
                case ToolKind.Generator: return "Generator";
                case ToolKind.Beacon: return "Beacon";
                case ToolKind.Firebreak: return "Firebreak";
                case ToolKind.Sprinkler: return "Sprinkler";
                case ToolKind.Crawler: return "Crawler";
                case ToolKind.Brace: return "Brace";
                case ToolKind.Winch: return "Cable Winch";
                case ToolKind.Relay: return "Power Relay";
                case ToolKind.Cooler: return "Lava Cooler";
                case ToolKind.Shelter: return "Heat Shelter";
                default: return t.ToString();
            }
        }

        public static Icon ToolIcon(ToolKind t) => t == ToolKind.Barrier ? Icon.Barrier : t == ToolKind.Bridge ? Icon.Bridge : t == ToolKind.Skiff ? Icon.Boat
            : t == ToolKind.Cooler || t == ToolKind.Sprinkler || t == ToolKind.Pump ? Icon.Drop : t == ToolKind.Firebreak ? Icon.Tree : Icon.Box;

        void OnEnable() { if (Driver != null) Driver.OnRunLoaded += Rebuild; }
        void OnDisable() { if (Driver != null) Driver.OnRunLoaded -= Rebuild; }

        public void Rebuild()
        {
            _def = Driver.Def;
            if (_glass == null) _glass = new Material(Shader.Find("Resq/Glass"));
            foreach (Transform t in transform) Destroy(t.gameObject);
            _rows.Clear(); _chips.Clear(); _bubbles.Clear();
            var ext = BoardSpace.Extent(_def) * 0.5f;
            _root = new GameObject("HudRoot").transform;
            _root.SetParent(transform, false);
            _root.localPosition = new Vector3(0, 0.135f, ext.y + 0.05f);   // within 30° of the seated gaze on the board centre (FovTest)
            _root.localRotation = Quaternion.Euler(-8f, 0, 0); // tipped slightly toward the seated player
            _root.localScale = Vector3.one * Comfort.UiScale;  // text-size setting: the whole HUD grows, so nothing clips

            BuildCard(ext);
            BuildChips(ext);
            BuildHintBar(ext);
            BuildTooltip();
            BuildSign(ext);
            BuildToast();
            // draw-call budget: the card, chips and toast (one root) and the world sign each draw their glass as one mesh
            GlassBatcher.Attach(_root, _glass);
            if (_sign != null) GlassBatcher.Attach(_sign, _glass);   // direct reference: the old sign is still pending Destroy
            // group bubbles move every frame: their glass still draws as one mesh, rebuilt per frame without allocations
            _bubbleRoot = new GameObject("Bubbles").transform;
            _bubbleRoot.SetParent(transform, false);
            _bubbleBatch = GlassBatcher.Attach(_bubbleRoot, _glass);
        }

        // ------------------------------------------------------------------ objectives card
        // the HUD spans ±HalfSpan, not the board's full width, so its corners stay within 30° of the seated gaze (FovTest)
        const float HalfSpan = 0.31f;

        void BuildCard(Vector2 ext)
        {
            var lines = ObjectiveLines(_def);
            float w = 0.31f, rowH = 0.024f, head = 0.062f;   // ends at the centre line, clear of the run controls
            float h = head + rowH * lines.Count + 0.014f;
            float left = -HalfSpan + w * 0.5f;
            _card = UiKit.Panel(_root, "ObjectivesCard", new Vector3(left, -h * 0.5f + 0.06f, 0), w, h, _glass);
            float top = h * 0.5f;
            UiKit.IconObj(_card, WorldIcon(_def.WorldId), new Vector3(-w / 2 + 0.024f, top - 0.026f, 0), 0.026f, UiKit.Accent, _glass);
            var title = UiKit.Text(_card, $"{WorldName(_def.WorldId)} - {_def.Title}", new Vector3(-w / 2 + 0.046f + 0.10f, top - 0.018f, 0), 0.14f, UiKit.Ink, true, 0.21f, 0.02f);
            // long "World - Title" lines shrink to fit rather than cut off, never below the tagline's size
            title.enableAutoSizing = true; title.fontSizeMin = 0.10f; title.fontSizeMax = 0.14f;
            UiKit.Text(_card, Taglines.TryGetValue(_def.WorldId ?? "", out var tag) ? tag : "", new Vector3(-w / 2 + 0.046f + 0.10f, top - 0.038f, 0), 0.10f, UiKit.Accent, false, 0.21f, 0.016f);
            _progress = UiKit.Text(_card, "", new Vector3(w / 2 - 0.03f, top - 0.022f, 0), 0.16f, UiKit.Ink, true, 0.05f, 0.02f, TextAlignmentOptions.Center);
            UiKit.Text(_card, "Objectives", new Vector3(w / 2 - 0.03f, top - 0.04f, 0), 0.085f, UiKit.InkSoft, false, 0.05f, 0.014f, TextAlignmentOptions.Center);
            // divider
            var div = new GameObject("Divider");
            div.transform.SetParent(_card, false);
            div.transform.localPosition = new Vector3(0, top - head + 0.004f, -0.0005f);
            div.AddComponent<MeshFilter>().sharedMesh = UiKit.RoundedRect(w - 0.02f, 0.0008f, 0.0003f, new Color(1, 1, 1, 0.14f));
            div.AddComponent<MeshRenderer>().sharedMaterial = _glass;
            for (int i = 0; i < lines.Count; i++)
            {
                float y = top - head - rowH * (i + 0.5f);
                var icon = UiKit.IconObj(_card, Icon.Ring, new Vector3(-w / 2 + 0.024f, y, 0), 0.016f, UiKit.InkSoft, _glass);
                var text = UiKit.Text(_card, lines[i].label, new Vector3(-w / 2 + 0.04f + 0.125f, y, 0), 0.11f, UiKit.Ink, false, 0.25f, 0.02f);
                _rows.Add((lines[i].objective, lines[i].label, text, icon.GetComponent<MeshFilter>()));
            }
        }

        /// <summary>Distinct, player-readable objective lines (critical duplicates of required lines folded in).</summary>
        public static List<(int objective, string label)> ObjectiveLines(MissionDef def)
        {
            var list = new List<(int, string)>();
            var seen = new HashSet<string>();
            list.Add((-1, $"Rescue {def.MinimumRescues} residents"));
            seen.Add(list[0].Item2);
            for (int i = 0; i < def.Objectives.Length; i++)
            {
                var o = def.Objectives[i];
                if (o.Condition.Kind == ConditionKind.RescueCount && o.Tier != Tier.Mastery) continue;
                string label = Describe(def, o);
                if (seen.Add(label)) list.Add((i, label));
            }
            return list;
        }

        public static string Describe(MissionDef def, ObjectiveDef o)
        {
            var c = o.Condition;
            string prefix = o.Tier == Tier.Mastery ? "Bonus: " : "";
            switch (c.Kind)
            {
                case ConditionKind.GroupEvacuated: return $"{prefix}Get the {BoardView.Pretty(def.GroupIds[c.A])} group to safety";
                case ConditionKind.InfrastructureOperational: return $"{prefix}Keep the {BoardView.Pretty(def.InfraIds[c.A])} running";
                case ConditionKind.ExposureBelow: return $"{prefix}Keep the {BoardView.Pretty(def.RegionIds[c.A])} safe";
                case ConditionKind.RouteAvailable: return $"{prefix}Keep the {BoardView.Pretty(def.RouteIds[c.A])} open";
                case ConditionKind.RescueCount: return $"{prefix}Rescue {c.Value} residents";
                case ConditionKind.TemporaryShelterEmpty: return $"{prefix}Everyone leaves the shelters";
                case ConditionKind.TemporaryStagingEmpty: return $"{prefix}Nobody left at staging points";
                case ConditionKind.AllVehiclesUnloaded: return $"{prefix}Every vehicle unloaded";
                case ConditionKind.EquipmentLossesAtMost: return $"{prefix}Lose no equipment";
                case ConditionKind.ResourceRemaining: return $"{prefix}Keep some in reserve";
                case ConditionKind.CounterAtMost when c.Metric == MetricKind.GeneratorShutdowns: return $"{prefix}No generator shutdowns";
                case ConditionKind.CounterAtMost when c.Metric == MetricKind.StationShutdowns: return $"{prefix}No station loses power";
                case ConditionKind.CounterAtMost when c.Metric == MetricKind.ShelterExhaustions: return $"{prefix}No shelter runs out";
                case ConditionKind.CounterAtMost when c.Metric == MetricKind.LoadedClosedDockWait: return $"{prefix}No loaded boat waits at a closed dock";
                case ConditionKind.CounterAtMost when c.Metric == MetricKind.MaxDockQueue: return $"{prefix}Keep the dock queue short";
                case ConditionKind.CounterAtMost when c.Metric == MetricKind.PeakNodeOccupancy: return $"{prefix}Don't crowd the {BoardView.Pretty(def.NodeIds[c.MetricTarget])}";
                case ConditionKind.CounterAtMost when c.Metric == MetricKind.MaxNodeDwell: return $"{prefix}Nobody waits long at the {BoardView.Pretty(def.NodeIds[c.MetricTarget])}";
                case ConditionKind.CounterAtMost when c.Metric == MetricKind.PumpedIntoRegion: return $"{prefix}Don't pump into the {BoardView.Pretty(def.RegionIds[c.MetricTarget])}";
                case ConditionKind.CounterAtLeast when c.Metric == MetricKind.EdgeTransfers: return $"{prefix}Use the bypass route";
                case ConditionKind.CounterAtLeast when c.Metric == MetricKind.EvacuatedByMode:
                    return $"{prefix}Bring someone out {(c.MetricMode == 1 ? "by boat" : c.MetricMode == 2 ? "by cable" : "on foot")}";
                default: return prefix + o.Id;
            }
        }

        // ------------------------------------------------------------------ chips
        void BuildChips(Vector2 ext)
        {
            var kinds = new List<(string kind, Icon icon, string label)> { ("rescued", Icon.People, "Rescued"), ("supplies", Icon.Box, "Supplies") };
            if (_def.TideIds.Length > 0) kinds.Add(("tide", Icon.Wave, "Tide"));
            if (_def.FireEnabled && HasWind()) kinds.Add(("wind", Icon.Wind, "Wind"));
            if (_def.SupportIds.Length > 0) kinds.Add(("stability", Icon.Mountain, "Stability"));
            if (_def.CoolantIds.Length > 0) kinds.Add(("coolant", Icon.Drop, "Coolant"));
            float w = 0.09f, h = 0.05f, gap = 0.006f;
            float right = HalfSpan;
            for (int i = 0; i < kinds.Count; i++)
            {
                int fromRight = kinds.Count - 1 - i;
                var chip = UiKit.Panel(_root, "Chip_" + kinds[i].kind, new Vector3(right - w * 0.5f - fromRight * (w + gap), 0.035f, 0), w, h, _glass);
                UiKit.IconObj(chip, kinds[i].icon, new Vector3(-w / 2 + 0.02f, 0, 0), 0.024f, UiKit.Accent, _glass);
                // one text object per chip (label line + value line): one draw instead of two
                var value = UiKit.Text(chip, "", new Vector3(0.012f, 0f, 0), 0.13f, UiKit.Ink, false, 0.06f, 0.044f);
                value.lineSpacing = 24f;
                _chips.Add((kinds[i].kind, value, kinds[i].label));
            }
        }

        bool HasWind()
        {
            foreach (int r in _def.CellWindRegion) if (r >= 0) return true;
            return false;
        }

        static readonly string[] WindNames = { "Calm", "W", "E", "N", "S", "NE", "SE", "SW", "NW" };

        // ------------------------------------------------------------------ hint bar, tooltip, toast, sign
        void BuildHintBar(Vector2 ext)
        {
            _hintBar = UiKit.Panel(transform, "HintBar", new Vector3(0, 0.03f, -ext.y - 0.115f), 0.30f, 0.032f, _glass, 0.016f);
            _hintBar.localRotation = Quaternion.Euler(30f, 0, 0);
            UiKit.IconObj(_hintBar, Icon.Pinch, new Vector3(-0.13f, 0, 0), 0.02f, UiKit.Accent, _glass);
            _hintText = UiKit.Text(_hintBar, "", new Vector3(0.012f, 0, 0), 0.115f, UiKit.Ink, false, 0.26f, 0.022f, TextAlignmentOptions.Center);
        }

        void BuildTooltip()
        {
            _tooltip = UiKit.Panel(transform, "Tooltip", Vector3.zero, 0.13f, 0.038f, _glass);
            _tooltip.gameObject.AddComponent<Billboard>();
            _tipIcon = UiKit.IconObj(_tooltip, Icon.Barrier, new Vector3(-0.048f, 0, 0), 0.022f, UiKit.Accent, _glass).GetComponent<MeshFilter>();
            _tipTitle = UiKit.Text(_tooltip, "", new Vector3(0.014f, 0.007f, 0), 0.11f, UiKit.Ink, true, 0.1f, 0.016f);
            _tipSub = UiKit.Text(_tooltip, "", new Vector3(0.014f, -0.009f, 0), 0.085f, UiKit.InkSoft, false, 0.1f, 0.014f);
            _tooltip.gameObject.SetActive(false);
        }

        void BuildToast()
        {
            _toast = UiKit.Panel(_root, "Toast", new Vector3(0.08f, -0.02f, 0), 0.24f, 0.03f, _glass, 0.015f, new Color(0.22f, 0.15f, 0.05f, 0.85f));
            UiKit.IconObj(_toast, Icon.Warning, new Vector3(-0.105f, 0, 0), 0.016f, UiKit.Warn, _glass);
            _toastText = UiKit.Text(_toast, "", new Vector3(0.01f, 0, 0), 0.1f, UiKit.Ink, false, 0.21f, 0.02f);
            _toast.gameObject.SetActive(false);
        }

        Transform _sign, _bubbleRoot;
        GlassBatcher _bubbleBatch;

        void BuildSign(Vector2 ext)
        {
            var sign = _sign = new GameObject("WorldSign").transform;
            sign.SetParent(transform, false);
            sign.localPosition = new Vector3(ext.x + 0.018f, -0.01f, -ext.y + 0.02f);
            sign.localRotation = Quaternion.Euler(0, -28f, 0);
            var wood = TerrainBuilder.Hex(0x6B4A32);
            var postMesh = new MeshKit().Box(new Vector3(-0.034f, 0.025f, 0.002f), new Vector3(0.004f, 0.07f, 0.004f), wood)
                                        .Box(new Vector3(0.034f, 0.025f, 0.002f), new Vector3(0.004f, 0.07f, 0.004f), wood).Build("SignPosts");
            var posts = new GameObject("Posts");
            posts.transform.SetParent(sign, false);
            posts.AddComponent<MeshFilter>().sharedMesh = postMesh;
            posts.AddComponent<MeshRenderer>().sharedMaterial = Panel != null && Panel.CardMaterial != null ? Panel.CardMaterial : _glass;
            var plate = UiKit.Panel(sign, "Plate", new Vector3(0, 0.05f, 0), 0.09f, 0.036f, _glass, 0.004f, new Color(0.13f, 0.32f, 0.24f, 0.97f));
            UiKit.Text(plate, WorldName(_def.WorldId), new Vector3(-0.006f, 0, 0), 0.12f, UiKit.Ink, true, 0.07f, 0.02f, TextAlignmentOptions.Center);
            UiKit.IconObj(plate, WorldIcon(_def.WorldId), new Vector3(0.034f, -0.004f, 0), 0.014f, UiKit.Ink, _glass);
        }

        // ------------------------------------------------------------------ live update
        void LateUpdate()
        {
            if (Driver?.Sim == null || _def != Driver.Def || _root == null) return;
            FaceViewer();
            var sim = Driver.Sim; var S = sim.S;
            // objectives and chips change at simulation pace: refresh their text at 5 Hz, not every frame
            // (each refresh formats strings; per-frame formatting was most of the HUD's garbage)
            if (Time.unscaledTime >= _nextText) { _nextText = Time.unscaledTime + 0.2f; RefreshTexts(sim, S); }
            UpdateTooltip(sim);
            UpdateHint();
            UpdateToast();
            UpdateBubbles(sim);
        }

        float _nextText;

        static string Chip(string label, string value) => "<size=58%><color=#CCDBDB>" + label + "</color></size>\n<b>" + value + "</b>";

        void RefreshTexts(Simulator sim, SimState S)
        {
            int done = 0;
            foreach (var row in _rows)
            {
                bool ok = row.objective < 0 ? S.C.Evacuated >= _def.MinimumRescues : sim.ObjectiveFulfilled(row.objective);
                bool mastery = row.objective >= 0 && _def.Objectives[row.objective].Tier == Tier.Mastery;
                if (ok) done++;
                row.icon.sharedMesh = UiKit.IconMesh(ok ? Icon.Check : mastery ? Icon.Star : Icon.Ring, 0.016f, ok ? UiKit.Good : mastery ? UiKit.Warn : UiKit.InkSoft);
                string progress = Progress(row.objective);
                row.text.text = progress.Length > 0 ? $"{row.label} {progress}" : row.label;
                row.text.color = ok ? UiKit.InkSoft : UiKit.Ink;
            }
            _progress.text = $"{done}/{_rows.Count}";

            foreach (var (kind, value, label) in _chips)
            {
                switch (kind)
                {
                    case "rescued": value.text = Chip(label, $"{S.C.Evacuated} / {_def.TotalResidents}"); break;
                    case "supplies": value.text = Chip(label, $"{sim.RemainingSupplies}"); break;
                    case "tide":
                    {
                        long lvl = S.TideLevel[0], end = S.TideEndLevel[0];
                        value.text = Chip(label, end > lvl ? "Rising" : end < lvl ? "Falling" : lvl >= 600 ? "High" : "Low");
                        break;
                    }
                    case "wind":
                    {
                        int code = S.WindCode.Length > 0 ? S.WindCode[_def.CellWindRegion[System.Array.FindIndex(_def.CellWindRegion, r => r >= 0)]] : 0;
                        value.text = Chip(label, code > 0 && code < WindNames.Length ? "→ " + WindNames[code] : "Calm");
                        break;
                    }
                    case "stability":
                    {
                        int min = 1000;
                        for (int i = 0; i < _def.SupportIds.Length; i++) if (!S.SupCollapsed[i]) min = Mathf.Min(min, sim.EffectiveStability(i));
                        value.text = Chip(label, min.ToString());
                        value.color = min <= Simulator.SupportWarnStability ? UiKit.Warn : UiKit.Ink;
                        break;
                    }
                    case "coolant":
                    {
                        int n = 0;
                        foreach (int c in S.CoolantStore) n += c;
                        for (int inst = 0; inst < S.C.EquipmentCount; inst++) if ((ToolKind)S.EqKind[inst] == ToolKind.Cooler) n += S.EqCharges[inst];
                        value.text = Chip(label, n.ToString());
                        break;
                    }
                }
            }
        }

        /// <summary>The HUD stays world-locked above the board but turns (smoothly) to face the player's head.</summary>
        void FaceViewer()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var dir = _root.position - cam.transform.position;
            if (dir.sqrMagnitude < 1e-6f) return;
            var target = Quaternion.LookRotation(dir.normalized, Vector3.up);
            _root.rotation = Quaternion.Slerp(_root.rotation, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 4f));
        }

        string Progress(int objective)
        {
            var S = Driver.Sim.S;
            if (objective < 0) return $"({Mathf.Min(S.C.Evacuated, _def.MinimumRescues)}/{_def.MinimumRescues})";
            var c = _def.Objectives[objective].Condition;
            if (c.Kind == ConditionKind.GroupEvacuated)
            {
                int n = 0;
                foreach (int r in _def.GroupResidents[c.A]) if (S.RState[r] == (byte)ResidentState.Evacuated) n++;
                return $"({Mathf.Min(n, (int)c.Value)}/{c.Value})";
            }
            return "";
        }

        void UpdateTooltip(Simulator sim)
        {
            var hover = Interaction != null ? Interaction.Hover : null;
            bool show = false;
            if (hover != null && Interaction.Mode == InteractMode.ToolArmed && hover.Kind == PickKind.Socket)
            {
                var verdict = Interaction.HoverVerdict;
                _tipIcon.sharedMesh = UiKit.IconMesh(ToolIcon(Interaction.ArmedTool), 0.022f, verdict == Reject.OK ? UiKit.Accent : UiKit.Bad);
                _tipTitle.text = "Place " + ToolLabel(Interaction.ArmedTool);
                _tipSub.text = verdict == Reject.OK ? (HandsActive ? "Pinch to place" : "Click to place") : BoardInteraction.Explain(verdict);
                _tipSub.color = verdict == Reject.OK ? UiKit.InkSoft : UiKit.Bad;
                show = true;
            }
            else if (hover != null && Interaction.Mode == InteractMode.Idle && hover.Kind == PickKind.Equipment && Interaction.EquipmentAction(hover.Index, out var cmd))
            {
                var tool = (ToolKind)sim.S.EqKind[hover.Index];
                _tipIcon.sharedMesh = UiKit.IconMesh(ToolIcon(tool), 0.022f, UiKit.Accent);
                _tipTitle.text = cmd.Kind == CommandKind.CoolSegment ? "Cool the lava" : cmd.Kind == CommandKind.RefillTool ? "Refill coolant"
                               : cmd.Kind == CommandKind.EnableTool ? "Switch on" : "Switch off";
                _tipSub.text = ToolLabel(tool) + " · " + (HandsActive ? "pinch" : "click");
                _tipSub.color = UiKit.InkSoft;
                show = true;
            }
            else if (hover != null && (hover.Kind == PickKind.Group || hover.Kind == PickKind.Vehicle) && Interaction.Mode == InteractMode.Idle)
            {
                _tipIcon.sharedMesh = UiKit.IconMesh(hover.Kind == PickKind.Group ? Icon.People : Icon.Boat, 0.022f, UiKit.Accent);
                _tipTitle.text = hover.Label;
                _tipSub.text = hover.Kind == PickKind.Group ? "Pinch, then choose where to go" : "Pinch, then a group and a drop-off";
                _tipSub.color = UiKit.InkSoft;
                show = true;
            }
            if (_tooltip.gameObject.activeSelf != show) _tooltip.gameObject.SetActive(show);
            if (show) _tooltip.localPosition = hover.transform.localPosition + new Vector3(0, 0.055f, 0);
        }

        void UpdateHint()
        {
            string act = HandsActive ? "Pinch" : "Click";
            switch (Interaction != null ? Interaction.Mode : InteractMode.Idle)
            {
                case InteractMode.ToolArmed: _hintText.text = $"{act} a glowing spot to place  |  drag from the card to aim"; break;
                case InteractMode.GroupRouting: _hintText.text = $"{act} where the group should go"; break;
                case InteractMode.VehiclePickup: _hintText.text = $"{act} a group to pick up, or a destination"; break;
                case InteractMode.VehicleDestination: _hintText.text = $"{act} a safe drop-off"; break;
                default: _hintText.text = !Driver.Paused ? $"{act} a tool, a group or a vehicle"
                    : Driver.Sim.S.C.O > 0 ? "Paused — press Start to continue" : "Plan first — press Start when ready"; break;
            }
        }

        void UpdateToast()
        {
            string msg = Panel != null ? Panel.Toast : "";
            bool show = !string.IsNullOrEmpty(msg);
            if (_toast.gameObject.activeSelf != show) _toast.gameObject.SetActive(show);
            if (show) _toastText.text = msg;
        }

        void UpdateBubbles(Simulator sim)
        {
            var S = sim.S;
            var actors = Interaction?.Board?.Actors;
            if (actors == null) return;
            int used = 0;
            for (int v = 0; v < S.C.VehicleCount; v++)
            {
                if (S.VState[v] != (byte)VehicleState.Moving || S.VOccupantCount[v] == 0) continue;
                var t = actors.VehicleTransform(v);
                if (t != null) PlaceBubble(used++, t.localPosition + new Vector3(0, 0.05f, 0), "Evacuating…");
            }
            for (int g = 0; g < _def.GroupIds.Length && used < 6; g++)
            {
                if (!S.GroupEvacuate[g]) continue;
                Vector3 sum = Vector3.zero; int n = 0;
                foreach (int r in _def.GroupResidents[g])
                    if (S.RState[r] == (byte)ResidentState.Walking) { var rt = actors.ResidentTransform(r); if (rt != null) { sum += rt.localPosition; n++; } }
                if (n > 0) PlaceBubble(used++, sum / n + new Vector3(0, 0.06f, 0), "Evacuating…");
            }
            for (int i = used; i < _bubbles.Count; i++) if (_bubbles[i].gameObject.activeSelf) _bubbles[i].gameObject.SetActive(false);
        }

        void PlaceBubble(int i, Vector3 pos, string text)
        {
            while (_bubbles.Count <= i)
            {
                var b = UiKit.Panel(_bubbleRoot, "Bubble", Vector3.zero, 0.075f, 0.024f, _glass, 0.012f);
                b.gameObject.AddComponent<Billboard>();
                UiKit.IconObj(b, Icon.People, new Vector3(-0.026f, 0, 0), 0.016f, UiKit.Accent, _glass);
                UiKit.Text(b, "", new Vector3(0.008f, 0, 0), 0.085f, UiKit.Ink, false, 0.055f, 0.016f).name = "BubbleText";
                _bubbles.Add(b);
                _bubbleBatch.Collect();
            }
            var bubble = _bubbles[i];
            if (!bubble.gameObject.activeSelf) bubble.gameObject.SetActive(true);
            bubble.localPosition = pos;
            var tmp = bubble.Find("BubbleText");
            if (tmp != null) tmp.GetComponent<TextMeshPro>().text = text;
        }
    }
}
