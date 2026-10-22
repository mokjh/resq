"""Validate actual captured research; never turn inaccessible sources into verified facts."""
from datetime import datetime,timezone
from urllib.parse import urlsplit
from bootstrap_doctor import ROOT,read,artifact,digest,relative
from task_execution import Blocked


def native_rows(context,observations):
    contract=next(c for c in read(ROOT/'specification/content/acceptance_cases.json')['contracts'] if c['id']==context['check']['id'])
    if len(observations)!=len(contract['required_cases']):raise ValueError('research assertion count mismatch')
    context['backend_started']=True
    return [{'case_id':c['case_id'],'kind':c['kind'],'dimensions':c['dimensions'],'status':'passed' if passed else 'failed',
      'metrics':{},'participant_alias':None,'proof_index':None,'observations':details}
      for c,(passed,details) in zip(contract['required_cases'],observations,strict=True)]


def inspect_rules(context):
    path=ROOT/'evidence/rules/source-capture.json'
    if not path.is_file():raise Blocked('RULE_SOURCES_NOT_CAPTURED: inspect current official rules/FAQ/announcement first')
    capture=read(path);required={'rules','faq','overview','announcement'};records={r['role']:r for r in capture['sources']}
    accessible=set();proofs=[]
    for role,row in records.items():
        host=urlsplit(row['url']).hostname or ''
        if host not in ('start-developer-competition-26.devpost.com','developers.meta.com'):raise ValueError('research must use declared official hosts')
        p=(ROOT/row['capture_path']).resolve()
        if not p.is_relative_to(ROOT) or not p.is_file() or digest(p)!=row['capture_sha256']:raise ValueError('source capture missing/hash mismatch')
        if row['retrieval_status']=='readable_verified':accessible.add(role)
        proofs.append(artifact(p,'actual_official_source_capture'))
    checked=datetime.fromisoformat(capture['checked_utc'].replace('Z','+00:00'))
    fresh=checked.tzinfo is not None and 0<=(datetime.now(timezone.utc)-checked).total_seconds()<86400
    outputs=all((ROOT/p).is_file() for p in context['task']['expected_artifacts'])
    eligibility=read(ROOT/'evidence/rules/eligibility-gates.json') if outputs else {}
    return native_rows(context,[
      (required<=accessible and fresh,{'sources':proofs,'accessible_roles':sorted(accessible),'fresh_within_day':fresh}),
      (outputs and capture.get('category_interpretation_recorded') is True and capture.get('provenance_recorded') is True,{'source_capture':artifact(path),'review':capture.get('category_interpretation')}),
      (eligibility.get('unverified_items') and eligibility.get('eligibility_status')=='requires_entrant_confirmation',{'eligibility':eligibility}),
      (eligibility.get('final_submission')=='entrant_only' and eligibility.get('submitted') is False,{'no_submission_performed':True})])


def inspect_reference_lock(context):
    path=ROOT/'evidence/web-research/references.json';lock=ROOT/'web/design/reference-lock.md'
    if not path.is_file() or not lock.is_file():raise Blocked('REFERENCE_LOCK_NOT_AUTHORED: actual Mobbin images and original direction are required')
    data=read(path);refs=data['references'];dominant=[r for r in refs if r['role']=='dominant'];supporting=[r for r in refs if r['role']=='supporting']
    valid=bool(refs);evidence=[]
    for ref in refs:
        valid=valid and urlsplit(ref['mobbin_url']).hostname=='mobbin.com' and ref.get('visually_inspected') is True
        p=(ROOT/ref['image_path']).resolve()
        valid=valid and p.is_relative_to(ROOT) and p.is_file() and digest(p)==ref['image_sha256']
        if p.is_file():evidence.append(artifact(p,'inspected_research_image_not_public_game_media'))
        valid=valid and bool(ref.get('visual_observations')) and bool(ref.get('layout_annotations'))
    purpose={'game_media','editorial','support'}<=set(data['research_needs_covered'])
    tokens=data.get('original_tokens',{});required={'palette','typography','spacing','layout','motion','responsive'}
    return native_rows(context,[
      (valid and purpose,{'references':evidence,'needs_covered':data['research_needs_covered']}),
      (len(dominant)==1 and len(supporting)<=2,{'dominant_count':len(dominant),'supporting_count':len(supporting)}),
      (required<=set(tokens) and all(tokens[k] for k in required),{'tokens':tokens,'reference_lock':artifact(lock)}),
      (data.get('reference_images_public_game_media') is False and data.get('public_media_source')=='qualified_resq_capture_only',{'public_media_policy':data.get('public_media_source')})])
