using System.Diagnostics;
using Resq.Sim;
using UnityEngine;

namespace Resq.Game
{
    /// <summary>
    /// Forecast preview (spec 02 forecast; RV01–RV02): while a tool is armed over a legal socket, run the real
    /// simulator ahead in isolated clones, once without and once with the placement, and ghost the difference on the
    /// board — green where the placement keeps a cell dry, red where it floods it, cyan where water still reaches.
    /// The live run is never touched. Results also give a one-line people summary for the operation board.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class ForecastView : MonoBehaviour
    {
        public SimDriver Driver;
        public BoardInteraction Interaction;
        public int HorizonTicks = Forecaster.DefaultHorizon;
        public float HoverDelay = 0.2f;
        public const long SafeDepthMm = 100;

        public string Summary { get; private set; } = "";
        public double LastMs { get; private set; }
        public bool Showing { get; private set; }

        MissionDef _def;
        Mesh _mesh;
        Color[] _colors;
        readonly Forecaster _forecaster = new Forecaster();
        ForecastResult _baseline, _preview;
        int _hoverSocket = -1;
        ToolKind _hoverTool;
        float _hoverSince;
        long _computedAtO = -1;
        int _computedSocket = -1;
        bool _pinned; // a dev/capture preview stays until a real pointer hover replaces it

        static readonly Color Saved = TerrainBuilder.Hex(0x7BD389), Worse = TerrainBuilder.Hex(0xE5484D), Ghost = TerrainBuilder.Hex(0x7FD4F0);

        public void Build(MissionDef d)
        {
            _def = d;
            _baseline = new ForecastResult(d);
            _preview = new ForecastResult(d);
            int n = d.GridW * d.GridH;
            var verts = new Vector3[n * 4];
            _colors = new Color[n * 4];
            var tris = new int[n * 6];
            float h = BoardSpace.CellSize(d) * 0.45f;
            for (int c = 0; c < n; c++)
            {
                var p = BoardSpace.CellCenter(d, c);
                p.y += 0.0015f;
                verts[c * 4 + 0] = p + new Vector3(-h, 0, -h);
                verts[c * 4 + 1] = p + new Vector3(-h, 0, h);
                verts[c * 4 + 2] = p + new Vector3(h, 0, h);
                verts[c * 4 + 3] = p + new Vector3(h, 0, -h);
                int t = c * 6, v = c * 4;
                tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2; tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
            }
            _mesh = new Mesh { name = "Forecast_" + d.MissionId, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            _mesh.MarkDynamic();
            _mesh.vertices = verts; _mesh.colors = _colors; _mesh.triangles = tris;
            _mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            Hide();
        }

        void Hide()
        {
            if (!Showing && Summary.Length == 0) return;
            for (int i = 0; i < _colors.Length; i++) _colors[i] = new Color(0, 0, 0, 0);
            if (_mesh != null) _mesh.colors = _colors;
            Showing = false;
            Summary = "";
            _computedSocket = -1;
        }

        void LateUpdate()
        {
            if (Driver == null || Driver.Sim == null || Driver.Def != _def || Interaction == null) return;
            var hover = Interaction.Hover;
            bool armed = Interaction.Mode == InteractMode.ToolArmed && hover != null && hover.Kind == PickKind.Socket &&
                         Interaction.HoverVerdict == Reject.OK;
            if (!armed) { _hoverSocket = -1; if (!_pinned) Hide(); return; }
            _pinned = false;
            if (hover.Index != _hoverSocket || Interaction.ArmedTool != _hoverTool)
            {
                _hoverSocket = hover.Index; _hoverTool = Interaction.ArmedTool; _hoverSince = Time.unscaledTime;
                return;
            }
            if (Time.unscaledTime - _hoverSince < HoverDelay) return;
            var sim = Driver.Sim;
            // recompute when the hovered placement changes or the live run has moved on a second
            if (_computedSocket == _hoverSocket && sim.S.C.O - _computedAtO < 10) return;
            Preview(_hoverSocket, _hoverTool);
        }

        /// <summary>Forecast one placement against doing nothing and paint the difference (also used by dev captures).</summary>
        public void Preview(int socket, ToolKind tool, bool pin = false)
        {
            _pinned = pin;
            var sim = Driver.Sim;
            var sw = Stopwatch.StartNew();
            _forecaster.Run(sim, null, HorizonTicks, _baseline);
            _forecaster.Run(sim, Command.Place(socket, tool, 0), HorizonTicks, _preview);
            LastMs = sw.Elapsed.TotalMilliseconds;
            _computedSocket = socket; _computedAtO = sim.S.C.O;
            Paint();
        }

        void Paint()
        {
            var d = _def;
            int n = d.GridW * d.GridH;
            for (int c = 0; c < n; c++)
            {
                long b = _baseline.MaxDepth[c], p = _preview.MaxDepth[c];
                bool landish = d.CellElevation[c] > 0 || d.CellTerrain[c] != 0;
                Color col = new Color(0, 0, 0, 0);
                if (landish && b > SafeDepthMm && p <= SafeDepthMm) col = new Color(Saved.r, Saved.g, Saved.b, 0.55f);
                else if (landish && p > SafeDepthMm && b <= SafeDepthMm) col = new Color(Worse.r, Worse.g, Worse.b, 0.6f);
                else if (landish && p > SafeDepthMm) col = new Color(Ghost.r, Ghost.g, Ghost.b, Mathf.Clamp01(p / 1200f) * 0.35f);
                int v = c * 4;
                _colors[v] = col; _colors[v + 1] = col; _colors[v + 2] = col; _colors[v + 3] = col;
            }
            _mesh.colors = _colors;
            Showing = true;
            int saved = _preview.Evacuated - _baseline.Evacuated;
            int stranded = _preview.Backup - _baseline.Backup;
            int routes = 0;
            for (int e = 0; e < d.EdgeIds.Length; e++)
                if (_baseline.EdgeEverClosed[e] && !_preview.EdgeEverClosed[e]) routes++;
            Summary = $"Forecast {HorizonTicks / 10}s: " +
                      (saved > 0 ? $"+{saved} safe " : "") + (stranded > 0 ? $"{stranded} stranded " : "") +
                      (routes > 0 ? $"{routes} route{(routes > 1 ? "s" : "")} kept open" : saved <= 0 && stranded <= 0 ? "no change in rescues" : "");
        }
    }
}
