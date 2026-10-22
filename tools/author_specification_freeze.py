#!/usr/bin/env python3
"""Regenerate immutable RESQ planning data, never execution state or game results.

Only run to author a new freeze revision. Existing mutable state is never overwritten.
The machine-readable JSON outputs are the handoff inputs; this script makes the
initial freeze reproducible. Numerical values are initial design tuning.
"""
from pathlib import Path
import hashlib
import json

ROOT = Path(__file__).resolve().parents[1]
PACK = ROOT / 'Resq_Production_Pack'
FREEZE = 'resq-spec-freeze-1.1.0'
RULES = 'resq-rules-1'


def write(path, data):
    target = ROOT / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(data, indent=2, ensure_ascii=False) + '\n')


def at_end():
    return {'kind': 'at_end'}


def always(end='operation_ended', start='operation_started'):
    return {'kind': 'always', 'start_event': start, 'end_event': end}


def objective(ident, tier, kind, params, temporal=None):
    return {'id': ident, 'tier': tier, 'kind': kind, 'params': params,
            'temporal': temporal or at_end()}


def group(ident, count):
    return {'id': ident, 'count': count, 'start_node_id': ident + '_start',
            'assembly_node_id': ident + '_assembly',
            'mobility_counts': {'standard': count - 1, 'step_free': 1}}


GROUPS = [
 [('bus',4),('low_dock',4)], [('orchard',4),('river_houses',4),('ferry_landing',4)],
 [('west_village',8),('east_workshop',6)], [('clinic',6),('tram',6),('riverside',4)],
 [('upper_village',5),('orchard',5),('workshop',5),('island',5)],
 [('campsite',6),('lookout',4)], [('north_camp',6),('east_camp',6),('west_camp',6)],
 [('orchard',8),('pond_houses',8)], [('lookout',8),('east_camp',6),('west_camp',6)],
 [('campsite',8),('orchard',8),('sawmill',8)],
 [('upper_station',6),('lower_station',4)], [('observatory',6),('station',4),('village',4)],
 [('weather_crew',6),('rock_shelf',4),('village',6)],
 [('upper_terrace',6),('cable_station',6),('lower_village',6)],
 [('observatory',8),('station',8),('village',6)],
 [('low_market',8),('outer_island',4)], [('west_island',6),('market',6),('lighthouse',4)],
 [('north_market',5),('east_market',5),('south_market',5),('west_market',5)],
 [('medical_landing',6),('market',8),('inland',8)],
 [('low_market',8),('middle_island',6),('lighthouse',6),('outer_island',6)],
 [('orchard',6),('research',6)], [('orchard',9),('village',9)],
 [('cooling_works',8),('research',6),('village',6)],
 [('research_crew',8),('coast',8),('inland',8)],
 [('orchard',6),('station',6),('research',6),('waterfront',6),('high_village',6)],
]

