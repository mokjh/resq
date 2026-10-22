#!/usr/bin/env python3
"""Author strict evidence schemas. These define future reports, not results."""
from pathlib import Path
import json

ROOT=Path(__file__).resolve().parents[1]
HEADER={'$schema':'https://json-schema.org/draft/2020-12/schema'}
TEXT={'type':'string','minLength':1}
HASH={'type':'string','pattern':'^[0-9a-f]{64}$'}
PATH={'type':'string','minLength':1,'pattern':'^(?!/)(?!.*(?:^|/)\\.\\.(?:/|$)).+$'}
INT={'type':'integer','minimum':0}


def obj(properties,required=None):
    return {'type':'object','properties':properties,'required':required or list(properties),'additionalProperties':False}


def arr(item,minimum=0):return {'type':'array','items':item,'minItems':minimum}


def write(name,data):
    p=ROOT/'specification/schemas'/name;p.parent.mkdir(parents=True,exist_ok=True)
    p.write_text(json.dumps(dict(HEADER,**data),indent=2)+'\n')


def inferred(values):
    """Closed wire-shape schemas for the authored planning data, not runtime DTOs."""
    if not values:return {}
    groups={}
    for value in values:groups.setdefault(type(value),[]).append(value)
    if len(groups)>1:return {'anyOf':[inferred(v) for v in groups.values()]}
    typ=next(iter(groups))
    if typ is dict:
        keys=set().union(*(set(v) for v in values))
        required=set.intersection(*(set(v) for v in values))
        return {'type':'object','properties':{k:inferred([v[k] for v in values if k in v]) for k in sorted(keys)},'required':sorted(required),'additionalProperties':False}
    if typ is list:return {'type':'array','items':inferred([x for v in values for x in v])}
    return {'type':{str:'string',int:'integer',float:'number',bool:'boolean',type(None):'null'}[typ]}


