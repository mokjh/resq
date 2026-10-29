using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Resq.EditorTools
{
    /// <summary>
    /// Website object shots (tools/web/capture_objects.sh): frames the running game's valley like a product render,
    /// hides the HUD, tool tray, menu card and board labels, and renders the camera twice in the same frame, once
    /// on black and once on white, into tools/.cache/objects/&lt;name&gt;_b.png / _w.png. tools/web/cutout.py
    /// recovers exact transparency from the pair, so the valley can sit on a flat colour field with no keying
    /// fringe. Presentation only: nothing here touches the simulation.
    /// </summary>
    public static class ObjectShot
    {
        public const int Version = 3;
        static readonly string[] Chrome = { "OperationPanel", "Hud", "Menu" };

        /// <summary>Whole valley: the camera looks at the board's bounds from yaw/pitch with the given lens and
        /// backs off until the bounds fit, with a margin.</summary>
        public static string Board(string name, int w, int h, float yaw, float pitch, float fov = 26f, float margin = 1.08f)
        {
            var root = GameObject.Find("BoardRoot").transform;
            var rs = root.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds;
            foreach (var r in rs) if (r.enabled && r.gameObject.activeInHierarchy) b.Encapsulate(r.bounds);
            float radius = b.extents.magnitude;
            float dist = radius * margin / Mathf.Sin(fov * 0.5f * Mathf.Deg2Rad);
            return Render(name, w, h, b.center, yaw, pitch, dist, fov);
        }

        /// <summary>Close detail around a point on the board (a group, a vehicle, a hazard front).</summary>
        public static string Detail(string name, int w, int h, Vector3 target, float yaw, float pitch, float dist, float fov = 30f)
            => Render(name, w, h, target, yaw, pitch, dist, fov);

        static string Render(string name, int w, int h, Vector3 target, float yaw, float pitch, float dist, float fov)
        {
            var cam = Camera.main;
            var board = GameObject.Find("Board").transform;
            var hidden = new List<GameObject>();
            foreach (var n in Chrome)
            {
                var t = board.Find(n);
                if (t != null && t.gameObject.activeSelf) { t.gameObject.SetActive(false); hidden.Add(t.gameObject); }
            }
            foreach (var tx in board.GetComponentsInChildren<TMPro.TMP_Text>())
                if (tx.gameObject.activeSelf) { tx.gameObject.SetActive(false); hidden.Add(tx.gameObject); }

            var pos = cam.transform.position; var rot = cam.transform.rotation;
            var clear = cam.clearFlags; var bg = cam.backgroundColor; var prevFov = cam.fieldOfView; var prevTarget = cam.targetTexture;
            var dir = Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
            cam.transform.SetPositionAndRotation(target - dir * dist, Quaternion.LookRotation(dir, Vector3.up));
            cam.fieldOfView = fov;
            cam.clearFlags = CameraClearFlags.SolidColor;

            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../tools/.cache/objects"));
            Directory.CreateDirectory(outDir);
            // URP renders through a render request into a plain target (it resolves its own MSAA)
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            try
            {
                foreach (var (suffix, col) in new[] { ("b", Color.black), ("w", Color.white) })
                {
                    cam.backgroundColor = col;
                    var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
                    if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, req))
                        UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, req);
                    else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = prevTarget; }
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    tex.Apply();
                    File.WriteAllBytes(Path.Combine(outDir, name + "_" + suffix + ".png"), tex.EncodeToPNG());
                }
            }
            finally
            {
                RenderTexture.active = null;
                cam.targetTexture = prevTarget;
                cam.transform.SetPositionAndRotation(pos, rot);
                cam.clearFlags = clear; cam.backgroundColor = bg; cam.fieldOfView = prevFov;
                Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
                foreach (var g in hidden) g.SetActive(true);
            }
            return outDir + "/" + name;
        }
    }
}
