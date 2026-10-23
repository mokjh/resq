using System.Collections.Generic;
using Resq.Sim;
using UnityEngine;

namespace Resq.Game
{
    /// <summary>
    /// Plays RESQ's synthesised audio (tools/audio/synth.py → Resources/ResqAudio). Presentation only: it reads sim
    /// events and state, never writes them.
    ///   • music: each world's calm stem, with its problem stem faded in while a warning is fresh, a hazard is due
    ///     within ten seconds or residents await backup; the resolve sting on a successful outcome;
    ///   • ambience: the world's loop under everything, quieter in menus;
    ///   • effects: placement lock, blocked target, warning, boarding, engines, arrival, rescue chime, pump, power,
    ///     cooling, creak/collapse and the outcome stings, placed on the board where they happen;
    ///   • UI: press and hover ticks from the shared interaction layer.
    /// Music, effects and warnings follow the Settings volumes (U21). Critical signals (warning, blocked, backup)
    /// use the Warnings volume so they stay audible when music or effects are turned down.
    /// </summary>
    public sealed class AudioDirector : MonoBehaviour
    {
        public SimDriver Driver;
        public BoardInteraction Interaction;

        AudioSource _calm, _problem, _amb, _ui, _sting;
        readonly List<AudioSource> _pool = new List<AudioSource>();
        readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        string _world;
        float _problemLevel, _warnUntil, _lastResidentTick, _lastBoard, _lastHover;
        readonly HashSet<int> _groupsSafe = new HashSet<int>();
        int[] _vEdgePrev = new int[0];
        Pickable _hoverPrev;

        const float MusicBase = 0.55f, AmbBase = 0.35f;

        // read-only views for tests and diagnostics
        public AudioClip MusicClip => _calm != null ? _calm.clip : null;
        public AudioClip AmbienceClip => _amb != null ? _amb.clip : null;
        public float MusicVolume => _calm != null ? _calm.volume : 0;
        public float ProblemLevel => _problemLevel;

        void Awake()
        {
            _calm = Source2D("MusicCalm", true);
            _problem = Source2D("MusicProblem", true);
            _amb = Source2D("Ambience", true);
            _ui = Source2D("UI", false);
            _sting = Source2D("Sting", false);
            for (int i = 0; i < 10; i++)
            {
                var go = new GameObject("Sfx" + i);
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false; s.spatialBlend = 0.7f; s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 0.4f; s.maxDistance = 6f; s.dopplerLevel = 0;
                _pool.Add(s);
            }
        }

        void Start()
        {
            // the desktop camera is created at runtime without a listener; the XR rig's centre eye brings its own
            if (FindFirstObjectByType<AudioListener>() == null && Camera.main != null) Camera.main.gameObject.AddComponent<AudioListener>();
        }

        AudioSource Source2D(string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false; s.loop = loop; s.spatialBlend = 0; s.dopplerLevel = 0;
            return s;
        }

        void OnEnable()
        {
            if (Driver != null) { Driver.OnRunLoaded += OnRunLoaded; Driver.OnSimEvent += OnSimEvent; }
            BoardInteraction.Pressed += OnPressed;
            BoardInteraction.Rejected += OnRejected;
        }

        void OnDisable()
        {
            if (Driver != null) { Driver.OnRunLoaded -= OnRunLoaded; Driver.OnSimEvent -= OnSimEvent; }
            BoardInteraction.Pressed -= OnPressed;
            BoardInteraction.Rejected -= OnRejected;
        }

        AudioClip Clip(string name)
        {
            if (_clips.TryGetValue(name, out var c)) return c;
            c = Resources.Load<AudioClip>("ResqAudio/" + name);
            _clips[name] = c;
            return c;
        }

        static Settings Prefs => SaveStore.Current.Settings;
        static float Music => Prefs.Music / 100f;
        static float Effects => Prefs.Effects / 100f;
        static float Warnings => Prefs.Warnings / 100f;

