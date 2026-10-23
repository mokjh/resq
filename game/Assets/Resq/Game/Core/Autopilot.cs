using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Resq.Sim;
using UnityEngine;

namespace Resq.Game
{
    /// <summary>
    /// Development builds only (BUILD_PLAN P4.5): plays every mission's validated reference strategies through the
    /// normal command path in real time and logs each run's outcome and frame timing, so the P5 device runs and
    /// soaks need no human at the controls. It's off unless a flag file exists in the app's data folder:
    ///   adb shell touch /sdcard/Android/data/com.playresq.resq/files/autopilot.flag
    /// Optional flag contents: "missions=W1-M1,W2-M1 strategies=A,B layout=wide loops=1". Results go to
    /// autopilot.json beside the flag and to logcat as "[RESQ autopilot]" lines. Evidence kind: device when run on a
    /// headset; the Editor run is only a functional check.
    /// </summary>
    public sealed class Autopilot : MonoBehaviour
    {
        public GameBootstrap Boot;
        public static string FlagPath => Path.Combine(Application.persistentDataPath, "autopilot.flag");
        public static string ResultPath => Path.Combine(Application.persistentDataPath, "autopilot.json");
        public bool Finished { get; private set; }
        public readonly List<string> Lines = new List<string>();

        public static bool Requested => (Debug.isDebugBuild || Application.isEditor) && File.Exists(FlagPath);

        IEnumerator Start()
        {
            var opts = ParseFlag();
            yield return null;
            GameFlow.Disabled = true;   // no menus: the autopilot drives runs directly
            var missions = opts.missions ?? ContentLibrary.MissionIds();
            var sb = new StringBuilder("{\"runs\": [");
            bool first = true;
            for (int loop = 0; loop < opts.loops; loop++)
                foreach (var mission in missions)
                    foreach (var strategy in opts.strategies)
                    {
                        if (ContentLibrary.Strategy(mission, strategy) == null) continue;
                        var drv = Boot.Driver;
                        drv.LoadRun(mission, opts.layout, RuleProfile.Standard, false);
                        Boot.Interaction.MenuOnly = false;
                        yield return null;
                        drv.StartAutopilot(strategy);
                        drv.SetPaused(false);
                        int frames = 0; double sum = 0, worst = 0; int over14 = 0;
                        long gcStart = System.GC.CollectionCount(0);
                        while (!drv.Sim.IsOver)
                        {
                            yield return null;
                            double ms = Time.unscaledDeltaTime * 1000.0;
                            frames++; sum += ms; if (ms > worst) worst = ms; if (ms > 13.9) over14++;
                        }
                        var c = drv.Sim.S.C;
                        string line = $"{mission} {strategy} {opts.layout}: success={c.Success} stars={c.Stars} score={c.ScoreValue} " +
                                      $"frames={frames} avg={(frames > 0 ? sum / frames : 0):F2}ms worst={worst:F1}ms over13.9ms={over14} gc0={System.GC.CollectionCount(0) - gcStart} dropped={drv.DroppedSteps}";
                        Lines.Add(line);
                        Debug.Log("[RESQ autopilot] " + line);
                        sb.Append(first ? "" : ", ").Append("{\"mission\": \"").Append(mission).Append("\", \"strategy\": \"").Append(strategy)
                          .Append("\", \"success\": ").Append(c.Success ? "true" : "false").Append(", \"stars\": ").Append(c.Stars)
                          .Append(", \"frames\": ").Append(frames).Append(", \"avg_ms\": ").Append((frames > 0 ? sum / frames : 0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
                          .Append(", \"worst_ms\": ").Append(worst.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)).Append(", \"frames_over_13_9ms\": ").Append(over14).Append('}');
                        first = false;
                        File.WriteAllText(ResultPath, sb.ToString() + "]}");
                    }
            File.WriteAllText(ResultPath, sb.Append("]}").ToString());
            Debug.Log("[RESQ autopilot] done: " + Lines.Count + " runs → " + ResultPath);
            Finished = true;
        }

        (List<string> missions, string[] strategies, string layout, int loops) ParseFlag()
        {
            List<string> missions = null; string[] strategies = { "A", "B" }; string layout = "wide"; int loops = 1;
            string text = File.Exists(FlagPath) ? File.ReadAllText(FlagPath) : "";
            foreach (var part in text.Split(new[] { ' ', '\n', '\r', '\t' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=');
                if (kv.Length != 2) continue;
                switch (kv[0])
                {
                    case "missions": missions = new List<string>(kv[1].Split(',')); break;
                    case "strategies": strategies = kv[1].Split(','); break;
                    case "layout": layout = kv[1]; break;
                    case "loops": int.TryParse(kv[1], out loops); loops = Mathf.Max(1, loops); break;
                }
            }
            return (missions, strategies, layout, loops);
        }
    }
}
