#!/usr/bin/env python3
"""Generate closed content schemas and typed immutable readers from frozen tables.
This is schema/code generation only. It never authors a level or simulates rescue.
"""
from pathlib import Path
import json
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'game/Assets/Resq/Content'
S={}; TYPES={}
def string(pattern=None): return {'type':'string',**({'pattern':pattern} if pattern else {})}
def integer(lo=0,hi=2147483647): return {'type':'integer','minimum':lo,'maximum':hi}
ID=string('^[A-Za-z0-9_.:@-]{1,128}$'); HASH=string('^[0-9a-f]{64}$'); N=integer(); Q=integer(0,10**12); T=integer(0,1000000); POS=integer(1); B={'type':'boolean'}
def en(s):return {'enum':s.split('|'),'type':'string'}
def const(v):return {'const':v,'type':{str:'string',int:'integer',bool:'boolean'}[type(v)]}
def ref(n):return {'$ref':'#/$defs/'+n}
def arr(x,hi=16384,lo=0):return {'type':'array','items':x,'minItems':lo,'maxItems':hi}
def nullable(x):return {'oneOf':[x,{'type':'null'}]}
def obj(p):return {'type':'object','properties':p,'required':list(p),'additionalProperties':False}
def define(n,p):S[n]=obj(p);TYPES[n]=p;return ref(n)
def union(n,variants):S[n]={'oneOf':variants};return ref(n)
EC=define('EquipmentCount',{'equipment_id':ID,'count':N})
def ids(hi=16384):return arr(ID,hi)
def records(n,hi=16384,lo=0):return arr(ref(n),hi,lo)
def common(n,fields):return define(n,fields)
common('WorldDef',dict(id=ID,order=integer(1,5),release_scope=en('competition|full_release'),title_key=ID,art_bundle_id=ID,audio_bundle_id=ID))
PARAMETERS={
'barrier':dict(crest_mm=N,warning_head_mm=N,damage_head_mm=N,damage_per_pulse=N),
'bridge':dict(close_depth_mm=N,damage_depth_mm=N), 'pump':dict(rate_mm3_per_second=integer(0,1000000000),max_lift_mm=N),
'skiff':dict(boarding_operation_ticks=N,unboarding_operation_ticks=N),'crawler':dict(boarding_operation_ticks=N,unboarding_operation_ticks=N),
'beacon':{},'generator':dict(shutdown_depth_mm=N,restart_depth_mm=N,damage_depth_mm=N),
'firebreak':dict(maximum_cells=N,uses=N),'sprinkler':dict(rate_mm3_per_second=integer(0,1000000000),maximum_cells=N),
'brace':dict(stability_bonus=N,maximum_per_support=N),'winch':dict(crossing_operation_ticks=N,duty_operation_ticks=N,rest_operation_ticks=N,brake_operation_ticks=N),
'relay':dict(disconnect_depth_mm=N,reconnect_depth_mm=N),'cooler':dict(initial_charges=N,capacity_charges=N,cooling_scenario_ticks=N,crust_scenario_ticks=N,reheat_scenario_ticks=N),
'shelter':dict(initial_protection_scenario_ticks=N)}
for n,p in PARAMETERS.items():define(n.title()+'Parameters',p)
# Equipment parameters are discriminated by owning equipment ID, never by a CLR name.
base=dict(id=ID,revision=POS,supply_cost=POS,first_world_id=ID,socket_kinds=ids(),power_supply=N,power_demand=N,resident_seats=N,movement_slots=N,relocation_policy=en('none|idle_same_zone'),durability=N,parameters={},prefab_id=ID)
S['EquipmentDef']={'oneOf':[obj(dict(base,id=const(n),parameters=ref(n.title()+'Parameters'))) for n in PARAMETERS]}
TYPES['EquipmentDef']=base
common('RuleProfileDef',dict(id=en('standard|relaxed|command'),scenario_numerator=integer(1,2),scenario_denominator=const(2),planning_allowed=const(True),extra_stock=arr(EC),permitted_input_adapters=ids()))
CH={
'ShiftEvent':dict(kind=const('shift_event'),event_id=ID,delta_scenario_ticks=integer(-1000000,1000000)),
'ReplacePickupNode':dict(kind=const('replace_pickup_node'),group_id=ID,node_id=ID),
'ReverseWindVectors':dict(kind=const('reverse_wind_vectors'),enabled=const(True)),
'TideStartIndex':dict(kind=const('tide_start_index'),controller_id=ID,index=integer(0,3)),
'MaximumEquipmentLosses':dict(kind=const('maximum_equipment_losses'),maximum=N)}
for n,p in CH.items():define(n+'Operation',p)
union('ChallengeOperation',[ref(n+'Operation') for n in CH])
common('ChallengeDef',dict(id=ID,revision=POS,base_mission_version_id=ID,operations=records('ChallengeOperation',2048),replacement_default_stock=arr(EC),replacement_maximum_stock=arr(EC),resulting_budget=POS))
common('GroupDef',dict(id=ID,resident_ids=ids(256),start_node_id=ID,assembly_node_id=ID,initially_held=B))
TEMP={ 'AtEnd':dict(kind=const('at_end')),'EverBy':dict(kind=const('ever_by'),deadline_tick=T),
'Always':dict(kind=const('always'),start_event=ID,end_event=ID),'EverBefore':dict(kind=const('ever_before'),end_event=ID),
'HoldFor':dict(kind=const('hold_for'),scenario_ticks=integer(1,1000000),start_event=ID,end_event=ID)}
for n,p in TEMP.items():define(n+'Temporal',p)
union('TemporalDef',[ref(n+'Temporal') for n in TEMP])
CONDS={
'RescueCount':('rescue_count',dict(minimum=N)), 'GroupEvacuated':('group_evacuated',dict(group_id=ID,minimum=N)),
'InfrastructureOperational':('infrastructure_operational',dict(infrastructure_id=ID,operational=B)),
'RouteAvailable':('route_available',dict(route_id=ID,traversal_class=en('ground|water|cable'))),
'ExposureBelow':('exposure_below',dict(region_id=ID,hazard=en('water|fire|lava|runout'),threshold=Q)),
'EquipmentLossesAtMost':('equipment_losses_at_most',dict(maximum=N)), 'SupplyRemainingAtLeast':('supply_remaining_at_least',dict(minimum=N)),
'ResourceRemaining':('resource_remaining',dict(store_id=ID,minimum=Q)),
'AllVehiclesUnloaded':('all_vehicles_unloaded',{}),'TemporarySheltersEmpty':('temporary_shelters_empty',{}),'TemporaryStagingEmpty':('temporary_staging_empty',{}),
'ScenarioElapsedAtMost':('scenario_elapsed_at_most',dict(maximum=T)),
'All':('all',dict(conditions=records('ConditionDef',16,1))),'Any':('any',dict(conditions=records('ConditionDef',16,1)))}
METRICS={'EvacuatedByMode':('evacuated_by_mode',dict(group_id=nullable(ID),mode=en('ground|water|cable'))),
'EdgeTransfers':('edge_transfers',dict(edge_tag=ID)), 'EquipmentDeployments':('equipment_deployments',dict(equipment_id=ID)),
'MaxNodeDwellScenarioTicks':('max_node_dwell_scenario_ticks',dict(node_id=ID)), 'PeakNodeOccupancy':('peak_node_occupancy',dict(node_id=ID)),
'MaxDockQueueOperationTicks':('max_dock_queue_operation_ticks',dict(node_id=ID)),
'LoadedClosedDockWaitOperationTicks':('loaded_closed_dock_wait_operation_ticks',{}),'PumpedIntoRegionMm3':('pumped_into_region_mm3',dict(region_id=ID)),
'GeneratorShutdowns':('generator_shutdowns',{}),'StationShutdowns':('station_shutdowns',{}),'ShelterExhaustions':('shelter_exhaustions',{})}
for suffix,kind,threshold in [('AtLeast','counter_at_least','minimum'),('AtMost','counter_at_most','maximum')]:
 for n,(metric,p) in METRICS.items():CONDS[n+suffix]=(kind,dict(metric=const(metric),**p,**{threshold:Q}))
