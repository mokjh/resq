using Resq.Sim;
using UnityEngine;

namespace Resq.Game
{
    /// <summary>
    /// Maps logical simulation coordinates (32x24 cells, mm elevations) to board-local metres. Wide boards use
    /// 20 mm cells (1:50); compact boards scale uniformly by 5/6 — presentation only, never rules.
    /// </summary>
    public static class BoardSpace
    {
        public const float WideCell = 0.02f;
        public const float VerticalExaggeration = 2.0f;
        public const float BaseDepth = 0.035f;   // cutaway block thickness below elevation 0

        public static float CellSize(MissionDef d) => d.LayoutClass == "compact" ? WideCell * 5f / 6f : WideCell;

        public static float Scale(MissionDef d) => CellSize(d) / 1000f; // metres per logical mm (horizontal)

        public static float Height(MissionDef d, long mm) => mm * Scale(d) * VerticalExaggeration;

        public static Vector3 CellCenter(MissionDef d, int cell, long elevationMm)
        {
            float s = CellSize(d);
            float x = (d.CellX(cell) + 0.5f - d.GridW * 0.5f) * s;
            float z = (d.CellY(cell) + 0.5f - d.GridH * 0.5f) * s;
            return new Vector3(x, Height(d, elevationMm), z);
        }

        public static Vector3 CellCenter(MissionDef d, int cell) => CellCenter(d, cell, d.CellElevation[cell]);

        public static Vector2 Extent(MissionDef d) => new Vector2(d.GridW * CellSize(d), d.GridH * CellSize(d));
    }
}
