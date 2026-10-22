"""Frozen case identities and coverage reducers; does not run any acceptance case."""
from itertools import product


def case_contract(check):
    tid=check['task_id'];cid=check['id'];cases=[];reducers={}
    def add(kind,dimensions,minimum_metrics=None,maximum_metrics=None):
        # Dimensions are part of the immutable ID, not arbitrary wrapper labels.
        key='|'.join(f'{k}={v}' for k,v in sorted(dimensions.items()))
        cases.append({'case_id':cid+'/'+kind+'/'+key,'kind':kind,'dimensions':dimensions,
                      'minimum_metrics':minimum_metrics or {},'maximum_metrics':maximum_metrics or {}})
    def reduce(key,operation,kind=None,fields=(),metric=None,minimum=None,group_by=()):
        reducers[key]={'operation':operation,'kind':kind,'fields':list(fields),
                       'metric':metric,'metric_minimum':minimum,'group_by':list(group_by)}
    mission=tid[:5] if tid.startswith('W') and '-M' in tid else None
    if mission and tid.endswith('-AUTHOR'):
        for layout in ('compact','wide'):add('layout',{'layout':layout})
        reduce('layout_count','count','layout')
    replay=(mission and tid.endswith(('-REPLAY','-QUALIFY'))) or tid.endswith('-REGRESSION') or tid=='FULL01'
    if replay:
        worlds=range(1,int(tid[1])+1) if tid.endswith('-REGRESSION') else range(1,6)
        mids=[mission] if mission else [f'W{w}-M{m}' for w in worlds for m in range(1,6)]
        targets=('host',) if tid.endswith('-REPLAY') else ('host','quest3','quest3s')
        for mid,layout,profile,strategy,target,repeat in product(mids,('compact','wide'),('standard','relaxed','command'),('A','B'),targets,(1,2)):
            add('base',{'mission':mid,'layout':layout,'profile':profile,'strategy':strategy,'target':target,'repeat':repeat},{'rescued_minimum_met':1})
        for mid in mids:
            challenge_ids=('early_surge','alternate_dock') if mid.startswith('W1-') else ({'W2':'crosswind','W3':'careful_operation','W4':'high_tide_start','W5':'careful_operation'}[mid[:2]],)
            for challenge,layout,strategy,target,repeat in product(challenge_ids,('compact','wide'),('A','B'),targets,(1,2)):
                add('challenge',{'mission':mid,'challenge':challenge,'layout':layout,'profile':'standard','strategy':strategy,'target':target,'repeat':repeat},{'rescued_minimum_met':1})
        for mid,layout,profile,negative,target in product(mids,('compact','wide'),('standard','relaxed','command'),('no_input','major_mistake','save_resume','tracking_recovery','anchor_recovery'),targets):
            add('negative_recovery',{'mission':mid,'layout':layout,'profile':profile,'case':negative,'target':target})
        reduce('base_success_configurations','distinct_count','base',('mission','layout','profile','strategy'))
        reduce('challenge_success_configurations','distinct_count','challenge',('mission','challenge','layout','strategy'))
        reduce('target_runtimes','set','base',('target',))
        reduce('repeats_per_target','minimum_group_count','base',('repeat',),group_by=('mission','layout','profile','strategy','target'))
        reduce('three_star_configurations','distinct_count','base',('mission','layout','profile'),metric='stars',minimum=3)
        reduce('mission_count','distinct_count','base',('mission',))
    device=tid=='FULL-DEVICE' or (tid.startswith('W') and tid.endswith('-DEVICE'))
    performance=tid=='FULL-PERFORMANCE' or (tid.startswith('W') and tid.endswith('-PERFORMANCE'))
    human=tid=='FULL-HUMAN' or (tid.startswith('W') and tid.endswith('-HUMAN'))
    worlds=range(1,6) if tid.startswith('FULL') else [int(tid[1])] if (device or performance or human) else []
    if device:
        for w in worlds:
            for m,model,layout in product(range(1,6),('quest3','quest3s'),('compact','wide')):
                add('full_mission',{'world':f'W{w}','mission':f'W{w}-M{m}','model':model,'layout':layout,'input':'hands','profile':'standard','placement':'real' if layout=='wide' else 'virtual','variant':'baseline'})
            for m,model,inp in product((1,5),('quest3','quest3s'),('right_only','left_only','head_dwell')):
                add('full_mission',{'world':f'W{w}','mission':f'W{w}-M{m}','model':model,'layout':'wide' if m==1 else 'compact','input':inp,'profile':'command' if inp=='head_dwell' else 'standard','placement':'virtual' if m==1 else 'real','variant':'alternative_input'})
            for model,layout in product(('quest3','quest3s'),('compact','wide')):
                for variant in ('relaxed','opposite_placement'):
                    add('full_mission',{'world':f'W{w}','mission':f'W{w}-M5','model':model,'layout':layout,'input':'hands','profile':'relaxed' if variant=='relaxed' else 'standard','placement':'virtual' if layout=='wide' else 'real','variant':variant})
            for i,model in enumerate(('quest3','quest3s')):
                layout='wide' if (w+i)%2 else 'compact'
                add('full_mission',{'world':f'W{w}','mission':f'W{w}-M1','model':model,'layout':layout,'input':'controllers','profile':'standard','placement':'real' if layout=='wide' else 'virtual','variant':'controller'})
            for model,state,inp in product(('quest3','quest3s'),range(1,25),('hands','right_only','left_only','head_dwell','controllers')):
                add('ui_state',{'world':f'W{w}','model':model,'state':f'U{state:02}','input':inp})
            for model,phase,trial in product(('quest3','quest3s'),('hover','grab','route_confirmation','release','resume'),range(1,6)):
                add('tracking_trial',{'world':f'W{w}','model':model,'phase':phase,'trial':trial})
            rooms=('table','no_table','permission_denied','narrow','cluttered','moved_furniture','changed_lighting','recenter','different_room','near_wall')
            lifecycle=('suspend_resume','headset_removal','app_interruption','clean_launch','reinstall_preserve','reinstall_clear','corrupt_save','migration')
            for model in ('quest3','quest3s'):
                for room in rooms:add('room',{'world':f'W{w}','model':model,'room':room})
                for step in lifecycle:add('lifecycle',{'world':f'W{w}','model':model,'case':step})
                for band in ('100_300','300_700'):add('lighting',{'world':f'W{w}','model':model,'lux_band':band},{'lighting_measured':1})
                add('continuous_hands_session',{'world':f'W{w}','model':model},{'controllers_unpaired':1})
        reduce('physical_full_mission_runs','count','full_mission')
        reduce('models','set','full_mission',('model',))
        reduce('mission_count','distinct_count','full_mission',('mission',))
    if performance:
        limits={'cpu_p95_ms':10,'gpu_p95_ms':11,'simulation_p95_ms':1.5,'steady_allocation_bytes_per_frame':0,
          'missed_application_frames_percent':1,'unexplained_half_second_miss_segments':0,
          'application_memory_mib':1536,'visible_triangles':300000,'draw_calls':150,
          'resident_views':60,'gameplay_vehicles':8,'shadow_lights':1,'audio_voices':24,'native_refresh_hz':72}
        for w,model in product(worlds,('quest3','quest3s')):
            add('thermal_soak',{'world':f'W{w}','model':model},{'duration_minutes':30,'native_refresh_hz':72},limits)
            for layout,effects in product(('compact','wide'),('normal','reduced')):
                add('profile',{'world':f'W{w}','model':model,'layout':layout,'effects':effects},{'duration_minutes':5,'native_refresh_hz':72},limits)
        reduce('thermal_soaks','count','thermal_soak');reduce('five_minute_profiles','count','profile')
        reduce('thermal_minutes_per_model','minimum','thermal_soak',metric='duration_minutes')
        reduce('models','set','thermal_soak',('model',))
    if human:
        for w in worlds:
            for slot in range(1,11 if w==1 else 6):add('participant',{'world':f'W{w}','participant_slot':slot})
            for m,slot in product(range(1,6),(1,2)):
                add('mission_human',{'world':f'W{w}','mission':f'W{w}-M{m}','mission_player_slot':slot},{'first_time_mission_player':1 if slot==1 else 0})
        reduce('consenting_participants','distinct_count','participant',('participant_alias',))
        reduce('mission_count','distinct_count','mission_human',('mission',))
    if tid=='C09' or tid.endswith('-MECHANIC'):
        w='W1' if tid=='C09' else tid[:2]
        for seed in range(1000):add('fuzzer',{'world':w,'seed':seed},{'attempted_commands':1000})
        reduce('fuzzer_seeds_per_world','minimum_group_count','fuzzer',('seed',),group_by=('world',))
        reduce('attempted_commands_per_seed','minimum','fuzzer',metric='attempted_commands')
    if tid in ('D05','FULL-WEB'):
        outcomes=('home','how_to_play','worlds_index','world1','world2','world3','world4','world5','accessibility','press','support','privacy','not_found')
        for width,page in product((320,360,768,1440),outcomes):add('web_page',{'width_css_px':width,'page':page})
        reduce('widths_css_px','set','web_page',('width_css_px',));reduce('required_page_outcomes','distinct_count','web_page',('page',))
    if tid in ('D03','FULL-VIDEO'):
        for edition in ('clean','captioned'):add('video_export',{'edition':edition},{'timeline_seconds':160,'measured_duration_ms':159967},{'timeline_seconds':160,'measured_duration_ms':160033})
        for fmt in ('srt','vtt'):add('sidecar',{'format':fmt})
        reduce('timeline_seconds','minimum','video_export',metric='timeline_seconds');reduce('caption_variants','count','video_export')
        reduce('sidecar_formats','set','sidecar',('format',))
    # Every prose assertion must also have an executed, evidence-bound native case.
    for a in check['assertions']:add('assertion',{'assertion_id':a['id']})
    coverage={key:reducers[key] for key in check['expected_coverage']}
    return {'id':cid,'task_id':tid,'required_cases':cases,'coverage_derivation':coverage}


def derive_coverage(records,reducer):
    rows=[r for r in records if reducer['kind'] is None or r['kind']==reducer['kind']]
    if reducer['metric_minimum'] is not None:
        rows=[r for r in rows if r['metrics'].get(reducer['metric'],-1)>=reducer['metric_minimum']]
    def values(row,fields):
        return tuple(row['dimensions'].get(f,row.get(f)) for f in fields)
    operation=reducer['operation'];fields=reducer['fields']
    if operation=='count':return len(rows)
    if operation=='distinct_count':return len({values(r,fields) for r in rows})
    if operation=='set':return sorted({values(r,fields)[0] for r in rows})
    if operation=='minimum':return min((r['metrics'].get(reducer['metric'],-1) for r in rows),default=-1)
    if operation=='minimum_group_count':
        groups={}
        for r in rows:groups.setdefault(values(r,reducer['group_by']),set()).add(values(r,fields))
        return min((len(x) for x in groups.values()),default=0)
    raise ValueError('unknown coverage reducer: '+operation)
