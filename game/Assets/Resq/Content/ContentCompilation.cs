using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Resq.Content
{
    public sealed class ResolvedEventOperation
    {
        public string EventId { get; }
        public long ScenarioTick { get; }
        public string Kind { get; }
        public string TargetKind { get; }
        public ReadOnlyCollection<string> TargetIds { get; }
        public long Value { get; }
        internal ResolvedEventOperation(EventDef definition, EventOperation operation, RoleBinding role)
        { EventId = definition.id; ScenarioTick = definition.at_scenario_tick; Kind = operation.kind; TargetKind = role.target_kind; TargetIds = role.target_ids; Value = operation.value; }
    }
    public sealed class CompiledContent
    {
        public ContentBundleEnvelope Envelope { get; }
        public ReadOnlyDictionary<string, ReadOnlyCollection<ResolvedEventOperation>> LayoutOperations { get; }
        public ReadOnlyDictionary<string, CompiledContent> ChallengeVariants { get; }
        internal CompiledContent(ContentBundleEnvelope envelope, Dictionary<string, ReadOnlyCollection<ResolvedEventOperation>> operations, Dictionary<string,CompiledContent> challenges)
        { Envelope = envelope; LayoutOperations = new ReadOnlyDictionary<string, ReadOnlyCollection<ResolvedEventOperation>>(operations); ChallengeVariants=new ReadOnlyDictionary<string,CompiledContent>(challenges); }
        public string CanonicalEnvelope => Envelope.ToJson().Canonical();
        public string ContentHash => Envelope.payload_sha256;
        public string LayoutSignature(string layoutId)
        {
            var layout = Envelope.payload.layouts.Single(item => item.id == layoutId);
            return JsonValue.Object(new Dictionary<string, JsonValue> { {"layout_class",JsonValue.String(layout.layout_class)}, {"graph_hash",JsonValue.String(layout.graph_hash)} }).Sha256();
        }
    }

    // The same pure compiler and strict loader are callable by Editor tests and gameplay.
    // Input is an explicit runtime definition, never a mission brief/script expression.
    public static partial class ContentCompilation
    {
        private static void Require(bool valid, string code, string detail) { if (!valid) throw new ContentException(code, detail); }
        private static Dictionary<string,T> Index<T>(IEnumerable<T> source, Func<T,string> id, string label)
        {
            var result = new Dictionary<string,T>(StringComparer.Ordinal);
            foreach (var item in source) { string key = id(item); Require(!result.ContainsKey(key), "DUPLICATE_ID", label + ":" + key); result.Add(key,item); } return result;
        }
        private static void Unique(IEnumerable<string> values, string label) { var seen=new HashSet<string>(StringComparer.Ordinal); foreach (string value in values) Require(seen.Add(value),"DUPLICATE_ID",label+":"+value); }
        private static void References<T>(IEnumerable<string> references, IDictionary<string,T> index, string label) { foreach (string value in references) Require(index.ContainsKey(value),"UNKNOWN_REFERENCE",label+":"+value); }
        private static void NullableReference<T>(string value, IDictionary<string,T> index, string label) { if(value!=null) References(new[]{value},index,label); }
        // Authoring callers hash the normalized logical definition, exactly as Compile
        // does. Computing this identity does not validate or qualify a candidate layout.
        public static string ComputeGraphHash(JsonValue runtimeLayout) => Normalize(runtimeLayout).Without("graph_hash","geometry_art_id").Sha256();
        public static CompiledContent Compile(string runtimePayloadJson) => Compile(StrictJson.Parse(runtimePayloadJson));
        public static CompiledContent Compile(JsonValue runtimePayload)
        {
            ContentSchemas.Validate("ContentBundle",runtimePayload);
            // Stable IDs sort entity definitions; semantic arrays (waypoints, coordinates,
            // event operations, challenge operations, tide knots) preserve author order.
            var normalized = Normalize(runtimePayload);
            var bundle = ContentBundle.Read(normalized);
            var operations = Validate(bundle);
            var envelope=ContentBundleEnvelope.Read(JsonValue.Object(new Dictionary<string, JsonValue> {
                {"schema_version",JsonValue.Number(1)},{"payload_sha256",JsonValue.String(normalized.Sha256())},{"payload",normalized} }));
            var variants=new Dictionary<string,CompiledContent>(StringComparer.Ordinal);
            foreach(var challenge in bundle.challenges) variants.Add(challenge.id,Compile(ExpandChallenge(bundle,challenge)));
            return new CompiledContent(envelope,operations,variants);
        }
        public static CompiledContent Load(byte[] envelopeBytes)
        {
            var raw=StrictJson.Parse(envelopeBytes); var envelope=ContentBundleEnvelope.Read(raw);
            Require(envelope.payload.ToJson().Sha256()==envelope.payload_sha256,"HASH_MISMATCH","content payload");
            var compiled=Compile(envelope.payload.ToJson());
            Require(compiled.ContentHash==envelope.payload_sha256,"NONCANONICAL_DEFINITIONS","definitions must be ordinal sorted");
            return compiled;
        }
        private static JsonValue Normalize(JsonValue value)
        {
            if(value.Kind=="object") return JsonValue.Object(value.Members.ToDictionary(pair=>pair.Key,pair=>Normalize(pair.Value),StringComparer.Ordinal));
            if(value.Kind!="array") return value;
            var items=value.Items.Select(Normalize).ToList();
            if(items.Count>0 && items.All(item=>item.Kind=="object"))
            {
                string key=new[]{"id","mission_version_id","resident_id","vehicle_id","role_id","equipment_id"}.FirstOrDefault(candidate=>items.All(item=>item.Has(candidate)));
                if(key!=null) items=items.OrderBy(item=>item[key].Text,StringComparer.Ordinal).ToList();
            }
            return JsonValue.Array(items);
        }
        private static Dictionary<string,ReadOnlyCollection<ResolvedEventOperation>> Validate(ContentBundle bundle)
        {
            var worlds=Index(bundle.worlds,item=>item.id,"world"); Unique(bundle.worlds.Select(item=>item.order.ToString()),"world order");
            var equipment=Index(bundle.equipment,item=>item.id,"equipment");
            foreach(var item in equipment.Values) { References(new[]{item.first_world_id},worlds,"equipment first world"); Unique(item.socket_kinds,"socket kinds"); Require(item.socket_kinds.Count>0,"SOCKET_KIND","equipment needs socket kind"); if(item.id=="brace") Require(((BraceParameters)item.parameters).maximum_per_support<=3,"BRACE_LIMIT",item.id); }
            var profiles=Index(bundle.rule_profiles,item=>item.id,"profile");
            foreach(var profile in profiles.Values) { Require(profile.scenario_numerator==(profile.id=="relaxed"?1:2),"RULE_PROFILE","scenario rate"); Stock(profile.extra_stock,equipment,null,null); Unique(profile.permitted_input_adapters,"input adapters"); }
            var missions=Index(bundle.missions,item=>item.id,"mission"); var versions=Index(bundle.missions,item=>item.mission_version_id,"mission version");
            var layouts=Index(bundle.layouts,item=>item.id,"layout"); var challenges=Index(bundle.challenges,item=>item.id,"challenge");
            var result=new Dictionary<string,ReadOnlyCollection<ResolvedEventOperation>>(StringComparer.Ordinal);
            foreach(var mission in missions.Values)
            {
                References(new[]{mission.world_id},worlds,"mission world"); References(mission.permitted_equipment_ids,equipment,"permitted equipment"); Unique(mission.permitted_equipment_ids,"permitted equipment");
                foreach(string id in mission.permitted_equipment_ids) Require(worlds[equipment[id].first_world_id].order<=worlds[mission.world_id].order,"FUTURE_EQUIPMENT",id);
                var maximum=Stock(mission.maximum_stock,equipment,mission.permitted_equipment_ids,null); Stock(mission.default_stock,equipment,mission.permitted_equipment_ids,maximum);
                Require(mission.minimum_rescues<=mission.total_residents,"RESCUE_BOUNDS",mission.id);
                long cheapest=mission.default_stock.Sum(item=>checked(item.count*equipment[item.equipment_id].supply_cost)); Require(cheapest<=mission.supply_budget,"BUDGET_STOCK","default stock exceeds budget");
                References(mission.rule_profile_ids,profiles,"mission profiles"); Unique(mission.rule_profile_ids,"mission profiles"); Require(mission.rule_profile_ids.Count>0,"RULE_PROFILE","none");
                References(mission.challenge_ids,challenges,"mission challenges"); Unique(mission.challenge_ids,"mission challenges");
                var groups=Index(mission.groups,item=>item.id,"group"); var residents=mission.groups.SelectMany(item=>item.resident_ids).ToArray(); Unique(residents,"resident");
                Require(residents.Length==mission.total_residents,"POPULATION",mission.id);
                foreach(var group in mission.groups) { Require(group.resident_ids.Count>0,"GROUP_EMPTY",group.id); Require(group.resident_ids.OrderBy(id=>id,StringComparer.Ordinal).SequenceEqual(Enumerable.Range(1,group.resident_ids.Count).Select(index=>mission.id+":resident:"+group.id+":"+index.ToString("00",System.Globalization.CultureInfo.InvariantCulture))),"RESIDENT_ID",group.id); }
                Index(mission.events,item=>item.id,"event"); var objectives=Index(mission.objectives,item=>item.id,"objective");
                References(mission.protection_objective_ids,objectives,"protection objectives"); Unique(mission.protection_objective_ids,"protection objectives");
                foreach(string id in mission.protection_objective_ids) Require(objectives[id].kind=="infrastructure_operational"||objectives[id].kind=="route_available"||(objectives[id].kind=="exposure_below"&&mission.layout_ids.All(layoutId=>layouts.ContainsKey(layoutId)&&(layouts[layoutId].infrastructure.Any(item=>item.hazard_region_ids.Contains(((ExposureBelowParams)objectives[id].@params).region_id))||layouts[layoutId].nav_edges.Any(item=>item.hazard_region_ids.Contains(((ExposureBelowParams)objectives[id].@params).region_id))))),"PROTECTION_KIND",id);
                Require(objectives.ContainsKey("rescue_minimum")&&objectives["rescue_minimum"].tier=="required"&&objectives["rescue_minimum"].kind=="rescue_count"&&((RescueCountParams)objectives["rescue_minimum"].@params).minimum==mission.minimum_rescues,"RESCUE_OBJECTIVE",mission.id);
                Require(objectives.Values.Any(item=>item.tier=="critical")&&objectives.Values.Any(item=>item.tier=="mastery"),"OBJECTIVE_TIERS",mission.id);
                Require(maximum.Values.Sum()<=512,"EQUIPMENT_BOUND",mission.id);
                var milestones=Index(mission.milestones,item=>item.id,"milestone"); Require(!milestones.ContainsKey("operation_started")&&!milestones.ContainsKey("operation_ended"),"RESERVED_MILESTONE",mission.id);
                foreach(var milestone in milestones.Values)
                {
                    if(milestone.kind=="group_boarded") References(new[]{milestone.target},groups,"milestone group");
                    else if(milestone.kind=="final_unload_started"||milestone.kind=="all_evacuated") Require(milestone.target=="all_residents","MILESTONE_TARGET",milestone.id);
                }
                foreach(var objective in mission.objectives) Temporal(objective.temporal,mission,milestones);
                References(mission.layout_ids,layouts,"mission layouts"); Unique(mission.layout_ids,"mission layouts"); Require(mission.layout_ids.Count==2,"LAYOUT_CLASSES","compact and wide required");
                Require(mission.layout_ids.Select(id=>layouts[id].layout_class).OrderBy(id=>id,StringComparer.Ordinal).SequenceEqual(new[]{"compact","wide"}),"LAYOUT_CLASSES",mission.id);
                foreach(string id in mission.layout_ids)
                {
                    var layout=layouts[id]; Require(layout.mission_version_id==mission.mission_version_id,"LAYOUT_MISSION",id);
                    result.Add(id,ValidateLayout(layout,mission,equipment));
                }
            }
            foreach(var layout in layouts.Values) { References(new[]{layout.mission_version_id},versions,"layout mission"); Require(versions[layout.mission_version_id].layout_ids.Contains(layout.id),"ORPHAN_LAYOUT",layout.id); }
            foreach(var challenge in challenges.Values)
            {
                References(new[]{challenge.base_mission_version_id},versions,"challenge mission"); var mission=versions[challenge.base_mission_version_id];
                Require(mission.challenge_ids.Contains(challenge.id),"ORPHAN_CHALLENGE",challenge.id);
                var maximum=Stock(challenge.replacement_maximum_stock,equipment,mission.permitted_equipment_ids,null); Stock(challenge.replacement_default_stock,equipment,mission.permitted_equipment_ids,maximum);
                Require(challenge.replacement_default_stock.Sum(item=>checked(item.count*equipment[item.equipment_id].supply_cost))<=challenge.resulting_budget,"BUDGET_STOCK",challenge.id);
                foreach(var operation in challenge.operations)
                {
                    var raw=operation.ToJson(); string kind=raw["kind"].Text;
                    if(kind=="shift_event") { var target=mission.events.SingleOrDefault(item=>item.id==raw["event_id"].Text); Require(target!=null,"UNKNOWN_REFERENCE","shift event"); long shift=raw["delta_scenario_ticks"].Integer; Require(target.at_scenario_tick+shift>=1 && target.at_scenario_tick+shift<mission.duration_scenario_ticks && target.warning_at_scenario_tick+shift>=0,"EVENT_BOUND","challenge shift"); }
                    if(kind=="replace_pickup_node") { References(new[]{raw["group_id"].Text},Index(mission.groups,item=>item.id,"group"),"challenge group"); foreach(string id in mission.layout_ids) Require(layouts[id].nav_nodes.Any(item=>item.id==raw["node_id"].Text),"UNKNOWN_REFERENCE","challenge node"); }
                    if(kind=="tide_start_index") foreach(string id in mission.layout_ids) Require(layouts[id].tides.Any(item=>item.id==raw["controller_id"].Text),"UNKNOWN_REFERENCE","challenge tide");
                    if(kind=="reverse_wind_vectors") Require(mission.events.Any(item=>item.operations.Any(op=>op.kind=="set_wind")),"CHALLENGE_NO_EFFECT","wind absent");
                }
            }
            return result;
        }
        private static Dictionary<string,long> Stock(IEnumerable<EquipmentCount> stock, IDictionary<string,EquipmentDef> equipment, IEnumerable<string> permitted, IDictionary<string,long> maximum)
        {
            var result=Index(stock,item=>item.equipment_id,"stock"); References(result.Keys,equipment,"stock");
            foreach(var item in result.Values) { if(permitted!=null) Require(permitted.Contains(item.equipment_id),"STOCK_NOT_PERMITTED",item.equipment_id); if(maximum!=null) Require(maximum.ContainsKey(item.equipment_id)&&item.count<=maximum[item.equipment_id],"STOCK_MAXIMUM",item.equipment_id); }
            return result.ToDictionary(pair=>pair.Key,pair=>pair.Value.count,StringComparer.Ordinal);
        }
        private static void Temporal(TemporalDef temporal, CompiledMission mission, IDictionary<string,MilestoneDef> milestones)
        {
            var raw=temporal.ToJson(); string kind=raw["kind"].Text;
            foreach(string key in new[]{"start_event","end_event"}) if(raw.Has(key)) Require(raw[key].Text=="operation_started"||raw[key].Text=="operation_ended"||milestones.ContainsKey(raw[key].Text),"TEMPORAL_REFERENCE",raw[key].Text);
            if(kind=="ever_by") Require(raw["deadline_tick"].Integer<=mission.duration_scenario_ticks,"TEMPORAL_BOUND",mission.id);
            if(kind=="hold_for") Require(raw["scenario_ticks"].Integer<=mission.duration_scenario_ticks,"TEMPORAL_BOUND",mission.id);
            if(raw.Has("start_event")) Require(raw["start_event"].Text!=raw["end_event"].Text && raw["start_event"].Text!="operation_ended" && raw["end_event"].Text!="operation_started","TEMPORAL_INTERVAL",mission.id);
        }
        private static ReadOnlyCollection<ResolvedEventOperation> ValidateLayout(LayoutDef layout, CompiledMission mission, IDictionary<string,EquipmentDef> equipment)
        {
            Require(layout.graph_hash==layout.ToJson().Without("graph_hash","geometry_art_id").Sha256(),"GRAPH_HASH",layout.id);
            var nodes=Index(layout.nav_nodes,item=>item.id,"nav node"); var edges=Index(layout.nav_edges,item=>item.id,"nav edge"); var sockets=Index(layout.sockets,item=>item.id,"socket");
            var routes=Index(layout.routes,item=>item.id,"route"); var water=Index(layout.water_nodes,item=>item.id,"water"); var hydraulic=Index(layout.hydraulic_links,item=>item.id,"hydraulic link");
            var sources=Index(layout.sources,item=>item.id,"source"); var sinks=Index(layout.sinks,item=>item.id,"sink"); var power=Index(layout.power_nodes,item=>item.id,"power node"); var powerLinks=Index(layout.power_links,item=>item.id,"power link");
            var infrastructure=Index(layout.infrastructure,item=>item.id,"infrastructure"); var fire=Index(layout.fire_cells,item=>item.id,"fire cell"); Index(layout.fire_links,item=>item.id,"fire link");
            var supports=Index(layout.supports,item=>item.id,"support"); var runouts=Index(layout.runouts,item=>item.id,"runout"); var tides=Index(layout.tides,item=>item.id,"tide"); var lava=Index(layout.lava_segments,item=>item.id,"lava");
            var stores=Index(layout.resources,item=>item.id,"store"); var producers=Index(layout.producers,item=>item.id,"producer"); var spawns=Index(layout.spawns,item=>item.resident_id,"spawn"); var vehicles=Index(layout.fixed_vehicles,item=>item.vehicle_id,"vehicle"); var roles=Index(layout.roles,item=>item.role_id,"role"); var groups=Index(mission.groups,item=>item.id,"group");
            var regions=new HashSet<string>(layout.water_nodes.Select(item=>item.region_id).Concat(layout.fire_cells.Select(item=>item.region_id)).Concat(layout.supports.Select(item=>item.region_id)).Concat(layout.lava_segments.Select(item=>item.region_id)).Concat(layout.runouts.SelectMany(item=>item.segments.SelectMany(segment=>segment.region_ids))),StringComparer.Ordinal);
            Action<IEnumerable<string>> checkRegions=ids=> { foreach(string id in ids) Require(regions.Contains(id),"EMPTY_REGION",id); };
            Require(nodes.Count>0,"NAV_EMPTY",layout.id);
            foreach(var node in nodes.Values) { Unique(node.mobility_classes,"node mobility"); checkRegions(node.hazard_region_ids); NullableReference(node.power_consumer_id,infrastructure,"node consumer"); Require(node.dock_min_depth_mm<=node.dock_max_depth_mm,"DEPTH_INTERVAL",node.id); if(node.kind=="evacuation") Require(!node.is_temporary_staging,"TEMPORARY_EVACUATION",node.id); }
            foreach(var edge in edges.Values)
            {
                References(new[]{edge.from_node_id,edge.to_node_id},nodes,"edge endpoint"); Unique(edge.mobility_classes,"edge mobility"); Unique(edge.tags,"edge tags"); checkRegions(edge.hazard_region_ids);
                NullableReference(edge.required_equipment_socket_id,sockets,"edge socket"); NullableReference(edge.required_power_consumer_id,infrastructure,"edge power");
                Require(edge.minimum_water_depth_mm<=edge.maximum_water_depth_mm,"DEPTH_INTERVAL",edge.id); Require(edge.step_free==edge.mobility_classes.Contains("step_free"),"MOBILITY",edge.id);
            }
            foreach(var route in routes.Values) { References(new[]{route.from_node_id,route.to_node_id}.Concat(route.required_waypoint_ids),nodes,"route node"); if(route.traversal_class=="water") Require(nodes[route.from_node_id].dock_berths>0&&nodes[route.to_node_id].dock_berths>0,"ROUTE_TRANSFER_BERTH",route.id); Require(Path(route.from_node_id,route.required_waypoint_ids.Concat(new[]{route.to_node_id}),route.traversal_class,route.mobility_class,edges.Values,sockets,equipment,mission,layout),"ROUTE_TOPOLOGY",route.id); }
            foreach(var group in groups.Values)
            {
                References(new[]{group.start_node_id,group.assembly_node_id},nodes,"group node"); Require(nodes[group.start_node_id].resident_capacity>=group.resident_ids.Count(id=>spawns.ContainsKey(id)&&spawns[id].fixed_vehicle_id==null),"START_CAPACITY",group.id);
                Require(nodes[group.assembly_node_id].resident_capacity>=group.resident_ids.Count,"ASSEMBLY_CAPACITY",group.id);
                foreach(string id in group.resident_ids) Require(spawns.ContainsKey(id)&&spawns[id].group_id==group.id&&spawns[id].start_node_id==group.start_node_id,"SPAWN_GROUP",id);
                Require(group.resident_ids.Count(id=>spawns[id].mobility_class=="step_free")==1,"STEP_FREE_RESIDENT",group.id);
                foreach(string mobility in group.resident_ids.Select(id=>spawns[id].mobility_class).Distinct())
                {
                    Require(Reachable(group.start_node_id,"all",mobility,edges.Values,sockets,equipment,mission,layout).Contains(group.assembly_node_id),"ASSEMBLY_REACHABILITY",group.id);
                    var reachable=Reachable(group.assembly_node_id,"all",mobility,edges.Values,sockets,equipment,mission,layout);
                    Require(nodes.Values.Where(node=>node.kind=="evacuation"&&!node.is_temporary_staging&&node.mobility_classes.Contains(mobility)&&reachable.Contains(node.id)).Sum(node=>node.resident_capacity)>=group.resident_ids.Count,"EVACUATION_REACHABILITY",group.id);
                    var refugeIds=nodes.Values.Where(node=>node.kind=="evacuation"&&reachable.Contains(node.id)).Select(node=>node.id).ToArray();
                    Require(HasAlternativeCorridor(group.assembly_node_id,mobility,refugeIds,edges.Values,sockets,equipment,mission,layout),"CORRIDOR_ALTERNATIVES",group.id);
                }
            }
            foreach(var stock in mission.maximum_stock)
            {
                int required=checked((int)Math.Max(2,stock.count+1));
                Require(sockets.Values.Count(socket=>socket.enabled&&socket.permitted_equipment_ids.Contains(stock.equipment_id))>=required,"SOCKET_ALTERNATIVES",stock.equipment_id);
                Require(NonOverlapping(sockets.Values.Where(socket=>socket.enabled&&socket.permitted_equipment_ids.Contains(stock.equipment_id)).ToArray(),checked((int)Math.Max(2,stock.count))),"SOCKET_OVERLAP",stock.equipment_id);
                for(int index=1;index<=required;index++) Require(sockets.ContainsKey(stock.equipment_id+":"+index.ToString("00",System.Globalization.CultureInfo.InvariantCulture))&&sockets[stock.equipment_id+":"+index.ToString("00",System.Globalization.CultureInfo.InvariantCulture)].permitted_equipment_ids.Contains(stock.equipment_id),"SOCKET_NAMING",stock.equipment_id);
            }
            Require(spawns.Count==mission.total_residents,"SPAWN_POPULATION",layout.id);
            Require(nodes.Values.Where(node=>node.kind=="evacuation"&&!node.is_temporary_staging).Sum(node=>node.resident_capacity)==mission.total_residents,"EVACUATION_CAPACITY",layout.id);
            var occupants=vehicles.Values.SelectMany(vehicle=>vehicle.initial_occupant_ids).ToArray(); Unique(occupants,"vehicle resident");
            foreach(var spawn in spawns.Values) { References(new[]{spawn.group_id},groups,"spawn group"); References(new[]{spawn.start_node_id},nodes,"spawn node"); NullableReference(spawn.fixed_vehicle_id,vehicles,"spawn vehicle"); Require((spawn.fixed_vehicle_id!=null)==occupants.Contains(spawn.resident_id),"VEHICLE_OCCUPANCY",spawn.resident_id); if(spawn.fixed_vehicle_id!=null) Require(vehicles[spawn.fixed_vehicle_id].initial_occupant_ids.Contains(spawn.resident_id),"VEHICLE_OCCUPANCY",spawn.resident_id); }
            foreach(var node in nodes.Values)
            {
                Require(spawns.Values.Count(spawn=>spawn.start_node_id==node.id&&spawn.fixed_vehicle_id==null)<=node.resident_capacity,"INITIAL_NODE_CAPACITY",node.id);
                Require(vehicles.Values.Count(vehicle=>vehicle.node_id==node.id)<=(node.kind=="dock"?node.dock_berths:node.vehicle_slots),"INITIAL_VEHICLE_CAPACITY",node.id);
            }
            foreach(var vehicle in vehicles.Values) { References(new[]{vehicle.node_id},nodes,"vehicle node"); References(vehicle.initial_occupant_ids,spawns,"occupant"); NullableReference(vehicle.initial_route_id,routes,"vehicle route"); Require(vehicle.initial_occupant_ids.Count<=vehicle.seats,"VEHICLE_SEATS",vehicle.vehicle_id); if(vehicle.kind=="fixed_bus") Require(vehicle.seats==4&&vehicle.movement_slots==2,"BUS_CLEARANCE",vehicle.vehicle_id);
                foreach(string id in vehicle.initial_occupant_ids) Require(spawns[id].fixed_vehicle_id==vehicle.vehicle_id&&spawns[id].start_node_id==vehicle.node_id,"VEHICLE_NODE_CONSISTENCY",id);
                if(vehicle.initial_route_id!=null) { var route=routes[vehicle.initial_route_id]; Require(route.from_node_id==vehicle.node_id,"VEHICLE_ROUTE_ORIGIN",vehicle.vehicle_id); Require(VehiclePath(vehicle,route,layout),"VEHICLE_ROUTE_CLEARANCE",vehicle.vehicle_id); } }
            foreach(var rate in sources.Values.Concat(sinks.Values)) References(new[]{rate.water_node_id},water,"rate water");
            foreach(var link in hydraulic.Values) { if(link.from_kind=="water_node") References(new[]{link.from_id},water,"hydraulic source"); else References(new[]{link.from_id},tides,"sea boundary"); if(link.to_kind=="water_node") References(new[]{link.to_id},water,"hydraulic destination"); else References(new[]{link.to_id},tides,"sea boundary"); NullableReference(link.barrier_socket_id,sockets,"barrier socket"); if(link.barrier_socket_id!=null) Require(sockets[link.barrier_socket_id].hydraulic_link_id==link.id&&sockets[link.barrier_socket_id].permitted_equipment_ids.Contains("barrier"),"BARRIER_BINDING",link.id); Require(link.from_id!=link.to_id,"HYDRAULIC_SELF_LINK",link.id); }
            foreach(var node in water.Values) Require(layout.hydraulic_links.Count(link=>link.from_id==node.id||link.to_id==node.id)+layout.sources.Count(rate=>rate.water_node_id==node.id)+layout.sinks.Count(rate=>rate.water_node_id==node.id)+layout.sockets.Count(socket=>socket.intake_node_id==node.id||socket.outlet_node_id==node.id)<=32,"HYDRAULIC_FAN",node.id);
            foreach(var node in power.Values) { checkRegions(new[]{node.platform_region_id}); Require(node.reconnect_depth_mm<=node.disconnect_depth_mm,"POWER_HYSTERESIS",node.id); }
            foreach(var link in powerLinks.Values) { References(new[]{link.from_node_id,link.to_node_id},power,"power link"); NullableReference(link.relay_socket_id,sockets,"relay socket"); if(link.relay_socket_id!=null) Require(sockets[link.relay_socket_id].permitted_equipment_ids.Contains("relay")&&(sockets[link.relay_socket_id].power_node_id==link.from_node_id||sockets[link.relay_socket_id].power_node_id==link.to_node_id),"RELAY_BINDING",link.id); }
            foreach(var item in infrastructure.Values) { NullableReference(item.power_node_id,power,"infrastructure power"); References(item.required_nav_node_ids,nodes,"infrastructure node"); References(item.required_support_ids,supports,"infrastructure support"); References(item.required_equipment_socket_ids,sockets,"infrastructure socket"); checkRegions(item.hazard_region_ids); }
            foreach(var link in layout.fire_links) References(new[]{link.from_cell_id,link.to_cell_id},fire,"fire link");
            foreach(var cell in fire.Values) Require(layout.fire_links.Count(link=>link.to_cell_id==cell.id)<=8,"FIRE_FAN",cell.id);
            foreach(var item in supports.Values) { References(item.node_ids,nodes,"support node"); References(item.edge_ids.Concat(item.collapse_edge_ids),edges,"support edge"); References(item.brace_socket_ids,sockets,"brace socket"); Require(item.brace_socket_ids.Count<=3,"BRACE_LIMIT",item.id); foreach(string id in item.brace_socket_ids) Require(item.kind=="structure"&&sockets[id].support_id==item.id&&sockets[id].permitted_equipment_ids.Contains("brace"),"BRACE_BINDING",id); References(item.station_ids,infrastructure,"support station"); }
            foreach(var item in runouts.Values) { NullableReference(item.source_support_id,supports,"runout support"); var segments=Index(item.segments,segment=>segment.id,"runout segment"); foreach(var segment in segments.Values) { References(segment.successor_ids,segments,"runout successor"); References(segment.edge_ids,edges,"runout edge"); checkRegions(segment.region_ids); } Acyclic(segments.Keys,id=>segments[id].successor_ids,"RUNOUT_CYCLE"); }
            foreach(var item in tides.Values) { if(item.kind=="event_ramps") Require(item.knots.Count==0&&item.repeat_period==null,"TIDE_TRACK",item.id); else { Require(item.knots.Count>=2&&item.knots[0].scenario_tick==0,"TIDE_TRACK",item.id); long previous=-1; foreach(var knot in item.knots) { Require(knot.scenario_tick>previous,"TIDE_ORDER",item.id); previous=knot.scenario_tick; } Require(item.initial_level_mm==item.knots[0].level_mm,"TIDE_INITIAL",item.id); if(item.repeat_period!=null) Require(item.repeat_period==previous&&item.knots[item.knots.Count-1].level_mm==item.knots[0].level_mm,"TIDE_PERIOD",item.id); } }
            foreach(var item in lava.Values) { References(item.successor_ids,lava,"lava successor"); References(item.crossing_edge_ids,edges,"lava edge"); References(item.cooler_socket_ids,sockets,"cooler socket"); foreach(string id in item.cooler_socket_ids) Require(sockets[id].lava_segment_id==item.id&&sockets[id].permitted_equipment_ids.Contains("cooler"),"COOLER_BINDING",id); }
            Acyclic(lava.Keys,id=>lava[id].successor_ids,"LAVA_CYCLE");
            var tankAliases=new HashSet<string>(StringComparer.Ordinal);
            foreach(var store in stores.Values) { Require(store.initial_amount<=store.capacity,"STORE_CAPACITY",store.id); if(store.unit=="water_mm3") { Require(store.water_node_id!=null,"WATER_ALIAS",store.id); References(new[]{store.water_node_id},water,"store water"); Require(water[store.water_node_id].kind=="tank"&&water[store.water_node_id].initial_volume_mm3==store.initial_amount&&tankAliases.Add(store.water_node_id),"WATER_ALIAS",store.id); } else Require(store.water_node_id==null,"WATER_ALIAS",store.id); }
            foreach(var item in producers.Values) { References(new[]{item.infrastructure_id},infrastructure,"producer infrastructure"); References(new[]{item.input_store_id,item.output_store_id},stores,"producer store"); Require(stores[item.input_store_id].unit=="coolant_feedstock"&&stores[item.output_store_id].unit=="coolant_charge","PRODUCER_UNITS",item.id); }
            foreach(var socket in sockets.Values)
            {
                Require(socket.footprint_cell_ids.Count>0,"SOCKET_FOOTPRINT",socket.id); Unique(socket.footprint_cell_ids,"socket footprint"); foreach(string cell in socket.footprint_cell_ids) Require(water.ContainsKey(cell)||fire.ContainsKey(cell),"SOCKET_FOOTPRINT",cell); NullableReference(socket.node_id,nodes,"socket node"); References(socket.permitted_equipment_ids,equipment,"socket equipment"); Unique(socket.permitted_equipment_ids,"socket equipment"); Unique(socket.orientation_steps.Select(value=>value.ToString()),"orientations");
                NullableReference(socket.hydraulic_link_id,hydraulic,"socket hydraulic"); NullableReference(socket.intake_node_id,water,"socket intake"); NullableReference(socket.outlet_node_id,water,"socket outlet"); NullableReference(socket.power_node_id,power,"socket power"); NullableReference(socket.support_id,supports,"socket support"); NullableReference(socket.lava_segment_id,lava,"socket lava"); References(socket.fire_cell_ids,fire,"socket fire"); NullableReference(socket.resource_store_id,stores,"socket store");
                foreach(string id in socket.permitted_equipment_ids) Require(mission.permitted_equipment_ids.Contains(id)&&equipment[id].socket_kinds.Contains(socket.kind),"SOCKET_COMPATIBILITY",socket.id+":"+id);
                if(socket.permitted_equipment_ids.Contains("pump")) Require(socket.intake_node_id!=null&&socket.outlet_node_id!=null&&socket.intake_node_id!=socket.outlet_node_id&&socket.power_node_id!=null,"PUMP_SOCKET",socket.id);
                if(socket.permitted_equipment_ids.Contains("sprinkler")) Require(socket.resource_store_id!=null&&stores[socket.resource_store_id].unit=="water_mm3"&&socket.fire_cell_ids.Count>0&&socket.fire_cell_ids.Count<=4,"SPRINKLER_SOCKET",socket.id);
                if(socket.permitted_equipment_ids.Contains("brace")) Require(socket.support_id!=null&&supports[socket.support_id].kind=="structure"&&supports[socket.support_id].brace_socket_ids.Contains(socket.id),"BRACE_BINDING",socket.id);
                if(socket.permitted_equipment_ids.Contains("barrier")) Require(socket.hydraulic_link_id!=null&&hydraulic[socket.hydraulic_link_id].barrier_socket_id==socket.id,"BARRIER_BINDING",socket.id);
                if(socket.permitted_equipment_ids.Contains("relay")) Require(socket.power_node_id!=null&&powerLinks.Values.Count(link=>link.relay_socket_id==socket.id)==1,"RELAY_BINDING",socket.id);
                if(socket.permitted_equipment_ids.Contains("cooler")) Require(socket.lava_segment_id!=null&&lava[socket.lava_segment_id].cooler_socket_ids.Contains(socket.id),"COOLER_BINDING",socket.id);
                if(socket.permitted_equipment_ids.Contains("firebreak")) Require(socket.fire_cell_ids.Count>0&&socket.fire_cell_ids.Count<=8&&socket.fire_cell_ids.All(id=>!fire[id].building_cell&&(fire[id].initial_heat<600||fire[id].initial_fuel==0))&&Contiguous(socket.fire_cell_ids,layout.fire_links),"FIREBREAK_SOCKET",socket.id);
            }
            foreach(var role in roles.Values)
            {
                Unique(role.target_ids,"role targets");
                switch(role.target_kind) { case "water_source": References(role.target_ids,sources,"role source"); break; case "nav_edge_set": References(role.target_ids,edges,"role edge"); break; case "nav_node": References(role.target_ids,nodes,"role node"); break; case "group": References(role.target_ids,groups,"role group"); break; case "support": References(role.target_ids,supports,"role support"); break; case "runout": References(role.target_ids,runouts,"role runout"); break; case "tide": References(role.target_ids,tides,"role tide"); break; case "infrastructure": References(role.target_ids,infrastructure,"role infrastructure"); break; case "lava_source": References(role.target_ids,lava,"role lava"); break; case "socket": References(role.target_ids,sockets,"role socket"); break; case "water_store": References(role.target_ids,stores,"role store"); Require(role.target_ids.All(id=>stores[id].unit=="water_mm3"),"ROLE_TYPE",role.role_id); break; case "fire_region": case "wind_region": Require(role.target_ids.All(id=>fire.Values.Any(cell=>cell.region_id==id)),"ROLE_TYPE",role.role_id); break; case "shelter_region": checkRegions(role.target_ids); break; default: throw new ContentException("ROLE_TYPE",role.target_kind); }
            }
            foreach(var objective in mission.objectives) Condition(objective.kind,objective.@params.ToJson(),mission,layout,0);
            foreach(var milestone in mission.milestones) if(milestone.kind=="first_cooled_crossing_closed") Require(roles.ContainsKey(milestone.target)&&roles[milestone.target].target_kind=="lava_source","MILESTONE_ROLE",milestone.id);
            var resolved=new List<ResolvedEventOperation>(); var writes=new Dictionary<string,long>(StringComparer.Ordinal);
            foreach(var definition in mission.events)
            {
                Require(definition.at_scenario_tick<mission.duration_scenario_ticks&&definition.warning_at_scenario_tick<=definition.at_scenario_tick,"EVENT_BOUND",definition.id);
                foreach(var operation in definition.operations)
                {
                    Require(roles.ContainsKey(operation.target_role),"UNBOUND_ROLE",operation.target_role); var role=roles[operation.target_role];
                    Require(TargetKinds(operation.kind).Split('|').Contains(role.target_kind),"ROLE_TYPE",operation.kind+":"+role.target_kind);
                    
                    if(IsDisruptive(operation)) Require(definition.at_scenario_tick-definition.warning_at_scenario_tick>=100,"EVENT_WARNING",definition.id);
                    foreach(string id in role.target_ids)
                    {
                        string key=definition.at_scenario_tick+":"+operation.kind+":"+id;
                        Require(!writes.ContainsKey(key),"CONFLICTING_EVENT_WRITES",key); writes[key]=operation.value;
                    }
                    if(operation.kind=="warn_wind_change"||operation.kind=="warn_runout")
                    {
                        string effect=operation.kind=="warn_wind_change"?"set_wind":"release_avalanche";
                        Require(mission.events.Any(item=>item.at_scenario_tick==definition.at_scenario_tick+operation.value&&item.operations.Any(op=>op.kind==effect&&roles.ContainsKey(op.target_role)&&roles[op.target_role].target_ids.SequenceEqual(role.target_ids))),"UNBOUND_WARNING_EFFECT",definition.id);
                    }
                    resolved.Add(new ResolvedEventOperation(definition,operation,role));
                }
            }
            return resolved.AsReadOnly();
        }
        private static string TargetKinds(string kind)
        {
            switch(kind) { case "set_source_rate": return "water_source"; case "warn_route": case "set_edge_enabled": case "set_current_multiplier": return "nav_edge_set"; case "release_group": return "group"; case "ignite_region": case "set_region_cooling": case "add_heat": return "fire_region"; case "set_wind": case "warn_wind_change": return "wind_region"; case "warn_resource": return "water_store"; case "set_stability_loss": case "set_support_load": case "tremor": return "support"; case "warn_runout": case "release_avalanche": return "runout"; case "warn_support": return "support|infrastructure"; case "set_tide_segment": return "tide"; case "set_infrastructure_demand": return "infrastructure"; case "advance_lava": case "warn_reheat": return "lava_source"; case "enable_cooler_socket": return "socket"; case "enable_shelter_hazard": return "shelter_region"; default: throw new ContentException("UNKNOWN_KIND",kind); }
        }
        private static void Condition(string kind,JsonValue parameters,CompiledMission mission,LayoutDef layout,int depth)
        {
            Require(depth<4,"CONDITION_DEPTH",kind);
            Func<string,bool> region=id=>layout.water_nodes.Any(item=>item.region_id==id)||layout.fire_cells.Any(item=>item.region_id==id)||layout.supports.Any(item=>item.region_id==id)||layout.lava_segments.Any(item=>item.region_id==id)||layout.runouts.Any(item=>item.segments.Any(segment=>segment.region_ids.Contains(id)));
            Action<bool,string> check=(valid,id)=>Require(valid,"CONDITION_REFERENCE",kind+":"+id);
            if(kind=="all"||kind=="any") { Require(LeafCount(parameters)<=16,"CONDITION_LEAVES",kind); foreach(var child in parameters["conditions"].Items) Condition(child["kind"].Text,child["params"],mission,layout,depth+1); return; }
            if(parameters.Has("group_id")&&!parameters["group_id"].IsNull) { string id=parameters["group_id"].Text; check(mission.groups.Any(item=>item.id==id),id); if(kind=="group_evacuated") Require(parameters["minimum"].Integer<=mission.groups.Single(item=>item.id==id).resident_ids.Count,"OBJECTIVE_POPULATION",id); }
            if(kind=="rescue_count") Require(parameters["minimum"].Integer<=mission.total_residents,"OBJECTIVE_POPULATION",mission.id);
            if(parameters.Has("infrastructure_id")) { string id=parameters["infrastructure_id"].Text; check(layout.infrastructure.Any(item=>item.id==id),id); }
            if(parameters.Has("route_id")) { string id=parameters["route_id"].Text; check(layout.routes.Any(item=>item.id==id&&item.traversal_class==parameters["traversal_class"].Text),id); }
            if(parameters.Has("region_id")) { string id=parameters["region_id"].Text; check(region(id),id); if(kind=="exposure_below") { string hazard=parameters["hazard"].Text; check(hazard=="water"?layout.water_nodes.Any(item=>item.region_id==id):hazard=="fire"?layout.fire_cells.Any(item=>item.region_id==id):hazard=="lava"?layout.lava_segments.Any(item=>item.region_id==id):layout.runouts.Any(item=>item.segments.Any(segment=>segment.region_ids.Contains(id))),id); } }
            if(parameters.Has("store_id")) { string id=parameters["store_id"].Text; check(layout.resources.Any(item=>item.id==id),id); Require(parameters["minimum"].Integer<=layout.resources.Single(item=>item.id==id).capacity,"OBJECTIVE_RESOURCE",id); }
            if(parameters.Has("node_id")) { string id=parameters["node_id"].Text; check(layout.nav_nodes.Any(item=>item.id==id),id); }
            if(parameters.Has("equipment_id")) { string id=parameters["equipment_id"].Text; check(mission.permitted_equipment_ids.Contains(id),id); }
            if(parameters.Has("edge_tag")) { string id=parameters["edge_tag"].Text; check(layout.nav_edges.Any(item=>item.tags.Contains(id)),id); }
        }
        private static int LeafCount(JsonValue parameters) => parameters.Has("conditions")?parameters["conditions"].Items.Sum(item=>LeafCount(item["params"])):1;
        private static HashSet<string> Reachable(string start,string traversal,string mobility,IEnumerable<NavEdge> edges,IDictionary<string,Socket> sockets,IDictionary<string,EquipmentDef> equipment,CompiledMission mission,LayoutDef layout)
            => Traverse(start,traversal,mobility,edges,sockets,equipment,mission,layout,null).Reachable;
        private static bool HasAlternativeCorridor(string start,string mobility,string[] refuges,IEnumerable<NavEdge> edges,IDictionary<string,Socket> sockets,IDictionary<string,EquipmentDef> equipment,CompiledMission mission,LayoutDef layout)
        {
            var search=Traverse(start,"all",mobility,edges,sockets,equipment,mission,layout,new HashSet<string>(refuges,StringComparer.Ordinal));
            if(search.Path==null||search.Path.Length==0) return false;
            foreach(var edge in search.Path) if(Reachable(start,"all",mobility,edges.Where(item=>item.id!=edge.id),sockets,equipment,mission,layout).Any(refuges.Contains)) return true;
            return false;
        }
        private static bool Path(string start,IEnumerable<string> stops,string traversal,string mobility,IEnumerable<NavEdge> edges,IDictionary<string,Socket> sockets,IDictionary<string,EquipmentDef> equipment,CompiledMission mission,LayoutDef layout)
        { foreach(string stop in stops) { if(!Traverse(start,traversal,mobility,edges,sockets,equipment,mission,layout,null,true).Reachable.Contains(stop)) return false; start=stop; } return true; }
        private static void Acyclic(IEnumerable<string> ids,Func<string,IEnumerable<string>> successors,string code)
        {
            var state=new Dictionary<string,int>(StringComparer.Ordinal); Action<string> visit=null; visit=id=> { if(state.TryGetValue(id,out int existing)) { Require(existing!=1,code,id); return; } state[id]=1; foreach(string next in successors(id)) visit(next); state[id]=2; }; foreach(string id in ids) visit(id);
        }
    }
}
