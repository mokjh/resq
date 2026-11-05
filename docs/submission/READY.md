# RESQ submission: what's ready and what's left

Updated 5 Nov 2026. Nothing has been submitted or uploaded. Deadline: Wed 18 Nov, 12:00 PT (22:00 Cairo).

Evidence labels: automated (Editor or headless), simulator (Meta XR Simulator), device (headset), human.

## Ready
| Item | Where | Evidence |
|---|---|---|
| 25 missions, both plans, both layouts, 3 presets | `python3 tools/simtest/simtest.py sim` and `lab validate` | automated: 450/450, 120/120 challenge runs (last run before 5 Nov) |
| PlayMode gate | `tools/unity/gate_when_free.sh` | automated: 18/18, 5 Nov |
| Hands-only journey with real pinches: Hub to placed barrier to running mission | `tools/xr/hands_test.py` | simulator: PASS, 5 Nov |
| XR flow: placement, virtual table, move board, anchor save and restore, hand-loss pause, off-view hint | STATUS item 7 | simulator: 5 Nov |
| Release key | `~/keys/resq.keystore`, password in the login Keychain (`resq-keystore`) | **Back it up off this Mac** |
| Release build and store-rule check | `tools/unity/release_apk.sh`, `tools/unity/check_apk.sh` | written; **signed build not yet made** (needs 21 GB free) |
| Debug APK | `build/resq-xr.apk` | static manifest checks only; predates the 32-bit fix and later commits; never run on a headset |
| Devpost text | `docs/submission/devpost-draft.md` | 500/500 words; `[P5]` and `[you]` markers remain |
| Store page copy | `docs/submission/store-listing.md` | within field limits |
| Store art | `docs/submission/store/` | game renders; screenshots are Editor renders until device captures replace them |
| Trailer rough cut | `video/exports/resq-trailer-captioned.mp4` | simulator footage, autopilot-driven, 72 s, QC pass; choppy (2 to 6 captured fps) and not final |
| Website | https://resq-woad.vercel.app | live |

## Needs the owner
1. **Entry route.** The rules allow one Entry per person and per organization. RESQ can only go alongside Waz World if a real, eligible person is RESQ's Team Representative and submits it. Confirm the setup with the hackathon manager first (draft below).
2. **Team Representative's account:** Meta VR Start member, Developer Access, age 18+, joined on Devpost.
3. **Second Meta developer account:** sign in to it in Claude's browser pane, so the app can be created there. Only Frameless Plays is reachable now.
4. **Free disk to 21 GB** for the signed build (14 GB now).
5. **In the dashboard, as legal statements:** Developer Distribution Agreement and age self-certification. These unlock the Competition channel.
6. **Back up** `~/keys/resq.keystore` off this Mac. Losing it means no updates under the same signature.
7. **Support email** and the developer name to show on the store page.
8. **Headset, 12 to 15 Nov:** see below.
9. **Your review** of the audio and the final trailer, then the YouTube upload.
10. **Final Devpost submit and attestations**, by the Team Representative.

Question for the hackathon manager:
> Hi, I'm submitting one Entry as the representative of my team. A second, separate project is being built by a team where [friend's name] is the Team Representative and I'm a non-Representative member who does much of the development. Is this allowed under the Entry Limit, and may that team publish under a separate Meta developer organization that I own?

## Then, in order (Claude, with your go-ahead for uploads)
1. Create the app "RESQ" and put its App ID in the build settings.
2. Signed build, `check_apk.sh` PASS.
3. After the dashboard statements: create the private **Competition** channel with a URL invite.
4. Upload with `metavr store dist upload --app-id ID --channel Competition --apk build/resq-release.apk --publish`, after confirming with you at that moment.
5. Fill the store page, but **never press "Submit for Review"**.
6. Put the invite link in the Devpost testing instructions and on the site.

## Only a headset can answer
- Real pinch feel, ray comfort, one-hand play, head dwell
- Frame rate on Quest 3 and 3S (at least 60, target 72), thermal, memory
- MRUK in real rooms, anchor relocalization across sessions
- **HUD readability:** body text is about 0.7° at the HUD's distance, under Meta's 1° guidance. It looks fine in captures but needs judging in the headset; Settings offers 125 and 150%
- Launching with controllers in hand, now that hands are required
- Device footage for the trailer and store screenshots
- Invite install from a second Meta account
- First-time playtesters
