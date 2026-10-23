using Resq.Game;
using UnityEngine;

namespace Resq.XR
{
    /// <summary>
    /// The fallback surface when the room has no table that fits (or no scene data): a simple wooden table under
    /// the board, top flush with the board's base, legs down to the floor. It's parented to the board root, so it
    /// moves and anchors with the diorama.
    /// </summary>
    public static class VirtualTable
    {
        public static GameObject Build(GameBootstrap boot, Pose board, Vector2 footprint)
        {
            float topY = board.position.y - BoardSpace.BaseDepth;
            float floorY = Mathf.Min(0f, topY - 0.4f);   // FloorLevel tracking: the floor is y = 0
            float w = footprint.x + 0.08f, d = footprint.y + 0.08f, thick = 0.03f, leg = 0.045f;
            float legH = topY - thick - floorY;
            var wood = TerrainBuilder.Hex(0x8A6242); var woodDark = TerrainBuilder.Hex(0x6B4A32);
            var kit = new MeshKit();
            kit.Box(new Vector3(0, -BoardSpace.BaseDepth - thick * 0.5f, 0), new Vector3(w, thick, d), wood);
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    kit.Box(new Vector3(sx * (w * 0.5f - leg), -BoardSpace.BaseDepth - thick - legH * 0.5f, sz * (d * 0.5f - leg)), new Vector3(leg, legH, leg), woodDark);
            var go = new GameObject("VirtualTable");
            go.transform.SetParent(boot.BoardRoot, false);
            go.AddComponent<MeshFilter>().sharedMesh = kit.Build("VirtualTable");
            go.AddComponent<MeshRenderer>().sharedMaterial = boot.ActorMaterial;
            return go;
        }
    }
}
