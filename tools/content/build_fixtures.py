#!/usr/bin/env python3
"""Explicit synthetic compiler fixtures; these are not authored RESQ levels."""
from pathlib import Path
import json,hashlib
ROOT=Path(__file__).resolve().parents[2]
def canonical(value):return json.dumps(value,sort_keys=True,separators=(',',':'),ensure_ascii=False).encode()
resident='fixture:resident:group:01'
mission=dict(mission_version_id='fixture-v1',id='fixture',revision=1,world_id='W1',rules_version='resq-rules-1',duration_scenario_ticks=1000,total_residents=1,minimum_rescues=1,supply_budget=1,default_stock=[dict(equipment_id='beacon',count=1)],maximum_stock=[dict(equipment_id='beacon',count=1)],permitted_equipment_ids=['beacon'],layout_ids=['fixture-compact','fixture-wide'],groups=[dict(id='group',resident_ids=[resident],start_node_id='start',assembly_node_id='assembly',initially_held=False)],objectives=[dict(id='rescue_minimum',tier='required',kind='rescue_count',params=dict(minimum=1),temporal=dict(kind='at_end')),dict(id='mastery',tier='mastery',kind='exposure_below',params=dict(region_id='wet',hazard='water',threshold=100),temporal=dict(kind='hold_for',scenario_ticks=50,start_event='operation_started',end_event='operation_ended'))],protection_objective_ids=['critical'],milestones=[],events=[dict(id='surge',at_scenario_tick=100,warning_at_scenario_tick=0,known_at_start=True,operations=[dict(kind='set_source_rate',target_role='source',value=100)])],rule_profile_ids=['standard'],challenge_ids=[],tutorial_policy='none')
def node(id,kind):return dict(id=id,kind=kind,position_mm=[0,0,0],resident_capacity=1,vehicle_slots=1,dock_berths=1,mobility_classes=['standard','step_free'],hazard_region_ids=[],power_consumer_id=None,is_temporary_staging=False,safe_depth_mm=100,safe_heat_points=600,dock_min_depth_mm=0,dock_max_depth_mm=2000)
def edge(id,a,b):return dict(id=id,from_node_id=a,to_node_id=b,traversal_class='ground',mobility_classes=['standard','step_free'],movement_capacity=2,base_operation_ticks=10,current_multiplier_permille=1000,administrative_enabled=True,required_equipment_socket_id=None,required_power_consumer_id=None,hazard_region_ids=[],maximum_water_depth_mm=100,minimum_water_depth_mm=0,maximum_heat_points=600,step_free=True,tags=['fixture_path'])
mission['objectives'].append(dict(id='critical',tier='critical',kind='route_available',params=dict(route_id='exit',traversal_class='ground'),temporal=dict(kind='at_end')))
layouts=[]
for cls in ['compact','wide']:
 layout=dict(id='fixture-'+cls,mission_version_id='fixture-v1',layout_class=cls,geometry_art_id='fixture.geometry',nav_nodes=[node('start','assembly'),node('assembly','assembly'),node('refuge','evacuation'),node('junction','junction')],nav_edges=[edge('a','start','assembly'),edge('b','assembly','refuge'),edge('c','assembly','junction'),edge('d','junction','refuge')],routes=[dict(id='exit',from_node_id='assembly',to_node_id='refuge',required_waypoint_ids=[],traversal_class='ground',mobility_class='step_free')],sockets=[],water_nodes=[dict(id='water',kind='terrain',region_id='wet',elevation_mm=0,area_mm2=1000000,initial_volume_mm3=0)],hydraulic_links=[],sources=[dict(id='inflow',water_node_id='water',initial_rate_mm3_per_second=0)],sinks=[],power_nodes=[],power_links=[],infrastructure=[],fire_cells=[],fire_links=[],supports=[],runouts=[],tides=[],lava_segments=[],resources=[],producers=[],spawns=[dict(resident_id=resident,group_id='group',mobility_class='step_free',start_node_id='start',fixed_vehicle_id=None)],fixed_vehicles=[],roles=[dict(role_id='source',target_kind='water_source',target_ids=['inflow'])])
 for i in range(1,3):
  cell='dry'+str(i)
  layout['water_nodes'].append(dict(id=cell,kind='terrain',region_id='dry',elevation_mm=1200,area_mm2=1000000,initial_volume_mm3=0))
  layout['sockets'].append(dict(id='beacon:0'+str(i),kind='assembly',node_id='assembly',permitted_equipment_ids=['beacon'],orientation_steps=[0,1,2,3],relocation_zone='dry',footprint_cell_ids=[cell],hydraulic_link_id=None,intake_node_id=None,outlet_node_id=None,power_node_id=None,support_id=None,lava_segment_id=None,fire_cell_ids=[],resource_store_id=None,enabled=True))
 # Canonical definition sorting, matching compiler entity ordering.
 for k,v in layout.items():
  if isinstance(v,list) and v and isinstance(v[0],dict):
   key=next((name for name in ['id','resident_id','vehicle_id','role_id','equipment_id'] if all(name in x for x in v)),None)
   if key:v.sort(key=lambda x:x[key])
 layout['graph_hash']=hashlib.sha256(canonical({k:v for k,v in layout.items() if k!='geometry_art_id'})).hexdigest()
 layouts.append(layout)
