# RESQ submission: what's ready and what's left

Updated 5 Nov 2026. Nothing has been submitted or uploaded. Deadline: Wed 18 Nov, 12:00 PT (22:00 Cairo).

Evidence labels: automated (Editor or headless), simulator (Meta XR Simulator), device (headset), human.

## Ready
| Item | Where | Evidence |
|---|---|---|
| 25 missions, both plans, both layouts, 3 presets | `python3 tools/simtest/simtest.py sim` and `lab validate` | automated: sim 68/68, 450/450, 120/120 challenge runs, all 5 Nov |
| PlayMode gate | `tools/unity/gate_when_free.sh` | automated: 20/20, 5 Nov |
| All 25 missions through hand, one-hand and head-gaze input | `GATE=matrix.sh tools/unity/gate_when_free.sh` | **not yet run:** the first attempt on 5 Nov started no tests (script fixed), and the disk is now too full |
| Hands-only journey with real pinches: Hub to placed barrier to running mission | `tools/xr/hands_test.py` | simulator: PASS, 5 Nov |
| XR flow: placement, virtual table, move board, anchor save and restore, hand-loss pause, off-view hint | STATUS item 7 | simulator: 5 Nov |
| Release key | `~/keys/resq.keystore`, password in the login Keychain (`resq-keystore`) | **Back it up off this Mac** |
| Release build and store-rule check | `tools/unity/release_apk.sh`, `tools/unity/check_apk.sh` | written; **signed build not yet made** (needs 21 GB free) |
| Debug APK | `build/resq-xr.apk` | static manifest checks only; predates the 32-bit fix and this week's commits, including real hands and room weather; never run on a headset |
| Devpost text | `docs/submission/devpost-draft.md` | 498/500 words; `[P5]` and `[you]` markers remain |
| Store page copy | `docs/submission/store-listing.md` | within field limits |
| Store art | `docs/submission/store/` | game renders; screenshots are Editor renders until device captures replace them |
| Trailer rough cut | `video/exports/resq-trailer-captioned.mp4` | simulator footage, autopilot-driven, 72 s, QC pass; choppy (2 to 6 captured fps) and not final |
| Website | https://resq-woad.vercel.app | live |

## Entry route: the owner decides
Checked 5 Nov against the Official Rules, the FAQ and the live Devpost form (read on the `Resq` draft, nothing saved).

RESQ can only go in beside Waz World as a **declared team**. That is Permitted Participation Scenario (ii) in Rules §2, and the FAQ says yes to working on one project with a team and another alone. If Mohamed Tarek won't really be part of RESQ's team, enter one game only.

| | Waz World | RESQ |
|---|---|---|
| Submitter Type on the form | Individual | Team of Individuals |
| Submitted by | Amir Lotfy, from Amir's own Devpost account | Mohamed Tarek, from his own Devpost account, as Team Representative |
| Amir's part | Solo entrant | Non-Representative member, listed with name, email and role |

**What connects Devpost to Meta:** only the form. It asks for the email tied to the developer organization or Start membership, each teammate's name, email and role, and the Competition channel URL. The APK carries no App ID or account (checked in the manifest and settings), so the same build can go to whichever team holds the app.

**What voids it:**
- Mohamed enters as an Individual, or Amir is left off the team. Rules §3 disqualifies an Entry made by someone else on an Entrant's behalf, unless that person is a Team or Organization Representative, and §4 needs the Project to be original to the Entrant.
- Anyone other than Mohamed uses his Devpost account. Devpost's terms, which are part of the rules, forbid using another user's account.
- Amir's `Resq` Devpost draft gets submitted. It would be Amir's Entry, and Waz World a second one. Delete it or leave it unsubmitted.
- False information anywhere. §14 allows disqualification and damages, and the Sponsor judges fairness at its sole discretion. A disqualification could reach Waz World too (§3).

