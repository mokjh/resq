# RESQ — production specification and Codex handoff

**Small worlds. Big saves.**

A seated, hands-first miniature rescue strategy game for Meta Quest. Prepared 15 October 2026. Working name and intended domain `playresq.com` are provisional; no name clearance or domain purchase is implied.

## What is included

This pack contains a full product/design specification, 25 authored mission briefs across five worlds, a logical data ERD and runtime flow, technical/UX/art requirements, a dependency-based Codex production plan, JSON contracts, a real pack-consistency validator, website/video specifications, release gates and a starter prompt.

**It does not contain a built game, APK, finished 3D assets, recorded gameplay, a rendered trailer or a deployed website.** Machine-readable mission briefs describe design; they are not runtime level geometry. The included validator tests this pack's integrity, not game playability.

## Start

1. Extract the pack into a new selected working folder or an appropriate documentation area of the actual project. Preserve existing work.
2. Run `python3 tools/validate_pack.py` from this folder. It needs only the Python standard library.
3. Open the folder in Codex. Paste `CODEX_START_PROMPT.md`; Codex first reads `AGENTS.md`, discovers the real environment and records any required setup/permission gates.
4. Build and qualify Alder Valley before expanding the commercial catalogue. Use `automation/tasks.json` as a dependency plan, not an already running orchestrator.

No Unity package versions are fabricated. `automation/toolchain.lock.template.json` must become a real lock after a compatible Android/XR probe. Paid generation and public writes remain disabled in the policy until actual authorization is recorded.

## Document map

| File | Purpose |
|---|---|
| `docs/00_DECISIONS_AND_SOURCES.md` | Verified rules, assumptions, naming, sources and truth boundaries |
| `docs/01_PRODUCT_PRD.md` | Product identity, audience, pillars, requirements, modes and release scope |
| `docs/02_GAMEPLAY_SYSTEMS.md` | Core rules, tools, hazards, routes, states, scoring and tutorial |
| `docs/03_WORLD_MISSION_BIBLE.md` | Five worlds and 25 detailed authored scenarios |
| `docs/04_UX_ACCESSIBILITY.md` | Full UI/state flow, hands, dwell, seated reach and room fallback |
| `docs/05_TECHNICAL_SPEC.md` | Unity architecture, deterministic simulation, persistence, budgets and builds |
| `docs/06_ERD_AND_DATA_CONTRACTS.md` | Domain entities, keys, invariants, commands and data boundaries |
| `docs/07_ART_AND_ASSET_PIPELINE.md` | Cohesive art direction, source/export pipeline, budgets and rights |
| `docs/08_CODEX_AUTOMATION_PLAN.md` | Capability discovery, safe autonomy, work streams, evidence and tools to implement |
| `docs/09_QA_AND_RELEASE_GATES.md` | Content, simulation, XR, device, comfort, performance and release checks |
| `docs/10_MARKETING_WEBSITE_SPEC.md` | Mobbin research process, original site direction, pages, copy and Vercel gates |
| `docs/11_VIDEO_AND_DEVPOST_SPEC.md` | Real-footage capture, 160-second edit, narration, captions and submission draft |
| `docs/12_ROADMAP_AND_RISK_REGISTER.md` | Competition checkpoints, cuts, full-game expansion and risks |
| `diagrams/` | Mermaid ERD and runtime flow source |
| `content/` | Machine-readable briefs and honest empty release/asset templates |
| `schemas/` | JSON Schema contracts for briefs, assets and task results |
| `automation/` | 38-task dependency plan, autonomy policy and toolchain template |
| `tools/validate_pack.py` | Standard-library pack checker |
| `tools/test_validate_pack.py` | Mutation-based self-tests of the checker |
| `AGENTS.md` | Persistent Codex repository instructions |
| `CODEX_START_PROMPT.md` | Copy/paste kickoff instruction |

`RESQ_MASTER_SPEC.md` and `RESQ_MASTER_SPEC.html` combine the readable documents for browsing. Individual files remain the preferred modular input for coding agents. The ERD describes offline domain data; no unnecessary cloud database is prescribed.

## Scope in one glance

**Competition target:** Alder Valley, five polished flood-rescue missions, tutorial, progression, save/resume, one-hand-completable hand tracking, optional Command mode, room-aware placement with virtual-table fallback, real gameplay trailer and truthful website.

**Commercial design:** add Emberwood (fire/wind), Frostline (structural/vertical rescue), Tidemark (tide/navigation), and Cinder Reach (lava/cooling/shelter). Twenty-five missions total; later worlds are planned, not implemented.

## Important deadline correction

Official deadline: **18 November 2026 at 12:00 PST = 20:00 UTC = 22:00 Cairo**. The earlier conversation's 23:00 Cairo conversion was incorrect. The entrant must recheck rules, eligibility, rights, access and final submission requirements. Final Devpost entry is not an automated task.

## Evidence status

Only the specification pack and its validator can be verified here. Check `evidence/pack_validation.json` and `evidence/validator_self_tests.txt` for actual local validation results when present. They are not Unity, headset, gameplay, marketing or contest results.