        // ------------------------------------------------------------------ world beds
        void OnRunLoaded()
        {
            _groupsSafe.Clear();
            _warnUntil = 0; _problemLevel = 0;
            _vEdgePrev = new int[Driver.Sim?.S.C.VehicleCount ?? 0];
            for (int i = 0; i < _vEdgePrev.Length; i++) _vEdgePrev[i] = -1;
            string w = (Driver.Def?.WorldId ?? "W1").ToLowerInvariant();
            if (w == _world) return;
            _world = w;
            SetLoop(_calm, $"music_{w}_calm");
            SetLoop(_problem, $"music_{w}_problem");
            string amb = w == "w1" ? "amb_w1_water" : w == "w2" ? "amb_w2_forest" : w == "w3" ? "amb_w3_wind" : w == "w4" ? "amb_w4_harbour" : "amb_w5_rumble";
            SetLoop(_amb, amb);
            // the stems share one clock, so the problem layer always lands on the beat
            if (_calm.clip != null && _problem.clip != null) _problem.timeSamples = _calm.timeSamples % _problem.clip.samples;
        }

        void SetLoop(AudioSource s, string clip)
        {
            var c = Clip(clip);
            if (c == null) { s.Stop(); return; }
            if (s.clip == c && s.isPlaying) return;
            s.clip = c; s.time = 0; s.Play();
        }

        void Update()
        {
            if (Driver?.Sim == null) return;
            bool menus = Interaction != null && Interaction.MenuOnly;
            float target = 0;
            if (!menus && !Driver.Paused && !Driver.Sim.IsOver)
            {
                var sim = Driver.Sim;
                if (Time.unscaledTime < _warnUntil || sim.S.C.AwaitingBackup > 0) target = 1;
                else foreach (var ev in Driver.Def.Events)
                    {
                        long dt = ev.AtTick - sim.S.C.S;
                        if (ev.WarningTick <= sim.S.C.S && dt > 0 && dt <= 100) { target = 1; break; }   // due within 10 s
                    }
            }
            _problemLevel = Mathf.MoveTowards(_problemLevel, target, Time.unscaledDeltaTime / 1.5f);
            float m = Music * MusicBase * (menus ? 0.6f : 1f);
            _calm.volume = m;
            _problem.volume = m * _problemLevel;
            _amb.volume = Effects * AmbBase * (menus ? 0.5f : 1f);
            WatchVehicles();
            WatchHover();
        }

        /// <summary>Engines start when a vehicle leaves a node (the sim has no departure event).</summary>
        void WatchVehicles()
        {
            var st = Driver.Sim.S;
            if (_vEdgePrev.Length != st.C.VehicleCount) return;
            for (int v = 0; v < st.C.VehicleCount; v++)
            {
                int e = st.VEdge[v];
                if (e >= 0 && _vEdgePrev[v] < 0)
                {
                    var kind = (VehicleKind)st.VKind[v];
                    var pos = NodePos(st.VFrom[v]);
                    if (kind == VehicleKind.Winch) Play("winch", pos, 0.6f);
                    else Play("boat_depart", pos, kind == VehicleKind.Skiff ? 0.55f : 0.45f, kind == VehicleKind.Skiff ? 1f : 0.7f);
                }
                _vEdgePrev[v] = e;
            }
        }

        void WatchHover()
        {
            if (Interaction == null) return;
            var h = Interaction.Hover;
            if (h != _hoverPrev && h != null && (h.Kind == PickKind.Button || h.Kind == PickKind.Tool) && Time.unscaledTime - _lastHover > 0.08f)
            {
                _ui.PlayOneShot(Clip("ui_hover"), Effects * 0.25f);
                _lastHover = Time.unscaledTime;
            }
            _hoverPrev = h;
        }

        // ------------------------------------------------------------------ input
        void OnPressed(Pickable p)
        {
            if (p == null) return;
            _ui.PlayOneShot(Clip("ui_press"), Effects * 0.5f);
        }

        void OnRejected() => _ui.PlayOneShot(Clip("blocked"), Warnings * 0.6f);

