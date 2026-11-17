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
| Developer Distribution Agreement | **Signed by the entrant, 15 Nov** |
| Age group self-certification | **Done 15 Nov:** Teens and adults (13+), from the owner's interview answers (designed for 13 and older; RESQ is offline, with no accounts, chat, ads or data collection). Can be changed any time |
| "Competition" channel | **Created 15 Nov:** private, URL Auto Join (automatic approval), user limit 200. The invite URL appears only after the first build upload ("Please upload a build to enable url"); then it goes into Devpost |
| Upload | `metavr auth login` (device code; the entrant approves it in his signed-in browser), then `metavr store dist upload --app-id 1493749823815011 --channel Competition --apk build/resq-release.apk --notes "…" --publish`, confirmed with the user at that moment |


**17 Nov:** build 0.2.0 (26111709) uploaded and published to Competition as Build ID 1498012956722031 by the entrant's `metavr` sign-in ("Mo Tarek"). Meta's Basic malware test and Security Vulnerability Review both passed. Invite URL: https://www.meta.com/s/6Qf2ugekL (in the Devpost form). `metavr` is now signed in as the entrant, not Amir.

## Devpost draft "RESQ" (submission 1284442, entrant's account)
| Step | State |
|---|---|
| Overview | Name RESQ, tagline (131 chars), thumbnail (1500×1000, an in-game render with the title set by script) |
| Details | Story from `devpost-draft.md` (491 words), 16 "Built with" tags, website link, 9 gallery images (in-game renders, captioned). **Video URL missing** |
| Additional info | Team of Individuals, Gaming, New, Unity, team name ZeOnes, capabilities, hand interactions. **17 Nov:** channel URL, Start membership email, country (Egypt), teammates with roles and emails, and the Google AI Studio tag are saved. **Was missing:** the entrant's email, country, his role and Amir's email in the teammates line; target launch date (optional) |
| Team | Amir joins from his own Devpost account through the project's secret invite link (Manage team) |
| Finalization | The entrant ticks the four eligibility boxes and the terms, and clicks Submit |

## Build and tests
| Item | Evidence |
|---|---|
| Headless: sim 68/68, missions 450/450, challenges 120/120 | automated, 5 Nov |
| Judge-proofing: no permission dialog at launch, virtual table first, room data only on "Use my table", recentre moves the virtual table back, Open play on | automated: `JudgeProofTest` 3/3 (batch-mode PlayMode, 15 Nov) |
| PlayMode gate (now 23 tests) and the 25-mission input matrix | **not run since the changes:** the disk guard stopped the Editor |
| Release key | `~/keys/resq.keystore`, password in the login Keychain. **Back it up off this Mac** |
| Signed build `build/resq-release.apk` | **Built 17 Nov:** 0.2.0, versionCode 26111709, 106 MB, release-signed (CN=Amir Lotfy, O=RESQ); the skill's `check_apk.sh` prints APK CHECK PASS. Unity aborted while quitting, after the APK was written |

## Trailer and media
- **Voice-over and music:** script and prompt in `video/voiceover-and-music.md`. If the AI voice or music is used: YouTube altered content Yes, and Google AI Studio goes in Devpost's "Built with".
- **Capture:** frame-stepped Simulator capture (`ResqCapture`, exact left-eye view over a Simulator room still) is written but not yet run.
- **Rough cut:** `video/exports/` holds the choppy 5 Nov version. Not for submission.
- **Website:** https://resq-game.vercel.app. Its press page lists only Blender under "Made with"; add Meshy and Higgsfield on the next deploy, with the user's OK.

## Blockers for the owner
1. **The entrant:** approve the `metavr` device-code sign-in at upload time. The DDA, the age group and the channel are done.
2. **Disk:** 6 GB free with the Editor closed; swap grows by GBs while Unity runs on this 8 GB Mac. Free at least 10 GB more for tests and capture, and 15 GB for the signed build.
3. **YouTube:** which channel uploads the trailer.
4. **Voice-over and music:** generate them from the script and prompt, or tell me to use the game's own music with captions only.
5. **Back up** the release key.

## Repo
Pushed 17 Nov to the public repo github.com/mokjh/resq, from a cleaned copy:
- author rewritten to `Amir Lotfy <178108135+AmirmLotfy@users.noreply.github.com>`;
- the old email and home paths removed from all history;
- `art/` (Meshy and Blender source files, about 340 MB) left out, since the game builds from `game/`.

This local repo is unchanged and has no remote.
