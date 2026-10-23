using System.IO;
using UnityEditor;
using UnityEngine;

namespace Resq.EditorTools
{
    /// <summary>
    /// Copies the frozen specification inputs and authored mission content into Resources so the player build
    /// carries exactly the data the headless validator proved. Run after any map/strategy change.
    /// </summary>
    public static class ContentSync
    {
        const string Dest = "Assets/Resq/Content/Resources/ResqContent";

        [MenuItem("RESQ/Content/Sync Mission Content")]
        public static void Sync()
        {
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            Directory.CreateDirectory(Dest);
            Copy(Path.Combine(repo, "specification/content/mission_contracts.json"), Path.Combine(Dest, "mission_contracts.json"));
            Copy(Path.Combine(repo, "specification/content/equipment_rules.json"), Path.Combine(Dest, "equipment_rules.json"));
            Copy(Path.Combine(repo, "specification/content/modes_and_challenges.json"), Path.Combine(Dest, "modes_and_challenges.json"));
            Copy(Path.Combine(repo, "content/briefings.json"), Path.Combine(Dest, "briefings.json"));   // tools/content/briefings.py
            int maps = 0;
            string content = Path.Combine(repo, "content");
            if (Directory.Exists(content))
            {
                foreach (var map in Directory.GetFiles(content, "map.json", SearchOption.AllDirectories))
                {
                    string missionDir = Path.GetDirectoryName(map);
                    string id = MissionId(missionDir);
                    string outDir = Path.Combine(Dest, "missions", id);
                    Directory.CreateDirectory(outDir);
                    Copy(map, Path.Combine(outDir, "map.json"));
                    string overrides = Path.Combine(missionDir, "contract_overrides.json");
                    if (File.Exists(overrides)) Copy(overrides, Path.Combine(outDir, "contract_overrides.json"));
                    string strategies = Path.Combine(missionDir, "strategies");
                    if (Directory.Exists(strategies))
                        foreach (var s in Directory.GetFiles(strategies, "*.json"))
                            Copy(s, Path.Combine(outDir, "strategy_" + Path.GetFileName(s)));
                    maps++;
                }
            }
            AssetDatabase.Refresh();
            Debug.Log($"[RESQ] Synced content: {maps} missions");
        }

        static string MissionId(string missionDir)
        {
            // .../content/w1/m1 -> W1-M1
            string m = Path.GetFileName(missionDir).ToUpperInvariant();
            string w = Path.GetFileName(Path.GetDirectoryName(missionDir)).ToUpperInvariant();
            return $"{w}-{m}";
        }

        static void Copy(string from, string to)
        {
            if (!File.Exists(from)) { Debug.LogWarning("[RESQ] missing " + from); return; }
            File.Copy(from, to, true);
        }
    }
}
