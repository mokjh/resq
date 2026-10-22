#!/usr/bin/env python3
"""Validate RESQ frozen planning integrity. Does not execute game acceptance."""
from __future__ import annotations
import argparse
from collections import Counter
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
from typing import Any
import xml.etree.ElementTree as ET
from evidence_case_contracts import case_contract,derive_coverage

ROOT=Path(__file__).resolve().parents[1]
FREEZE='resq-spec-freeze-1.1.0'
RULES='resq-rules-1'
MISSION_IDS={f'W{w}-M{m}' for w in range(1,6) for m in range(1,6)}


class Invalid(ValueError):pass


def require(condition:bool,message:str):
    if not condition:raise Invalid(message)


def unique_object(pairs):
    result={}
    for k,v in pairs:
        require(k not in result,'duplicate JSON key: '+k);result[k]=v
    return result


def read(path:Path):
    try:return json.loads(path.read_text(),object_pairs_hook=unique_object,
                          parse_constant=lambda x:(_ for _ in ()).throw(Invalid('nonfinite JSON number: '+x)))
    except (OSError,json.JSONDecodeError) as exc:raise Invalid(f'{path}: {exc}') from exc


def sha(path:Path):return hashlib.sha256(path.read_bytes()).hexdigest()


def local_path(root:Path,value:str,must_exist=False):
    require(isinstance(value,str) and value and not Path(value).is_absolute(),'artifact path must be relative')
    require('..' not in Path(value).parts,'artifact path traversal')
    p=(root/value).resolve()
    require(p.is_relative_to(root.resolve()),'artifact symlink escapes root')
    if must_exist:require(p.is_file(),'missing artifact: '+value)
    return p


def index(rows,key='id'):
    require(isinstance(rows,list),'expected rows array')
    result={}
    for r in rows:
        require(isinstance(r,dict) and isinstance(r.get(key),str) and r[key],'missing row ID')
        require(r[key] not in result,'duplicate row ID: '+r[key]);result[r[key]]=r
    return result


def schema_check(value:Any,schema:dict,where='$'):
    """Focused validator for every assertion keyword used by our closed schemas.

    This is not a general JSON Schema engine. Unsupported assertion keywords fail
    rather than being silently ignored. Metadata annotations do not assert values.
    """
    supported={'$schema','$id','title','description','type','const','enum','properties','required',
      'additionalProperties','items','minItems','maxItems','uniqueItems','minLength','maxLength',
      'minimum','maximum','pattern','allOf','anyOf','oneOf','if','then','else'}
    require(not(set(schema)-supported),f'{where}: unsupported schema keywords {set(schema)-supported}')
    def valid(s):
        try:schema_check(value,s,where);return True
        except Invalid:return False
    if 'type' in schema:
        t=schema['type'];checks={'object':isinstance(value,dict),'array':isinstance(value,list),
          'string':isinstance(value,str),'integer':type(value)is int,'number':type(value)in(int,float),
          'boolean':type(value)is bool,'null':value is None}
        require(checks.get(t,False),f'{where}: expected {t}')
    if 'const' in schema:require(value==schema['const'] and type(value)is type(schema['const']),f'{where}: const mismatch')
    if 'enum' in schema:require(any(value==x and type(value)is type(x) for x in schema['enum']),f'{where}: enum mismatch')
    for s in schema.get('allOf',[]):schema_check(value,s,where)
    if 'anyOf' in schema:require(any(valid(s) for s in schema['anyOf']),f'{where}: no anyOf match')
    if 'oneOf' in schema:require(sum(valid(s) for s in schema['oneOf'])==1,f'{where}: not exactly oneOf')
    if 'if' in schema:
        branch='then' if valid(schema['if']) else 'else'
        if branch in schema:schema_check(value,schema[branch],where)
    if isinstance(value,dict):
        require(set(schema.get('required',[]))<=set(value),f'{where}: missing required fields')
        props=schema.get('properties',{})
        if schema.get('additionalProperties') is False:require(set(value)<=set(props),f'{where}: extra fields {set(value)-set(props)}')
        for k,v in value.items():
            if k in props:schema_check(v,props[k],where+'.'+k)
            elif isinstance(schema.get('additionalProperties'),dict):schema_check(v,schema['additionalProperties'],where+'.'+k)
    if isinstance(value,list):
        require(len(value)>=schema.get('minItems',0),f'{where}: minItems')
        require(len(value)<=schema.get('maxItems',len(value)),f'{where}: maxItems')
        if schema.get('uniqueItems'):require(len({json.dumps(x,sort_keys=True) for x in value})==len(value),f'{where}: duplicate items')
        if 'items' in schema:
            for n,v in enumerate(value):schema_check(v,schema['items'],f'{where}[{n}]')
    if isinstance(value,str):
        require(len(value)>=schema.get('minLength',0),f'{where}: minLength')
        require(len(value)<=schema.get('maxLength',len(value)),f'{where}: maxLength')
        if 'pattern' in schema:require(re.search(schema['pattern'],value) is not None,f'{where}: pattern mismatch')
    if type(value)in(int,float):
        if 'minimum' in schema:require(value>=schema['minimum'],f'{where}: below minimum')
        if 'maximum' in schema:require(value<=schema['maximum'],f'{where}: above maximum')