# Each entry is an explicit operation list for the corresponding authored event.
# All IDs bind to a layout role in layout_recipes.json; English briefs are not parsed.
EVENTS = [
 [[('set_source_rate','reservoir',60_000_000)], [('set_source_rate','low_basin',10_000_000)], [('set_source_rate','reservoir',90_000_000)]],
 [[('set_source_rate','upper_basin',40_000_000)], [('warn_route','lower_road',400)], [('set_source_rate','upper_basin',80_000_000)]],
 [[('set_source_rate','central_basin',60_000_000)], [('set_edge_enabled','boat_lane_inner',0)], [('set_current_multiplier','east_lane',1500)]],
 [[('set_source_rate','clinic_basin',30_000_000)], [('release_group','tram',1)], [('set_source_rate','power_basin',30_000_000)], [('set_source_rate','second_basin',50_000_000)]],
 [[('set_source_rate','reservoir',50_000_000)], [('set_edge_enabled','short_road',0)], [('set_source_rate','reservoir',80_000_000)], [('set_source_rate','downstream_basin',30_000_000)]],
 [[('ignite_region','dry_cluster',700)], [('set_wind','forest',1)], [('set_region_cooling','firebreak_approach',1)]],
 [[('ignite_region','short_road_fuel',700)], [('set_wind','forest',2)], [('add_heat','refill_station_region',200)]],
 [[('ignite_region','orchard_edge',700)], [('warn_resource','tank',500)], [('ignite_region','return_road_fuel',700)]],
 [[('set_wind','forest',1)], [('warn_wind_change','forest',400)], [('set_wind','forest',1)]],
 [[('ignite_region','outer_forest',700)], [('add_heat','sawmill_access',200)], [('set_wind','forest',2)], [('add_heat','corridor_flank',200)]],
 [[('set_stability_loss','weak_support',1)], [('set_support_load','weak_support',100)], [('tremor','weak_support',150)]],
 [[('set_edge_enabled','upper_path',0)], [('release_group','station',1)], [('set_stability_loss','upper_platform',2)]],
 [[('warn_runout','quiet_slope',1200)], [('release_avalanche','quiet_slope',1)], [('set_edge_enabled','debris_bypass',1)]],
 [[('set_source_rate','lower_basin',20_000_000)], [('set_support_load','busy_cable',150)], [('set_source_rate','melt_basin',20_000_000)]],
 [[('set_stability_loss','upper_support',1)], [('warn_support','upper_support',600)], [('release_avalanche','valley_slope',1)], [('set_edge_enabled','lower_window',1)]],
 [[('set_tide_segment','tide',1)], [('set_tide_segment','tide',2)], [('set_tide_segment','tide',3)]],
 [[('set_tide_segment','tide',1)], [('set_infrastructure_demand','lighthouse',1)], [('set_tide_segment','tide',2)]],
 [[('set_edge_enabled','tidal_lane',1)], [('set_edge_enabled','pontoon_channel',0)], [('set_current_multiplier','outer_loop',1500)]],
 [[('set_source_rate','inland_basin',40_000_000)], [('set_tide_segment','tide',2)], [('set_tide_segment','tide',0)], [('set_source_rate','inland_basin',80_000_000)]],
 [[('set_tide_segment','tide',1)], [('warn_support','harbor_power',600)], [('set_current_multiplier','outer_loop',1500)], [('set_tide_segment','tide',2)]],
 [[('advance_lava','first_channel',1)], [('enable_cooler_socket','crossing_a',1)], [('warn_reheat','first_channel',100)]],
 [[('advance_lava','assembly_channel',1)], [('enable_shelter_hazard','shelter_a',1)], [('set_edge_enabled','outer_route',1)]],
 [[('set_source_rate','drainage_basin',30_000_000)], [('advance_lava','coolant_delivery',1)], [('advance_lava','second_channel',1)]],
 [[('warn_route','short_land_route',1300)], [('advance_lava','land_channel',1)], [('set_edge_enabled','sea_route',1)], [('enable_cooler_socket','crossing_b',1)]],
 [[('set_source_rate','drainage_basin',30_000_000),('advance_lava','orchard_channel',1)], [('warn_route','first_transport',1200)], [('set_source_rate','power_basin',40_000_000)], [('advance_lava','staging_channel',1)]],
]