        // ------------------------------------------------------------------ sim events
        void OnSimEvent(SimEvent e)
        {
            var d = Driver.Def; var st = Driver.Sim.S;
            switch (e.Kind)
            {
                case SimEventKind.EquipmentPlaced:
                {
                    var pos = SocketPos(e.A);
                    var tool = (ToolKind)e.B;
                    Play("place_lock", pos, 0.8f);
                    if (tool == ToolKind.Firebreak) Play("firebreak", pos, 0.5f);
                    if (tool == ToolKind.Pump) Play("pump_start", pos, 0.45f);
                    if (tool == ToolKind.Barrier) Play("water_divert", pos, 0.4f);
                    break;
                }
                case SimEventKind.CommandResolved when e.Code != Reject.OK:
                    _ui.PlayOneShot(Clip("blocked"), Warnings * 0.55f);
                    break;
                case SimEventKind.Warning:
                    _warnUntil = Time.unscaledTime + 8f;
                    _ui.PlayOneShot(Clip("warning"), Warnings * 0.75f);
                    break;
                case SimEventKind.TransferStarted:
                    if (Time.unscaledTime - _lastBoard > 0.35f) { Play("boarding", NodePos(e.B), 0.5f); _lastBoard = Time.unscaledTime; }
                    break;
                case SimEventKind.VehicleArrived:
                    Play("vehicle_arrive", NodePos(e.A), 0.45f);
                    break;
                case SimEventKind.ResidentEvacuated:
                {
                    int g = e.Subject >= 0 && e.Subject < d.ResidentGroup.Length ? d.ResidentGroup[e.Subject] : -1;
                    if (g >= 0 && !_groupsSafe.Contains(g) && GroupDone(g)) { _groupsSafe.Add(g); _ui.PlayOneShot(Clip("rescue_chime"), Effects * 0.7f); }
                    else if (Time.unscaledTime - _lastResidentTick > 0.15f) { Play("resident_safe", NodePos(e.A), 0.35f); _lastResidentTick = Time.unscaledTime; }
                    break;
                }
                case SimEventKind.ResidentBackup:
                    _warnUntil = Time.unscaledTime + 8f;
                    if (Time.unscaledTime - _lastResidentTick > 1f) { _ui.PlayOneShot(Clip("outcome_backup"), Warnings * 0.35f); _lastResidentTick = Time.unscaledTime; }
                    break;
                case SimEventKind.PowerChanged: Play("power_on", Board, 0.45f); break;
                case SimEventKind.GeneratorShutdown: Play("power_off", Board, 0.5f); break;
                case SimEventKind.SegmentCooling: Play("cooling", Board, 0.55f); break;
                case SimEventKind.SupportWarning: Play("creak", Board, 0.45f); break;
                case SimEventKind.SupportCollapsed: case SimEventKind.RunoutStarted: Play("collapse", Board, 0.5f); break;
                case SimEventKind.EquipmentBroken: Play("collapse", SocketPos(e.A), 0.35f, 1.3f); break;
                case SimEventKind.OperationEnded:
                    _problemLevel = 0;
                    if (st.C.Success)
                    {
                        _sting.PlayOneShot(Clip("outcome_success"), Effects * 0.7f);
                        var res = Clip($"music_{_world}_resolve");
                        if (res != null) _sting.PlayOneShot(res, Music * MusicBase);
                    }
                    else _sting.PlayOneShot(Clip("outcome_backup"), Warnings * 0.6f);
                    break;
            }
        }

        Vector3 Board => Driver.transform.position;

        bool GroupDone(int g)
        {
            var st = Driver.Sim.S;
            foreach (int r in Driver.Def.GroupResidents[g]) if (st.RState[r] != (byte)ResidentState.Evacuated) return false;
            return true;
        }

        Vector3 NodePos(int node)
        {
            var d = Driver.Def;
            if (d == null || node < 0 || node >= d.NodeCell.Length || d.NodeCell[node] < 0) return Board;
            return Driver.transform.TransformPoint(BoardSpace.CellCenter(d, d.NodeCell[node]));
        }

        Vector3 SocketPos(int socket)
        {
            var d = Driver.Def;
            if (d == null || socket < 0 || socket >= d.SocketCell.Length || d.SocketCell[socket] < 0) return Board;
            return Driver.transform.TransformPoint(BoardSpace.CellCenter(d, d.SocketCell[socket]));
        }

        void Play(string clip, Vector3 pos, float volume, float pitch = 1f)
        {
            var c = Clip(clip);
            if (c == null) return;
            AudioSource src = null;
            foreach (var s in _pool) if (!s.isPlaying) { src = s; break; }
            if (src == null) src = _pool[0];
            src.transform.position = pos;
            src.pitch = pitch;
            src.clip = c;
            src.volume = volume * Effects;
            src.Play();
        }
    }
}
