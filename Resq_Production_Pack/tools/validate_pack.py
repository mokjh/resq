#!/usr/bin/env python3
"""Validate Resq specification integrity, not Unity/runtime behavior.

Uses only the Python standard library. This is a focused consistency checker,
not a full JSON Schema implementation. It never calls external services.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import re
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

class ValidationFailure(Exception):
    pass

def fail_unless(condition: bool, message: str) -> None:
    if not condition:
        raise ValidationFailure(message)

def unique_pairs(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        fail_unless(key not in result, f"Duplicate JSON key: {key}")
        result[key] = value
    return result

def load_json(path: Path) -> Any:
    try:
        return json.loads(path.read_text(encoding='utf-8'), object_pairs_hook=unique_pairs)
    except (OSError, ValueError, ValidationFailure) as exc:
        raise ValidationFailure(f"{path.name}: {exc}") from exc

def identifier_map(rows: Any, label: str) -> dict[str, dict[str, Any]]:
    fail_unless(isinstance(rows, list), f"{label} must be an array")
    result: dict[str, dict[str, Any]] = {}
    for row in rows:
        fail_unless(isinstance(row, dict), f"{label} row must be an object")
        ident = row.get('id')
        fail_unless(isinstance(ident, str) and bool(ident), f"{label} needs string id")
        fail_unless(ident not in result, f"Duplicate {label} id: {ident}")
        result[ident] = row
    return result

def integer(value: Any, label: str, minimum: int = 0) -> int:
    fail_unless(type(value) is int and value >= minimum, f"{label} must be integer >= {minimum}")
    return value

def nonempty(value: Any, label: str) -> None:
    fail_unless(isinstance(value, str) and bool(value.strip()), f"{label} must be nonempty text")

def validate(root: Path) -> dict[str, Any]:
    root = root.resolve()
    required = ['README.md', 'AGENTS.md', 'CODEX_START_PROMPT.md',
        'content/worlds.json', 'content/missions.json', 'content/equipment.json',
        'content/asset_manifest.template.json', 'content/release_manifest.template.json',
        'automation/tasks.json', 'automation/autonomy_policy.json',
        'automation/toolchain.lock.template.json',
        'schemas/mission_briefs.schema.json', 'schemas/task_result.schema.json',
        'schemas/asset_manifest.schema.json', 'diagrams/game_data_erd.mmd',
        'diagrams/runtime_flow.mmd']
    for path in required:
        fail_unless((root/path).is_file(), f"Missing required file: {path}")
    docs = sorted((root/'docs').glob('*.md'))
    fail_unless(len(docs) == 13, 'Expected 13 modular specification documents')
    json_paths = sorted(p for p in root.rglob('*.json') if 'evidence' not in p.relative_to(root).parts)
    parsed = {str(p.relative_to(root)): load_json(p) for p in json_paths}
    worlds = identifier_map(parsed['content/worlds.json'], 'world')
    equipment = identifier_map(parsed['content/equipment.json'], 'equipment')
    missions = identifier_map(parsed['content/missions.json'], 'mission')
    fail_unless(len(worlds) == 5, 'Expected 5 planned worlds')
    fail_unless(len(missions) == 25, 'Expected 25 authored mission briefs')
    fail_unless(len(equipment) == 14, 'Expected 14 equipment definitions')
    costs = {key: integer(row.get('supply_cost'), f'{key} cost', 1) for key,row in equipment.items()}
    slugs: set[str] = set()
    for wid,world in worlds.items():
        slug = world.get('slug')
        nonempty(slug, f'{wid} slug')
        fail_unless(slug not in slugs, f'Duplicate world slug: {slug}')
        slugs.add(slug)
        for tool in world.get('new_tools', []):
            fail_unless(tool in equipment, f'{wid} references missing tool {tool}')
        for field in ['identity','mechanic','geometry','sound','reveal']:
            nonempty(world.get(field), f'{wid} {field}')
    text_fields = ['title','core_decision','layout_brief','required_objectives_brief',
        'mastery_brief','reference_plan_a','reference_plan_b','failure_explanation','signature_moment']
    scope_counts = {'competition':0, 'full_release':0}
    mission_keys = set(parsed['schemas/mission_briefs.schema.json']['items']['required'])
    for mid,mission in missions.items():
        fail_unless(set(mission) == mission_keys, f'{mid} missing or extra brief fields')
        fail_unless(re.fullmatch(r'W[1-5]-M[1-5]',mid) is not None, f'Bad mission id: {mid}')
        wid = mission.get('world_id')
        fail_unless(wid in worlds, f'{mid} references missing world {wid}')
        order = integer(mission.get('order'), f'{mid} order', 1)
        fail_unless(mid == f'{wid}-M{order}', f'{mid} world/order mismatch')
        fail_unless(mission.get('status') == 'authored_brief_unimplemented', f'{mid} must remain an authored brief in this pack')
        scope = mission.get('release_scope')
        fail_unless(scope in scope_counts, f'{mid} invalid release scope')
        fail_unless(scope == worlds[wid].get('release'), f'{mid} scope differs from world')
        scope_counts[scope] += 1
        duration = integer(mission.get('duration_seconds'),f'{mid} duration',60)
        fail_unless(duration <= 600, f'{mid} exceeds 10-minute target')
        total = integer(mission.get('total_residents'),f'{mid} total',1)
        minimum = integer(mission.get('minimum_rescues'),f'{mid} rescue minimum',1)
        fail_unless(minimum <= total, f'{mid} minimum exceeds total residents')
        budget = integer(mission.get('supply_budget'),f'{mid} budget',1)
        loadout = mission.get('default_loadout')
        fail_unless(isinstance(loadout,dict) and bool(loadout),f'{mid} needs loadout')
        spent = 0
        for tool,qty in loadout.items():
            fail_unless(tool in costs,f'{mid} references missing equipment {tool}')
            spent += costs[tool] * integer(qty,f'{mid} {tool} quantity',1)
        fail_unless(spent <= budget, f'{mid} loadout cost {spent} exceeds budget {budget}')
        previous = -1
        events = mission.get('event_briefs')
        fail_unless(isinstance(events,list) and bool(events),f'{mid} needs events')
        for event in events:
            fail_unless(isinstance(event,dict),f'{mid} event must be object')
            tick = integer(event.get('simulation_second'),f'{mid} event time')
            fail_unless(previous <= tick < duration,f'{mid} events unordered/outside mission')
            nonempty(event.get('description'),f'{mid} event description')
            previous = tick
        for field in text_fields:
            nonempty(mission.get(field),f'{mid} {field}')
        fail_unless(mission['reference_plan_a'] != mission['reference_plan_b'],f'{mid} reference plans duplicate')
        presets = mission.get('input_presets',[])
        fail_unless(set(presets) == {'standard_hands','relaxed_hands','command_head_dwell'},f'{mid} input presets incomplete')
        fail_unless(len(mission.get('required_evidence',[])) >= 4,f'{mid} evidence requirements missing')
    for wid in worlds:
        orders = sorted(m['order'] for m in missions.values() if m['world_id'] == wid)
        fail_unless(orders == [1,2,3,4,5],f'{wid} must have five ordered missions')
    fail_unless(scope_counts == {'competition':5,'full_release':20},'Release scope counts inconsistent')
    task_file = parsed['automation/tasks.json']
    fail_unless(task_file.get('status') == 'plan_not_executed','Task graph must not claim execution')
    tasks = identifier_map(task_file.get('tasks'),'task')
    for tid,task in tasks.items():
        fail_unless(task.get('status') == 'pending',f'{tid} incorrectly claims execution in plan')
        fail_unless(bool(task.get('expected_artifacts')),f'{tid} needs expected outputs')
        nonempty(task.get('acceptance'),f'{tid} acceptance')
        for dep in task.get('depends_on',[]):
            fail_unless(dep in tasks,f'{tid} missing dependency {dep}')
            fail_unless(dep != tid,f'{tid} depends on itself')
    seen: set[str] = set()
    visiting: set[str] = set()
    ordered: list[str] = []
    def visit(tid: str) -> None:
        fail_unless(tid not in visiting,f'Task cycle at {tid}')
        if tid in seen:
            return
        visiting.add(tid)
        for dep in tasks[tid]['depends_on']:
            visit(dep)
        visiting.remove(tid)
        seen.add(tid)
        ordered.append(tid)
    for tid in tasks:
        visit(tid)
    policy = parsed['automation/autonomy_policy.json']
    fail_unless(policy.get('devpost_final_submission') == 'entrant_only','Final entry must remain entrant action')
    fail_unless(policy.get('paid_generation',{}).get('enabled') is False,'Pack must not enable paid generation')
    release = parsed['content/release_manifest.template.json']
    fail_unless(release.get('status') == 'specification_only','Template makes false release claim')
    for field in ['implemented_missions','qualified_missions','qualified_devices','verified_features']:
        fail_unless(release.get(field) == [],f'Template must not claim {field}')
    fail_unless(set(release['target_missions']) == {i for i,m in missions.items() if m['release_scope']=='competition'},'Release target mismatch')
    for path in [p for p in parsed if p.startswith('schemas/')]:
        fail_unless(parsed[path].get('$schema') == 'https://json-schema.org/draft/2020-12/schema',f'Invalid schema header: {path}')
    fail_unless((root/'diagrams/game_data_erd.mmd').read_text().lstrip().startswith('erDiagram'),'Missing ERD source')
    fail_unless((root/'diagrams/runtime_flow.mmd').read_text().lstrip().startswith('flowchart'),'Missing runtime flow source')
    manifest = []
    for path in sorted(root.rglob('*')):
        if not path.is_file() or any(part in {'evidence','__pycache__'} for part in path.relative_to(root).parts):
            continue
        if path.suffix == '.pyc':
            continue
        manifest.append({'path':str(path.relative_to(root)), 'sha256':hashlib.sha256(path.read_bytes()).hexdigest()})
    return {'status':'passed','evidence_level':'specification_consistency_only',
        'checked_at_utc':datetime.now(timezone.utc).isoformat(),
        'counts':{'documents':len(docs),'worlds':len(worlds),'mission_briefs':len(missions),'equipment':len(equipment),'tasks':len(tasks),'json_files':len(json_paths)},
        'mission_scope':scope_counts,'task_topological_order':ordered,'file_manifest':manifest,
        'limitations':['Not a full JSON Schema engine.','Does not compile Unity or validate runtime geometry, route solvability, asset quality, hardware, comfort, footage, or deployment.','All missions remain authored unimplemented briefs.']}

def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root',type=Path,default=Path(__file__).resolve().parents[1])
    parser.add_argument('--report',type=Path,default=None,help='Optional JSON report destination')
    args = parser.parse_args()
    try:
        report = validate(args.root)
    except (ValidationFailure, KeyError, TypeError, RecursionError) as exc:
        print(f'FAILED: {exc}',file=sys.stderr)
        return 1
    if args.report:
        args.report.parent.mkdir(parents=True,exist_ok=True)
        args.report.write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({k:v for k,v in report.items() if k != 'file_manifest'},indent=2))
    return 0

if __name__ == '__main__':
    sys.exit(main())