bundle=dict(freeze_id='resq-spec-freeze-1.1.0',rules_version='resq-rules-1',worlds=[dict(id='W1',order=1,release_scope='competition',title_key='world.fixture',art_bundle_id='fixture.art',audio_bundle_id='fixture.audio')],equipment=[dict(id='beacon',revision=1,supply_cost=1,first_world_id='W1',socket_kinds=['assembly'],power_supply=0,power_demand=0,resident_seats=0,movement_slots=0,relocation_policy='idle_same_zone',durability=100,parameters={},prefab_id='fixture.beacon')],rule_profiles=[dict(id='standard',scenario_numerator=2,scenario_denominator=2,planning_allowed=True,extra_stock=[],permitted_input_adapters=['reference_replay'])],challenges=[],missions=[mission],layouts=layouts,asset_catalog_hash='0'*64)
out=ROOT/'content/runtime/fixtures';out.mkdir(parents=True,exist_ok=True)
(out/'compiler-valid.json').write_text(json.dumps(bundle,indent=2)+'\n')
(out/'README.md').write_text('These inputs exercise the pure content compiler. They are synthetic format/topology fixtures, not authored campaign levels, gameplay, reference solutions or acceptance evidence. No simulation outcome is asserted.\n')
# Nonempty typed-system controls for reciprocal socket, brace, and firebreak checks.
import copy
specs={
 'brace':('structural_support',2,dict(stability_bonus=200,maximum_per_support=3)),
 'barrier':('hydraulic_edge',2,dict(crest_mm=600,warning_head_mm=600,damage_head_mm=800,damage_per_pulse=1)),
 'relay':('power_link',2,dict(disconnect_depth_mm=150,reconnect_depth_mm=100)),
 'cooler':('cooling_segment',3,dict(initial_charges=1,capacity_charges=1,cooling_scenario_ticks=50,crust_scenario_ticks=200,reheat_scenario_ticks=50)),
 'firebreak':('fuel_strip',2,dict(maximum_cells=8,uses=1))}
