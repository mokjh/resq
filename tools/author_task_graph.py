#!/usr/bin/env python3
"""Author the immutable implementation task/check definitions; no tasks execute."""
from pathlib import Path
import json
from evidence_case_contracts import case_contract
from deferred_device_schedule import apply_schedule

ROOT=Path(__file__).resolve().parents[1]
FREEZE='resq-spec-freeze-1.1.0'
TASKS=[]
CHECKS=[]


def dump(path,data):
    p=ROOT/path;p.parent.mkdir(parents=True,exist_ok=True)
    p.write_text(json.dumps(data,indent=2,ensure_ascii=False)+'\n')


def task(ident,title,deps,areas,outputs,category,args,assertions,original,*,gates=(),locks=(),mode='automated',scope='competition',inputs=(),evidence_kind=None):
    check='check-'+ident.lower()
    report='evidence/checks/'+ident.lower()+'/{run_id}/{attempt_id}.json'
    argv=['python3','tools/resq.py',*args,'--report',report]
    kind=evidence_kind or {'automated':'automated','device':'physical_device','human':'human_observation','external':'external_verified'}[mode]
    TASKS.append({'id':ident,'title':title,'release_scope':scope,'workstream':category,'required_evidence_kind':kind,
      'depends_on':list(deps),'inputs':['specification/00_FREEZE_INDEX.md',*inputs],
      'write_areas':list(areas),'single_writer_locks':list(locks),
      'expected_artifacts':list(outputs),'acceptance_checks':[check],
      'required_gates':list(gates),'original_task_ids':list(original),
      'execution_type':mode,'max_repair_attempts':0 if mode=='human' else 3,'max_asset_repair_attempts':2 if category=='art' else None,
      'stop_condition':('await authentic human action/records; do not simulate approval' if mode=='human' else 'acceptance passes, or a named gate blocks, or three root-cause repair attempts fail; preserve evidence and continue independent ready tasks')})
    CHECKS.append({'id':check,'task_id':ident,'execution_kind':mode,
      'command':argv,'report_path':report,'assertions':[{'id':check+f'-a{n+1:02}','description':a,'required':True} for n,a in enumerate(assertions)],
      'required_evidence_kind':kind,
      'identity_fields':['freeze_id','rules_version','source_identity','content_hash','toolchain_hash'],
      'runner_status':'pending_implementation','implemented_by':'F02',
      'failure_policy':'nonzero exit and failed/blocked report; required skipped/not_run checks never pass'})


def add_foundation():
    task('F01','Preserve baseline, implement standalone bootstrap doctor and record preflight',[],['tools/bootstrap_doctor.py','docs','automation','evidence','game','.gitignore','.gitattributes'],
      ['tools/bootstrap_doctor.py','evidence/foundation/initial-preflight.json','evidence/foundation/initial-state.json','evidence/foundation/initial-status.md','evidence/foundation/initial-blockers.md'], 'foundation', ['doctor','--scope','repository'],
      ['original pack hashes unchanged','actual cwd, Git and storage recorded without secrets','all build tasks initially pending','no unrelated work modified'],['T01'])
    CHECKS[-1]['command']=['python3','tools/bootstrap_doctor.py','--report',CHECKS[-1]['report_path']]
    CHECKS[-1]['implemented_by']='F01'
    CHECKS[-1]['runner_status']='bootstrap_implemented_in_own_task'
    task('F02','Implement real CLI execution, locks and task-result validation',['F01'],['tools','automation','specification/schemas','game/Assets/Resq/Editor/Automation'],
      ['tools/resq.py','tools/verify_task_result.py','game/Assets/Resq/Editor/Automation/EntryPoint.cs'], 'foundation', ['doctor','--scope','runner'],
      ['all registered command families resolve to real adapters','unimplemented adapter exits blocked, never success','process/XML/build/report statuses cross-checked','stale hash, failed check, missing artifact and synthetic human proof are rejected','lock contention tested; owned processes cleaned up'],['T01','T08'])
    task('F03','Create host Unity project and pure logic/test assemblies',['F01','F02'],['game/Assets/Resq/Runtime/Core','game/Assets/Resq/Tests','game/Packages','game/ProjectSettings'],
      ['game/ProjectSettings/ProjectVersion.txt','game/Packages/manifest.json','game/Assets/Resq/Runtime/Core/Resq.Core.asmdef','game/Assets/Resq/Tests/EditMode/Resq.Tests.asmdef'], 'foundation', ['game','test','--suite','host-bootstrap'],
      ['licensed actual Editor compiles','at least one isolated pure-state test executes','Core/Simulation do not reference UnityEngine','original files and unrelated Editor lock respected'],['T03','T04'],gates=['host_license','host_working_storage'],locks=['unity-project','heavy-job'])
    task('F04','Provision and verify matched Android/XR packages',['F03'],['game/Packages','game/ProjectSettings','automation/toolchain.lock.json','evidence/toolchain'],
      ['automation/toolchain.lock.json','evidence/toolchain/android-build-probe.json'], 'foundation', ['game','build','--profile','xr-probe'],
      ['actual ARM64 IL2CPP APK and BuildReport exist','resolved exact Editor/URP/OpenXR/Core/ISDK/MRUK/SDK/NDK/JDK hashes recorded','one rig and input provider','no duplicate or redirected MCP'],['T03'],gates=['local_working_storage','unity_setup_authorization'],locks=['unity-project','heavy-job'])
    task('F05','Qualify minimal XR probe on Quest 3 and Quest 3S',['F04'],['evidence/toolchain/device'],
      ['evidence/toolchain/device/probe.json'], 'foundation', ['device','qualify','--scope','xr-probe','--models','quest3,quest3s'],
      ['actual authorized devices and OS/build identities','hands, passthrough, fallback, placement and suspend observed on both','no simulator evidence accepted as physical','exact package lock accepted only after probe'],['T03'],gates=['quest_devices'],mode='device',locks=['heavy-job'])
    task('F06','Prove Blender source/export/Unity calibration',['F03'],['art/calibration','tools/art','evidence/calibration','game/Assets/Resq/Art/Calibration'],
      ['art/calibration/unit.blend','art/calibration/unit.fbx','evidence/calibration/unity-contact-sheet.png','evidence/calibration/audit.json'], 'foundation', ['art','audit','--kit','calibration'],
      ['real Blender export succeeds without untrusted auto-exec','measured units/pivot/axes round trip','actual Unity render inspected','no desktop or unrelated Blender state changed'],['T01','T15'],gates=['local_working_storage'],locks=['unity-project','blender-calibration','heavy-job'])
    task('F07','Snapshot current rules, provenance and entrant prerequisites',['F01','F02'],['docs/rules','evidence/rules'],
      ['evidence/rules/rules-check.md','evidence/rules/eligibility-gates.json'], 'foundation', ['doctor','--scope','competition-rules'],
      ['live primary-source rules/deadline captured','Gaming/New Experience interpretation and provenance documented','unknown eligibility remains explicit','final submission remains entrant action'],['T02'])
    task('F08','Inspect actual Mobbin references and lock original website direction',['F01','F02'],['web/design','evidence/web-research'],
      ['web/design/reference-lock.md','evidence/web-research/references.json'], 'web', ['web','check','--stage','reference-lock'],
      ['actual screenshot inspection for game/media, editorial and support needs','one dominant reference and at most two supporting patterns','IDs, URLs, observations, exact tokens/layout annotation recorded','no research screenshots treated as licensed public game media'],['T24'],gates=['mobbin_access'])