for n,(kind,p) in CONDS.items():define(n+'Params',p);define(n+'Condition',dict(kind=const(kind),params=ref(n+'Params')))
union('ConditionDef',[ref(n+'Condition') for n in CONDS])
S['ObjectiveDef']={'oneOf':[obj(dict(id=ID,tier=en('required|critical|mastery|optional'),kind=const(kind),params=ref(n+'Params'),temporal=ref('TemporalDef'))) for n,(kind,p) in CONDS.items()]}
TYPES['ObjectiveDef']=dict(id=ID,tier=en('required|critical|mastery|optional'),kind=ID,params={},temporal=ref('TemporalDef'))
common('MilestoneDef',dict(id=ID,kind=en('group_boarded|final_unload_started|all_evacuated|first_cooled_crossing_closed'),target=ID))
OPS={
'set_source_rate':('water_source',0,10**9),'warn_route':('nav_edge_set',1,1000000),'set_edge_enabled':('nav_edge_set',0,1),
'set_current_multiplier':('nav_edge_set',500,2000),'release_group':('group',1,1),'ignite_region':('fire_region',0,1000),
'set_wind':('wind_region',0,8),'set_region_cooling':('fire_region',0,100),'warn_resource':('water_store',0,1000),
'add_heat':('fire_region',0,1000),'warn_wind_change':('wind_region',1,1000000),'set_stability_loss':('support',0,100),
'set_support_load':('support',0,1000),'tremor':('support',0,1000),'warn_runout':('runout',1,1000000),
'release_avalanche':('runout',1,1),'warn_support':('support|infrastructure',1,1000000),'set_tide_segment':('tide',0,3),
'set_infrastructure_demand':('infrastructure',0,32),'advance_lava':('lava_source',1,1),'enable_cooler_socket':('socket',0,1),
'warn_reheat':('lava_source',1,1000000),'enable_shelter_hazard':('shelter_region',0,1)}
S['EventOperation']={'oneOf':[obj(dict(kind=const(k),target_role=ID,value=integer(lo,hi))) for k,(_,lo,hi) in OPS.items()]}
TYPES['EventOperation']=dict(kind=ID,target_role=ID,value=Q)
common('EventDef',dict(id=ID,at_scenario_tick=integer(1,1000000),warning_at_scenario_tick=T,known_at_start=B,operations=records('EventOperation',2048,1)))
common('CompiledMission',dict(mission_version_id=ID,id=ID,revision=POS,world_id=ID,rules_version=const('resq-rules-1'),duration_scenario_ticks=integer(1,1000000),total_residents=integer(1,256),minimum_rescues=N,supply_budget=POS,default_stock=arr(EC),maximum_stock=arr(EC),permitted_equipment_ids=ids(),layout_ids=ids(2),groups=records('GroupDef',256,1),objectives=records('ObjectiveDef',128,1),protection_objective_ids=ids(128),milestones=records('MilestoneDef',256),events=records('EventDef',2048),rule_profile_ids=ids(3),challenge_ids=ids(),tutorial_policy=en('none|guided_first_play_then_unguided|world_mechanic_guidance')))
MOB=arr(en('standard|step_free'),2,1); TR=en('ground|water|cable')
common('NavNode',dict(id=ID,kind=en('assembly|junction|dock|shelter|evacuation|vehicle_parking'),position_mm=arr(integer(-1000000,1000000),3,3),resident_capacity=N,vehicle_slots=N,dock_berths=N,mobility_classes=MOB,hazard_region_ids=ids(),power_consumer_id=nullable(ID),is_temporary_staging=B,safe_depth_mm=Q,safe_heat_points=integer(0,1000),dock_min_depth_mm=Q,dock_max_depth_mm=Q))
common('NavEdge',dict(id=ID,from_node_id=ID,to_node_id=ID,traversal_class=TR,mobility_classes=MOB,movement_capacity=POS,base_operation_ticks=POS,current_multiplier_permille=integer(500,2000),administrative_enabled=B,required_equipment_socket_id=nullable(ID),required_power_consumer_id=nullable(ID),hazard_region_ids=ids(),maximum_water_depth_mm=Q,minimum_water_depth_mm=Q,maximum_heat_points=integer(0,1000),step_free=B,tags=ids()))
common('NamedRoute',dict(id=ID,from_node_id=ID,to_node_id=ID,required_waypoint_ids=ids(),traversal_class=TR,mobility_class=en('standard|step_free')))
common('Socket',dict(id=ID,kind=ID,node_id=nullable(ID),permitted_equipment_ids=ids(),orientation_steps=arr(integer(0,3),4,1),relocation_zone=ID,footprint_cell_ids=ids(),hydraulic_link_id=nullable(ID),intake_node_id=nullable(ID),outlet_node_id=nullable(ID),power_node_id=nullable(ID),support_id=nullable(ID),lava_segment_id=nullable(ID),fire_cell_ids=ids(),resource_store_id=nullable(ID),enabled=B))
common('WaterNode',dict(id=ID,kind=en('terrain|tank'),region_id=ID,elevation_mm=integer(-10000,100000),area_mm2=integer(10000,10000000),initial_volume_mm3=Q))
common('HydraulicLink',dict(id=ID,from_id=ID,to_id=ID,from_kind=en('water_node|sea_boundary'),to_kind=en('water_node|sea_boundary'),direction=en('both|forward'),sill_mm=integer(-10000,100000),conductance_mm2_per_second=integer(0,1000000),maximum_transfer_mm3=integer(0,100000000),gate_id=nullable(ID),initial_gate_open=B,barrier_socket_id=nullable(ID)))
common('WaterRateDef',dict(id=ID,water_node_id=ID,initial_rate_mm3_per_second=integer(0,1000000000)))
common('PowerNode',dict(id=ID,platform_region_id=ID,initial_enabled=B,fixed_supply=N,disconnect_depth_mm=Q,reconnect_depth_mm=Q))
common('PowerLink',dict(id=ID,from_node_id=ID,to_node_id=ID,initial_enabled=B,relay_socket_id=nullable(ID)))
common('InfrastructureDef',dict(id=ID,kind=en('clinic|lighthouse|dock|station|pump_house|cooling_works|route_service'),power_node_id=nullable(ID),initial_demand=N,initial_priority=N,required_nav_node_ids=ids(),required_support_ids=ids(),required_equipment_socket_ids=ids(),hazard_region_ids=ids(),maximum_depth_mm=Q,maximum_heat_points=integer(0,1000)))
common('FireCellDef',dict(id=ID,region_id=ID,initial_fuel=integer(0,1000),initial_heat=integer(0,1000),initial_wetness=integer(0,1000),extra_cooling_points_per_pulse=N,building_cell=B))
common('FireLink',dict(id=ID,from_cell_id=ID,to_cell_id=ID,direction_code=integer(1,8)))
common('SupportDef',dict(id=ID,kind=en('structure|slope'),node_ids=ids(),edge_ids=ids(),region_id=ID,initial_base_stability=integer(-1000,1000),static_load_points=integer(0,1000),loss_points_per_second=integer(0,100),brace_socket_ids=ids(),collapse_edge_ids=ids(),station_ids=ids()))
common('RunoutSegment',dict(id=ID,successor_ids=ids(),travel_scenario_ticks=POS,settle_scenario_ticks=POS,region_ids=ids(),edge_ids=ids(),debris_reopenable=B))
common('RunoutDef',dict(id=ID,source_support_id=nullable(ID),segments=records('RunoutSegment',4096,1)))
common('TideKnot',dict(scenario_tick=T,level_mm=Q))
common('TideDef',dict(id=ID,kind=en('event_ramps|fixed_track'),initial_level_mm=Q,knots=records('TideKnot'),repeat_period=nullable(T)))
common('LavaSegmentDef',dict(id=ID,region_id=ID,successor_ids=ids(),arrival_delay_scenario_ticks=integer(50,6000),crossing_edge_ids=ids(),cooler_socket_ids=ids(),source_enabled=B))
common('ResourceStoreDef',dict(id=ID,unit=en('water_mm3|coolant_charge|coolant_feedstock'),initial_amount=Q,capacity=Q,water_node_id=nullable(ID)))
common('ProducerDef',dict(id=ID,infrastructure_id=ID,input_store_id=ID,output_store_id=ID,input_per_charge=const(1),production_scenario_ticks=const(100),power_demand=const(1)))
common('ResidentSpawn',dict(resident_id=ID,group_id=ID,mobility_class=en('standard|step_free'),start_node_id=ID,fixed_vehicle_id=nullable(ID)))
common('VehicleSpawn',dict(vehicle_id=ID,kind=en('fixed_bus|skiff|crawler|winch'),node_id=ID,seats=POS,movement_slots=POS,initial_route_id=nullable(ID),initial_occupant_ids=ids(256)))
common('RoleBinding',dict(role_id=ID,target_kind=en('water_source|nav_edge_set|nav_node|group|fire_region|wind_region|water_store|support|runout|tide|infrastructure|lava_source|socket|shelter_region'),target_ids=arr(ID,16384,1)))
LAYOUT=dict(id=ID,mission_version_id=ID,layout_class=en('wide|compact'),graph_hash=HASH,geometry_art_id=ID)
for field,typ,limit in [('nav_nodes','NavNode',4096),('nav_edges','NavEdge',16384),('routes','NamedRoute',16384),('sockets','Socket',512),('water_nodes','WaterNode',4096),('hydraulic_links','HydraulicLink',16384),('sources','WaterRateDef',4096),('sinks','WaterRateDef',4096),('power_nodes','PowerNode',4096),('power_links','PowerLink',16384),('infrastructure','InfrastructureDef',4096),('fire_cells','FireCellDef',4096),('fire_links','FireLink',32768),('supports','SupportDef',4096),('runouts','RunoutDef',4096),('tides','TideDef',32),('lava_segments','LavaSegmentDef',4096),('resources','ResourceStoreDef',4096),('producers','ProducerDef',4096),('spawns','ResidentSpawn',256),('fixed_vehicles','VehicleSpawn',32),('roles','RoleBinding',4096)]:LAYOUT[field]=records(typ,limit)
common('LayoutDef',LAYOUT)
common('ContentBundle',dict(freeze_id=const('resq-spec-freeze-1.1.0'),rules_version=const('resq-rules-1'),worlds=records('WorldDef',5,1),equipment=records('EquipmentDef',14,1),rule_profiles=records('RuleProfileDef',3,1),challenges=records('ChallengeDef',30),missions=records('CompiledMission',25,1),layouts=records('LayoutDef',50,1),asset_catalog_hash=HASH))
common('ContentBundleEnvelope',dict(schema_version=const(1),payload_sha256=HASH,payload=ref('ContentBundle')))
# Closed typed representation, generated explicit constructors; no reflection/dynamic loading.
def cs_type(s):
 if '$ref' in s:return s['$ref'].split('/')[-1]
 if 'oneOf' in s and any(x.get('type')=='null' for x in s['oneOf']):return cs_type(next(x for x in s['oneOf'] if x.get('type')!='null'))+('?' if next(x for x in s['oneOf'] if x.get('type')!='null').get('type')=='integer' else '')
 if s.get('type')=='array':return 'ReadOnlyCollection<'+cs_type(s['items'])+'>'
 return {'integer':'long','boolean':'bool','string':'string'}.get(s.get('type'),'JsonValue')
