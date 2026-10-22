using System;
using System.Collections.Generic;
using System.Linq;

namespace Resq.Content
{
    public static partial class ContentCompilation
    {
        private static JsonValue Change(JsonValue owner,string field,Func<JsonValue,bool> match,Func<JsonValue,JsonValue> transform)
            =>owner.With(field,JsonValue.Array(owner[field].Items.Select(item=>match(item)?transform(item):item)));
        private static JsonValue ExpandChallenge(ContentBundle bundle,ChallengeDef challenge)
        {
            var raw=bundle.ToJson();var source=bundle.missions.Single(item=>item.mission_version_id==challenge.base_mission_version_id);
            var mission=source.ToJson().With("default_stock",challenge.ToJson()["replacement_default_stock"]).With("maximum_stock",challenge.ToJson()["replacement_maximum_stock"]).With("supply_budget",JsonValue.Number(challenge.resulting_budget));
            var layouts=bundle.layouts.Where(item=>item.mission_version_id==source.mission_version_id).Select(item=>item.ToJson()).ToList();
            foreach(var operation in challenge.operations)
            {
                var op=operation.ToJson();string kind=op["kind"].Text;
                if(kind=="shift_event") mission=Change(mission,"events",item=>item["id"].Text==op["event_id"].Text,item=>item.With("at_scenario_tick",JsonValue.Number(checked(item["at_scenario_tick"].Integer+op["delta_scenario_ticks"].Integer))).With("warning_at_scenario_tick",JsonValue.Number(checked(item["warning_at_scenario_tick"].Integer+op["delta_scenario_ticks"].Integer))));
                else if(kind=="reverse_wind_vectors")
                {
                    int[] reverse={0,2,1,4,3,7,8,5,6};
                    mission=Change(mission,"events",item=>true,item=>Change(item,"operations",entry=>entry["kind"].Text=="set_wind",entry=>entry.With("value",JsonValue.Number(reverse[checked((int)entry["value"].Integer)]))));
                }
                else if(kind=="tide_start_index")
                {
                    long[] levels={0,600,1000,800};
                    layouts=layouts.Select(layout=>Change(layout,"tides",item=>item["id"].Text==op["controller_id"].Text,item=> { Require(item["kind"].Text=="event_ramps","CHALLENGE_TIDE_CONTROLLER",item["id"].Text); return item.With("initial_level_mm",JsonValue.Number(levels[checked((int)op["index"].Integer)])); })).ToList();
                }
                else if(kind=="maximum_equipment_losses")
                {
                    string id=challenge.id+":equipment_losses";
                    var objective=JsonValue.Object(new Dictionary<string,JsonValue>{{"id",JsonValue.String(id)},{"tier",JsonValue.String("required")},{"kind",JsonValue.String("equipment_losses_at_most")},{"params",JsonValue.Object(new Dictionary<string,JsonValue>{{"maximum",op["maximum"]}})},{"temporal",JsonValue.Object(new Dictionary<string,JsonValue>{{"kind",JsonValue.String("at_end")}})}});
                    mission=mission.With("objectives",JsonValue.Array(mission["objectives"].Items.Concat(new[]{objective})));
                }
                else if(kind=="replace_pickup_node")
                {
                    string group=op["group_id"].Text,target=op["node_id"].Text;
                    // Frozen recipe names the group's pickup node {group_id}_pickup.
                    // Bind the new pickup in route/role definitions; never move the group's
                    // residents, start/assembly node, or mutate physical graph geometry.
                    string old=group+"_pickup";
                    layouts=layouts.Select(layout=>
                    {
                        var nodes=layout["nav_nodes"].Items;
                        var before=nodes.SingleOrDefault(item=>item["id"].Text==old);var after=nodes.SingleOrDefault(item=>item["id"].Text==target);
                        Require(before!=null&&after!=null,"CHALLENGE_PICKUP_BINDING",group);
                        Require(after["kind"].Text=="dock"&&after["resident_capacity"].Integer>=before["resident_capacity"].Integer&&after["dock_berths"].Integer>=1,"CHALLENGE_PICKUP_CAPACITY",target);
                        bool bound=layout["routes"].Items.Any(route=>route["from_node_id"].Text==old||route["to_node_id"].Text==old||route["required_waypoint_ids"].Items.Any(id=>id.Text==old));
                        Require(bound,"CHALLENGE_PICKUP_BINDING",old);
                        var updated=Change(layout,"routes",route=>true,route=>route.With("from_node_id",JsonValue.String(route["from_node_id"].Text==old?target:route["from_node_id"].Text)).With("to_node_id",JsonValue.String(route["to_node_id"].Text==old?target:route["to_node_id"].Text)).With("required_waypoint_ids",JsonValue.Array(route["required_waypoint_ids"].Items.Select(id=>id.Text==old?JsonValue.String(target):id))));
                        return Change(updated,"roles",role=>role["target_kind"].Text=="nav_node",role=>role.With("target_ids",JsonValue.Array(role["target_ids"].Items.Select(id=>id.Text==old?JsonValue.String(target):id))));
                    }).ToList();
                }
                else throw new ContentException("UNKNOWN_KIND",kind);
            }
            // Each variant is isolated, fully normalized and validated with no recursive
            // challenge catalogue. Base bytes/graphs remain immutable and untouched.
            var byId=layouts.Select(layout=>layout.With("graph_hash",JsonValue.String(Normalize(layout).Without("graph_hash","geometry_art_id").Sha256()))).ToDictionary(item=>item["id"].Text,item=>item,StringComparer.Ordinal);
            raw=raw.With("layouts",JsonValue.Array(raw["layouts"].Items.Select(item=>byId.ContainsKey(item["id"].Text)?byId[item["id"].Text]:item)));
            raw=raw.With("missions",JsonValue.Array(raw["missions"].Items.Select(item=>(item["mission_version_id"].Text==source.mission_version_id?mission:item).With("challenge_ids",JsonValue.Array(new JsonValue[0])))));
            return raw.With("challenges",JsonValue.Array(new JsonValue[0]));
        }
        private static bool IsDisruptive(EventOperation operation)
        {
            switch(operation.kind)
            {
                case "set_source_rate": case "ignite_region": case "add_heat": case "set_wind": case "set_current_multiplier": case "set_tide_segment": case "set_stability_loss": case "set_support_load": case "tremor": case "release_avalanche": case "advance_lava": return true;
                case "set_edge_enabled": return operation.value==0;
                case "set_infrastructure_demand": return operation.value>0;
                case "enable_shelter_hazard": return operation.value==1;
                default: return false;
            }
        }
        private static bool NonOverlapping(Socket[] sockets,int needed)
        {
            int visits=0;
            Func<int,int,HashSet<string>,bool> search=null;
            search=(start,remaining,occupied)=>
            {
                if(remaining==0) return true;
                if(++visits>100000) throw new ContentException("CONTENT_SEARCH_BOUND","socket independent set");
                for(int index=start;index<=sockets.Length-remaining;index++)
                {
                    if(sockets[index].footprint_cell_ids.Any(occupied.Contains)) continue;
                    var next=new HashSet<string>(occupied,StringComparer.Ordinal); next.UnionWith(sockets[index].footprint_cell_ids);
                    if(search(index+1,remaining-1,next)) return true;
                }
                return false;
            };
            return search(0,needed,new HashSet<string>(StringComparer.Ordinal));
        }
        private static bool Contiguous(IEnumerable<string> ids,IEnumerable<FireLink> links)
        {
            var required=new HashSet<string>(ids,StringComparer.Ordinal); if(required.Count==0) return false;
            var visited=new HashSet<string>(StringComparer.Ordinal){required.First()};bool changed;
            do { changed=false; foreach(var link in links) if(required.Contains(link.from_cell_id)&&required.Contains(link.to_cell_id)) { if(visited.Contains(link.from_cell_id)&&visited.Add(link.to_cell_id)) changed=true; if(visited.Contains(link.to_cell_id)&&visited.Add(link.from_cell_id)) changed=true; } } while(changed);
            return visited.SetEquals(required);
        }
        private static bool VehiclePath(VehicleSpawn vehicle,NamedRoute route,LayoutDef layout)
        {
            string traversal=vehicle.kind=="skiff"?"water":vehicle.kind=="winch"?"cable":"ground";
            if(route.traversal_class!=traversal) return false;
            string start=route.from_node_id;
            foreach(string stop in route.required_waypoint_ids.Concat(new[]{route.to_node_id}))
            {
                var reached=new HashSet<string>(StringComparer.Ordinal){start};var queue=new Queue<string>();queue.Enqueue(start);
                while(queue.Count>0)
                {
                    string node=queue.Dequeue();
                    foreach(var edge in layout.nav_edges.Where(item=>item.from_node_id==node&&item.traversal_class==traversal&&item.movement_capacity>=vehicle.movement_slots&&item.administrative_enabled))
                    {
                        var destination=layout.nav_nodes.Single(item=>item.id==edge.to_node_id);
                        if((traversal=="water"?destination.dock_berths:destination.vehicle_slots)<1) continue;
                        if(reached.Add(edge.to_node_id)) queue.Enqueue(edge.to_node_id);
                    }
                }
                if(!reached.Contains(stop)) return false;start=stop;
            }
            return true;
        }
    }
}