def artifact(root,record,allow_empty=False):
    p=local_path(root,record['path'],True)
    require(sha(p)==record['sha256'],'artifact hash mismatch: '+record['path'])
    if 'byte_length' in record:require(p.stat().st_size==record['byte_length'],'artifact byte length mismatch')
    require(allow_empty or p.stat().st_size>0,'empty artifact: '+record['path'])
    return p


def validate_identity(root,identity):
    source=artifact(root,identity['source_manifest']);content=artifact(root,identity['content_manifest'])
    artifact(root,identity['toolchain_lock'])
    require(identity['source_identity']=='sha256:'+identity['source_manifest']['sha256'],'source identity mismatch')
    require(identity['toolchain_hash']==identity['toolchain_lock']['sha256'],'toolchain identity mismatch')
    content_data=read(content)
    require(content_data.get('payload_sha256')==identity['content_hash'],'content identity mismatch')
    artifact(root,{'path':content_data['payload_path'],'sha256':identity['content_hash']})
    for path in (source,content):
        manifest=read(path);require(manifest.get('files'),'empty identity manifest')
        seen=set()
        for entry in manifest['files']:
            require(entry['path'] not in seen,'duplicate identity manifest entry');seen.add(entry['path'])
            require(local_path(root,entry['path'])!=path.resolve(),'recursive identity manifest')
            artifact(root,entry)


def validate_native(path,runner):
    if path.suffix=='.xml':
        try:node=ET.fromstring(path.read_text())
        except ET.ParseError as exc:raise Invalid('invalid native XML') from exc
        require(node.tag in ('test-run','test-results'),'unknown native XML report')
        total=int(node.attrib.get('total',node.attrib.get('testcasecount','0')))
        failed=int(node.attrib.get('failed',node.attrib.get('failures','0')))
        skipped=int(node.attrib.get('skipped',node.attrib.get('ignored','0')))
        require(total>0 and failed==0 and skipped==0,'native XML has zero/failed/skipped tests')
        require(runner['executed_count']==total and runner['failed_count']==failed,'native XML count mismatch')
        rows=node.findall('.//test-case')
        passed={x.attrib.get('fullname',x.attrib.get('name')) for x in rows if x.attrib.get('result','').lower() in ('passed','success') or x.attrib.get('success')=='True'}
        return passed
    elif path.suffix=='.json':
        native=read(path)
        require(native.get('status')=='passed','native JSON did not pass')
        for key in ('executed_count','failed_count','required_skipped_count'):
            require(type(native.get(key))is int and native[key]==runner[key],'native JSON count mismatch: '+key)
        require(native['executed_count']>0 and native['failed_count']==0 and native['required_skipped_count']==0,'native JSON failures/skips')
        if 'build_result' in native:require(native['build_result']=='Succeeded' and native.get('build_errors')==0,'native BuildReport failed')
        rows=index(native.get('case_records',[]),'case_id')
        require(rows and all(r.get('status')=='passed' for r in rows.values()),'native missing/failed individual cases')
        require(len(rows)<=native['executed_count'],'native executed count below case count')
        return set(rows)
    else:raise Invalid('native report must use parsed XML or canonical native-summary JSON')


def validate_gate(root,gid,record,require_current=True):
    gates=index(read(root/'automation/gates.json')['gates']);g=gates[gid]
    if require_current:
        require(g['status']=='passed','prerequisite gate not passed: '+gid)
        require(record['evidence_path']==g['evidence_path'] and record['evidence_sha256']==g['evidence_sha256'],'gate head mismatch: '+gid)
    p=artifact(root,{'path':record['evidence_path'],'sha256':record['evidence_sha256']})
    evidence=read(p);schema_check(evidence,read(root/'specification/schemas/gate_evidence.schema.json'))
    require(evidence['gate_id']==gid and evidence['type']==g['kind'],'gate evidence identity/type mismatch')
    require(evidence['criteria']=={'pass_condition':True},'gate criteria mismatch')
    if g['kind'] in ('human','permission','external','paid'):require(evidence['reviewer_kind']=='human','gate needs actual human review')
    for a in evidence['evidence']:artifact(root,a)
    authorization=evidence['authorization_path']
    if g['kind'] in ('permission','external','paid'):require(authorization is not None,'gate lacks action authorization')
    if authorization is not None:
        require(authorization in {a['path'] for a in evidence['evidence']},'gate authorization not hash bound')
        observed=datetime.fromisoformat(evidence['observed_utc'].replace('Z','+00:00'))
        require(observed.tzinfo is not None,'gate observation lacks timezone')
        auth=read(local_path(root,authorization,True))
        validate_authorization(root,auth,None if require_current else observed)
        require(not g['authorization_actions'] or auth['action'] in g['authorization_actions'],'gate authorization action scope mismatch')


def validate_authorization(root,data,at_time=None):
    schema_check(data,read(root/'specification/schemas/authorization.schema.json'))
    try:
        recorded=datetime.fromisoformat(data['recorded_utc'].replace('Z','+00:00'))
        check_time=at_time or datetime.now(timezone.utc)
        require(recorded.tzinfo is not None and recorded<=check_time,'invalid authorization time')
        if data['expires_utc'] is not None:
            expiry=datetime.fromisoformat(data['expires_utc'].replace('Z','+00:00'))
            require(expiry.tzinfo is not None and expiry>check_time,'expired authorization')
    except ValueError as exc:raise Invalid('invalid authorization timestamps') from exc