for tool,(kind,cost,parameters) in specs.items():
 fixture=copy.deepcopy(bundle);m=fixture['missions'][0];m['supply_budget']+=cost;m['permitted_equipment_ids'].append(tool)
 for field in ['default_stock','maximum_stock']:m[field].append(dict(equipment_id=tool,count=1))
 fixture['equipment'].append(dict(id=tool,revision=1,supply_cost=cost,first_world_id='W1',socket_kinds=[kind],power_supply=0,power_demand=0,resident_seats=0,movement_slots=0,relocation_policy='none',durability=100,parameters=parameters,prefab_id='fixture.'+tool))
 for layout in fixture['layouts']:
  for index in range(1,3):
   socket=copy.deepcopy(layout['sockets'][index-1]);socket['id']=tool+':0'+str(index);socket['kind']=kind;socket['permitted_equipment_ids']=[tool];subject=tool+str(index)
   if tool=='brace':
    socket['support_id']=subject;layout['supports'].append(dict(id=subject,kind='structure',node_ids=[],edge_ids=[],region_id='dry',initial_base_stability=1000,static_load_points=0,loss_points_per_second=0,brace_socket_ids=[socket['id']],collapse_edge_ids=[],station_ids=[]))
   elif tool=='barrier':
    socket['hydraulic_link_id']=subject;layout['hydraulic_links'].append(dict(id=subject,from_id='dry1',to_id='dry2',from_kind='water_node',to_kind='water_node',direction='both',sill_mm=0,conductance_mm2_per_second=100000,maximum_transfer_mm3=100000000,gate_id=None,initial_gate_open=True,barrier_socket_id=socket['id']))
   elif tool=='relay':
    socket['power_node_id']=subject;layout['power_nodes'].append(dict(id=subject,platform_region_id='dry',initial_enabled=True,fixed_supply=0,disconnect_depth_mm=150,reconnect_depth_mm=100))
    layout['power_links'].append(dict(id=subject,from_node_id='relay1',to_node_id='relay2',initial_enabled=True,relay_socket_id=socket['id']))
   elif tool=='cooler':
    socket['lava_segment_id']=subject;layout['lava_segments'].append(dict(id=subject,region_id='dry',successor_ids=[],arrival_delay_scenario_ticks=100,crossing_edge_ids=[],cooler_socket_ids=[socket['id']],source_enabled=False))
   elif tool=='firebreak':
    cells=[subject+'a',subject+'b'];socket['fire_cell_ids']=cells;socket['footprint_cell_ids']=[cells[0]]
    for cell in cells:layout['fire_cells'].append(dict(id=cell,region_id='burn',initial_fuel=1000,initial_heat=0,initial_wetness=0,extra_cooling_points_per_pulse=0,building_cell=False))
    layout['fire_links'].append(dict(id=subject,from_cell_id=cells[0],to_cell_id=cells[1],direction_code=1))
   layout['sockets'].append(socket)
  for value in layout.values():
   if isinstance(value,list) and value and isinstance(value[0],dict):
    key=next((name for name in ['id','resident_id','vehicle_id','role_id','equipment_id'] if all(name in x for x in value)),None)
    if key:value.sort(key=lambda x:x[key])
  layout['graph_hash']=hashlib.sha256(canonical({k:v for k,v in layout.items() if k not in ['graph_hash','geometry_art_id']})).hexdigest()
 (out/('compiler-'+tool+'.json')).write_text(json.dumps(fixture,indent=2)+'\n')
skiff=copy.deepcopy(bundle)
for objective in skiff['missions'][0]['objectives']:
 if objective['kind']=='route_available' and objective['params']['route_id']=='exit':objective['params']['traversal_class']='water'
for layout in skiff['layouts']:
 for e in layout['nav_edges']:
  if e['id']!='a':e['traversal_class']='water'
 layout['routes'][0]['traversal_class']='water'
 layout['fixed_vehicles']=[dict(vehicle_id='fixture-skiff',kind='skiff',node_id='assembly',seats=4,movement_slots=1,initial_route_id='exit',initial_occupant_ids=[])]
 layout['graph_hash']=hashlib.sha256(canonical({k:v for k,v in layout.items() if k not in ['graph_hash','geometry_art_id']})).hexdigest()
(out/'compiler-skiff.json').write_text(json.dumps(skiff,indent=2)+'\n')
pickup=copy.deepcopy(bundle)
for layout in pickup['layouts']:
 layout['nav_nodes'] += [node('group_pickup','dock'),node('alternate','dock')]
 for ident,a,b in [('pickup_ground','assembly','group_pickup'),('alternate_ground','assembly','alternate'),('pickup_water','group_pickup','refuge'),('alternate_water','alternate','refuge'),('connect_water','group_pickup','alternate')]:
  e=edge(ident,a,b)
  if ident.endswith('water'):e['traversal_class']='water'
  layout['nav_edges'].append(e)
 layout['routes'].append(dict(id='pickup_route',from_node_id='group_pickup',to_node_id='refuge',required_waypoint_ids=[],traversal_class='water',mobility_class='step_free'))
 layout['roles'].append(dict(role_id='pickup_binding',target_kind='nav_node',target_ids=['group_pickup']))
 layout['fixed_vehicles']=[dict(vehicle_id='fixture-skiff',kind='skiff',node_id='group_pickup',seats=4,movement_slots=1,initial_route_id=None,initial_occupant_ids=[])]
 for value in layout.values():
  if isinstance(value,list) and value and isinstance(value[0],dict):
   key=next((name for name in ['id','resident_id','vehicle_id','role_id','equipment_id'] if all(name in x for x in value)),None)
   if key:value.sort(key=lambda x:x[key])
 layout['graph_hash']=hashlib.sha256(canonical({k:v for k,v in layout.items() if k not in ['graph_hash','geometry_art_id']})).hexdigest()
(out/'compiler-pickup.json').write_text(json.dumps(pickup,indent=2)+'\n')