**Mohamed, himself:**
1. Meta account with Developer Access (verified with 2FA or a payment method). 18 or over, eligible country.
2. Join Meta VR Start at https://developers.meta.com/horizon/programs/start/apply/. It is mandatory for whoever submits (§2(d)); Horizon Start is the same program. The competition page estimates 5 minutes. Proof is the "Start Member" tag on the Developer Forum; the FAQ says to open a support case if it is missing after 24 hours.
3. Create a new Meta team for RESQ (the name shows on the store) and invite Amir's Meta account as Administrator.
4. As its owner: the NDA, the Developer Distribution Agreement and age self-certification, which unlock "Create channel". Identity verification if Meta asks (government ID and selfie, about two minutes).
5. Join the hackathon on Devpost, create a new project and send Amir the team invite link.
6. At the end: paste from `devpost-draft.md`, tick the attestations, press Submit. Stay reachable until the winners are announced (about 11 Dec): a Representative who can't be reached can be disqualified (§6).

**Amir:**
1. Accept both invites and read the rules: the form attests that every team member has.
2. Put in writing with Mohamed who owns RESQ, how a prize is split and who pays tax. A prize is paid only to the Representative, who allocates it and carries the tax, with no help from Meta (§7). Winner documents can be asked of every team member (§6). The Project stays the property of the people who developed it (§9).
3. Send the manager question below, through "Message manager" on the Devpost project list.
4. Keep Waz World as Amir's one Entry.

**Alternative:** Amir creates the team and invites Mohamed as Administrator. §2(c) accepts administrative control and Amir keeps the app after the contest, but both entries' teams then sit under one login, so get that confirmed by the manager first. The dashboard and CLI only show Frameless Plays today. Don't put RESQ there: it holds Waz World.

Question for the hackathon manager, once Mohamed's real role is filled in:
> Hi, please confirm this setup before we submit. RESQ: Mohamed Tarek is the Team Representative and submits it from his own account; his role is [his real role]. I, Amir Lotfy, am a non-Representative member and built most of the game. We'll list both of us with our roles on the form. The app sits in a Meta developer team Mohamed created, where I'm also an administrator. Separately, I'm submitting a different project, Waz World, as a solo entrant. Does this fit the Entry Limit and Permitted Participation Scenario (ii)?

## Also needs the owner
1. **Empty the Trash.** It holds 21 GB, and under 3 GB is free (5 Nov, 07:15). The matrix needs 10 GB and the signed build 21 GB.
2. **Back up** `~/keys/resq.keystore` off this Mac. Losing it means no updates under the same signature.
3. **Support email** and the developer name to show on the store page.
4. **Headset, 12 to 15 Nov:** see below.
5. **Your review** of the audio and the final trailer, then the YouTube upload.

## Then, in order (Claude, with your go-ahead for uploads)
1. Create the app "RESQ" in RESQ's team. The build doesn't change: the App ID only goes in the upload command.
2. Signed build, `check_apk.sh` PASS.
3. After the team's legal statements: create the private **Competition** channel with a URL invite.
4. Upload with `metavr store dist upload --app-id ID --channel Competition --apk build/resq-release.apk --publish`, after confirming with you at that moment.
5. Fill the store page, but **never press "Submit for Review"**.
6. Put the invite link in the Devpost form and on the site.

## Only a headset can answer
- Real pinch feel, ray comfort, one-hand play, head dwell
- **Real hands over the board:** the cut-through to passthrough, the forearm estimate and any halo at the edge. If it looks wrong, `RealHandsView.Wanted` returns to the SDK's hands in one line
- **Room weather on real passthrough:** how the graded room, lightning, rain and the cloud under the ceiling look; reading the real ceiling from MRUK
- Dynamic foveation and CPU/GPU levels: text stays sharp and the frame rate holds
- Frame rate on Quest 3 and 3S (at least 60, target 72), thermal, memory
- MRUK in real rooms, anchor relocalization across sessions
- **HUD readability:** body text is about 0.7° at the HUD's distance, under Meta's 1° guidance. It looks fine in captures but needs judging in the headset; Settings offers 125 and 150%
- Launching with controllers in hand, now that hands are required
- Device footage for the trailer and store screenshots
- Invite install from a second Meta account
- First-time playtesters