def validate_result_ref(root,record,stack,cache):
    state=read(root/'automation/task_state.json')['task_states']
    tid=record['task_id'];require(tid in state,'unknown prerequisite result')
    require(state[tid]['status']=='passed','prerequisite task not passed: '+tid)
    require(state[tid]['result_path']==record['result_path'] and state[tid]['result_sha256']==record['result_sha256'],'prerequisite head mismatch')
    p=artifact(root,{'path':record['result_path'],'sha256':record['result_sha256']})
    result=read(p)
    require(result.get('status')=='passed','referenced task did not pass')
    validate_task_result(root,result,expected_task=tid,stack=stack,cache=cache,verify_current_gates=False)
    return result


def validate_cases(root,report,check,native_ids):
    contracts=index(read(root/'specification/content/acceptance_cases.json')['contracts'])
    contract=contracts[check['case_contract_id']]
    expected=index(contract['required_cases'],'case_id');actual=index(report['case_records'],'case_id')
    require(set(actual)==set(expected),'required case set mismatch')
    require(set(actual)<=native_ids,'case not executed by native runner')
    kind=report['evidence_kind'];proofs=report['proof_records']
    for cid,row in actual.items():
        e=expected[cid]
        require(row['kind']==e['kind'] and row['dimensions']==e['dimensions'],'case dimensions mismatch')
        for metric,minimum in e['minimum_metrics'].items():
            require(type(row['metrics'].get(metric)) in (int,float) and row['metrics'][metric]>=minimum,'case metric below minimum: '+metric)
        for metric,maximum in e['maximum_metrics'].items():
            require(type(row['metrics'].get(metric)) in (int,float) and 0<=row['metrics'][metric]<=maximum,'case metric above maximum/out of bounds: '+metric)
        p=artifact(root,row['evidence']);native_case=read(p)
        require(native_case.get('case_id')==cid and native_case.get('status')=='passed','case evidence identity/status mismatch')
        for field in ('kind','dimensions','metrics','participant_alias','proof_index'):
            require(native_case.get(field)==row[field],'case evidence differs from wrapper: '+field)
        require(native_case.get('identity')==report['identity'],'case evidence stale identity')
        if kind!='automated':
            n=row['proof_index'];require(type(n)is int and 0<=n<len(proofs),'case missing genuine proof reference')
            proof=proofs[n]
            require(proof['record_type']==kind,'case wrong proof type')
            if kind=='physical_device':
                if 'model' in row['dimensions']:require(proof['model']==row['dimensions']['model'],'case wrong device model')
            if kind=='human_observation':require(row['participant_alias']==proof['participant_alias'],'case wrong participant')
        else:require(row['proof_index'] is None,'automated case cannot claim nonautomated proof')
    for key,reducer in contract['coverage_derivation'].items():
        value=derive_coverage(list(actual.values()),reducer)
        require(report['coverage'][key]==value,'coverage not derived from individual cases: '+key)
    # Distinct slots require distinct consented people, not duplicated aliases.
    for world in {r['dimensions'].get('world') for r in actual.values() if r['kind']=='participant'}:
        aliases=[r['participant_alias'] for r in actual.values() if r['kind']=='participant' and r['dimensions']['world']==world]
        require(None not in aliases and len(aliases)==len(set(aliases)),'duplicated/missing participant alias')
    for mission in {r['dimensions'].get('mission') for r in actual.values() if r['kind']=='mission_human'}:
        aliases=[r['participant_alias'] for r in actual.values() if r['kind']=='mission_human' and r['dimensions']['mission']==mission]
        require(None not in aliases and len(set(aliases))>=2,'mission lacks two distinct humans')
    if kind=='physical_device':
        sessions=[proofs[r['proof_index']]['session_id'] for r in actual.values() if r['kind'] in ('full_mission','thermal_soak','profile')]
        require(len(sessions)==len(set(sessions)),'distinct physical runs reuse a session proof')
    assertions=index(report['assertions'])
    for row in actual.values():
        if row['kind']=='assertion':
            a=assertions[row['dimensions']['assertion_id']]
            require(a['evidence_path']==row['evidence']['path'] and a['evidence_sha256']==row['evidence']['sha256'],'assertion not bound to individual native case')


