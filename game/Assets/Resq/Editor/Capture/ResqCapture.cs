// Frame-stepped trailer footage from the running game (Editor only; nothing here ships). Written for RESQ after the Waz VR
// entry's capture: the Operator's composited stills come at 2 to 6 fps on this Mac, so instead every frame is rendered:
//   - Time.captureFramerate and SimDriver.FixedFrameStep: game time and the simulation advance exactly 1/fps per rendered
//     frame, however slowly the Mac runs, so the clip plays back at true game speed (no slow-motion factor to undo);
//   - a second, mono camera rides on the player's eye and renders 16:9 with a transparent clear into a RenderTexture,
//     read back and piped to ffmpeg, which lays it over a background still of the Simulator room taken from the same head
//     pose (the room is what passthrough would show; the board, hands and weather are the game's own render);
//   - every sound effect the game starts is logged by frame (AudioDirector.Played), so the soundtrack can be rebuilt.
// Active only with RESQ_CAPTURE=1. A driver asks for a clip by writing tools/.cache/cap/cmd.json:
//   { "name": "w1m1_flood", "frames": 600, "eye": true, "bg": "/abs/path/room.png" }
// With "eye": true the camera renders exactly the left eye's view and projection at the eye-buffer size, so it lines up
// pixel for pixel with a composited left-eye still of the room from the XR Operator; ffmpeg lays the game over that still,
// then crops a 16:9 band around the board and scales it to 1920x1080. Without it: 1920x1080 at "fov" degrees, "pitch" down.
// and waits for "[ResqCap] done <name>" in the Editor log. Output: video/raw/cap/<name>.mp4 and <name>.jsonl.
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using Resq.Game;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;

namespace Resq.EditorTools
{
    [InitializeOnLoad]
    public static class ResqCapture
    {
        [Serializable] class Cmd { public string name; public int frames; public float fov; public float pitch; public string bg; public bool eye; }

        static readonly bool On = Environment.GetEnvironmentVariable("RESQ_CAPTURE") == "1";
        const int Fps = 60, W = 1920, H = 1080;
        const float DefaultFov = 70f;
        static readonly string Ffmpeg = File.Exists("/opt/homebrew/bin/ffmpeg") ? "/opt/homebrew/bin/ffmpeg" : "ffmpeg";
        static readonly string Root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        static readonly string CmdFile = Path.Combine(Root, "tools", ".cache", "cap", "cmd.json");
        static readonly string OutDir = Path.Combine(Root, "video", "raw", "cap");
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static Camera _cam; static RenderTexture _rt, _flat; static int _w = W, _h = H; static bool _eye;
        static Cmd _clip; static int _asked, _written; static Process _ff; static Thread _writer;
        static BlockingCollection<byte[]> _queue; static readonly ConcurrentBag<byte[]> Pool = new ConcurrentBag<byte[]>();
        static StreamWriter _side; static SimDriver _driver;