def add_core():
    rows=[
      ('C01','Compile typed immutable runtime content and canonical schemas',['F03'],['game/Assets/Resq/Content','game/Assets/Resq/Editor/Content','content/runtime'],['game/Assets/Resq/Editor/Content/ContentCompiler.cs','evidence/content/compiler.json'],'runtime-content',['T05'],['IDs, budgets, bounds, roles, topology, temporal objectives and events validated','invalid/duplicate/future-schema fixtures rejected','authoring briefs never executed as logic']),
      ('C02','Implement deterministic clocks, canonical state, commands and RNG',['C01'],['game/Assets/Resq/Runtime/Core','game/Assets/Resq/Runtime/Simulation'],['game/Assets/Resq/Runtime/Core/Commands.cs','game/Assets/Resq/Runtime/Simulation/OperationClock.cs'],'runtime-commands',['T04','T08'],['golden canonical/RNG/order fixtures pass','immediate pause/resume and sequence deduplication','same-tick ordering/deadline and all clock profiles exact']),
      ('C03','Implement flood grid and exact resource-conserving transfers',['C02'],['game/Assets/Resq/Runtime/Simulation/Water'],['game/Assets/Resq/Runtime/Simulation/Water/WaterSystem.cs'],'runtime-water',['T06'],['all water golden fixtures pass','old-state shared allocation, sink/source ledger and pump derating exact','high-gradient/overflow/negative-state rejection']),
      ('C04','Implement authored routes, groups, fixed bus and vehicle reservations',['C02'],['game/Assets/Resq/Runtime/Simulation/Rescue'],['game/Assets/Resq/Runtime/Simulation/Rescue/TransportSystem.cs'],'runtime-transport',['T07'],['people conserved at every tick','slots/seats/berths/receiving reservations separate','blocked crossings and partial boarding follow frozen no-teleport policy','step-free routes tested']),
      ('C05','Implement stock, equipment lifecycle and dry power networks',['C03','C04'],['game/Assets/Resq/Runtime/Simulation/Tools'],['game/Assets/Resq/Runtime/Simulation/Tools/EquipmentSystem.cs'],'runtime-equipment',['T08'],['14 definitions dispatch correctly when introduced','no refund/resource/duplicate placement exploit','priority allocation and generator restart exact','unavailable later mechanics reject until world integration']),
      ('C06','Implement temporal objectives, scoring, debrief causes and progression receipts',['C05'],['game/Assets/Resq/Runtime/Simulation/Objectives','game/Assets/Resq/Runtime/Progression'],['game/Assets/Resq/Runtime/Simulation/Objectives/ObjectiveSystem.cs'],'runtime-objectives',['T09'],['all 25 typed objective definitions compile','critical/mastery and terminal safety separate','boundary inclusivity and exact rational score rounding','causal debrief from event ledger; rewards once']),
      ('C07','Implement complete snapshots, atomic saves, migration and replay',['C06'],['game/Assets/Resq/Runtime/Persistence','game/Assets/Resq/Editor/Replay'],['game/Assets/Resq/Runtime/Persistence/SaveStore.cs','game/Assets/Resq/Editor/Replay/ReplayRunner.cs'],'runtime-persistence',['T13'],['resume identical hashes at partial travel/boarding and pending events','truncation/write-failure/future-schema backup fixtures','ordinary resume unassisted, rollback assisted','no pose/room/private data saved']),
      ('C08','Implement bounded forecasts and risk/path explanations',['C07'],['game/Assets/Resq/Runtime/Simulation/Forecast'],['game/Assets/Resq/Runtime/Simulation/Forecast/ForecastService.cs'],'runtime-forecast',['T06'],['live state hash unchanged by forecast','known deterministic horizon matches replay','invalid/risky/valid explanation includes cost/capacity/power','future unknown events excluded and confidence honest']),
    ]
    for ident,title,deps,areas,outputs,suite,orig,assertions in rows:
        task(ident,title,deps,areas,outputs,'gameplay',['game','test','--suite',suite],assertions,orig,locks=['unity-project','heavy-job'])
    task('C09','Implement seeded invariant fuzzer and matrix replay automation',['C08'],['game/Assets/Resq/Tests','tools/content'],
      ['game/Assets/Resq/Tests/EditMode/CommandFuzzer.cs','tools/content/replay_matrix.py'],'qa',['game','test','--suite','runtime-fuzz'],
      ['1000 fixed W1 seeds x1000 commands; each later mechanic adds its own world corpus','illegal/duplicate/stale commands included','assert conservation and deterministic hashes at each tick','no direct success-state solver'],['T20'],locks=['unity-project','heavy-job'])


def add_xr():
    rows=[
      ('X01','Implement safe placement, locked layout choice and fallback',['F04'],['game/Assets/Resq/Runtime/XR/Placement'],'xr-placement',['T11'],['wide/compact tested dimensions','permission denial/tiny/sloped/occluded surface fallback','relocation preserves mission/layout and pauses']),
      ('X02','Implement hands intent resolver, near/ray preview and one-hand controls',['X01','C05'],['game/Assets/Resq/Runtime/Input','game/Assets/Resq/Runtime/XR/Interaction'],'xr-input',['T12'],['same typed commands from near and far','cancel/tracking-null never commits or spends','one selection owner and left/right mirrored access']),
      ('X03','Implement U01-U24 navigation and contextual HUD',['X02','C07'],['game/Assets/Resq/Runtime/UI','game/Assets/Resq/Scenes'],'xr-states',['T12','T19'],['24 states each back/recovery path','separate new-run/continue/interruption flows','scene callback cancellation and memory cleanup','no unreleased navigation']),
      ('X04','Implement lifecycle, save/anchor/tracking recovery',['X03'],['game/Assets/Resq/Runtime/XR/Lifecycle','game/Assets/Resq/Runtime/Persistence'],'xr-recovery',['T13'],['immediate freeze and explicit resume','save error retains valid generation','relocalize/fallback preserves active state','sleep/menu time never advances hazards']),
      ('X05','Complete head-dwell Command mode and accessibility settings',['X04'],['game/Assets/Resq/Runtime/Input/HeadDwell','game/Assets/Resq/Runtime/UI/Accessibility'],'xr-command',['T14'],['entire U01-U24 path and every action','reading never activates; destructive confirmation','advance1s/5s/run and adjusted dwell','hands absent expected in Command']),
      ('X06','Implement controller adapters and verify command equivalence',['X03'],['game/Assets/Resq/Runtime/Input/Controllers'],'xr-controller',['T12','T14'],['all supported rule/input combinations','no controller-exclusive action','same command sequences yield same simulation hashes','pause and cancel remain accessible']),
    ]
    for ident,title,deps,areas,suite,orig,assertions in rows:
        task(ident,title,deps,areas,[areas[0]+'/implementation-manifest.json'],'xr',['game','test','--suite',suite],assertions,orig,locks=['unity-project','heavy-job'])


