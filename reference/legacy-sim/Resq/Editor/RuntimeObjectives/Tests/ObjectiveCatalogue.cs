using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using Resq.Content;

namespace Resq.Tests.RuntimeObjectives
{
    // Explicit projection of the typed frozen catalogue. Descriptive briefs are traceability
    // text, have no executable representation, and do not enter runtime definitions.
    internal static class ObjectiveCatalogue
    {
        public static ReadOnlyCollection<CompiledMission> ReadMissionDefinitions(string source)
        {
            var catalogue=StrictJson.Parse(source);
            if(catalogue.Members.Count!=3||!catalogue.Has("schema_version")||!catalogue.Has("freeze_id")||!catalogue.Has("missions")||catalogue["schema_version"].Integer!=1||catalogue["freeze_id"].Text!="resq-spec-freeze-1.1.0") throw new ContentException("AUTHORING_SCHEMA","frozen catalogue version/root");
            var definitions=new List<CompiledMission>();
            foreach(var mission in catalogue["missions"].Items)
            {
                var fields=new Dictionary<string,JsonValue>(StringComparer.Ordinal);
                foreach(string key in new[]{"id","world_id","rules_version","duration_scenario_ticks","total_residents","minimum_rescues","supply_budget","objectives","protection_objective_ids","milestones","events","tutorial_policy"}) fields.Add(key,mission[key]);
                string id=mission["id"].Text; long revision=mission["mission_revision"].Integer;
                fields.Add("mission_version_id",JsonValue.String(id+"-v"+revision)); fields.Add("revision",JsonValue.Number(revision));
                fields.Add("permitted_equipment_ids",mission["permitted_tools"]); fields.Add("rule_profile_ids",mission["base_rule_profiles"]);
                fields.Add("layout_ids",JsonValue.Array(new[]{JsonValue.String(id+"-compact-v"+revision),JsonValue.String(id+"-wide-v"+revision)}));
                // Challenges require separately authored immutable definitions; linking is explicit.
                fields.Add("challenge_ids",JsonValue.Array(new JsonValue[0]));
                foreach(var stock in new[]{new[]{"default_stock","default_loadout"},new[]{"maximum_stock","maximum_stock"}})
                    fields.Add(stock[0],JsonValue.Array(mission[stock[1]].Members.OrderBy(pair=>pair.Key,StringComparer.Ordinal).Select(pair=>JsonValue.Object(new Dictionary<string,JsonValue>{{"equipment_id",JsonValue.String(pair.Key)},{"count",pair.Value}}))));
                var groups=new List<JsonValue>();
                foreach(var group in mission["groups"].Items)
                {
                    int count=checked((int)group["count"].Integer); string groupId=group["id"].Text;
                    if(group["mobility_counts"]["step_free"].Integer!=1||group["mobility_counts"]["standard"].Integer+1!=count) throw new ContentException("AUTHORING_POPULATION",groupId);
                    bool held=mission["events"].Items.Any(ev=>ev["operations"].Items.Any(op=>op["kind"].Text=="release_group"&&op["target_role"].Text==groupId));
                    groups.Add(JsonValue.Object(new Dictionary<string,JsonValue>{{"id",JsonValue.String(groupId)},{"resident_ids",JsonValue.Array(Enumerable.Range(1,count).Select(index=>JsonValue.String(id+":resident:"+groupId+":"+index.ToString("00",System.Globalization.CultureInfo.InvariantCulture))))},{"start_node_id",group["start_node_id"]},{"assembly_node_id",group["assembly_node_id"]},{"initially_held",JsonValue.Bool(held)}}));
                }
                fields.Add("groups",JsonValue.Array(groups)); definitions.Add(CompiledMission.Read(JsonValue.Object(fields)));
            }
            if(definitions.Count!=25||definitions.Select(item=>item.id).Distinct(StringComparer.Ordinal).Count()!=25) throw new ContentException("CAMPAIGN_SCOPE","25 unique mission definitions required");
            for(int world=1;world<=5;world++) for(int mission=1;mission<=5;mission++) if(!definitions.Any(item=>item.id=="W"+world+"-M"+mission&&item.world_id=="W"+world)) throw new ContentException("CAMPAIGN_SCOPE","five worlds, five missions each");
            return definitions.OrderBy(item=>item.id,StringComparer.Ordinal).ToList().AsReadOnly();
        }
    }
}
