using System;
using System.Collections.Generic;
using Resq.Sim.Rules;

namespace Resq.Sim
{
    /// <summary>
    /// Named challenges (spec 04 §6, content/modes_and_challenges.json): one modifier per definition, no stacking,
    /// Standard rules. A challenge is applied to copies of the mission's map and contract before compiling, so it
    /// yields its own immutable <see cref="MissionDef"/>; the base mission is never changed.
    ///   early_surge        — the named event's effect and warning move earlier by shift_scenario_ticks;
    ///   alternate_dock     — the group's pickup binds to the map node with the alternate_pickup role;
    ///   crosswind          — every authored wind vector is reversed (W↔E, N↔S, NE↔SW, SE↔NW);
    ///   high_tide_start    — event-driven tides start at the given segment's level with no active ramp;
    ///   careful_operation  — adds a required "lose no equipment" condition (base mastery still applies);
    ///   careful_light_kit  — careful_operation plus a smaller budget and default kit.
    /// </summary>
    public static class Challenges
    {
        public static readonly string[] Modifiers = { "early_surge", "alternate_dock", "crosswind", "high_tide_start", "careful_operation", "careful_light_kit" };

        public static void Apply(JsonNode challenge, ref JsonNode map, ref JsonNode contract)
        {
            if (challenge == null) return;
            map = JsonNode.Parse(map.ToCanonical());
            contract = JsonNode.Parse(contract.ToCanonical());
            var p = challenge["parameters"] ?? JsonNode.NewObject();
            string id = challenge.Str("id");
            switch (challenge.Str("modifier"))
            {
                case "early_surge": EarlySurge(contract, p, id); break;
                case "alternate_dock": AlternateDock(map, p, id); break;
                case "crosswind": Crosswind(map, contract, id); break;
                case "high_tide_start": HighTideStart(map, p, id); break;
                case "careful_operation": Careful(contract, p); break;
                case "careful_light_kit": Careful(contract, p); LightKit(contract, p, id); break;
                default: throw new ContentException($"challenge {id}: unknown modifier '{challenge.Str("modifier")}'");
            }
            contract.Set("challenge_id", id);
        }

        static void EarlySurge(JsonNode contract, JsonNode p, string id)
        {
            string ev = p.Str("event_id");
            long shift = p.Long("shift_scenario_ticks");
            foreach (var e in contract["events"].Elements())
            {
                if (e.Str("id") != ev) continue;
                long at = e.Long("at_scenario_tick") + shift, warn = e.Long("warning_at_scenario_tick") + shift;
                if (warn < 0) throw new ContentException($"challenge {id}: the shifted warning would precede the start");
                e.Set("at_scenario_tick", at);
                e.Set("warning_at_scenario_tick", warn);
                return;
            }
            throw new ContentException($"challenge {id}: event {ev} not in the contract");
        }

        static void AlternateDock(JsonNode map, JsonNode p, string id)
        {
            string group = p.Str("group_id"), role = p.Str("dock_role");
            var roleNode = map["roles"]?[role];
            string node = roleNode?.Str("node") ?? (roleNode?["nodes"]?.Count > 0 ? roleNode["nodes"][0].AsString() : null);
            if (node == null) throw new ContentException($"challenge {id}: the map has no '{role}' role");
            foreach (var g in map["groups"].Elements())
                if (g.Str("id") == group) { g.Set("pickup", node); return; }
            throw new ContentException($"challenge {id}: group {group} not in the map");
        }

        /// <summary>Wind codes (spec 02 §7): 0 calm, 1 W, 2 E, 3 N, 4 S, 5 NE, 6 SE, 7 SW, 8 NW.</summary>
        public static long ReverseWind(long w)
        {
            switch (w)
            {
                case 1: return 2; case 2: return 1; case 3: return 4; case 4: return 3;
                case 5: return 7; case 7: return 5; case 6: return 8; case 8: return 6;
                default: return w;
            }
        }

