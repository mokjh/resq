using System.Collections.Generic;
using Resq.Sim;
using UnityEngine;

namespace Resq.Game
{
    /// <summary>Loads mission content synced into Resources/ResqContent and compiles it with the shared MapCompiler.</summary>
    public static class ContentLibrary
    {
        static JsonNode _contracts, _rules;
        static readonly Dictionary<string, JsonNode> _contractById = new Dictionary<string, JsonNode>();

        static JsonNode Load(string path)
        {
            var asset = Resources.Load<TextAsset>("ResqContent/" + path);
            return asset == null ? null : JsonNode.Parse(asset.text);
        }

        static void EnsureLoaded()
        {
            if (_contracts != null) return;
            _contracts = Load("mission_contracts");
            _rules = Load("equipment_rules");
            foreach (var m in _contracts["missions"].Elements()) _contractById[m.Str("id")] = m;
        }

        public static bool HasMission(string missionId) =>
            Resources.Load<TextAsset>($"ResqContent/missions/{missionId}/map") != null;

        public static JsonNode Contract(string missionId)
        {
            EnsureLoaded();
            return _contractById.TryGetValue(missionId, out var c) ? c : null;
        }

        public static MissionDef Compile(string missionId, string layoutClass)
        {
            EnsureLoaded();
            var map = Load($"missions/{missionId}/map");
            var contract = _contractById[missionId];
            var overrides = Load($"missions/{missionId}/contract_overrides");
            if (overrides != null)
            {
                contract = JsonNode.Parse(contract.ToCanonical());
                foreach (var kv in overrides.Properties()) contract.Set(kv.Key, kv.Value);
            }
            return MapCompiler.Compile(map, contract, _rules, layoutClass);
        }

        public static StrategyScript Strategy(string missionId, string name)
        {
            var doc = Load($"missions/{missionId}/strategy_{name}");
            return doc == null ? null : StrategyScript.Parse(doc);
        }
    }
}