def mission_tasks(m,world_first=None):
    mid=m['id'];wid=m['world_id'];order=int(mid[-1]);base=f'content/runtime/{wid}/M{order}'
    if wid=='W1': deps=['C08'] if order==1 else ['W1-VERTICAL']
    else:deps=[wid+'-MECHANIC'] if order==1 else [wid+'-FIRST']
    task(mid+'-AUTHOR','Author both runtime layouts and typed content for '+m['title'],deps,[base,'game/Assets/Resq/Content/'+wid+'/M'+str(order)],
      [base+'/mission.json',base+'/compact.layout.json',base+'/wide.layout.json'],'content',
      ['content','validate','--mission',mid,'--stage','author'],
      ['frozen mission groups, counts, stock, events, required/critical/mastery match','layout recipe roles and topology resolved to concrete IDs','both graph candidates and equipment alternatives structurally reachable','default safe capacity and step-free paths; no unfinished geometry'],['T10'] if mid=='W1-M1' else (['T18'] if wid=='W1' else [{'W2':'T33' if order==1 else 'T34','W3':'T35','W4':'T36','W5':'T37'}[wid]]),scope='competition' if wid=='W1' else 'full_release',locks=['content-'+mid])
    task(mid+'-REPLAY','Record two strategies and failure/recovery matrix for '+m['title'],[mid+'-AUTHOR','C09'],[base+'/replays','evidence/replays/'+mid],
      [base+'/replays/manifest.json','evidence/replays/'+mid+'/matrix.json'],'qa',
      ['content','replay','--mission',mid,'--matrix','all-supported'],
      ['2 layouts x3 rules x2 meaningfully distinct successes =12 base executions','named challenges each2 layouts x2 successes','no-input does not win; deliberate mistake yields actual cause','save/resume and forecasting match decisive-tick hashes','at least one three-star solution per layout/profile; no direct flag writes'],['T10'] if mid=='W1-M1' else (['T18'] if wid=='W1' else [{'W2':'T33' if order==1 else 'T34','W3':'T35','W4':'T36','W5':'T37'}[wid]]),scope='competition' if wid=='W1' else 'full_release',locks=['content-'+mid,'unity-project','heavy-job'])
    task(mid+'-INTEGRATE','Integrate playable blockout '+m['title'],[mid+'-REPLAY','X04'],['game/Assets/Resq/Content/'+wid+'/M'+str(order),'game/Assets/Resq/Presentation/'+mid],
      ['game/Assets/Resq/Content/'+wid+'/M'+str(order)+'/Board.prefab'],'gameplay',
      ['game','test','--suite','mission-blockout','--mission',mid],
      ['actual Operation scene uses real simulation','equipment routes warning/outcome/debrief visible','no residents or occupied vehicle grabbing','one-hand near/ray completion; pause/resume and fallback'],['T10'] if mid=='W1-M1' else (['T18'] if wid=='W1' else [{'W2':'T33' if order==1 else 'T34','W3':'T35','W4':'T36','W5':'T37'}[wid]]),scope='competition' if wid=='W1' else 'full_release',locks=['unity-project','heavy-job'])
    task(mid+'-POLISH','Integrate final art/audio/onboarding for '+m['title'],[mid+'-INTEGRATE',wid+'-KIT',wid+'-AUDIO',wid+'-ONBOARD'],['game/Assets/Resq/Content/'+wid+'/M'+str(order),'game/Assets/Resq/Presentation/'+mid],
      ['evidence/renders/'+mid+'/contact-sheet.png','evidence/content/'+mid+'/integration.json'],'art',
      ['game','test','--suite','mission-integrated','--mission',mid],
      ['audited prefabs and final contextual sounds replace blockout','threatened/recovered states and signature payoff present','UI readable from seated view in both layouts and reduced effects','missing references/texture/rig errors zero'],['T17'] if mid=='W1-M1' else (['T18','T19'] if wid=='W1' else [{'W2':'T34','W3':'T35','W4':'T36','W5':'T37'}[wid]]),scope='competition' if wid=='W1' else 'full_release',locks=['unity-project','heavy-job'])
    task(mid+'-QUALIFY','Run final automated mission regression for '+m['title'],[mid+'-POLISH','X05','X06'],['evidence/content/'+mid,'evidence/regression/'+mid],
      ['evidence/regression/'+mid+'/qualification.json'],'qa',
      ['game','test','--suite','mission-qualification','--mission',mid],
      ['all required EditMode/PlayMode checks executed with0required skips','replay matrix rerun against integrated content hashes','tracking/anchor/save/mistake/no-input recovery regression','final three-star/reference strategies preserved; noP0/P1'],['T20'] if wid=='W1' else [{'W2':'T34','W3':'T35','W4':'T36','W5':'T37'}[wid]],scope='competition' if wid=='W1' else 'full_release',locks=['unity-project','heavy-job'])


