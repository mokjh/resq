using UnityEngine;

namespace Resq.Game
{
    /// <summary>
    /// Maps board actor roles to art-kit models (Resources/ResqActorKit, built by RESQ/Art/Build Actor Kit).
    /// Kit assets are authored at board scale (1:50, millimetre-accurate), so wide boards use them at scale 1 and
    /// compact boards at 5/6. Presentation only: the simulation never reads the kit. Missing entries fall back to greybox.
    /// </summary>
    [CreateAssetMenu(menuName = "RESQ/Actor Kit")]
    public sealed class ActorKit : ScriptableObject
    {
        public GameObject ResidentAdult, ResidentChild, ResidentElder, ResidentWheelchair;
        public GameObject Skiff, Bridge, Cottage;

        [Tooltip("Yaw that turns each model's authored front towards +Z (Unity forward).")]
        public float ResidentYaw = 180f, SkiffYaw = -90f, CottageYaw = 180f;

        [Tooltip("Bridge length along its local X in the kit, in metres (sidecar: 120 mm).")]
        public float BridgeLength = 0.12f;

        [Tooltip("Upper bound on decorative cottages per board.")]
        public int MaxCottages = 6;

        static ActorKit _cached;
        static bool _looked;

        public static ActorKit Load()
        {
            if (!_looked) { _cached = Resources.Load<ActorKit>("ResqActorKit"); _looked = true; }
            return _cached;
        }

        /// <summary>Resident model by mobility and a deterministic per-resident mix (about 1 in 5 children, 1 in 5 elders).</summary>
        public GameObject ResidentFor(int residentIndex, bool stepFree)
        {
            if (stepFree && ResidentWheelchair != null) return ResidentWheelchair;
            uint h = unchecked((uint)(residentIndex + 1) * 2654435761u) >> 16;
            int pick = (int)(h % 10);
            if (pick < 2 && ResidentChild != null) return ResidentChild;
            if (pick < 4 && ResidentElder != null) return ResidentElder;
            return ResidentAdult;
        }

        /// <summary>
        /// Instantiates a kit model as pure presentation: its proxy colliders are removed so they can never block
        /// pointer picks on sockets, nodes, groups or vehicles.
        /// </summary>
        public static GameObject Spawn(GameObject prefab, Transform parent, string name)
        {
            var go = Instantiate(prefab, parent, false);
            go.name = name;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            return go;
        }
    }
}