def expr(s,v):
 if '$ref' in s:
  n=s['$ref'].split('/')[-1];return n+'.Read('+v+')'
 if 'oneOf' in s:
  p=next(x for x in s['oneOf'] if x.get('type')!='null');return v+'.IsNull ? '+('('+cs_type(s)+')null' if p.get('type')=='integer' else 'null')+' : '+expr(p,v)
 if s.get('type')=='array':return v+'.Items.Select(item => '+expr(s['items'],'item')+').ToList().AsReadOnly()'
 return v+{'integer':'.Integer','boolean':'.Boolean','string':'.Text'}.get(s.get('type'),'')
lines=['// Generated by tools/content/generate_contracts.py. Do not hand edit.','using System;','using System.Linq;','using System.Collections.ObjectModel;','namespace Resq.Content {']
union_bases={'TemporalDef':list(TEMP),'ChallengeOperation':list(CH),'ConditionDef':list(CONDS)}
for n,p in TYPES.items():
 base='ContentRecord'
 if n.endswith('Temporal') and n[:-len('Temporal')] in TEMP:base='TemporalDef'
 if n.endswith('Operation') and n[:-len('Operation')] in CH:base='ChallengeOperation'
 if n.endswith('Condition') and n[:-len('Condition')] in CONDS:base='ConditionDef'
 if n.endswith('Parameters') and n[:-len('Parameters')].lower() in PARAMETERS:base='EquipmentParameters'
 if n.endswith('Params') and n[:-len('Params')] in CONDS:base='ConditionParameters'
 lines.append('public sealed class '+n+' : '+base+' {')
 for f,s in p.items():
  typ='EquipmentParameters' if n=='EquipmentDef' and f=='parameters' else 'ConditionParameters' if n=='ObjectiveDef' and f=='params' else cs_type(s)
  lines.append('public '+typ+' '+('@'+f if f=='params' else f)+' { get; }')
 if base=='ConditionDef':lines.append('public override ConditionParameters Parameters => this.@params;')
 lines.append('private '+n+'(JsonValue value) : base(value) {')
 for f,s in p.items():
  read='EquipmentParameters.Read(value["id"].Text,value["parameters"])' if n=='EquipmentDef' and f=='parameters' else 'ConditionParameters.Read(value["kind"].Text,value["params"])' if n=='ObjectiveDef' and f=='params' else expr(s,'value["'+f+'"]')
  lines.append('this.'+('@'+f if f=='params' else f)+' = '+read+';')
 lines+=['}','public '+('new ' if base!='ContentRecord' else '')+'static '+n+' Read(JsonValue value) { ContentSchemas.Validate("'+n+'", value); return new '+n+'(value); }','}']
