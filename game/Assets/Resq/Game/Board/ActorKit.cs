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

        [Header("Rigged residents (Meshy, Look 3)")]
        [Tooltip("Second adult, mixed in with ResidentAdult.")]
        public GameObject ResidentAdultB;
        [Tooltip("Per rigged model: its textured material and walk controller (same order: Adult, AdultB, Elder).")]
        public Material AdultMaterial, AdultBMaterial, ElderMaterial;
        public RuntimeAnimatorController AdultAnim, AdultBAnim, ElderAnim;
        [Tooltip("Rescue crew at landings: the AdultB rig in a hi-vis uniform (presentation only).")]
        public Material CrewMaterial;
        [Tooltip("Step-free residents: the kit chair with the seated rig in it (replaces ResidentWheelchair when set).")]
        public GameObject WheelchairChair, SeatedRider;
        public Material SeatedMaterial;
        public RuntimeAnimatorController SeatedAnim;
        [Tooltip("Walk-clip time held as the standing pose (feet together).")]
        public float IdleTime = 0f;
        [Tooltip("When no child model exists, children are the adult scaled down with a larger head.")]
        public float ChildScale = 0.72f, ChildHead = 1.28f;
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
            if (pick < 2 && ResidentChild == null && AdultAnim != null) return ResidentAdult;   // child from the adult rig
            if (pick < 4 && ResidentElder != null) return ResidentElder;
            // children and seated residents use the woman rig, so two in three adults are the man; a second, well-mixed
            // hash keeps consecutive residents (one group) from all landing on the same model
            uint g = unchecked((uint)(residentIndex + 1) * 0x9E3779B1u ^ 0x5bd1e995u);
            g ^= g >> 15; g = unchecked(g * 0x2c1b3c6du); g ^= g >> 12;
            if (ResidentAdultB != null && g % 3 != 0) return ResidentAdultB;
            return ResidentAdult;
        }

        /// <summary>True when this resident index is shown as a child made from the adult rig.</summary>
        public bool IsDerivedChild(int residentIndex, bool stepFree)
        {
            if (stepFree || ResidentChild != null || AdultAnim == null) return false;
            uint h = unchecked((uint)(residentIndex + 1) * 2654435761u) >> 16;
            return (int)(h % 10) < 2;
        }

        /// <summary>Material and walk controller for a rigged resident model (null for the vertex-colour kit).</summary>
        public void RigFor(GameObject model, out Material mat, out RuntimeAnimatorController anim)
        {
            mat = null; anim = null;
            if (model == null) return;
            if (model == ResidentAdult) { mat = AdultMaterial; anim = AdultAnim; }
            else if (model == ResidentAdultB) { mat = AdultBMaterial; anim = AdultBAnim; }
            else if (model == ResidentElder) { mat = ElderMaterial; anim = ElderAnim; }
            else if (model == SeatedRider) { mat = SeatedMaterial; anim = SeatedAnim; }
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