def special_objectives():
    def g(x,n): return ('group_evacuated',{'group_id':x,'minimum':n})
    def infra(x): return ('infrastructure_operational',{'infrastructure_id':x,'operational':True})
    def route(x): return ('route_available',{'route_id':x,'traversal_class':{'powered_cable':'cable','final_ferry':'water'}.get(x,'ground')})
    def counter_max(metric, maximum, **extra): return ('counter_at_most',dict(metric=metric,maximum=maximum,**extra))
    def counter_min(metric, minimum, **extra): return ('counter_at_least',dict(metric=metric,minimum=minimum,**extra))
    # required semantic, critical star semantic, additional mastery semantics
    return [
      ([g('bus',4)],g('bus',4),[('infrastructure_operational',{'infrastructure_id':'low_dock','operational':True},always('low_dock_group_boarded'))]),
      ([infra('medical_station')],infra('medical_station'),[('exposure_below',{'region_id':'orchard_assembly','hazard':'water','threshold':1},always())]),
      ([g('east_workshop',6)],g('east_workshop',6),[(*counter_min('evacuated_by_mode',4,group_id='west_village',mode='ground'),at_end())]),
      ([infra('clinic_power')],infra('clinic_power'),[('counter_at_most',{'metric':'generator_shutdowns','maximum':0},always())]),
      ([route('depot_upper')],route('depot_upper'),[(*route('depot_upper'),{'kind':'hold_for','scenario_ticks':100,'start_event':'operation_started','end_event':'operation_ended'})]),
      ([route('lookout_path')],route('lookout_path'),[('resource_remaining',{'store_id':'tank','minimum':1_000_000},at_end())]),
      ([('all_vehicles_unloaded',{})],infra('refill_station'),[(*infra('refill_station'),always())]),
      ([route('orchard_assembly_route')],route('orchard_assembly_route'),[('resource_remaining',{'store_id':x,'minimum':1_000_000},at_end()) for x in ('pond','tank')]),
      ([g('lookout',8)],g('lookout',8),[(*counter_max('equipment_deployments',2,equipment_id='firebreak'),at_end())]),
      ([route('central_route')],route('central_route'),[(*infra('orchard_pump_house'),at_end())]),
      ([infra('cable_support')],infra('cable_support'),[('counter_at_most',{'metric':'station_shutdowns','maximum':0},always())]),
      ([g('observatory',6)],g('observatory',6),[(*counter_max('max_node_dwell_scenario_ticks',100,node_id='intermediate'),at_end())]),
      ([g('weather_crew',6)],g('weather_crew',6),[(*counter_min('edge_transfers',1,edge_tag='rock_shelf_bypass'),at_end())]),
      ([route('powered_cable')],route('powered_cable'),[('exposure_below',{'region_id':x,'hazard':'water','threshold':1},always()) for x in ('lower_platform_a','lower_platform_b')]),
      ([g('observatory',8),g('station',8)],g('observatory',8),[(*counter_max('peak_node_occupancy',6,node_id='receiving_station'),at_end())]),
      ([g('low_market',8)],g('low_market',8),[('counter_at_most',{'metric':'loaded_closed_dock_wait_operation_ticks','maximum':0},always())]),
      ([infra('lighthouse')],infra('lighthouse'),[(*infra('lighthouse'),always('final_unloading_ended','final_unloading_started'))]),
      ([('all_vehicles_unloaded',{})],infra('receiving_harbor'),[(*counter_max('max_dock_queue_operation_ticks',50,node_id='receiving_dock'),at_end())]),
      ([route('medical_landing_access')],route('medical_landing_access'),[('counter_at_most',{'metric':'pumped_into_region_mm3','region_id':'market_basin','maximum':0},always())]),
      ([infra('lighthouse'),('temporary_staging_empty',{})],infra('lighthouse'),[(*route('high_causeway'),at_end())]),
      ([('temporary_shelters_empty',{})],route('high_landing_path'),[('rescue_count',{'minimum':12},{'kind':'ever_before','end_event':'first_crossing_reheated'})]),
      ([('temporary_shelters_empty',{})],('temporary_shelters_empty',{}),[('counter_at_most',{'metric':'shelter_exhaustions','maximum':0},always())]),
      ([infra('cooling_works')],infra('cooling_works'),[('resource_remaining',{'store_id':'coolant','minimum':1},at_end())]),
      ([g('research_crew',8),('temporary_shelters_empty',{})],g('research_crew',8),[(*counter_min('evacuated_by_mode',1,group_id=None,mode=x),at_end()) for x in ('ground','water')]),
      ([('temporary_shelters_empty',{}),route('final_ferry')],route('final_ferry'),[('equipment_losses_at_most',{'maximum':0},always())]),
    ]


