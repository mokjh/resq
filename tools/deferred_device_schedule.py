"""User-directed scheduling amendment; preserves every acceptance check/case."""


POLISH_TASKS = tuple(f'W{w}-M{m}-POLISH' for w in range(1, 6) for m in range(1, 6))


def dependency_overrides():
    rows = {
        'F05': ['F04', *POLISH_TASKS, 'X05', 'X06', 'W1-TUTORIAL-LOGIC'],
        'ART-LOOKDEV': ['W1-M1-INTEGRATE', 'W1-TUTORIAL-LOGIC', 'F06'],
        'ART-SHARED': ['ART-LOOKDEV'],
        'ART-HF-BRIEF': ['ART-LOOKDEV'],
        'ART-LOOKDEV-HUMAN': ['ART-LOOKDEV', 'F05'],
        'D01': [*POLISH_TASKS[:5], 'F02'],
        'D02': ['D01', 'W1-FREEZE'],
    }
    for w in range(1, 6):
        wid = f'W{w}'
        for m in range(2, 6):
            rows[f'{wid}-M{m}-AUTHOR'] = [f'{wid}-M1-INTEGRATE']
            if w == 1:
                rows[f'{wid}-M{m}-AUTHOR'].append('W1-TUTORIAL-LOGIC')
        first = 'W1-VERTICAL' if w == 1 else f'{wid}-FIRST'
        for m in range(1, 6):
            rows[f'{wid}-M{m}-QUALIFY'] = [f'{wid}-M{m}-POLISH', 'X05', 'X06', first, 'ART-LOOKDEV-HUMAN']
        if w > 1:
            rows[f'{wid}-MECHANIC'] = [*(f'W{w-1}-M{m}-POLISH' for m in range(1, 6)), 'C09']
            for suffix in ('KIT', 'AUDIO'):
                rows[f'{wid}-{suffix}'] = [f'{wid}-M1-INTEGRATE', 'ART-SHARED']
            rows[f'{wid}-REGRESSION'] = [*(f'{wid}-M{m}-QUALIFY' for m in range(1, 6)), f'W{w-1}-FREEZE']
    return rows


def apply_schedule(tasks):
    indexed = {task['id']: task for task in tasks}
    for tid, dependencies in dependency_overrides().items():
        indexed[tid]['depends_on'] = dependencies
    look = indexed['ART-LOOKDEV-HUMAN']
    if 'quest_devices' not in look['required_gates']:
        look['required_gates'].append('quest_devices')
    indexed['W1-VERTICAL']['title'] = 'Qualify retained W1-M1 mechanics slice in final device phase'
    for w in range(2, 6):
        indexed[f'W{w}-FIRST']['title'] = f'Qualify retained W{w} first-mission mechanic in final device phase'


def device_dependent_tasks(tasks, contracts):
    targets = {row['task_id'] for row in contracts.values()
               if any(case['dimensions'].get('target') in ('quest3', 'quest3s')
                      for case in row['required_cases'])}
    return targets | {tid for tid, task in tasks.items()
                      if task['required_evidence_kind'] in ('physical_device', 'human_observation')
                      or 'quest_devices' in task['required_gates']} | {'ART-LOOKDEV-HUMAN'}


def validate_schedule(tasks, contracts, ancestors, require):
    device_tasks = device_dependent_tasks(tasks, contracts)
    development = set(POLISH_TASKS)
    for tid in sorted(device_tasks):
        require(development <= ancestors(tid), 'physical testing precedes full25 development: ' + tid)
    for tid in POLISH_TASKS:
        require(not (device_tasks & ancestors(tid)), 'local development depends on physical qualification: ' + tid)
    for tid, expected in dependency_overrides().items():
        require(tasks[tid]['depends_on'] == expected, 'deferred device dependency drift: ' + tid)
    require('quest_devices' in tasks['ART-LOOKDEV-HUMAN']['required_gates'], 'device look review needs current device gate')
    for w in range(1, 6):
        required = {'F05', 'ART-LOOKDEV-HUMAN', 'W1-VERTICAL' if w == 1 else f'W{w}-FIRST'}
        required.update(f'W{k}-FREEZE' for k in range(1, w))
        require(required <= ancestors(f'W{w}-FREEZE'), 'qualified world lost physical/human closure')
