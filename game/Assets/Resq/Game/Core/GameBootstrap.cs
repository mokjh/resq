using Resq.Sim;
using UnityEngine;
using UnityEngine.XR;

namespace Resq.Game
{
    /// <summary>
    /// Wires a playable operation: sim driver, board, interaction, panels and pointer adapters. Works in XR
    /// (OVR rig present) and on the desktop (mouse + an orbiting seated-view camera) for Editor development.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        public string MissionId = "W1-M1";
        public string LayoutClass = "wide";
        public RuleProfile Profile = RuleProfile.Standard;
        public bool Guided;
        public Material TerrainMaterial, WaterMaterial, ActorMaterial, MarkerMaterial, CardMaterial;
        [Tooltip("Board placement when no MR placement has run yet (metres, rig space).")]
        public Vector3 DefaultBoardPosition = new Vector3(0, 0.74f, 0.55f);

        public SimDriver Driver { get; private set; }
        public BoardView Board { get; private set; }
        public BoardInteraction Interaction { get; private set; }

        void Awake()
        {
            EnsureMaterials();
            var boardGo = new GameObject("Board");
            boardGo.transform.position = DefaultBoardPosition;
            Driver = boardGo.AddComponent<SimDriver>();
            Driver.AutoStart = false;
            Interaction = boardGo.AddComponent<BoardInteraction>();
            Board = boardGo.AddComponent<BoardView>();
            Board.Driver = Driver; Board.Interaction = Interaction;
            Board.TerrainMaterial = TerrainMaterial; Board.WaterMaterial = WaterMaterial;
            Board.ActorMaterial = ActorMaterial; Board.MarkerMaterial = MarkerMaterial;
            Interaction.Driver = Driver; Interaction.Board = Board;
            var panelGo = new GameObject("OperationPanel");
            panelGo.transform.SetParent(boardGo.transform, false);
            var panel = panelGo.AddComponent<OperationPanel>();
            panel.Driver = Driver; panel.Interaction = Interaction; panel.CardMaterial = CardMaterial;
            // enable after wiring so OnEnable subscriptions see the driver
            Board.enabled = false; Board.enabled = true;
            panel.enabled = false; panel.enabled = true;
        }

        void Start()
        {
            SetupPointers();
            Driver.LoadRun(MissionId, LayoutClass, Profile, Guided);
            if (XRSettings.isDeviceActive) StartCoroutine(PlaceInFrontOfHead());
        }

        /// <summary>
        /// Default seated placement (spec 05: board height = eye − 300 mm, 550 mm in front, facing the player).
        /// MRUK table placement replaces this when a real surface is chosen.
        /// </summary>
        System.Collections.IEnumerator PlaceInFrontOfHead()
        {
            var rig = FindFirstObjectByType<OVRCameraRig>();
            for (int i = 0; i < 30 && (rig == null || rig.centerEyeAnchor.position.sqrMagnitude < 1e-6f); i++) yield return null;
            if (rig == null) yield break;
            yield return null;
            var head = rig.centerEyeAnchor;
            var fwd = head.forward; fwd.y = 0;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            var board = Driver.transform;
            board.position = head.position + fwd * 0.55f + Vector3.down * 0.30f;
            board.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        }

        void SetupPointers()
        {
            var rig = FindFirstObjectByType<OVRCameraRig>();
            bool xr = rig != null && XRSettings.isDeviceActive;
            if (xr)
            {
                foreach (var hand in FindObjectsByType<OVRHand>(FindObjectsSortMode.None))
                {
                    var skel = hand.GetComponent<OVRSkeleton>();
                    bool left = hand.GetHand() == OVRPlugin.Hand.HandLeft;
                    Interaction.AddSource(new HandPointer(hand, skel, left));
                }
                if (Profile == RuleProfile.Command) Interaction.AddSource(new HeadDwellPointer(rig.centerEyeAnchor));
            }
            var cam = Camera.main;
            if (cam == null || !xr)
            {
                cam = DesktopCamera();
                Interaction.AddSource(new DesktopPointer(cam));
            }
            else Interaction.AddSource(new DesktopPointer(cam));
        }

        Camera DesktopCamera()
        {
            var existing = Camera.main;
            if (existing != null && existing.GetComponent<DesktopOrbit>() != null) return existing;
            var go = existing != null ? existing.gameObject : new GameObject("DesktopCamera", typeof(Camera));
            go.tag = "MainCamera";
            var cam = go.GetComponent<Camera>();
            cam.nearClipPlane = 0.01f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.13f, 0.14f);
            var orbit = go.AddComponent<DesktopOrbit>();
            orbit.Target = DefaultBoardPosition;
            return cam;
        }

        void EnsureMaterials()
        {
            if (TerrainMaterial == null) TerrainMaterial = new Material(Shader.Find("Resq/VertexLit"));
            if (ActorMaterial == null) ActorMaterial = TerrainMaterial;
            if (MarkerMaterial == null) MarkerMaterial = new Material(Shader.Find("Resq/VertexLit"));
            if (CardMaterial == null) CardMaterial = new Material(Shader.Find("Resq/VertexLit"));
            if (WaterMaterial == null) WaterMaterial = new Material(Shader.Find("Resq/Water"));
        }
    }

    /// <summary>Editor/desktop seated view: right-drag orbits, scroll zooms, around the board.</summary>
    public sealed class DesktopOrbit : MonoBehaviour
    {
        public Vector3 Target;
        public float Distance = 0.62f, Pitch = 38f, Yaw = 0f;

        void LateUpdate()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                if (mouse.rightButton.isPressed)
                {
                    var d = mouse.delta.ReadValue();
                    Yaw += d.x * 0.2f; Pitch = Mathf.Clamp(Pitch - d.y * 0.2f, 10f, 85f);
                }
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f) Distance = Mathf.Clamp(Distance * (1f - scroll * 0.0015f), 0.25f, 1.5f);
            }
            var rot = Quaternion.Euler(Pitch, Yaw, 0);
            transform.position = Target + rot * new Vector3(0, 0, -Distance);
            transform.rotation = Quaternion.LookRotation(Target - transform.position, Vector3.up);
        }
    }
}