for name,catalog,suffix in [('EquipmentParameters',PARAMETERS,'Parameters'),('ConditionParameters',CONDS,'Params')]:
 lines.append('public abstract class '+name+' : ContentRecord { protected '+name+'(JsonValue value) : base(value) {}')
 lines.append('public static '+name+' Read(string kind, JsonValue value) {')
 for variant,data in catalog.items():
  key=variant if name=='EquipmentParameters' else data[0]
  condition='kind == \"'+key+'\"'
  if name=='ConditionParameters' and 'metric' in data[1]:condition+=' && value[\"metric\"].Text == \"'+data[1]['metric']['const']+'\"'
  klass=variant.title()+suffix if name=='EquipmentParameters' else variant+suffix
  lines.append('if ('+condition+') return '+klass+'.Read(value);')
 lines.append('throw new ContentException(\"UNKNOWN_KIND\",kind); } }')
for n,variants in union_bases.items():
 lines+=['public abstract class '+n+' : ContentRecord { protected '+n+'(JsonValue value) : base(value) {}',('public abstract ConditionParameters Parameters { get; }' if n=='ConditionDef' else ''),'public static '+n+' Read(JsonValue value) { ContentSchemas.Validate("'+n+'",value); string kind=value["kind"].Text;']
 for v in variants:
  suffix={'TemporalDef':'Temporal','ChallengeOperation':'Operation','ConditionDef':'Condition'}[n]
  klass=v+suffix;kind=S[klass]['properties']['kind']['const']
  if n=='ConditionDef' and 'metric' in CONDS[v][1]:condition='kind == "'+kind+'" && value["params"]["metric"].Text == "'+CONDS[v][1]['metric']['const']+'"'
  else:condition='kind == "'+kind+'"'
  lines.append('if ('+condition+') return '+klass+'.Read(value);')
 lines+=['throw new ContentException("UNKNOWN_KIND", kind); }','}']
