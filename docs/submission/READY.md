# RESQ submission: what's ready and what's left

Updated 15 Nov 2026. Nothing has been submitted. Deadline: Wed 18 Nov, 12:00 PT (22:00 Cairo); aim for Tue 17 Nov.

Evidence labels: automated (Editor or headless), simulator (Meta XR Simulator), device (headset), human.

## Entry
- **Team of Individuals.** The Representative is the entrant who owns:
  - the Meta account shown as "Mo Tarek", username osamo (an active Meta VR Start member);
  - the Devpost account "osamo" (creator name Osama Mohamed).

  The user confirmed on 15 Nov that these are one person.
- **Amir Lotfy** is a listed non-Representative teammate. Waz World is Amir's solo Entry, submitted 15 Nov. Amir must not submit RESQ or represent it.
- **Don't use** Amir's own `Resq` Devpost draft (project 1260669). Never submit it.
- **Privacy:** keep the entrant's email and account ids out of the repo and commits.

## Meta (entrant's account, built-in browser)
| Item | State |
|---|---|
| Team | **ZeOnes** (existing; the name shows in the store) |
| App | **RESQ**, App ID 1493749823815011, Meta Horizon Store, not released. Never press "Start submission" |
| Developer Distribution Agreement | **Not signed: the entrant signs it** (Release channels → "View and sign") |
| Age group self-certification | **Done 15 Nov:** Teens and adults (13+), from the owner's interview answers (designed for 13 and older; RESQ is offline, with no accounts, chat, ads or data collection). Can be changed any time |
| "Competition" channel | To create after the DDA: private, "URL Auto Join"; copy the invite URL |
| Upload | `metavr auth login` (device code; the entrant approves it in his signed-in browser), then `metavr store dist upload --app-id 1493749823815011 --channel Competition --apk build/resq-release.apk --notes "…" --publish`, confirmed with the user at that moment |

## Devpost draft "RESQ" (submission 1284442, entrant's account)
| Step | State |
|---|---|
| Overview | Name RESQ, tagline (131 chars), thumbnail (1500×1000, an in-game render with the title set by script) |
| Details | Story from `devpost-draft.md` (491 words), 16 "Built with" tags, website link, 9 gallery images (in-game renders, captioned). **Video URL missing** |
| Additional info | Team of Individuals, Gaming, New, Unity, team name ZeOnes, capabilities, hand interactions. **Missing:** channel URL; the entrant's email, country, his role and Amir's email in the teammates line; target launch date (optional) |
| Team | Amir joins from his own Devpost account through the project's secret invite link (Manage team) |
| Finalization | The entrant ticks the four eligibility boxes and the terms, and clicks Submit |

## Build and tests
| Item | Evidence |
|---|---|
| Headless: sim 68/68, missions 450/450, challenges 120/120 | automated, 5 Nov |
| Judge-proofing: no permission dialog at launch, virtual table first, room data only on "Use my table", recentre moves the virtual table back, Open play on | automated: `JudgeProofTest` 3/3 (batch-mode PlayMode, 15 Nov) |
| PlayMode gate (now 23 tests) and the 25-mission input matrix | **not run since the changes:** the disk guard stopped the Editor |
| Release key | `~/keys/resq.keystore`, password in the login Keychain. **Back it up off this Mac** |
| Signed 0.2.0 build, then `check_apk.sh` from the competition skill | **not made:** needs about 11 GB free at peak plus the Editor closed |

## Trailer and media
- **Voice-over and music:** script and prompt in `video/voiceover-and-music.md`. If the AI voice or music is used: YouTube altered content Yes, and Google AI Studio goes in Devpost's "Built with".
- **Capture:** frame-stepped Simulator capture (`ResqCapture`, exact left-eye view over a Simulator room still) is written but not yet run.
- **Rough cut:** `video/exports/` holds the choppy 5 Nov version. Not for submission.
- **Website:** https://resq-woad.vercel.app. Its press page lists only Blender under "Made with"; add Meshy and Higgsfield on the next deploy, with the user's OK.

## Blockers for the owner
1. **The entrant:** sign the DDA on RESQ's Release channels page ("View and sign" → "I agree" → Submit). The age group is done.
2. **Disk:** 6 GB free with the Editor closed; swap grows by GBs while Unity runs on this 8 GB Mac. Free at least 10 GB more for tests and capture, and 15 GB for the signed build.
3. **YouTube:** which channel uploads the trailer.
4. **Voice-over and music:** generate them from the script and prompt, or tell me to use the game's own music with captions only.
5. **Back up** the release key.