def validate_task_result(root:Path,result:dict,expected_task=None,stack=None,cache=None,verify_current_gates=True):
    schema_check(result,read(root/'specification/schemas/task_result.schema.json'))
    tasks=index(read(root/'automation/tasks.json')['tasks'])
    checks=index(read(root/'automation/checks.json')['checks'])
    require(result['task_id'] in tasks,'unknown result task')
    if expected_task is not None:require(result['task_id']==expected_task,'task result identity does not match state/reference')
    t=tasks[result['task_id']]
    expected_kind=t['required_evidence_kind']
    require(result['evidence_kind']==expected_kind,'wrong task evidence kind')
    if result['status']!='passed':return
    stack=set() if stack is None else stack;cache=set() if cache is None else cache
    key=(result['task_id'],hashlib.sha256(json.dumps(result,sort_keys=True).encode()).hexdigest(),verify_current_gates)
    require(result['task_id'] not in stack,'cyclic result evidence')
    if key in cache:return
    stack=stack|{result['task_id']}
    deps=index(result['prerequisite_results'],'task_id')
    require(set(deps)==set(t['depends_on']),'prerequisite result set mismatch')
    for ref in deps.values():validate_result_ref(root,ref,stack,cache)
    gates=index(result['gate_results'],'gate_id')
    require(set(gates)==set(t['required_gates']),'prerequisite gate set mismatch')
    for gid,ref in gates.items():validate_gate(root,gid,ref,require_current=verify_current_gates)
    validate_identity(root,result['identity'])
    actual=index(result['checks'],'check_id')
    require(set(actual)==set(t['acceptance_checks']),'required check set mismatch')
    artifacts={a['path']:a for a in result['artifacts']}
    require(len(artifacts)==len(result['artifacts']),'duplicate task artifact')
    bindings=index(result['output_bindings'],'expected_path')
    require(set(bindings)==set(t['expected_artifacts']),'missing expected task artifact binding')
    require({b['artifact_path'] for b in bindings.values()}<=set(artifacts),'missing expected task artifact')
    for a in result['artifacts']:artifact(root,a)
    attempt_identity=None
    for cid,record in actual.items():
        p=local_path(root,record['report_path'],True)
        require(sha(p)==record['report_sha256'],'check report hash mismatch')
        report=read(p)
        schema_check(report,read(root/'specification/schemas/check_report.schema.json'))
        context=report['execution_context']
        for field in ('run_id','attempt_id'):require(re.fullmatch('[a-z0-9][a-z0-9_-]{0,63}',context[field]) is not None,'unsafe run/attempt ID')
        substitutions={x:context[x] for x in ('run_id','attempt_id')}
        if attempt_identity is None:attempt_identity=substitutions
        require(substitutions==attempt_identity,'mixed task run/attempt evidence')
        require(record['report_path']==checks[cid]['report_path'].format(**substitutions),'wrong registered check report path')
        require(report['check_id']==cid and report['status']=='passed' and report['exit_code']==0,'underlying check failed')
        require(report['identity']==result['identity'],'stale check identity')
        require(report['evidence_kind']==expected_kind,'underlying evidence kind mismatch')
        require(report['command']==[x.format(**substitutions) for x in checks[cid]['command']],'underlying command mismatch')
        coverage=checks[cid]['expected_coverage']
        require(set(report['coverage'])==set(coverage),'required coverage keys mismatch')
        for key,condition in coverage.items():
            value=report['coverage'][key]
            if 'exact' in condition:require(value==condition['exact'],'coverage exact mismatch: '+key)
            if 'minimum' in condition:require(type(value)in(int,float) and value>=condition['minimum'],'coverage below minimum: '+key)
            if 'set' in condition:require(isinstance(value,list) and len(value)==len(condition['set']) and set(value)==set(condition['set']),'coverage set mismatch: '+key)
        require(context['task_id']==t['id'] and Path(context['cwd']).resolve()==root.resolve(),'wrong execution context')
        artifact(root,context['stdout'],True);artifact(root,context['stderr'],True)
        try:
            start=datetime.fromisoformat(context['started_utc'].replace('Z','+00:00'))
            end=datetime.fromisoformat(context['finished_utc'].replace('Z','+00:00'))
            require(start.tzinfo is not None and end>=start,'invalid execution timestamps')
        except ValueError as exc:raise Invalid('invalid execution timestamp') from exc
        expected_assertions={x['id'] for x in checks[cid]['assertions'] if x['required']}
        assertions=index(report['assertions'])
        require(set(assertions)==expected_assertions,'required assertion set mismatch')
        native=artifact(root,report['runner']['native_report'])
        require(native.resolve()!=p.resolve(),'check cannot use itself as native evidence')
        require(report['runner']['executed'] and report['runner']['exit_code']==0,'runner did not succeed')
        require(report['runner']['executed_count']>0 and report['runner']['failed_count']==0 and report['runner']['required_skipped_count']==0,'native counts do not pass')
        native_ids=validate_native(native,report['runner'])
        validate_cases(root,report,checks[cid],native_ids)
        for a in report['assertions']:
            require(a['status']=='passed','required assertion did not pass')
            artifact(root,{'path':a['evidence_path'],'sha256':a['evidence_sha256']})
        for a in report['artifacts']:artifact(root,a)
        if expected_kind!='automated':require(report['proof_records']==result['proof_records'],'underlying nonautomated proof mismatch')
    prefix=f'evidence/runs/{t["id"]}/{attempt_identity["run_id"]}/{attempt_identity["attempt_id"]}/artifacts/'
    for b in bindings.values():require(b['artifact_path'].startswith(prefix),'output must bind immutable per-attempt artifact copy')
    if expected_kind!='automated':
        require(result['identity']['build_sha256'] is not None or expected_kind in ('external_verified','human_signoff'),'device/human proof requires build hash')
        require(result['proof_records'],'missing nonautomated proof')
        for proof in result['proof_records']:
            require(proof['record_type']==expected_kind,'proof type mismatch')
            if expected_kind=='physical_device':
                require(proof['build_sha256']==result['identity']['build_sha256'],'device wrong build')
                artifact(root,{'path':proof['capture_path'],'sha256':proof['capture_sha256']})
            elif expected_kind=='human_observation':
                require(proof['build_sha256']==result['identity']['build_sha256'],'human wrong build')
                for prefix in ('consent_record','observations'):
                    artifact(root,{'path':proof[prefix+'_path'],'sha256':proof[prefix+'_sha256']})
            elif expected_kind=='human_signoff':
                artifact(root,{'path':proof['review_record_path'],'sha256':proof['review_record_sha256']})
                require(set(proof['reviewed_artifact_hashes'])<={a['sha256'] for a in result['artifacts']},'human signoff refers to unprovided artifact')
            else:
                for prefix in ('authorization_record','verification'):
                    artifact(root,{'path':proof[prefix+'_path'],'sha256':proof[prefix+'_sha256']})
                authorization=read(local_path(root,proof['authorization_record_path'],True))
                historical_time=max(datetime.fromisoformat(x['finished_utc'].replace('Z','+00:00')) for x in result['checks'])
                validate_authorization(root,authorization,None if verify_current_gates else historical_time)
                require(authorization['destination']==proof['destination'],'external authorization destination mismatch')
                if authorization['allowed_artifact_hashes']:
                    require(set(authorization['allowed_artifact_hashes'])<={a['sha256'] for a in result['artifacts']},'external authorization artifact scope mismatch')
        for cid in actual:
            if '--models' in checks[cid]['command']:
                models=checks[cid]['command'][checks[cid]['command'].index('--models')+1].split(',')
                require(set(models)<={x['model'] for x in result['proof_records']},'missing physical target model')
    cache.add(key)


