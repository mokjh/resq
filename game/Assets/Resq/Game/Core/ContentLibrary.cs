using System.Collections.Generic;
using Resq.Sim;
using UnityEngine;

namespace Resq.Game
{
    /// <summary>Loads mission content synced into Resources/ResqContent and compiles it with the shared MapCompiler.</summary>
    public static class ContentLibrary
    {
        static JsonNode _contracts, _rules, _modes;
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
            _modes = Load("modes_and_challenges");
            foreach (var m in _contracts["missions"].Elements()) _contractById[m.Str("id")] = m;
        }

        /// <summary>Synced, playable missions in contract order (W1-M1 … W5-M5).</summary>
        public static List<string> MissionIds()
        {
            EnsureLoaded();
            var list = new List<string>();
            foreach (var m in _contracts["missions"].Elements())
                if (HasMission(m.Str("id"))) list.Add(m.Str("id"));
            return list;
        }

        public static bool HasMission(string missionId) =>
            Resources.Load<TextAsset>($"ResqContent/missions/{missionId}/map") != null;

        public static JsonNode Contract(string missionId)
        {
            EnsureLoaded();
            return _contractById.TryGetValue(missionId, out var c) ? c : null;
        }

        /// <summary>The named challenges for a mission (spec 04 §6), in file order.</summary>
        public static List<JsonNode> ChallengesFor(string missionId)
        {
            EnsureLoaded();
            return _modes == null ? new List<JsonNode>() : Challenges.ForMission(_modes, missionId);
        }

        public static JsonNode Challenge(string challengeId)
        {
            EnsureLoaded();
            return string.IsNullOrEmpty(challengeId) || _modes == null ? null : Challenges.Find(_modes, challengeId);
        }

        public static MissionDef Compile(string missionId, string layoutClass, string challengeId = null)
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
            var challenge = Challenge(challengeId);
            if (challengeId != null && challenge == null) throw new ContentException($"unknown challenge {challengeId}");
            Challenges.Apply(challenge, ref map, ref contract);
            return MapCompiler.Compile(map, contract, _rules, layoutClass);
        }

        /// <summary>A validated reference strategy; under a challenge, its own plan when it has one (as SimLab does).</summary>
        public static StrategyScript Strategy(string missionId, string name, string challengeId = null)
        {
            var doc = challengeId != null ? Load($"missions/{missionId}/strategy_{challengeId}_{name}") : null;
            doc = doc ?? Load($"missions/{missionId}/strategy_{name}");
            return doc == null ? null : StrategyScript.Parse(doc);
        }
    }
}