def add_world(wid,previous):
    scope='competition' if wid=='W1' else 'full_release'
    orig={'W1':['T16','T19'],'W2':['T33','T34'],'W3':['T35'],'W4':['T36'],'W5':['T37']}[wid]
    if wid=='W1':
        task('W1-TUTORIAL-LOGIC','Implement persisted guided first rescue and unguided replay',['W1-M1-INTEGRATE'],['game/Assets/Resq/Content/Tutorial'],
          ['game/Assets/Resq/Content/Tutorial/W1Tutorial.asset'],'gameplay',['game','test','--suite','tutorial-logic'],
          ['real bridge/bus rescue then barrier/skiff guidance','tutorial and hazard epochs distinct/persisted','A/B replays use same unguided configuration','no timed instruction assuming action occurred'],['T17'],locks=['unity-project','heavy-job'])
        task('W1-VERTICAL','Qualify complete W1-M1 mechanics slice before final art',['W1-M1-INTEGRATE','W1-TUTORIAL-LOGIC','F05','X04'],['evidence/vertical-slice'],
          ['evidence/vertical-slice/qualification.json'],'qa',['device','qualify','--scope','vertical-slice','--mission','W1-M1','--models','quest3,quest3s'],
          ['real complete playable rescue on both models','both strategies and cause/effect interpretable','hand-only placement through outcome and recovery','blockout clearly labeled; initial usability observations recorded'],['T10','T12','T13','T17'],gates=['quest_devices'],mode='device',locks=['heavy-job'])
        task('ART-LOOKDEV','Lock cottage bridge skiff resident set in actual Unity',['W1-VERTICAL','F06'],['art/lookdev','art/provenance','evidence/lookdev'],
          ['art/lookdev/look-lock.json','evidence/lookdev/contact-sheet.png'],'art',['art','audit','--kit','lookdev'],
          ['four source/export assets meet common style/pivots/materials','6 specified lighting/view scenarios rendered in Unity','machine review and human review distinct','style source repaired before kit expansion'],['T15'],locks=['unity-project','blender-lookdev','heavy-job'])
        task('ART-LOOKDEV-HUMAN','Review exact look-development Unity/device contact sheets',['ART-LOOKDEV'],['art/lookdev','evidence/lookdev/human'],
          ['evidence/lookdev/human/review.json'],'art',['human','validate','--scope','lookdev','--review','evidence/lookdev/human/review.json'],
          ['real human reviews exact fourasset images and deviceviews','all aesthetic/material/silhouette/contact criteria accepted','AI findings remain separately labeled','approval binds source/export/render hashes'],['T15'],mode='human',evidence_kind='human_signoff')
        task('ART-SHARED','Produce shared equipment people infrastructure UI and animations',['ART-LOOKDEV-HUMAN'],['art/source/shared','art/exports/shared','game/Assets/Resq/Art/Shared'],
          ['art/manifests/shared.json'],'art',['art','audit','--kit','shared'],
          ['inventory/counts/budgets from production acceptance met','all sources rights LODs colliders materials rigs actual import evidence','later equipment sources built with its world, not mass-generated early'],['T16'],locks=['unity-project','blender-shared','heavy-job'])
        task('ART-HF-BRIEF','Prepare priced Higgsfield asset batch and scoped destination',['ART-LOOKDEV-HUMAN'],['art/briefs/higgsfield','evidence/provider-preflight'],
          ['art/briefs/higgsfield/batch.json','evidence/provider-preflight/higgsfield.json'],'art',['art','audit','--kit','higgsfield-request'],
          ['actual current models/formats/rights/costs discovered','exact assetbriefs dimensions revisions prompt references and maximum credits','unknown cost remains blocker','concrete reviewable request; no provider mutation implied'],['T15','T16'])
        task('ART-HF-GENERATE','Execute only authorized bounded Higgsfield batch and audit sources',['ART-HF-BRIEF'],['art/raw/higgsfield','art/provenance/higgsfield','evidence/provider-operations'],
          ['art/provenance/higgsfield/operations.json','art/provenance/higgsfield/rights.json'],'art',['art','audit','--kit','higgsfield-results'],
          ['exactprovider/project/budget authorization','realGLB/source format and operationIDs reconciled on timeout','actualcredits/license/rawhashes preserved','source requires Blender/Unity qualification before use','optional provider path; original Blender assets can satisfy kit gate'],['T16'],gates=['paid_generation_authorization'],mode='external',locks=['higgsfield-project'])
        first='ART-SHARED'
    else:
        suite={'W2':'runtime-fire','W3':'runtime-stability','W4':'runtime-tide','W5':'runtime-lava'}[wid]
        task(wid+'-MECHANIC','Implement and test '+{'W2':'fire wind wetness finite-water','W3':'stability runout winch','W4':'tide current relay windows','W5':'lava cooling coolant shelter'}[wid]+' mechanic',[previous,'C09'],['game/Assets/Resq/Runtime/Simulation/'+wid,'game/Assets/Resq/Tests/'+wid],
          ['game/Assets/Resq/Runtime/Simulation/'+wid+'/MechanicSystem.cs'],'gameplay',['game','test','--suite',suite],
          ['frozen v1 equations/units/limits and golden fixtures','new mechanic consequential versus prior world','conservation/temporal/save/forecast integration','no future world scene mutation of frozen candidate'],orig,scope=scope,locks=['unity-project','heavy-job'])
        task(wid+'-FIRST','Qualify new mechanic first mission before kit expansion',[wid+'-M1-INTEGRATE','F05'],['evidence/first-missions/'+wid],
          ['evidence/first-missions/'+wid+'/qualification.json'],'qa',['device','qualify','--scope','first-mission','--mission',wid+'-M1','--models','quest3,quest3s'],
          ['new mechanic understandable in hand-only complete mission','two reference strategies work','both models and one-hand path exercised','no final art claim for blockout'],orig,scope=scope,mode='device',gates=['quest_devices'],locks=['heavy-job'])
        first=wid+'-FIRST'
    task(wid+'-KIT','Produce audited final '+wid+' art and equipment kit',[first],['art/source/'+wid,'art/exports/'+wid,'game/Assets/Resq/Art/'+wid],
      ['art/manifests/'+wid+'.json','evidence/renders/'+wid+'/kit-contact-sheet.png'],'art',['art','audit','--kit',wid],
      ['exact inventory in production acceptance','neutral/threatened/recovered variants reuse sources','dimensions/material/LOD/collider/license/import checks pass','real Unity images inspected'],orig,scope=scope,locks=['unity-project','blender-'+wid,'heavy-job'])
    task(wid+'-AUDIO','Produce and integrate '+wid+' sound set',[first],['art/audio/'+wid,'game/Assets/Resq/Audio/'+wid],
      ['art/audio/'+wid+'/manifest.json'],'art',['art','audit','--kit','audio-'+wid],
      ['four original/licensed stems +world ambience/sting and relevant effects','no missing warnings/captions/concurrency setup','seamless loops clipping/license checks','final warning audibility human checks remain required'],orig,scope=scope,locks=['unity-project','heavy-job'])
    task(wid+'-ONBOARD','Finish '+wid+' onboarding aftercare and progression',[wid+'-M1-INTEGRATE',wid+'-KIT',wid+'-AUDIO'],['game/Assets/Resq/Content/'+wid+'/Onboarding','game/Assets/Resq/Content/'+wid+'/Aftercare'],
      ['game/Assets/Resq/Content/'+wid+'/Onboarding/manifest.json','game/Assets/Resq/Content/'+wid+'/Aftercare/manifest.json'],'gameplay',['game','test','--suite','world-onboarding','--world',wid],
      ['new mechanic real state-driven onboarding and reset','all five mission cards/postcards and aftercare behaviors','rewards/unlocks idempotent; reduced effects complete','English captioned warnings; credits/privacy accurate'],['T17','T19'] if wid=='W1' else orig,scope=scope,locks=['unity-project','heavy-job'])
    finalmissions=[f'{wid}-M{n}-QUALIFY' for n in range(1,6)]
    task(wid+'-REGRESSION','Audit '+wid+' and all previously completed worlds',finalmissions,['evidence/regression/'+wid],
      ['evidence/regression/'+wid+'/world.json'],'qa',['game','test','--suite','world-regression','--world',wid,'--include-previous'],
      ['all released content/prefabs/replays valid','legal+illegal fuzzer corpus','full U01-U24 routes, local offline behavior and transitions','noP0/P1 and no unbounded growth'],['T20'] if wid=='W1' else orig,scope=scope,locks=['unity-project','heavy-job'])
    task(wid+'-PERFORMANCE','Profile optimize and thermally qualify '+wid,[wid+'-REGRESSION'],['evidence/performance/'+wid,'game/Assets/Resq/Presentation','game/ProjectSettings'],
      ['evidence/performance/'+wid+'/qualification.json'],'qa',['device','qualify','--scope','performance','--world',wid,'--models','quest3,quest3s'],
      ['30min soak/model native72Hz worst-case build','CPU p95<=10ms GPU<=11ms simulation<=1.5ms','hotpath0B allocation and stable repeated world memory','all patches trigger affected regression and new immutable build identity'],['T22'] if wid=='W1' else orig,scope=scope,mode='device',gates=['quest_devices','working_storage'],locks=['heavy-job','unity-project'])
    task(wid+'-DEVICE','Qualify full input room lifecycle and offline matrix '+wid,[wid+'-PERFORMANCE'],['evidence/device/'+wid],
      ['evidence/device/'+wid+'/qualification.json'],'qa',['device','qualify','--scope','world','--world',wid,'--models','quest3,quest3s'],
      ['required model xmission xlayout xinput coverage','cold-start-to-exit hand-only and left/right','room denial/clutter/recenter/anchor/tracking/sleep','all five complete in airplane mode; no evidence substitutions'],['T21'] if wid=='W1' else orig,scope=scope,mode='device',gates=['quest_devices'],locks=['heavy-job'])
    task(wid+'-HUMAN','Observe and validate player understanding comfort and replay '+wid,[wid+'-DEVICE'],['evidence/human/'+wid],
      ['evidence/human/'+wid+'/observations.json','evidence/human/'+wid+'/qualification.json'],'qa',['human','validate','--world',wid,'--observations','evidence/human/'+wid+'/observations.json'],
      ['consented authentic observations with build identity','numeric gates in production acceptance','no unresolved recurring control/comfort problem','no AI/bot observation or invented participant evidence'],['T21'] if wid=='W1' else orig,scope=scope,mode='human',gates=['human_testers'])
    task(wid+'-FREEZE','Freeze complete qualified '+wid+' candidate',[wid+'-REGRESSION',wid+'-PERFORMANCE',wid+'-DEVICE',wid+'-HUMAN'],['releases/'+wid,'evidence/releases/'+wid],
      ['releases/'+wid+'/release-manifest.json','releases/'+wid+'/artifact-manifest.json'],'release',['release','package','--scope','world','--world',wid],
      ['signed APK actual file/hash/certificate fingerprint','exact code/content/rules/toolchain/assets linked','all five missions and required access paths qualified','noP0/P1/P2; reviewedP3 listed','new branch/artifact track for next world; frozen candidate immutable'],['T23'] if wid=='W1' else orig,scope=scope,locks=['release-manifest','unity-project','heavy-job'])