def validate_assets(root:Path,data:dict):
    schema_check(data,read(root/'specification/schemas/asset_manifest.schema.json'))
    rows=index(data['assets'],'asset_id')
    for a in rows.values():
        for key in ('source','export','rights_record','unity_render','unity_import_report','human_review_record','budget_exception'):
            if a[key] is not None:artifact(root,a[key])


def validate_release(root:Path,data:dict):
    schema_check(data,read(root/'specification/schemas/release_manifest.schema.json'))
    implemented=set(data['implemented_missions']);qualified=set(data['qualified_missions'])
    require(len(implemented)==len(data['implemented_missions']),'duplicate implemented missions')
    require(len(qualified)==len(data['qualified_missions']),'duplicate qualified missions')
    require(qualified<=implemented<=MISSION_IDS,'unknown or unimplemented qualified mission')
    if data['scope']=='full':require(implemented==MISSION_IDS,'full release must implement all25')
    if data['scope']=='competition':require(implemented=={f'W1-M{n}' for n in range(1,6)},'competition must contain fiveW1missions')
    if data['scope']=='world':
        worlds={x.split('-')[0] for x in implemented}
        require(len(worlds)==1 and len(implemented)==5,'world release requires one complete world')
    if data['status'] in ('qualified','released'):
        require(qualified==implemented,'qualified release contains unqualified mission')
        require(set(data['qualified_devices'])=={'quest3','quest3s'},'both physical targets required')
    artifact(root,data['apk'])
    artifact(root,data['source_snapshot'])
    require(data['capture_compatibility']['build_sha256']==data['apk']['sha256'],'capture wrong build')
    require(data['capture_compatibility']['content_hash']==data['content_hash'],'capture wrong content')
    require(data['capture_compatibility']['rules_version']==data['rules_version'],'capture wrong rules')
    for a in data['evidence']+data['publication_records']:artifact(root,a)
    refs=index(data['qualification_tasks'],'task_id')
    if data['status'] in ('qualified','released'):
        if data['scope']=='full':
            required={'FULL01','FULL-PERFORMANCE','FULL-DEVICE','FULL-HUMAN'}
            if data['edition']=='delivery_bundle':required|={'FULL-QUALIFY','FULL-VIDEO-REVIEW','FULL-WEB'}
        else:
            wid='W1' if data['scope']=='competition' else next(iter(implemented)).split('-')[0]
            required={wid+'-'+x for x in ('REGRESSION','PERFORMANCE','DEVICE','HUMAN')}
            if data['scope']=='competition':required|={'W1-FREEZE','D03-REVIEW','D05'}
        require(set(refs)==required,'release qualification task set mismatch')
    for ref in refs.values():
        result=validate_result_ref(root,ref,set(),set())
        if ref['task_id'].endswith(('-REGRESSION','-PERFORMANCE','-DEVICE','-HUMAN')) or ref['task_id']=='FULL01':
            identity=result['identity']
            require(identity['build_sha256']==data['apk']['sha256'],'release proof uses different APK')
            require(identity['content_hash']==data['content_hash'] and identity['rules_version']==data['rules_version'] and identity['toolchain_hash']==data['toolchain_hash'],'release qualification identity mismatch')