        /// <summary>Calm missions (no authored wind, e.g. W2-M3): the wind a crosswind brings from the first ignition on.
        /// Chosen by testing both directions against the reference plans (DECISIONS, 24 Oct).</summary>
        public static long CalmCrosswind = 2;

        static void Crosswind(JsonNode map, JsonNode contract, string id)
        {
            int changed = 0;
            foreach (var e in contract["events"].Elements())
                foreach (var op in e["operations"].Elements())
                    if (op.Str("kind") == "set_wind") { op.Set("value", ReverseWind(op.Long("value"))); changed++; }
            if (changed > 0) return;
            // a calm valley has nothing to reverse: the crosswind arrives with the first ignition instead
            string windRole = null;
            foreach (var kv in map["roles"].Properties())
            {
                var regions = kv.Value["regions"];
                if (regions == null) continue;
                foreach (var r in map["fire"]?["wind_regions"]?.Elements() ?? new List<JsonNode>())
                    foreach (var rr in regions.Elements())
                        if (rr.AsString() == r.AsString()) windRole = windRole ?? kv.Key;
            }
            if (windRole == null) throw new ContentException($"challenge {id}: no wind region for a crosswind");
            foreach (var e in contract["events"].Elements())
                foreach (var op in e["operations"].Elements())
                    if (op.Str("kind") == "ignite_region")
                    {
                        var wind = JsonNode.NewObject();
                        wind.Set("kind", "set_wind"); wind.Set("target_role", windRole); wind.Set("value", CalmCrosswind);
                        e["operations"].Add(wind);
                        return;
                    }
            throw new ContentException($"challenge {id}: no ignition to bring the crosswind");
        }

        static void HighTideStart(JsonNode map, JsonNode p, string id)
        {
            int seg = (int)p.Long("initial_tide_segment_index");
            if (seg < 0 || seg >= TideRules.SegmentTargets.Length) throw new ContentException($"challenge {id}: tide segment {seg} unknown");
            int changed = 0;
            foreach (var t in map["tides"]?.Elements() ?? new List<JsonNode>())
            {
                if (t["track"] != null) continue;   // fixed tracks follow their own timeline
                t.Set("initial_level", TideRules.SegmentTargets[seg]);
                changed++;
            }
            if (changed == 0) throw new ContentException($"challenge {id}: no event-driven tide to start high");
        }

        static void Careful(JsonNode contract, JsonNode p)
        {
            var obj = JsonNode.NewObject();
            obj.Set("id", "challenge_careful");
            obj.Set("tier", "required");
            obj.Set("kind", "equipment_losses_at_most");
            var prm = JsonNode.NewObject(); prm.Set("maximum", p.Long("maximum_equipment_losses"));
            obj.Set("params", prm);
            var tmp = JsonNode.NewObject(); tmp.Set("kind", "at_end");
            obj.Set("temporal", tmp);
            contract["objectives"].Add(obj);
        }

        static void LightKit(JsonNode contract, JsonNode p, string id)
        {
            contract.Set("supply_budget", contract.Long("supply_budget") + p.Long("supply_budget_delta"));
            var delta = p["default_loadout_delta"];
            if (delta == null) return;
            foreach (var kv in delta.Properties())
                foreach (var section in new[] { "default_loadout", "maximum_stock" })
                {
                    var kit = contract[section];
                    long v = kit.Long(kv.Key) + kv.Value.AsLong();
                    if (v < 0) throw new ContentException($"challenge {id}: {section}.{kv.Key} would go negative");
                    kit.Set(kv.Key, v);
                }
        }

        /// <summary>The definitions for one mission, in file order.</summary>
        public static List<JsonNode> ForMission(JsonNode modesAndChallenges, string missionId)
        {
            var list = new List<JsonNode>();
            foreach (var c in modesAndChallenges["challenges"].Elements())
                if (c.Str("mission_id") == missionId) list.Add(c);
            return list;
        }

        public static JsonNode Find(JsonNode modesAndChallenges, string challengeId)
        {
            foreach (var c in modesAndChallenges["challenges"].Elements())
                if (c.Str("id") == challengeId) return c;
            return null;
        }
    }
}
