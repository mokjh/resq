using System.Text;
using Unity.Profiling;
using UnityEngine;

namespace Resq.Game
{
    /// <summary>
    /// Development diagnostic: averages render and frame counters over a window of frames, for the per-world budget
    /// check (BUILD_PLAN: ≤150 draw calls, ≤300k triangles, 72 Hz on Quest 3/3S). Uses ProfilerRecorder, so it works in
    /// the Editor and development players. Editor numbers include Editor overhead and a mono view; they're a budget
    /// screen, not device evidence.
    /// </summary>
    public sealed class PerfProbe : MonoBehaviour
    {
        public int Frames = 120;
        public static string LastReport = "";
        public static bool Done;
        /// <summary>Numeric results of the last window (for budget assertions).</summary>
        public static float AvgDraw, AvgTris;
        public static long MaxDraw, MaxTris;
        public static int RenderedFrames;

        ProfilerRecorder _draw, _batches, _setPass, _tris, _verts, _shadowCasters, _main, _render, _gc;
        int _n;
        double _sDraw, _sBatch, _sSet, _sTris, _sVerts, _sShadow, _sMain, _sRender, _sGc, _sDt;
        long _maxDraw, _maxTris;

        void OnEnable()
        {
            Done = false; LastReport = ""; RenderedFrames = 0;
            _draw = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            _verts = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
            _shadowCasters = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
            _main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
            _render = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Render Thread", 15);
            _gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            _n = 0; _sDraw = _sBatch = _sSet = _sTris = _sVerts = _sShadow = _sMain = _sRender = _sGc = _sDt = 0; _maxDraw = _maxTris = 0;
        }

        void OnDisable()
        {
            _draw.Dispose(); _batches.Dispose(); _setPass.Dispose(); _tris.Dispose(); _verts.Dispose();
            _shadowCasters.Dispose(); _main.Dispose(); _render.Dispose(); _gc.Dispose();
        }

        void LateUpdate()
        {
            if (Done) return;
            _n++;
            if (_n <= 10) return; // let the board settle (first frames build meshes and upload)
            _sDraw += _draw.LastValue; _sBatch += _batches.LastValue; _sSet += _setPass.LastValue;
            _sTris += _tris.LastValue; _sVerts += _verts.LastValue; _sShadow += _shadowCasters.LastValue;
            _sMain += _main.LastValue * 1e-6; _sRender += _render.LastValue * 1e-6; _sGc += _gc.LastValue;
            _sDt += Time.unscaledDeltaTime * 1000.0;
            if (_draw.LastValue > 0) RenderedFrames++;
            if (_draw.LastValue > _maxDraw) _maxDraw = _draw.LastValue;
            if (_tris.LastValue > _maxTris) _maxTris = _tris.LastValue;
            if (_n - 10 >= Frames)
            {
                int k = _n - 10;
                var sb = new StringBuilder();
                sb.Append($"draw={_sDraw / k:F0} (max {_maxDraw}) batches={_sBatch / k:F0} setpass={_sSet / k:F0} ");
                sb.Append($"tris={_sTris / k / 1000:F1}k (max {_maxTris / 1000f:F1}k) verts={_sVerts / k / 1000:F1}k shadowCasters={_sShadow / k:F0} ");
                sb.Append($"main={_sMain / k:F2}ms render={_sRender / k:F2}ms frame={_sDt / k:F2}ms gc={_sGc / k:F0}B");
                LastReport = sb.ToString();
                AvgDraw = (float)(_sDraw / k); AvgTris = (float)(_sTris / k); MaxDraw = _maxDraw; MaxTris = _maxTris;
                Done = true;
            }
        }
    }
}