def add_delivery():
    task('D01','Implement real capture director and source manifest',['W1-FREEZE'],['game/Assets/Resq/Editor/Capture','captures/plans','tools/capture'],
      ['game/Assets/Resq/Editor/Capture/CaptureDirector.cs','captures/plans/competition-edl.json'],'capture',['capture','--stage','plan'],
      ['fixed160second storyboard and source requirements','human/device/replay/simulator types distinct','camera isolated from scoring/input','insufficient footage remains blocked, not synthesized'],['T25','T28'],locks=['unity-project'])
    task('D02','Capture approved actual W1 gameplay and room/input demonstrations',['D01'],['captures/raw','web/public/media','captures/manifests'],
      ['captures/manifests/competition.json'],'capture',['capture','--stage','record','--world','W1'],
      ['real frozen build/source/run/device/hash provenance','all storyboard claims covered','raw sources preserved; actual human hand/room shots','private room consent reviewed before reuse'],['T25'],gates=['capture_consent','quest_devices','working_storage'],mode='device',locks=['heavy-job'])
    task('D03','Assemble and fully review 160second gameplay edit',['D02'],['captures/edits','releases/video','evidence/video'],
      ['captures/edits/edl.json','releases/video/resq-competition-captioned.mp4','releases/video/resq-competition-master.mp4','releases/video/resq-competition.srt','releases/video/resq-competition.vtt'],'capture',['video','assemble','--profile','competition'],
      ['160seconds target and <180s hard limit','1920x1080 H264/AAC supported source cadence','captioned and clean master +SRT/VTT','-16LUFS target <=-1dBTP; no fake interpolation','entire export reviewed; first/last/transitions checked'],['T28'],gates=['working_storage'],locks=['heavy-job'])
    task('D03-REVIEW','Human watches and accepts exact complete W1 video exports',['D03'],['evidence/video/human'],
      ['evidence/video/human/review.json'],'capture',['human','validate','--scope','video','--edition','W1','--review','evidence/video/human/review.json'],
      ['humanwatched entire captioned and clean exports','causality framing captions intelligibility music/privacy accepted','review binds exact master/caption/source hashes'],['T28'],mode='human',evidence_kind='human_signoff')
    task('D04','Implement truthful complete Next.js website',['F08','D02'],['web/src','web/content','web/public','web/package.json','web/package-lock.json'],
      ['web/package.json','web/package-lock.json','web/content/release.json'],'web',['web','check','--stage','local'],
      ['all specified required routes and functional unavailable-media state','real game media and release-derived claims','npm pinned dependency lock no Corepack replacement','keyboard reduced-motion responsive content privacy/support','no fake store/waitlist/testimonial/empty links'],['T26'])
    task('D05','Qualify local website and package deploy candidate',['D04'],['evidence/web/local','web'],
      ['evidence/web/local/qualification.json'],'web',['web','check','--stage','release-candidate'],
      ['type/lint/content/build/browser/media/secret checks actual','320/360/768/1440 widths','Lighthouse performance/a11y>=90 LCP<=2.5s CLS<=0.1','automated accessibility plus visual/focus review','field INP not claimed without real field data'],['T27'],gates=['support_identity'],locks=['heavy-job'])
    task('D06','Deploy and verify scoped Vercel preview',['D05'],['evidence/web/preview'],
      ['evidence/web/preview/qualification.json'],'web',['web','check','--stage','deploy-preview'],
      ['actual approved account/team/project destination','returned deployment ID/URL and deployed checks','secrets absent; no unrelated project mutation'],['T27'],gates=['vercel_preview_authorization'],mode='external')
    task('D07','Prepare accurate Devpost draft and judge guide',['W1-FREEZE','D03-REVIEW','D05','F07'],['releases/competition'],
      ['releases/competition/devpost-draft.md','releases/competition/judge-guide.md'],'release',['release','package','--scope','competition-draft'],
      ['all claims trace qualified exact build','fiveW1 missions only; future worlds labeled','actual limitations input/fallback/offline/reset','pending channel/publicURLs explicitly unresolved until D09/D10'],['T29'])
    task('D08','Publish authorized site and link owned domain',['D06'],['evidence/web/production'],
      ['evidence/web/production/qualification.json'],'release',['web','check','--stage','deploy-production'],
      ['production authorization scope exact','public routes/media verified','custom domain only with ownership+DNS authorization','domain pending does not block preview or game'],['T30'],gates=['vercel_production_authorization'],mode='external')
    task('D09','Publish approved real video and verify anonymous playback',['D03-REVIEW'],['evidence/video/public'],
      ['evidence/video/public/qualification.json'],'release',['video','verify','--stage','public'],
      ['explicit upload authorization','actual publicYouTube/Vimeo playback no uploader session','duration captions source identity match final export'],['T31'],gates=['video_publication_authorization'],mode='external')
    task('D10','Upload exact competition APK and verify judge channel access',['W1-FREEZE','F07'],['evidence/judge-access'],
      ['evidence/judge-access/qualification.json'],'release',['release','package','--scope','judge-distribution'],
      ['explicit channel authorization and entrant eligibility','channelCompetition exactAPKhash','clean install/channel access and free judging access','no private invitation credentials in public artifacts'],['T31'],gates=['competition_eligibility','release_channel_authorization'],mode='external')
    task('D11','Reconcile final packet against actual public video and judge access',['D07','D09','D10'],['releases/competition'],
      ['releases/competition/final-packet-manifest.json'],'release',['release','package','--scope','competition-final'],
      ['exactAPK/video/content/toolchain/source hashes','working judge/video links inserted into draft','recheck currentrules and immutable frozen submission','site deployment optional to eligibility; no dead advertised links'],['T29','T31'])
    task('D12','Entrant personally submits and archives actual receipt',['D11'],['releases/submission-archive'],
      ['releases/submission-archive/receipt.json'],'human_gate',['release','package','--scope','submission-receipt'],
      ['human eligibility/legal/rights attestations','real entrant submission receipt before currentdeadline','judging artifacts/access frozen through requiredperiod','no bot submission'],['T32'],gates=['entrant_final_submission'],mode='human',evidence_kind='human_signoff')
    task('FULL01','Qualify complete campaign migrations and release matrix',['W5-FREEZE'],['evidence/full-game','releases/full-game-candidate'],
      ['evidence/full-game/qualification.json'],'qa',['game','test','--suite','full-game'],
      ['all25missions base+challenge matrix rerun','W1save upgrade preserves completion and partitions incompatible scores','bothmodels fullinput room offline memory profiles','zeroP0/P1; no omitted mission'],['T38'],scope='full_release',locks=['unity-project','heavy-job'])
    task('FULL-PERFORMANCE','Requalify all five worlds on exact final APK',['FULL01'],['evidence/full-game/performance'],
      ['evidence/full-game/performance/qualification.json'],'qa',['device','qualify','--scope','performance','--world','all','--models','quest3,quest3s'],
      ['exact finalbuild all5worlds xbothmodels x30minthermal','all40 five-minute scene/layout/quality profiles','native72Hz timing/allocation/memory budgets','no inherited obsolete build evidence'],['T38'],scope='full_release',mode='device',gates=['quest_devices','working_storage'],locks=['heavy-job'])
    task('FULL-DEVICE','Run all 210 physical mission cases on final candidate',['FULL-PERFORMANCE'],['evidence/full-game/device'],
      ['evidence/full-game/device/qualification.json'],'qa',['device','qualify','--scope','world','--world','all','--models','quest3,quest3s'],
      ['210current candidate physical mission cases and all recovery/room states','complete hands/onehand/Command/controller paths','all25 airplane-mode operations','eachproof exact finalAPK OS/content/toolchain'],['T38'],scope='full_release',mode='device',gates=['quest_devices'],locks=['heavy-job'])
    task('FULL-HUMAN','Accept full campaign comfort clarity and visual/audio quality',['FULL-DEVICE'],['evidence/full-game/human'],
      ['evidence/full-game/human/observations.json','evidence/full-game/human/qualification.json'],'qa',['human','validate','--world','all','--observations','evidence/full-game/human/observations.json'],
      ['all five human gates and each mission2authentic participants','exact finalbuild observations or explicitly reviewed compatibility evidence','W1novice target and allnewmechanic comprehension thresholds','no unresolved recurring control/comfort/visual/audio failure'],['T38'],scope='full_release',mode='human',gates=['human_testers'])
    task('FULL-QUALIFY','Freeze full game qualification before final marketing capture',['FULL01','FULL-PERFORMANCE','FULL-DEVICE','FULL-HUMAN'],['releases/full-game-candidate'],
      ['releases/full-game-candidate/qualification-manifest.json'],'release',['release','package','--scope','full-game-only'],
      ['all25 exact currentcandidate tests/evidence closure','actualsignedAPK and sourcearchive hashes','zeroP0/P1/P2; reviewedP3','domain/site/publicvideo approval independent'],['T38'],scope='full_release',locks=['release-manifest'])
    task('FULL02','Resolve name rights price ratings and current store requirements',['FULL-QUALIFY'],['releases/commercial'],
      ['releases/commercial/owner-decisions.json','releases/commercial/store-readiness.json'],'release',['release','package','--scope','commercial-readiness'],
      ['actualowner nameclearance/rights records','approved premiumprice and markets/ratings','currentplatform policy/entitlement offline behavior tested','no unapproved purchase/account or fabricated legal approval'],['T38'],scope='full_release',mode='human',gates=['commercial_owner_decisions'],evidence_kind='human_signoff')
    task('FULL03','Capture fullgame media and update truthful release site',['FULL-QUALIFY','D01','D05'],['captures/full-game','web/content','web/public/media'],
      ['captures/full-game/manifest.json','web/content/full-release.json'],'capture',['capture','--stage','full-release'],
      ['realqualified clips for all5worlds','firstchapter frozen media retained','siteclaim reconciliation and localwebsuite rerun','all25 actualmissions accurately described'],['T38'],scope='full_release',gates=['capture_consent','quest_devices','working_storage'],mode='device',locks=['heavy-job'])
    task('FULL-VIDEO','Assemble and review separate full five-world 160second edition',['FULL03','D03'],['captures/full-game/edits','releases/video/full-game'],
      ['releases/video/full-game/master.mp4','releases/video/full-game/captioned.mp4','releases/video/full-game/captions.srt','releases/video/full-game/captions.vtt','captures/full-game/edits/edl.json'],'capture',['video','assemble','--profile','full-game'],
      ['new fullgame160second timeline exact10segments from06','realqualified footage for all5worlds','provenance technical/audio/caption tests; humanreview separatelygated','W1masters remain immutable'],['T38'],scope='full_release',gates=['working_storage'],locks=['heavy-job'])
    task('FULL-VIDEO-REVIEW','Human accepts complete exact full-game video edition',['FULL-VIDEO'],['evidence/video/full-game-human'],
      ['evidence/video/full-game-human/review.json'],'capture',['human','validate','--scope','video','--edition','full-game','--review','evidence/video/full-game-human/review.json'],
      ['authentic human watches entire exports','fiveworld claims honest clear and correctly framed','audio/caption/sourceprivacy accepted with exact hashes'],['T38'],scope='full_release',mode='human',evidence_kind='human_signoff')
    task('FULL-WEB','Requalify full-game website and press release bundle',['FULL03','D05'],['web','evidence/web/full-game'],
      ['evidence/web/full-game/qualification.json','web/public/press/press-kit.zip','web/public/press/factsheet.pdf'],'web',['web','check','--stage','release-candidate','--edition','full-game'],
      ['all13required outcomes with all5qualifiedworlds and25missions','5worldclips/10screenshots/realhero/posters/factsheet/credits/support','fulltype/browser/a11y/visual/media/performance rerun','actualsupportcontact owneridentity and licenses'],['T38'],scope='full_release',gates=['support_identity'],locks=['heavy-job'])
    task('FULL-WEB-PREVIEW','Verify final site on approved actual preview',['FULL-WEB'],['evidence/web/full-game-preview'],
      ['evidence/web/full-game-preview/qualification.json'],'web',['web','check','--stage','deploy-preview','--edition','full-game'],
      ['actual finalsite deployment source/content identity','full deployed browser/media/claim checks','exact team/project authorization'],['T38'],scope='full_release',gates=['vercel_preview_authorization'],mode='external')
    task('FULL04','Package immutable signed commercial candidate',['FULL-QUALIFY','FULL02','FULL-VIDEO-REVIEW','FULL-WEB'],['releases/full-game-candidate'],
      ['releases/full-game-candidate/release-manifest.json','releases/full-game-candidate/artifact-manifest.json'],'release',['release','package','--scope','full'],
      ['25qualifiedmissions and allrequired modes','signedAPK/code/rules/content/assets/toolchain/evidence hashes','actualsupport/privacy/credits/storemedia','separate publication gate preserves candidate'],['T38'],scope='full_release',locks=['release-manifest','unity-project','heavy-job'])
    task('FULL05','Publish approved commercial build/site and verify rollback artifacts',['FULL04','FULL-WEB-PREVIEW'],['evidence/full-release'],
      ['evidence/full-release/publication.json'],'release',['release','package','--scope','commercial-publication'],
      ['scoped store/site/media publication approvals','actualcleaninstall and publiclinks','previousknown-good artifact retained; no judgingcandidate mutation','support and version/patch notes match actualrelease'],['T38'],scope='full_release',mode='external',gates=['commercial_publication_authorization'])