def validate(root:Path,verify_bundle=True):
    root=root.resolve()
    required=['AGENTS.md','BUILD_PLAN.md','specification/00_FREEZE_INDEX.md',
      *[f'specification/{x}' for x in ('01_SCOPE_AND_DECISIONS.md','02_RUNTIME_RULES.md','03_RUNTIME_CONTRACTS.md','04_MISSION_AUTHORING.md','05_PRODUCTION_ACCEPTANCE.md','06_RELEASE_AND_OPERATIONS.md')],
      'specification/07_EVIDENCE_AND_EXECUTION.md','specification/fixtures/runtime-golden-v1.json','automation/tasks.json','automation/checks.json','automation/gates.json','automation/task_state.json','automation/autonomy_policy.json']
    for p in required:require((root/p).is_file(),'missing required freeze file: '+p)
    for filename,schema_name in [('mission_contracts.json','mission_contracts'),('layout_recipes.json','layout_recipes'),('equipment_rules.json','equipment_rules'),('modes_and_challenges.json','modes_and_challenges'),('production_inventory.json','production_inventory'),('acceptance_cases.json','acceptance_cases')]:
        schema_check(read(root/'specification/content'/filename),read(root/'specification/schemas'/f'{schema_name}.schema.json'))
    for filename,schema_name in [('tasks.json','task_graph'),('checks.json','check_registry'),('gates.json','gates')]:
        schema_check(read(root/'automation'/filename),read(root/'specification/schemas'/f'{schema_name}.schema.json'))
    baseline=read(root/'specification/baseline_manifest.json')
    for a in baseline['files']:
        p=local_path(root,baseline['root']+'/'+a['path'],True)
        require(sha(p)==a['sha256'],'original pack changed: '+a['path'])
    require(len(baseline['files'])==41,'original baseline file count mismatch')
    graph=read(root/'automation/tasks.json');checks_file=read(root/'automation/checks.json')
    require(graph['freeze_id']==FREEZE and graph['definition_status']=='implementation_ready_unexecuted','graph status/version mismatch')
    require(graph['scope']=={'worlds':5,'missions':25,'competition_missions':5,'automatic_scope_cuts':False},'scope changed')
    tasks=index(graph['tasks']);checks=index(checks_file['checks']);gates=index(read(root/'automation/gates.json')['gates'])
    contracts=index(read(root/'specification/content/acceptance_cases.json')['contracts'])
    require(set(contracts)==set(checks),'case contract/check IDs mismatch')
    originals=set();assertion_ids=set();report_paths=set()
    for tid,t in tasks.items():
        require(t['depends_on'] and tid!='F01' or tid=='F01','unexpected dependency-free task')
        require(set(t['depends_on'])<=set(tasks),'unknown dependency: '+tid)
        require(tid not in t['depends_on'],'self dependency')
        require(t['write_areas'] and t['expected_artifacts'] and t['acceptance_checks'],'incomplete task: '+tid)
        for p in t['write_areas']+t['expected_artifacts']:local_path(root,p)
        require(not any(p.startswith('Resq_Production_Pack') for p in t['write_areas']),'task writes immutable baseline')
        require(set(t['required_gates'])<=set(gates),'unknown task gate')
        require(set(t['acceptance_checks'])<=set(checks),'unknown acceptance check')
        require(t['max_repair_attempts']==(0 if t['execution_type']=='human' else 3),'repair bound drift')
        originals.update(t['original_task_ids'])
        for cid in t['acceptance_checks']:
            c=checks[cid];require(c['task_id']==tid,'check ownership mismatch')
            entry='tools/bootstrap_doctor.py' if tid=='F01' else 'tools/resq.py'
            require(c['command'][:2]==['python3',entry],'future command shape mismatch')
            require(c['implemented_by']==('F01' if tid=='F01' else 'F02'),'check runner bootstrap ownership mismatch')
            require(c['command'][-2:]==['--report',c['report_path']],'check report argv mismatch')
            require('{run_id}/{attempt_id}' in c['report_path'],'report history must be append-only')
            require(c['report_path'] not in report_paths,'duplicate report destination');report_paths.add(c['report_path'])
            require(c['assertions'],'check has no assertions')
            for a in c['assertions']:
                require(a['id'] not in assertion_ids and a['description'] and a['required'] is True,'invalid/duplicate assertion')
                assertion_ids.add(a['id'])
            require(c['required_evidence_kind']==t['required_evidence_kind'],'evidence category drift')
            require(c['case_contract_id']==cid and contracts[cid]==case_contract(c),'case contract drift')
            if any(r['dimensions'].get('target') in ('quest3','quest3s') for r in contracts[cid]['required_cases']):
                require('quest_devices' in t['required_gates'],'Android replay needs current device readiness gate')
    require(originals=={f'T{n:02}' for n in range(1,39)},'original T01-T38 traceability missing')
    require(set(checks)=={c for t in tasks.values() for c in t['acceptance_checks']},'orphan acceptance check')
    require(set(checks_file['aliases'].values())<=set(checks),'unknown check alias')
    visiting=set();done=set();ordered=[]
    def visit(tid):
        require(tid not in visiting,'task cycle: '+tid)
        if tid in done:return
        visiting.add(tid)
        for dep in tasks[tid]['depends_on']:visit(dep)
        visiting.remove(tid);done.add(tid);ordered.append(tid)
    for tid in tasks:visit(tid)
    def ancestors(tid):
        found=set();queue=list(tasks[tid]['depends_on'])
        while queue:
            x=queue.pop()
            if x not in found:found.add(x);queue.extend(tasks[x]['depends_on'])
        return found
    require('F05' not in ancestors('C01'),'pure logic incorrectly depends on device qualification')
    for tid in tasks:
        if tid not in ('F01','F02'):require('F02' in ancestors(tid),'runner used before it exists: '+tid)
    from deferred_device_schedule import validate_schedule
    validate_schedule(tasks,contracts,ancestors,require)
    require('W1-TUTORIAL-LOGIC' in ancestors('ART-LOOKDEV'),'final art precedes complete tutorial slice')
    for w in range(2,6):
        wid=f'W{w}'
        require({f'W{w-1}-M{m}-POLISH' for m in range(1,6)}<=ancestors(wid+'-MECHANIC'),'world expansion skips local development')
        require(wid+'-M1-INTEGRATE' in ancestors(wid+'-KIT'),'world kit precedes local first mission')
        for n in range(2,6):require(wid+'-M1-INTEGRATE' in ancestors(f'{wid}-M{n}-AUTHOR'),'mission expansion precedes local first mission')
    for t in tasks.values():
        if t['release_scope']=='full_release':require('D12' not in ancestors(t['id']),'commercial work waits on entrant publication')
    require({'FULL-PERFORMANCE','FULL-DEVICE','FULL-HUMAN'}<=ancestors('FULL-QUALIFY'),'final candidate lacks current-build physical/human requalification')
    require({'FULL-VIDEO','FULL-WEB'}<=ancestors('FULL04'),'full local delivery lacks complete media/site')
    require('FULL-WEB-PREVIEW' not in ancestors('FULL04'),'local full candidate incorrectly depends on publication')
    state=read(root/'automation/task_state.json')['task_states']
    require(set(state)==set(tasks),'state/task IDs mismatch')
    for tid,s in state.items():
        require(s['status'] in ('pending','ready','running','blocked','failed','passed','skipped_with_reason'),'unknown execution status')
        if s['status']=='passed':
            require(s['result_path'],'passed state has no result')
            p=local_path(root,s['result_path'],True);r=read(p)
            require(s.get('result_sha256')==sha(p),'state result hash mismatch')
            require(r.get('status')=='passed','passed state refers to nonpassed result')
            validate_task_result(root,r,expected_task=tid,verify_current_gates=False)
    policy=read(root/'automation/autonomy_policy.json')
    authorizations=[]
    for item in policy['authorization_records']:
        record=read(local_path(root,item,True)) if isinstance(item,str) else item
        # Expired approvals remain auditable history; they cannot enable new work.
        issued=datetime.fromisoformat(record['recorded_utc'].replace('Z','+00:00'))
        require(issued<=datetime.now(timezone.utc),'authorization issued in future')
        validate_authorization(root,record,issued)
        if record['expires_utc'] is None or datetime.fromisoformat(record['expires_utc'].replace('Z','+00:00'))>datetime.now(timezone.utc):
            authorizations.append(record)
    if policy['paid_generation']['enabled']:
        require(any(a['action']=='paid_generation' and a['maximum_usd']>=policy['paid_generation']['total_budget_usd'] for a in authorizations),'unapproved paid allowance')
    else:require(policy['paid_generation']['total_budget_usd']==0,'disabled paid allowance must remain zero')
    for action,enabled in policy['external_actions'].items():
        if enabled:require(any(a['action']==action for a in authorizations),'unapproved external allowance')
    require(policy['devpost_final_submission']=='entrant_only','entrant gate removed')
    mission_data=read(root/'specification/content/mission_contracts.json');missions=index(mission_data['missions'])
    require(set(missions)==MISSION_IDS,'full25 mission coverage changed')
    originals=index(read(root/'Resq_Production_Pack/content/missions.json'))
    equipment=index(read(root/'specification/content/equipment_rules.json')['equipment'])
    require(set(equipment)==set(index(read(root/'Resq_Production_Pack/content/equipment.json'))),'equipment catalogue mismatch')
    layouts=index(read(root/'specification/content/layout_recipes.json')['recipes'])
    require(len(layouts)==25,'layout recipe count mismatch')
    objective_types={'rescue_count','group_evacuated','infrastructure_operational','route_available','exposure_below',
      'equipment_losses_at_most','supply_remaining_at_least','all_vehicles_unloaded','temporary_shelters_empty','temporary_staging_empty',
      'resource_remaining','counter_at_most','counter_at_least','scenario_elapsed_at_most','all','any'}
    temporal_keys={'at_end':{'kind'},'ever_by':{'kind','deadline_tick'},'always':{'kind','start_event','end_event'},
      'ever_before':{'kind','end_event'},'hold_for':{'kind','scenario_ticks','start_event','end_event'}}
    for mid,m in missions.items():
        b=originals[mid]
        require(m['status']=='frozen_design_unimplemented','mission falsely claims implementation')
        for k in ('title','world_id','release_scope','total_residents','minimum_rescues','supply_budget','default_loadout'):
            require(m[k]==b[k],mid+' original scope/tuning drift: '+k)
        require(m['duration_scenario_ticks']==b['duration_seconds']*10,'mission deadline drift')
        require(m['rules_version']==RULES and m['mission_revision']==1,'mission version drift')
        require(m['maximum_stock']==m['default_loadout'],'stock policy drift')
        require(sum(equipment[k]['supply_cost']*q for k,q in m['default_loadout'].items())<=m['supply_budget'],'overbudget default')
        groups=index(m['groups']);require(sum(g['count'] for g in groups.values())==m['total_residents'],'spawn population mismatch')
        for g in groups.values():require(sum(g['mobility_counts'].values())==g['count'] and g['mobility_counts']['step_free']==1,'mobility population mismatch')
        goals=index(m['objectives']);require(any(o['tier']=='critical' for o in goals.values()) and any(o['tier']=='mastery' for o in goals.values()),'missing critical/mastery')
        require('rescue_minimum' in goals and goals['rescue_minimum']['params']['minimum']==m['minimum_rescues'],'missing minimum rescue objective')
        require(set(m['protection_objective_ids'])<=set(goals),'unknown protection objective')
        milestones={'operation_started','operation_ended'}|set(index(m['milestones']))
        for o in goals.values():
            require(set(o)=={'id','tier','kind','params','temporal'},'objective wire shape drift')
            require(o['kind'] in objective_types,'unknown objective type')
            t=o['temporal'];require(t['kind'] in temporal_keys and set(t)==temporal_keys[t['kind']],'temporal shape drift')
            for key in ('start_event','end_event'):
                if key in t:require(t[key] in milestones,'unknown temporal milestone')
            if 'group_id' in o['params'] and o['params']['group_id'] is not None:require(o['params']['group_id'] in groups,'unknown objective group')
        last=-1
        for e in m['events']:
            require(1<=e['at_scenario_tick']<m['duration_scenario_ticks'] and last<=e['at_scenario_tick'],'event order/bound violation');last=e['at_scenario_tick']
            require(0<=e['warning_at_scenario_tick']<=e['at_scenario_tick'],'warning after effect')
            require(e['operations'] and e['known_at_start'] is True,'event has no typed effect/knowledge')
            if any(not o['kind'].startswith('warn_') for o in e['operations']):
                require(e['at_scenario_tick']-e['warning_at_scenario_tick']>=100,'disruptive event warning below 100 scenario ticks')
        require(m['reference_strategy_briefs']['A']!=m['reference_strategy_briefs']['B'],'identical strategy briefs')
        recipe=layouts[m['layout_recipe_id']]
        require(recipe['mission_id']==mid,'layout binds wrong mission')
        require(recipe['receiving']['total_permanent_capacity']==m['total_residents'],'receiving capacity mismatch')
        require({x['group_id'] for x in recipe['group_bindings']}==set(groups),'layout group binding missing')
        roles=set(recipe['required_roles'])
        require(all(o['target_role'] in roles for e in m['events'] for o in e['operations']),'unbound event role')
        require(recipe['logical_grid']=={'width':32,'height':24,'cell_pitch_mm':1000,'cell_area_mm2':1000000},'logical dimensions drift')
        for suffix in ('AUTHOR','REPLAY','INTEGRATE','POLISH','QUALIFY'):require(mid+'-'+suffix in tasks,'missing mission task stage')
    modes=read(root/'specification/content/modes_and_challenges.json');challenges=index(modes['challenges'])
    require(len(challenges)==30,'challenge scope mismatch')
    require(modes['base_success_replays']==300 and modes['challenge_success_replays']==120,'replay matrix count drift')
    counts=Counter(c['mission_id'] for c in challenges.values())
    for mid in missions:require(counts[mid]==(2 if mid.startswith('W1-') else 1),'challenge coverage missing')
    for c in challenges.values():
        require(c['mission_id'] in missions and c['layouts']==['compact','wide'] and c['rule_profiles']==['standard'],'unsupported challenge configuration')
        if c['modifier']=='early_surge':
            e=index(missions[c['mission_id']]['events'])[c['parameters']['event_id']]
            require(e['at_scenario_tick']+c['parameters']['shift_scenario_ticks']>=0,'negative shifted event')
    require(len(modes['supported_combinations'])==7,'input support scope changed')
    inventory=read(root/'specification/content/production_inventory.json')
    inventory_rows=inventory['rows']
    require(len(index(inventory_rows,'id'))==487,'production inventory must retain 487 unique rows')
    profile=index(modes['rule_profiles'])
    require(profile['relaxed']['hazard_clock_numerator']==1 and profile['relaxed']['hazard_clock_denominator']==2,'Relaxed clock mismatch')
    for p in sorted((root/'specification/schemas').glob('*.json')):
        schema=read(p);require(schema.get('$schema')=='https://json-schema.org/draft/2020-12/schema','schema dialect mismatch')
    if verify_bundle and (root/'specification/freeze_manifest.json').exists():
        manifest=read(root/'specification/freeze_manifest.json')
        for a in manifest['files']:artifact(root,a)
        gate_defs=[{k:v for k,v in row.items() if k not in ('status','evidence_path','evidence_sha256')} for row in gates.values()]
        digest=hashlib.sha256(json.dumps(gate_defs,sort_keys=True,separators=(',',':')).encode()).hexdigest()
        require(digest==manifest['gate_definitions_sha256'],'frozen gate definitions changed')
    return {'status':'passed','evidence_level':'specification_consistency_only','freeze_id':FREEZE,
      'checked_at_utc':datetime.now(timezone.utc).isoformat(),
      'counts':{'worlds':5,'missions':len(missions),'tasks':len(tasks),'acceptance_checks':len(checks),
      'assertions':len(assertion_ids),'required_case_records':sum(len(c['required_cases']) for c in contracts.values()),'production_inventory_rows':487,'challenges':len(challenges),'base_success_configurations':300,'challenge_success_configurations':120,
      'original_files_preserved':len(baseline['files'])},
      'initial_ready_tasks':[x for x in ordered if not tasks[x]['depends_on']],
      'limitations':['Does not compile Unity or execute registered future acceptance commands.','Does not prove mission solvability, balance, asset quality, physical behavior, comfort, footage or deployment.','All game/build tasks remain pending unless separately accepted real results exist.']}


def main():
    ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--root',type=Path,default=ROOT);ap.add_argument('--report',type=Path);ap.add_argument('--no-bundle',action='store_true')
    args=ap.parse_args()
    try:r=validate(args.root,not args.no_bundle)
    except (Invalid,KeyError,TypeError) as exc:print('FAILED: '+str(exc));return 1
    if args.report:
        args.report.parent.mkdir(parents=True,exist_ok=True);args.report.write_text(json.dumps(r,indent=2)+'\n')
    print(json.dumps(r,indent=2));return 0


if __name__=='__main__':raise SystemExit(main())
