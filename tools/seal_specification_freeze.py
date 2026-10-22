#!/usr/bin/env python3
"""Seal reviewed specification inputs; never mark game tasks accepted."""
from datetime import datetime,timezone
import hashlib
import json
from validate_spec_freeze import ROOT,read,sha,validate


def gate_digest(root):
    rows=[{k:v for k,v in row.items() if k not in ('status','evidence_path','evidence_sha256')}
          for row in read(root/'automation/gates.json')['gates']]
    return hashlib.sha256(json.dumps(rows,sort_keys=True,separators=(',',':')).encode()).hexdigest()


def main():
    summary=validate(ROOT,False)
    paths=[ROOT/'AGENTS.md',ROOT/'BUILD_PLAN.md',ROOT/'automation/tasks.json',ROOT/'automation/checks.json']
    paths.extend(p for p in (ROOT/'specification').rglob('*') if p.is_file() and p.name!='freeze_manifest.json')
    paths.extend((ROOT/'tools').glob('*.py'))
    records=[{'path':str(p.relative_to(ROOT)),'sha256':sha(p),'kind':'frozen_specification_input','byte_length':p.stat().st_size}
             for p in sorted(set(paths))]
    manifest={'schema_version':1,'freeze_id':summary['freeze_id'],'status':'specification_frozen_implementation_unexecuted',
      'sealed_utc':datetime.now(timezone.utc).isoformat(),'counts':summary['counts'],
      'gate_definitions_sha256':gate_digest(ROOT),'files':records,
      'mutable_exclusions':['STATUS.md','DECISIONS.md','BLOCKERS.md','HANDOFF.md','automation/task_state.json',
        'automation/gates.json execution heads only','automation/autonomy_policy.json authorized updates','evidence/','releases/']}
    (ROOT/'specification/freeze_manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
    print(f'Sealed {len(records)} specification inputs; game tasks remain pending.')


if __name__=='__main__':main()