def build_missions():
    originals=json.loads((PACK/'content/missions.json').read_text())
    specs=special_objectives()
    result=[]
    for i,m in enumerate(originals):
        req,crit,master=specs[i]
        objectives=[objective('rescue_minimum','required','rescue_count',{'minimum':m['minimum_rescues']})]
        objectives += [objective(f'required_{n+1}','required',k,p) for n,(k,p) in enumerate(req)]
        objectives += [objective('critical','critical',*crit)]
        objectives += [objective(f'mastery_{n+1}','mastery',k,p,t) for n,(k,p,t) in enumerate(master)]
        events=[]
        for j,(brief,ops) in enumerate(zip(m['event_briefs'],EVENTS[i])):
            tick=brief['simulation_second']*10
            events.append({'id':f'event_{j+1}','at_scenario_tick':tick,
                           'warning_at_scenario_tick':max(0,tick-100), 'known_at_start':True,
                           'operations':[{'kind':k,'target_role':target,'value':value} for k,target,value in ops]})
        # A wind warning at 200s announces the actual direction change at 240s.
        if m['id']=='W2-M4':
            events.append({'id':'wind_turn_announced','at_scenario_tick':2400,
                           'warning_at_scenario_tick':2000,'known_at_start':True,
                           'operations':[{'kind':'set_wind','target_role':'forest','value':2}]})
        events.sort(key=lambda x:(x['at_scenario_tick'],x['id']))
        milestone=[]
        if m['id']=='W1-M1':milestone=[{'id':'low_dock_group_boarded','kind':'group_boarded','target':'low_dock'}]
        if m['id']=='W4-M2':milestone=[{'id':'final_unloading_started','kind':'final_unload_started','target':'all_residents'}, {'id':'final_unloading_ended','kind':'all_evacuated','target':'all_residents'}]
        if m['id']=='W5-M1':milestone=[{'id':'first_crossing_reheated','kind':'first_cooled_crossing_closed','target':'first_channel'}]
        result.append({'id':m['id'],'title':m['title'],'world_id':m['world_id'],
            'release_scope':m['release_scope'],'status':'frozen_design_unimplemented',
            'mission_revision':1,'rules_version':RULES,
            'duration_scenario_ticks':m['duration_seconds']*10,
            'total_residents':m['total_residents'],'minimum_rescues':m['minimum_rescues'],
            'supply_budget':m['supply_budget'],'default_loadout':m['default_loadout'],
            'maximum_stock':m['default_loadout'], 'permitted_tools':sorted(m['default_loadout']),
            'groups':[group(x,n) for x,n in GROUPS[i]],
            'layout_recipe_id':m['id']+'-layout-v1',
            'objectives':objectives,'milestones':milestone,'events':events,
            'protection_objective_ids':['critical'] if crit[0] in ('infrastructure_operational','route_available') else [],
            'global_success_conditions':['minimum_and_required_objectives','all_vehicles_unloaded','temporary_shelters_empty'],
            'reference_strategy_briefs':{'A':m['reference_plan_a'],'B':m['reference_plan_b']},
            'failure_explanation_brief':m['failure_explanation'],
            'tutorial_policy':'guided_first_play_then_unguided' if m['id']=='W1-M1' else ('world_mechanic_guidance' if m['order']==1 else 'none'),
            'base_rule_profiles':['standard','relaxed','command'],
            'required_checks':['structural_compile','two_distinct_successes','no_input_failure','major_mistake_cause','save_resume_equivalence','tracking_and_anchor_recovery','device_input','final_art_audio'],
        })
    write('specification/content/mission_contracts.json',{'schema_version':1,'freeze_id':FREEZE,'missions':result})
    return result


