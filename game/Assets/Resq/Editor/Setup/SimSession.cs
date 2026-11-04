using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Resq.EditorTools
{
    /// <summary>
    /// Opens Operation_XR and enters Play mode for Meta XR Simulator sessions started from the shell (tools/xr/sim_editor.sh).
    /// The request survives domain reloads, because a start-up import or recompile would otherwise drop it.
    /// </summary>
    [InitializeOnLoad]
    public static class SimSession
    {
        const string Pending = "ResqSim.pending";

        static SimSession() { if (SessionState.GetBool(Pending, false)) Arm(); }

        public static void Play()
        {
            SessionState.SetBool(Pending, true);
            Arm();
        }

        static void Arm() { EditorApplication.update -= Tick; EditorApplication.update += Tick; }

        static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorApplication.update -= Tick;
            SessionState.SetBool(Pending, false);
            EditorSceneManager.OpenScene("Assets/Resq/Scenes/Operation_XR.unity");
            Debug.Log("[RESQ sim] entering play mode");
            EditorApplication.EnterPlaymode();
        }
    }
}
