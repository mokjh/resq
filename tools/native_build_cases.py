"""Cross-check executed native F04 inspectors against their immutable packet."""
import json
from pathlib import Path

from runner_core import ReportError, sha256_file


def validate_native_build_cases(packet, directory, build):
    def require(ok, message):
        if not ok:raise ReportError(message)
    root=Path(directory).resolve(strict=True)
    ref=packet.get('native_summary')
    require(isinstance(ref,dict),'F04 native executed-case summary required')
    path=Path(ref.get('path',''))
    if not path.is_absolute():path=root/path
    require('..' not in path.parts and path.is_relative_to(root),'F04 native summary outside owned run')
    current=root
    for part in path.relative_to(root).parts:
        current/=part
        require(not current.is_symlink(),'Linked F04 native summary refused')
    require(path.is_file(),'F04 native summary missing')
    digest=sha256_file(path)
    require(ref.get('sha256')==digest and type(ref.get('byte_length')) is int and ref['byte_length']==path.stat().st_size,'F04 native summary bytes differ')
    def closed_json(pairs):
        result={}
        for key,value in pairs:
            require(key not in result,'Duplicate native summary JSON key')
            result[key]=value
        return result
    summary=json.loads(path.read_text(),object_pairs_hook=closed_json)
    require(isinstance(summary,dict) and summary.get('schema_version')==1 and summary.get('evidence_kind')=='automated','Invalid F04 native summary envelope')
    expected={f'check-f04/assertion/assertion_id=check-f04-a{i:02}' for i in range(1,5)}
    rows=summary.get('cases');cases=packet.get('case_outcomes')
    require(isinstance(rows,list) and isinstance(cases,list) and len(rows)==len(cases)==4,'Exact four F04 native/packet cases required')
    require({r.get('case_id') for r in rows}==expected and {r.get('case_id') for r in cases}==expected,'F04 native/packet case IDs differ')
    require(all(r.get('executed') is True and r.get('status') in ('passed','failed') and isinstance(r.get('observations'),dict) and r['observations'] for r in rows),'F04 native execution observations required')
    counts={'executed':4,'passed':sum(r['status']=='passed' for r in rows),'failed':sum(r['status']=='failed' for r in rows),'skipped':0}
    require(all(type(summary.get(k)) is int and summary[k]==v for k,v in counts.items()),'F04 native counts disagree with cases')
    status='passed' if counts['failed']==0 else 'failed'
    require(summary.get('status')==packet.get('status')==status,'F04 native/packet status disagreement')
    binding=summary.get('build_binding')
    require(isinstance(binding,dict) and binding==packet.get('build_binding'),'F04 native/packet build binding differs')
    require(binding.get('build_guid')==build.get('build_guid') and binding.get('apk_sha256')==build.get('apk_sha256') and binding.get('build_report_sha256')==build.get('sha256'),'F04 native summary does not bind actual BuildReport/APK')
    indexed={r['case_id']:r for r in rows}
    for case in cases:
        row=indexed[case['case_id']];obs=case.get('observations')
        require(case.get('status')==row['status'] and isinstance(obs,dict) and obs.get('native_summary_sha256')==digest,'F04 packet execution/hash differs')
        projected={k:v for k,v in obs.items() if k not in ('native_summary_sha256','unity_version')}
        require(projected==row['observations'] and obs.get('unity_version')==summary.get('unity_version')==build.get('unity_version'),'F04 packet native observations differ')
    return summary