def build_equipment():
    # operational values are owned by the runtime-rules table; this registry freezes
    # supply/stock/role binding and references that exact table instead of duplicating it.
    roles={'barrier':'hydraulic_edge','bridge':'ground_gap','pump':'pump_link','skiff':'launch_dock','beacon':'assembly',
           'generator':'dry_power_source','firebreak':'fuel_strip','sprinkler':'wetting_area','crawler':'vehicle_depot',
           'brace':'structural_support','winch':'vertical_link','relay':'power_link','cooler':'cooling_segment','shelter':'refuge'}
    rows=json.loads((PACK/'content/equipment.json').read_text())
    for r in rows:
        r.update({'socket_kind':roles[r['id']], 'rules_ref':'specification/02_RUNTIME_RULES.md#equipment-defaults',
                  'prefab_id':'equipment.'+r['id'], 'finite_stock':True,'refund_on_reposition':False})
    write('specification/content/equipment_rules.json',{'schema_version':1,'rules_version':RULES,'equipment':rows,
      'fixed_vehicle':{'id':'bus','seats':4,'ground_movement_slots':2,'purchasable':False},
      'fixed_infrastructure':{
       'medical_station':{'power_demand':1},'clinic_power':{'power_demand':1},
       'lighthouse':{'power_demand':1},'cooling_works':{'power_demand':1},
       'refill_station':{'power_demand':0},'orchard_pump_house':{'power_demand':0},
       'cable_support':{'power_demand':0},'receiving_harbor':{'power_demand':0}},
      'supply_policy':'reserve_selected_stock_then_spend_on_successful_deployment; no purchase charge at loadout'})


def build_modes(missions):
    challenges=[]
    for m in missions:
        mid=m['id'];wid=m['world_id']
        if wid=='W1':
            event=next(e for e in m['events'] if any(o['kind']=='set_source_rate' for o in e['operations']))
            challenges += [dict(id=mid+'-early-surge',mission_id=mid,modifier='early_surge',
                parameters={'event_id':event['id'],'shift_scenario_ticks':-300},rule_profiles=['standard'],
                layouts=['compact','wide'],unlock='base_mission_success')]
            challenges += [dict(id=mid+'-alternate-dock',mission_id=mid,modifier='alternate_dock',
                parameters={'group_id':next(g['id'] for g in reversed(m['groups']) if g['id']!='bus'),
                            'dock_role':'alternate_pickup'},rule_profiles=['standard'],
                layouts=['compact','wide'],unlock='base_mission_success')]
        else:
            modifier={'W2':'crosswind','W3':'careful_operation','W4':'high_tide_start','W5':'careful_operation'}[wid]
            params={'reverse_wind_vectors':True} if wid=='W2' else ({'initial_tide_segment_index':1} if wid=='W4' else {'maximum_equipment_losses':0})
            # Careful operation must add a challenge beyond an already zero-loss mastery.
            if mid=='W5-M5':params['supply_budget_delta']=-1;params['default_loadout_delta']={'barrier':-1};modifier='careful_light_kit'
            challenges.append(dict(id=mid+'-'+modifier.replace('_','-'),mission_id=mid,modifier=modifier,
                parameters=params,rule_profiles=['standard'],layouts=['compact','wide'],unlock='base_mission_success'))
    write('specification/content/modes_and_challenges.json',{'schema_version':1,'rules_version':RULES,
      'rule_profiles':[
       {'id':'standard','hazard_clock_numerator':1,'hazard_clock_denominator':1,'warning_operation_time_multiplier':1,'advance_controls':False,'automatic_checkpoint_retry':False,'score_partition':'standard'},
       {'id':'relaxed','hazard_clock_numerator':1,'hazard_clock_denominator':2,'warning_operation_time_multiplier':2,'advance_controls':False,'automatic_checkpoint_retry':True,'score_partition':'relaxed'},
       {'id':'command','hazard_clock_numerator':1,'hazard_clock_denominator':1,'warning_operation_time_multiplier':1,'advance_controls':True,'automatic_checkpoint_retry':False,'score_partition':'command'}],
      'input_adapters':['hands','controller','head_dwell'],
      'supported_combinations':[{'rules':r,'input':a} for r,a in [('standard','hands'),('standard','controller'),('relaxed','hands'),('relaxed','controller'),('command','hands'),('command','controller'),('command','head_dwell')]],
      'legacy_preset_mapping':{'standard_hands':['standard','hands'],'relaxed_hands':['relaxed','hands'],'command_head_dwell':['command','head_dwell']},
      'challenge_combinations':'one named definition only; no stacking; standard rules with hands/controller',
      'challenges':challenges,'unsupported_ideas':['light_kit_except_W5-M5','arbitrary_modifier_combinations','head_dwell_under_standard_or_relaxed'],
      'assisted_score_partition':'separate; ordinary suspension/resume remains unassisted',
      'base_success_replays':25*2*3*2,'challenge_success_replays':len(challenges)*2*2})