def main():
    add_foundation();add_core();add_xr()
    missions=json.loads((ROOT/'specification/content/mission_contracts.json').read_text())['missions']
    for m in missions:mission_tasks(m)
    previous=None
    for wid in ('W1','W2','W3','W4','W5'):
        add_world(wid,previous);previous=wid+'-FREEZE'
    add_delivery()
    # Explicit user amendment: local full25 development precedes physical qualification.
    apply_schedule(TASKS)
    for owner,suite in [('C06','runtime-units'),('C08','runtime-ordering')]:
        ident='check-'+suite
        report='evidence/checks/'+suite+'/{run_id}/{attempt_id}.json'
        next(t for t in TASKS if t['id']==owner)['acceptance_checks'].append(ident)
        CHECKS.append({'id':ident,'task_id':owner,'execution_kind':'automated',
          'command':['python3','tools/resq.py','game','test','--suite',suite,'--report',report],
          'report_path':report,'assertions':[{'id':ident+'-a01','description':'all normative golden fixture and boundary cases execute in actual C#','required':True}],
          'required_evidence_kind':'automated','identity_fields':['freeze_id','rules_version','source_identity','content_hash','toolchain_hash'],
          'runner_status':'pending_implementation','implemented_by':'F02','failure_policy':'nonzero exit on mismatch; no skipped required cases'})
    for c in CHECKS:
        tid=c['task_id'];cov={}
        if tid.endswith('-AUTHOR') and tid[:5].startswith('W'):
            cov={'layout_count':{'exact':2}}
        if tid.endswith('-REPLAY'):
            cov={'base_success_configurations':{'exact':12},'challenge_success_configurations':{'exact':8 if tid.startswith('W1-') else 4},
                 'target_runtimes':{'set':['host']},'repeats_per_target':{'minimum':2},'three_star_configurations':{'minimum':6}}
        if tid.endswith('-QUALIFY') and tid[:5].startswith('W'):
            cov={'base_success_configurations':{'exact':12},'challenge_success_configurations':{'exact':8 if tid.startswith('W1-') else 4},
                 'target_runtimes':{'set':['host','quest3','quest3s']},'repeats_per_target':{'minimum':2},'three_star_configurations':{'minimum':6}}
        for w in range(1,6):
            if tid==f'W{w}-DEVICE':cov={'physical_full_mission_runs':{'minimum':42},'models':{'set':['quest3','quest3s']}}
            if tid==f'W{w}-PERFORMANCE':cov={'thermal_minutes_per_model':{'minimum':30},'thermal_soaks':{'minimum':2},'five_minute_profiles':{'minimum':8},'models':{'set':['quest3','quest3s']}}
            if tid==f'W{w}-HUMAN':cov={'consenting_participants':{'minimum':10 if w==1 else 5},'mission_count':{'exact':5}}
            if tid==f'W{w}-REGRESSION':cov={'base_success_configurations':{'exact':w*60},'challenge_success_configurations':{'exact':40+(w-1)*20}}
        if tid=='C09':cov={'fuzzer_seeds_per_world':{'minimum':1000},'attempted_commands_per_seed':{'minimum':1000}}
        if tid=='FULL01':cov={'base_success_configurations':{'exact':300},'challenge_success_configurations':{'exact':120},'mission_count':{'exact':25}}
        if tid=='FULL-DEVICE':cov={'physical_full_mission_runs':{'minimum':210},'models':{'set':['quest3','quest3s']},'mission_count':{'exact':25}}
        if tid=='FULL-PERFORMANCE':cov={'thermal_soaks':{'minimum':10},'thermal_minutes_per_model':{'minimum':30},'five_minute_profiles':{'minimum':40},'models':{'set':['quest3','quest3s']}}
        if tid in ('D05','FULL-WEB'):cov={'widths_css_px':{'set':[320,360,768,1440]},'required_page_outcomes':{'exact':13}}
        if tid in ('D03','FULL-VIDEO'):cov={'timeline_seconds':{'exact':160},'caption_variants':{'exact':2},'sidecar_formats':{'set':['srt','vtt']}}
        c['expected_coverage']=cov
        c['case_contract_id']=c['id']
    contracts=[case_contract(c) for c in CHECKS]
    for contract in contracts:
        if any(row['dimensions'].get('target') in ('quest3','quest3s') for row in contract['required_cases']):
            owner=next(t for t in TASKS if t['id']==contract['task_id'])
            if 'quest_devices' not in owner['required_gates']:owner['required_gates'].append('quest_devices')
    dump('specification/content/acceptance_cases.json',{'schema_version':1,'freeze_id':FREEZE,
      'status':'required_cases_unexecuted','contracts':contracts})
    shared=next(c for c in CHECKS if c['task_id']=='ART-SHARED')
    shared['command'][-2:-2]=['--scope-world','W1']
    dump('automation/tasks.json',{'schema_version':2,'freeze_id':FREEZE,'definition_status':'implementation_ready_unexecuted',
      'source_graph':'Resq_Production_Pack/automation/tasks.json','runtime_state_path':'automation/task_state.json',
      'scope':{'worlds':5,'missions':25,'competition_missions':5,'automatic_scope_cuts':False},'tasks':TASKS})
    aliases={'art-shared':'check-art-shared','xr-placement':'check-x01','xr-input':'check-x02','xr-command':'check-x05','xr-states':'check-x03',
      'web-local':'check-d05','web-preview':'check-d06','web-production':'check-d08','video-local':'check-d03-review','video-public':'check-d09',
      'video-full':'check-full-video-review','release-competition':'check-d11','release-full':'check-full04'}
    for w in range(1,6):
        for prefix,suffix in [('art','kit'),('device','device'),('human','human'),('performance','performance')]:aliases[f'{prefix}-W{w}']=f'check-w{w}-{suffix}'
    dump('automation/checks.json',{'schema_version':1,'freeze_id':FREEZE,'commands_status':'required_future_harness','aliases':aliases,'checks':CHECKS})
    p=ROOT/'automation/task_state.json'
    state=json.loads(p.read_text()) if p.exists() else {'schema_version':1,'freeze_id':FREEZE,'task_states':{}}
    for t in TASKS:
        state['task_states'].setdefault(t['id'],{'status':'pending','result_path':None,'repair_attempts':0})
        state['task_states'][t['id']].setdefault('result_sha256',None)
    dump('automation/task_state.json',state)
    gates=[
      ('host_license','account','Preflight confirms installed Editor and valid current license readiness; no activation bypass','Readiness permits F03; actual licensed compilation is required by F03 acceptance, never by its own prerequisite gate.'),
      ('host_working_storage', 'machine', 'F03only: exact lean native-generated host packages, <=8MiBassets, measured64MiBgrowthcap +64MiBovershoot +2GiBhardreserve; fresh volume observation and owned cancellation. Does not admit Android/render/other tasks.', 'User-directed F03 reserve reassessment16October2026; separate from unchanged working_storage gate for other operations.'),
      ('local_working_storage', 'machine', 'F04/F06 staged local work only: fresh per-stage admission from local_operation_profiles.json;2GiB hard reserve plus explicit enforced growth budget and cancellation margin; unknown full peaks stay unknown. No task acceptance from readiness.', 'User-directed16October2026 reassessment. Reuse installed Android tools; do not change global preferences or other projects. Generic large-operation policies unchanged.'),
      ('working_storage','machine','Use06section3.1: Unity max40GiB,peak+10; art max15GiB,3xnew+10; web max10GiB,peak+5; capture max20GiB,2xraw+3xexport+10; remeasure outputvolume','User chose freeing internal space; do not delete unrelated work.'),
      ('unity_setup_authorization','permission','Exact Android modules/package and any bridge changes approved','Original policy disallows implicit Unity package/MCP installation.'),
      ('quest_devices','hardware','Both target devices connected/trusted and model/OS verified','User confirmed access, not connection or qualification.'),
      ('human_testers','human','Consenting adult participants and observer are available; agreed session scope recorded','Readiness permits testing; actual observations are required by Wn-HUMAN/FULL-HUMAN, never by this prerequisite.'),
      ('mobbin_access','account','Actual authenticated image results inspected','Connection probe was successful; full reference lock still required.'),
      ('capture_consent','human','Specific room/person footage consent and privacy review recorded','No raw room uploads implied.'),
      ('vercel_preview_authorization','external','Exact account/team/project and preview exposure approved','Local site must be reviewable first.'),
      ('vercel_production_authorization','external','Exact production destination authorized','Domain additionally requires ownership+DNS authorization; without it use real Vercel host.'),
      ('video_publication_authorization','external','Final reviewed file and destination approved','Public upload separate from local assembly.'),
      ('competition_eligibility','human','Entrant confirms current eligibility/membership/rights','Deadline does not permit scope cuts.'),
      ('release_channel_authorization','external','ExactAPK and Competition channel approved','No silent external release.'),
      ('entrant_final_submission','human','Entrant is ready and accepts personal responsibility for final submission','Readiness permits D12; D12 supplies authentic receipt. No bot registration/legal attestation.'),
      ('commercial_owner_decisions','human','Nameclearance/price/ratings/markets legally reviewed and approved','Do not invent legalclearance or finalprice.'),
      ('commercial_publication_authorization','external','Final build/site/media destination explicitly approved','Local candidate and evidence remain available if blocked.'),
      ('paid_generation_authorization','paid','Exactprovider/project/assetbatch and verifiedcredit cap approved','Defaults0; Blender originals continue independently.'),
      ('support_identity','human','Real owner identity and support contact supplied for truthful privacy/press/support pages','Do not invent company/contact; content model and other local work continue.'),
    ]
    actions={'unity_setup_authorization':['unity_setup'],'vercel_preview_authorization':['preview_deployment'],
      'vercel_production_authorization':['production_deployment'],'video_publication_authorization':['public_video_upload'],
      'release_channel_authorization':['release_channel_upload'],'commercial_publication_authorization':['commercial_publication'],
      'paid_generation_authorization':['paid_generation']}
    # Definition generation must preserve actual volatile observations and approvals.
    gate_path=ROOT/'automation/gates.json'
    previous_gates={g['id']:g for g in json.loads(gate_path.read_text())['gates']} if gate_path.exists() else {}
    gate_rows=[]
    for i,k,p,n in gates:
        row={'id':i,'kind':k,'pass_condition':p,'notes':n,'authorization_actions':actions.get(i,[]),
          'status':'not_verified','evidence_path':None,'evidence_sha256':None}
        if i in previous_gates:
            row.update({key:previous_gates[i][key] for key in ('status','evidence_path','evidence_sha256')})
        gate_rows.append(row)
    dump('automation/gates.json',{'schema_version':1,'gates':gate_rows})
    policy=json.loads((ROOT/'Resq_Production_Pack/automation/autonomy_policy.json').read_text())
    policy['notes'].append('Freeze1.1.0 preserves full25scope; generating planning artifacts grants no spending/publication/install approval.')
    if not (ROOT/'automation/autonomy_policy.json').exists():dump('automation/autonomy_policy.json',policy)
    print(f'Authored {len(TASKS)} pending implementation tasks and {len(CHECKS)} acceptance checks; no task executed.')


if __name__=='__main__':main()