        static ResqCapture()
        {
            if (!On) return;
            EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.EnteredPlayMode) Begin(); else if (s == PlayModeStateChange.ExitingPlayMode) End(); };
            if (EditorApplication.isPlaying) Begin();
        }

        static void Begin()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CmdFile)); Directory.CreateDirectory(OutDir);
            if (File.Exists(CmdFile)) File.Delete(CmdFile);
            Time.captureFramerate = Fps;
            SimDriver.FixedFrameStep = 1f / Fps;
            RenderPipelineManager.endCameraRendering -= OnCameraDone; RenderPipelineManager.endCameraRendering += OnCameraDone;
            RenderPipelineManager.beginCameraRendering -= OnCameraBegin; RenderPipelineManager.beginCameraRendering += OnCameraBegin;
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            AudioDirector.Played -= OnSound; AudioDirector.Played += OnSound;
            Debug.Log($"[ResqCap] ready: {W}x{H} at {Fps} fps, game time stepped per frame -> {OutDir}");
        }

        static void End()
        {
            if (_clip != null) Finish();
            EditorApplication.update -= Tick; RenderPipelineManager.endCameraRendering -= OnCameraDone; RenderPipelineManager.beginCameraRendering -= OnCameraBegin; AudioDirector.Played -= OnSound;
            Time.captureFramerate = 0; SimDriver.FixedFrameStep = 0f;
            if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
            if (_rt != null) _rt.Release(); if (_flat != null) _flat.Release(); _cam = null; _rt = null; _flat = null;
        }

        /// <summary>Rides rigidly on the player's eye. Untagged, so menus and the HUD keep placing themselves from Camera.main.</summary>
        static void EnsureCamera()
        {
            if (_cam != null || Camera.main == null) return;
            var main = Camera.main;
            var go = new GameObject("ResqCapture camera") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(main.transform, false);
            _cam = go.AddComponent<Camera>(); _cam.CopyFrom(main);
            Allocate(W, H);
            _cam.stereoTargetEye = StereoTargetEyeMask.None; _cam.fieldOfView = DefaultFov;
            _cam.clearFlags = CameraClearFlags.SolidColor; _cam.backgroundColor = new Color(0, 0, 0, 0);   // the room goes behind
            _cam.depth = main.depth - 1; _cam.enabled = true;
            var data = _cam.GetUniversalAdditionalCameraData(); data.allowXRRendering = false; data.renderPostProcessing = false;
        }

        static void Allocate(int w, int h)
        {
            if (_rt != null && _w == w && _h == h) { _cam.targetTexture = _rt; return; }
            if (_rt != null) { _cam.targetTexture = null; _rt.Release(); _flat.Release(); }
            _w = w; _h = h;
            _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "ResqCapture" };
            _flat = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "ResqCapture resolved" };
            _cam.targetTexture = _rt;
        }

        /// <summary>Eye mode: the left eye's exact view and projection, every frame (the head can move between frames).</summary>
        static void OnCameraBegin(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam != _cam || !_eye || Camera.main == null) return;
            var main = Camera.main;
            _cam.worldToCameraMatrix = main.GetStereoViewMatrix(Camera.StereoscopicEye.Left);
            _cam.projectionMatrix = main.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            EnsureCamera();
            if (_clip == null && File.Exists(CmdFile)) StartClip();
        }

        static void StartClip()
        {
            Cmd c;
            try { c = JsonUtility.FromJson<Cmd>(File.ReadAllText(CmdFile)); File.Delete(CmdFile); }
            catch (Exception e) { Debug.LogWarning("[ResqCap] bad command: " + e.Message); return; }
            if (c == null || string.IsNullOrEmpty(c.name) || c.frames <= 0 || _cam == null) return;
            _eye = c.eye && UnityEngine.XR.XRSettings.eyeTextureWidth > 0;
            if (_eye) Allocate(UnityEngine.XR.XRSettings.eyeTextureWidth, UnityEngine.XR.XRSettings.eyeTextureHeight);
            else
            {
                Allocate(W, H);
                _cam.ResetWorldToCameraMatrix(); _cam.ResetProjectionMatrix();
                _cam.fieldOfView = c.fov > 1 ? c.fov : DefaultFov;
                _cam.transform.localRotation = Quaternion.Euler(c.pitch, 0f, 0f);
            }
            _driver = UnityEngine.Object.FindFirstObjectByType<SimDriver>();
            string stem = Path.Combine(OutDir, c.name);
            bool bg = !string.IsNullOrEmpty(c.bg) && File.Exists(c.bg);
            // raw RGBA in (a RenderTexture reads back bottom-up, hence vflip). The game renders transparent surfaces with
            // premultiplied colour over a clear of (0,0,0,0), so the overlay treats alpha as premultiplied.
            string input = $"-f rawvideo -pix_fmt rgba -s {_w}x{_h} -r {Fps} -i -";
            // eye mode: a 16:9 band of the eye frame, a little below the middle where the board sits, at 1920x1080
            string frame = _eye ? $",crop=iw:iw*9/16:0:(ih-iw*9/16)*0.72,scale={W}:{H}:flags=lanczos" : "";
            string args = bg
                ? $"-y -loglevel error -loop 1 -r {Fps} -i \"{c.bg}\" {input} -filter_complex \"[0:v]scale={_w}:{_h},setsar=1,format=rgba[room];[1:v]vflip[game];[room][game]overlay=shortest=1:alpha=premultiplied{frame},format=yuv420p\" "
                : $"-y -loglevel error {input} -vf vflip{frame},format=yuv420p ";
            args += $"-r {Fps} -c:v libx264 -preset ultrafast -crf 12 -g {Fps / 2} -color_primaries bt709 -color_trc bt709 -colorspace bt709 -movflags +faststart \"{stem}.mp4\"";
            _ff = Process.Start(new ProcessStartInfo(Ffmpeg, args) { RedirectStandardInput = true, UseShellExecute = false, CreateNoWindow = true });
            _queue = new BlockingCollection<byte[]>(6);
            var pipe = _ff.StandardInput.BaseStream; var q = _queue;
            _writer = new Thread(() => { foreach (var buf in q.GetConsumingEnumerable()) { pipe.Write(buf, 0, buf.Length); Pool.Add(buf); } pipe.Flush(); pipe.Close(); }) { IsBackground = true };
            _writer.Start();
            _side = new StreamWriter(stem + ".jsonl");
            _side.WriteLine($"{{\"clip\":\"{c.name}\",\"fps\":{Fps},\"render\":\"{_w}x{_h}\",\"eye\":{(_eye ? "true" : "false")},\"background\":{(bg ? "true" : "false")},\"evidence\":\"simulator\"}}");
            _clip = c; _asked = 0; _written = 0;
            Debug.Log($"[ResqCap] start {c.name} frames={c.frames} fov={_cam.fieldOfView:F0} pitch={c.pitch:F0} background={bg}");
        }

        static void OnSound(string clip, float volume)
        {
            if (_side == null || _clip == null) return;
            _side.WriteLine("{\"frame\":" + _asked + ",\"sound\":\"" + clip + "\",\"gain\":" + volume.ToString("F3", Inv) + "}");
        }

        static void OnCameraDone(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam != _cam || _clip == null || _asked >= _clip.frames) return;
            Graphics.Blit(_rt, _flat);   // resolve the anti-aliased target
            int index = _asked++;
            long tick = _driver != null && _driver.Sim != null ? _driver.Sim.S.C.O : -1;
            _side.WriteLine($"{{\"frame\":{index},\"time\":{Time.time.ToString("F4", Inv)},\"tick\":{tick}}}");
            AsyncGPUReadback.Request(_flat, 0, TextureFormat.RGBA32, r =>
            {
                if (_queue == null || _queue.IsAddingCompleted) return;
                if (r.hasError) { Debug.LogWarning("[ResqCap] readback failed for a frame"); return; }
                var src = r.GetData<byte>();
                if (!Pool.TryTake(out var buf) || buf.Length != src.Length) buf = new byte[src.Length];
                src.CopyTo(buf); _queue.Add(buf); _written++;
            });
            if (_asked >= _clip.frames) EditorApplication.delayCall += Finish;   // after this frame's readback is queued
        }

        static void Finish()
        {
            if (_clip == null) return;
            var c = _clip; _clip = null;
            AsyncGPUReadback.WaitAllRequests();   // every asked frame reaches the queue, in order
            _queue.CompleteAdding(); _writer.Join(20000);
            if (!_ff.WaitForExit(60000)) { try { _ff.Kill(); } catch { } }
            int code = _ff.HasExited ? _ff.ExitCode : -1; _ff.Dispose(); _ff = null; _queue = null;
            _side.Dispose(); _side = null;
            if (_cam != null) { _cam.transform.localRotation = Quaternion.identity; _cam.ResetWorldToCameraMatrix(); _cam.ResetProjectionMatrix(); }
            _eye = false;
            Debug.Log($"[ResqCap] done {c.name} frames={_written} asked={c.frames} ffmpeg={code}");
        }
    }
}