def build_layouts(missions):
    anchor_sets={
      'W1':{'source':[15,22],'fork':[15,15],'west':[7,12],'east':[23,12],'orchard':[8,7],'depot':[15,3],'high_landing':[25,4],'outlet':[16,0],'bridge_a':[11,10],'bridge_b':[19,10],'upper':[8,19]},
      'W2':{'source':[5,20],'fork':[15,14],'west':[6,13],'east':[25,13],'orchard':[8,7],'depot':[15,3],'high_landing':[25,4],'outlet':[16,0],'bridge_a':[10,11],'bridge_b':[20,11],'upper':[24,20]},
      'W3':{'source':[12,22],'fork':[15,16],'west':[6,18],'east':[25,18],'orchard':[7,9],'depot':[15,3],'high_landing':[26,5],'outlet':[16,0],'bridge_a':[11,13],'bridge_b':[20,13],'upper':[15,21]},
      'W4':{'source':[16,23],'fork':[15,14],'west':[5,15],'east':[26,15],'orchard':[8,7],'depot':[15,3],'high_landing':[25,4],'outlet':[16,0],'bridge_a':[11,11],'bridge_b':[20,11],'upper':[15,20]},
      'W5':{'source':[15,22],'fork':[15,15],'west':[5,14],'east':[26,14],'orchard':[7,8],'depot':[15,3],'high_landing':[25,4],'outlet':[16,0],'bridge_a':[10,11],'bridge_b':[21,11],'upper':[23,21]},
    }
    routes={
      'W1':['short_road','lower_road','depot_upper','boat_lane_inner','east_lane'],
      'W2':['lookout_path','orchard_assembly_route','central_route','short_forest_route','long_forest_route'],
      'W3':['powered_cable','upper_path','debris_bypass','lower_window','rock_shelf_bypass'],
      'W4':['high_causeway','medical_landing_access','tidal_lane','pontoon_channel','outer_loop'],
      'W5':['high_landing_path','short_land_route','outer_route','sea_route','final_ferry','first_transport'],
    }
    recipes=[]
    for m in missions:
        wid=m['world_id'];n=int(m['id'][-1]);anchors=anchor_sets[wid]
        groups=m['groups'];placements=['west','east','orchard','upper','fork']
        bindings=[]
        for j,g in enumerate(groups):
            bindings.append({'group_id':g['id'],'anchor':placements[j],
                'start_node_id':g['start_node_id'],'assembly_node_id':g['assembly_node_id'],
                'pickup_node_id':g['id']+'_pickup','start_capacity':g['count'],
                'assembly_capacity':g['count'],'pickup_capacity':max(g['count'],6),
                'ground_route':g['id']+'_ground','water_route':g['id']+'_water',
                'vertical_route':g['id']+'_cable' if wid=='W3' else None})
        roles={o['target_role'] for e in m['events'] for o in e['operations']}
        for o in m['objectives']:
            roles.update(v for k,v in o['params'].items() if k.endswith('_id') and isinstance(v,str))
        roles.update(routes[wid]);roles.update(['safe_receiving','alternate_pickup','intermediate','receiving_station','receiving_dock'])
        recipes.append({'id':m['layout_recipe_id'],'mission_id':m['id'],'world_id':wid,
          'logical_grid':{'width':32,'height':24,'cell_pitch_mm':1000,'cell_area_mm2':1_000_000},
          'anchors':anchors,'group_bindings':bindings,'required_roles':sorted(roles),
          'socket_counts':{k:max(2,q+1) for k,q in m['maximum_stock'].items()},
          'receiving':{'total_permanent_capacity':m['total_residents'],'berths_per_dock':1,
                       'wide_primary':'depot','wide_secondary':'high_landing',
                       'compact_primary':'high_landing','compact_secondary':'depot'},
          'nav_defaults':{'ground_edge_slots':2,'water_edge_slots':2,'dock_berths':1,
                          'start_to_assembly_ticks':20,'assembly_to_pickup_ticks':20,
                          'short_ground_ticks':300,'long_ground_ticks':600,
                          'water_leg_ticks':200,'vertical_leg_ticks':50,
                          'step_free_required':True},
          'terrain_profile':{'channel_elevation_mm':0,'low_assembly_elevation_mm':100,
                             'road_elevation_mm':300,'high_ground_elevation_mm':1200,
                             'default_cell_volume_mm3':0,'default_conductance_mm2_per_s':100000,
                             'outlet_capacity_mm3_per_s':80000000,
                             'initial_source_mm3_per_s':10000000 if wid=='W1' else 0},
          'mission_distinction_source':'Resq_Production_Pack/docs/03_WORLD_MISSION_BIBLE.md#'+m['id'],
          'fixed_sources':(['dry_source_2_capacity'] if m['id'] in ('W2-M4','W4-M1') else []),
          'resource_stores':({'tank':600_000_000,'pond':1_200_000_000} if wid=='W2' else ({'coolant':max(2,m['default_loadout'].get('cooler',0))} if wid=='W5' else {})),
          'topology_contract':{
             'alternative_rescue_corridors':2,'alternative_hazard_destinations':2 if wid in ('W1','W4') else 0,
             'no_input_auto_dispatch':False,'blocked_short_route_has_safe_bypass':True,
             'fixed_bus':m['id']=='W1-M1','room_dependent_dock_configuration':True,
             'safe_receiving_reachable_for_each_spawn':True,
             'runtime_generated_puzzles':False},
          'authoring_status':'recipe_requires_authored_runtime_geometry_and_solvability_proof',
        })
    write('specification/content/layout_recipes.json',{'schema_version':1,'freeze_id':FREEZE,
      'layout_classes':[{'id':'wide','board_width_mm':720,'board_depth_mm':540,'usable_width_mm':640,'usable_depth_mm':480},
                        {'id':'compact','board_width_mm':600,'board_depth_mm':450,'usable_width_mm_numerator':1600,'usable_depth_mm_numerator':1200,'denominator':3}],
      'recipes':recipes})


def baseline():
    write('specification/baseline_manifest.json',{'schema_version':1,'root':'Resq_Production_Pack',
      'files':[{'path':str(p.relative_to(PACK)),'sha256':hashlib.sha256(p.read_bytes()).hexdigest()}
               for p in sorted(PACK.rglob('*')) if p.is_file() and '__pycache__' not in p.parts and p.name!='.DS_Store']})


if __name__=='__main__':
    missions=build_missions()
    build_equipment()
    build_modes(missions)
    build_layouts(missions)
    if not (ROOT/'specification/baseline_manifest.json').exists():baseline()
    print('Authored frozen planning data for 25 missions; no runtime content or test results created.')