def main():
    data_schemas={'mission_contracts.json':'mission_contracts.schema.json','layout_recipes.json':'layout_recipes.schema.json',
      'equipment_rules.json':'equipment_rules.schema.json','modes_and_challenges.json':'modes_and_challenges.schema.json',
      'production_inventory.json':'production_inventory.schema.json','acceptance_cases.json':'acceptance_cases.schema.json'}
    for filename,schema_name in data_schemas.items():
        data=json.loads((ROOT/'specification/content'/filename).read_text());s=inferred([data])
        if filename=='mission_contracts.json':
            variants={}
            for m in data['missions']:
                for o in m['objectives']:
                    key=(o['kind'],o['params'].get('metric'),tuple(sorted(o['params'])))
                    variants.setdefault(key,[]).append(o)
            leaves=[]
            for (kind,metric,_),values in variants.items():
                leaf=inferred(values);leaf['properties']['kind']={'const':kind}
                if metric:leaf['properties']['params']['properties']['metric']={'const':metric}
                temps={}
                for v in values:temps.setdefault(v['temporal']['kind'],[]).append(v['temporal'])
                temporal=[]
                for name,rows in temps.items():
                    shape=inferred(rows);shape['properties']['kind']={'const':name};temporal.append(shape)
                leaf['properties']['temporal']={'oneOf':temporal};leaves.append(leaf)
            s['properties']['missions']['items']['properties']['objectives']['items']={'oneOf':leaves}
            operations={
              'set_source_rate':(0,1000000000),'warn_route':(1,1000000),'set_edge_enabled':(0,1),
              'set_current_multiplier':(500,2000),'release_group':(1,1),'ignite_region':(0,1000),
              'set_wind':(0,8),'set_region_cooling':(0,100),'warn_resource':(0,1000),'add_heat':(0,1000),
              'warn_wind_change':(1,1000000),'set_stability_loss':(0,100),'set_support_load':(0,1000),
              'tremor':(0,1000),'warn_runout':(1,1000000),'release_avalanche':(1,1),
              'warn_support':(1,1000000),'set_tide_segment':(0,3),'set_infrastructure_demand':(0,32),
              'advance_lava':(1,1),'enable_cooler_socket':(0,1),'warn_reheat':(1,1000000),'enable_shelter_hazard':(0,1)}
            events=s['properties']['missions']['items']['properties']['events']['items']['properties']
            events['at_scenario_tick']={'type':'integer','minimum':1,'maximum':1000000}
            events['warning_at_scenario_tick']=INT
            events['operations']['items']={'oneOf':[obj({'kind':{'const':k},'target_role':TEXT,
              'value':{'type':'integer','minimum':bounds[0],'maximum':bounds[1]}}) for k,bounds in operations.items()]}
        write(schema_name,s)
    for filename,schema_name in [('tasks.json','task_graph.schema.json'),('checks.json','check_registry.schema.json'),('gates.json','gates.schema.json')]:
        s=inferred([json.loads((ROOT/'automation'/filename).read_text())])
        if filename=='gates.json':
            props=s['properties']['gates']['items']['properties']
            props.update(status={'enum':['not_verified','passed','blocked','expired']},
              evidence_path={'anyOf':[PATH,{'type':'null'}]},evidence_sha256={'anyOf':[HASH,{'type':'null'}]})
        write(schema_name,s)
    artifact=obj({'path':PATH,'sha256':HASH,'kind':TEXT,'byte_length':INT})
    identity=obj({'freeze_id':{'const':'resq-spec-freeze-1.1.0'},'rules_version':{'const':'resq-rules-1'},
       'source_identity':{'type':'string','pattern':'^sha256:[0-9a-f]{64}$'},'content_hash':HASH,'toolchain_hash':HASH,
       'source_manifest':artifact,'content_manifest':artifact,'toolchain_lock':artifact,
       'git_head':{'anyOf':[{'type':'string','pattern':'^[0-9a-f]{40}$'},{'type':'null'}]},'dirty':{'type':'boolean'},
       'build_sha256':{'anyOf':[HASH,{'type':'null'}]}})
    task_ref=obj({'task_id':TEXT,'result_path':PATH,'result_sha256':HASH})
    gate_ref=obj({'gate_id':TEXT,'evidence_path':PATH,'evidence_sha256':HASH})
    check=obj({'check_id':TEXT,'status':{'enum':['passed','failed','blocked','not_run']},
       'exit_code':{'anyOf':[{'type':'integer'},{'type':'null'}]},'report_path':PATH,
       'report_sha256':HASH,'started_utc':TEXT,'finished_utc':TEXT})
    device=obj({'record_type':{'const':'physical_device'},'model':{'enum':['quest3','quest3s']},
       'os_version':TEXT,'local_device_alias':TEXT,'build_sha256':HASH,'session_id':TEXT,
       'capture_path':PATH,'capture_sha256':HASH,'observed_utc':TEXT})
    human=obj({'record_type':{'const':'human_observation'},'observer_kind':{'const':'human'},
       'observer_alias':TEXT,'participant_alias':TEXT,'consent_record_path':PATH,'consent_record_sha256':HASH,
       'build_sha256':HASH,'observations_path':PATH,'observations_sha256':HASH,'observed_utc':TEXT})
    signoff=obj({'record_type':{'const':'human_signoff'},'reviewer_kind':{'const':'human'},'reviewer_alias':TEXT,
       'reviewed_artifact_hashes':arr(HASH,1),'decision':{'const':'accepted'},'review_record_path':PATH,'review_record_sha256':HASH,'signed_utc':TEXT})
    external=obj({'record_type':{'const':'external_verified'},'destination':TEXT,
       'resource_id':TEXT,'verified_url':{'type':'string','pattern':'^https://'},
       'authorization_record_path':PATH,'authorization_record_sha256':HASH,
       'verification_path':PATH,'verification_sha256':HASH})
    result=obj({'schema_version':{'const':2},'task_id':TEXT,'status':{'enum':['running','blocked','failed','passed']},
       'evidence_kind':{'enum':['automated','physical_device','human_observation','human_signoff','external_verified']},
       'identity':identity,'prerequisite_results':arr(task_ref),'gate_results':arr(gate_ref),
       'output_bindings':arr(obj({'expected_path':PATH,'artifact_path':PATH})),
       'artifacts':arr(artifact),'checks':arr(check),
       'proof_records':arr({'oneOf':[device,human,signoff,external]}),
       'costs':arr(obj({'provider':TEXT,'credits':{'type':'number','minimum':0},'usd':{'type':'number','minimum':0},'operation_id':TEXT})),
       'limitations':arr(TEXT),'blockers':arr(TEXT)})
    result['allOf']=[
      {'if':{'properties':{'status':{'const':'passed'}}},'then':{
        'properties':{'artifacts':{'minItems':1},'checks':{'minItems':1,'items':{
           'properties':{'status':{'const':'passed'},'exit_code':{'const':0}}}},'blockers':{'maxItems':0}}}},
      *[{'if':{'properties':{'status':{'const':'passed'},'evidence_kind':{'const':kind}}},
         'then':{'properties':{'proof_records':{'minItems':1,'items':{'properties':{'record_type':{'const':kind}}}}}}}
        for kind in ('physical_device','human_observation','human_signoff','external_verified')]]
    write('task_result.schema.json',result)
    nullable_artifact={'anyOf':[artifact,{'type':'null'}]}
    asset=obj({'asset_id':TEXT,'world_id':{'enum':['shared','W1','W2','W3','W4','W5']},
       'status':{'enum':['planned','source_created','imported','qualified','quarantined']},
       'source':nullable_artifact,'export':nullable_artifact,'rights_status':{'enum':['not_verified','verified','rejected']},
       'rights_record':nullable_artifact,'statistics':obj({'triangles_lod0':INT,'material_count':INT,'maximum_texture_size':INT}),
       'verification':obj({x:{'type':'boolean'} for x in ('units','pivot','axes','materials','textures','colliders','lods','rig_or_not_applicable','unity_import','render_inspected')}),
       'unity_render':nullable_artifact,'unity_import_report':nullable_artifact,
       'machine_review':{'enum':['not_verified','pass','fail']},'human_review':{'enum':['not_verified','pass','fail']},
       'human_review_record':nullable_artifact,'budget_exception':nullable_artifact,'limitations':arr(TEXT)})
    asset['allOf']=[{'if':{'properties':{'status':{'const':'qualified'}}},'then':{'properties':{
      'rights_status':{'const':'verified'},'machine_review':{'const':'pass'},'human_review':{'enum':['not_verified','pass']},
      **{x:artifact for x in ('source','export','rights_record','unity_render','unity_import_report')},
      'verification':{'properties':{x:{'const':True} for x in asset['properties']['verification']['properties']}}}}},
      {'if':{'properties':{'human_review':{'const':'pass'}}},'then':{'properties':{'human_review_record':artifact}}}]
    write('asset_manifest.schema.json',obj({'schema_version':{'const':2},'freeze_id':{'const':'resq-spec-freeze-1.1.0'},'assets':arr(asset)}))
    release=obj({'schema_version':{'const':2},'status':{'enum':['candidate','qualified','released']},
      'scope':{'enum':['world','competition','full']},'edition':{'enum':['game_only','delivery_bundle']},'semantic_version':{'type':'string','pattern':'^[0-9]+\\.[0-9]+\\.[0-9]+(?:-[a-z0-9.-]+)?$'},
      'platform_version_code':{'type':'integer','minimum':1},'source_commit':{'anyOf':[{'type':'string','pattern':'^[0-9a-f]{40}$'},{'type':'null'}]},'source_snapshot':artifact,
      'rules_version':TEXT,'toolchain_hash':HASH,'content_hash':HASH,'asset_manifest_hash':HASH,
      'apk':artifact,'signing_certificate_sha256':HASH,'implemented_missions':arr(TEXT,1),'qualified_missions':arr(TEXT),
      'qualified_devices':arr({'enum':['quest3','quest3s']}),'verified_features':arr(TEXT),
      'evidence':arr(artifact,1),'qualification_tasks':arr(task_ref),'known_limitations':arr(TEXT),'open_issues':arr(obj({'id':TEXT,'severity':{'enum':['P0','P1','P2','P3']},'description':TEXT})),
      'capture_compatibility':obj({'build_sha256':HASH,'rules_version':TEXT,'content_hash':HASH}),
      'publication_records':arr(artifact)})
    release['allOf']=[{'if':{'properties':{'status':{'enum':['qualified','released']}}},'then':{
      'properties':{'qualified_missions':{'minItems':5},'qualified_devices':{'minItems':2,'uniqueItems':True},
       'open_issues':{'items':{'properties':{'severity':{'enum':['P3']}}}}}}},
      {'if':{'properties':{'status':{'const':'released'}}},'then':{'properties':{'publication_records':{'minItems':1}}}}]
    write('release_manifest.schema.json',release)
    scalar={'anyOf':[TEXT,{'type':'integer'},{'type':'boolean'}]}
    case_record=obj({'case_id':TEXT,'kind':TEXT,'status':{'enum':['passed','failed','blocked','not_run','skipped']},
      'dimensions':{'type':'object','additionalProperties':scalar},
      'metrics':{'type':'object','additionalProperties':{'type':'number'}},
      'participant_alias':{'anyOf':[TEXT,{'type':'null'}]},
      'proof_index':{'anyOf':[INT,{'type':'null'}]},'evidence':artifact})
    check_report=obj({'schema_version':{'const':1},'check_id':TEXT,
      'status':{'enum':['passed','failed','blocked','not_run']},'exit_code':{'type':'integer'},
      'evidence_kind':{'enum':['automated','physical_device','human_observation','human_signoff','external_verified']},
      'identity':identity,'command':arr(TEXT,1),
      'coverage':{'type':'object','additionalProperties':{'anyOf':[{'type':'number'},{'type':'array','items':{'anyOf':[TEXT,{'type':'integer'}]}}]}},
      'execution_context':obj({'run_id':TEXT,'attempt_id':TEXT,'task_id':TEXT,'started_utc':TEXT,'finished_utc':TEXT,'actor':TEXT,'cwd':TEXT,'stdout':artifact,'stderr':artifact}),
      'runner':obj({'executed':{'type':'boolean'},'exit_code':{'type':'integer'},'native_report':nullable_artifact,'executed_count':INT,'failed_count':INT,'required_skipped_count':INT}),
      'assertions':arr(obj({'id':TEXT,'status':{'enum':['passed','failed','blocked','not_run']},'evidence_path':PATH,'evidence_sha256':HASH})),
      'case_records':arr(case_record),
      'artifacts':arr(artifact),'proof_records':arr({'oneOf':[device,human,signoff,external]}),'limitations':arr(TEXT)})
    check_report['allOf']=[{'if':{'properties':{'status':{'const':'passed'}}},'then':{'properties':{
      'exit_code':{'const':0},'runner':{'properties':{'executed':{'const':True},'exit_code':{'const':0},'native_report':artifact,'executed_count':{'minimum':1},'failed_count':{'const':0},'required_skipped_count':{'const':0}}},
      'case_records':{'minItems':1,'items':{'properties':{'status':{'const':'passed'}}}},
      'assertions':{'minItems':1,'items':{'properties':{'status':{'const':'passed'}}}}}}}]
    write('check_report.schema.json',check_report)
    write('authorization.schema.json',obj({'schema_version':{'const':1},'action':TEXT,'provider':TEXT,'destination':TEXT,
      'authorized_by':{'const':'user'},'user_instruction_reference':TEXT,'recorded_utc':TEXT,
      'maximum_credits':{'type':'number','minimum':0},'maximum_usd':{'type':'number','minimum':0},
      'allowed_artifact_hashes':arr(HASH),'expires_utc':{'anyOf':[TEXT,{'type':'null'}]}}))
    write('gate_evidence.schema.json',obj({'schema_version':{'const':1},'gate_id':TEXT,'status':{'const':'passed'},
      'type':{'enum':['account','machine','permission','hardware','human','external','paid']},
      'reviewer_kind':{'enum':['human','machine']},'reviewer_alias':TEXT,'observed_utc':TEXT,
      'criteria':{'type':'object','additionalProperties':{'const':True}},'evidence':arr(artifact,1),
      'authorization_path':{'anyOf':[PATH,{'type':'null'}]}}))
    print('Authored strict task, asset, check, authorization and release contracts; no claims recorded.')


if __name__=='__main__':main()