lines+=['public static class ContentSchemas {','private static readonly JsonValue Definitions = StrictJson.Parse('+json.dumps(json.dumps(S,separators=(',',':')))+');','public static void Validate(string name, JsonValue value) { SchemaValidator.Validate(Definitions[name],value,Definitions,"$" ); }','public static string Export(string name) { return JsonValue.Object(new System.Collections.Generic.Dictionary<string, JsonValue> { {"$schema", JsonValue.String("https://json-schema.org/draft/2020-12/schema")}, {"$ref",JsonValue.String("#/$defs/"+name)}, {"$defs",Definitions} }).Canonical(); }','}','}']
OUT.mkdir(parents=True,exist_ok=True);(OUT/'ContentDefinitions.g.cs').write_text('\n'.join(lines)+'\n')
for n in ['ContentBundleEnvelope','ContentBundle','CompiledMission','LayoutDef','ObjectiveDef','EventDef']:
 (ROOT/'content/runtime/schemas'/f'{n}.schema.json').write_text(json.dumps({'$schema':'https://json-schema.org/draft/2020-12/schema','$ref':'#/$defs/'+n,'$defs':S},indent=2)+'\n')
(ROOT/'tools/content/operation_targets.json').write_text(json.dumps({k:v[0] for k,v in OPS.items()},indent=2)+'\n')
print('Generated',len(TYPES),'typed records and',len(S),'closed schema definitions.')
