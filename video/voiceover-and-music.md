# RESQ trailer: voice-over script and music prompt

For Google AI Studio (speech generation and music). Target cut 2:20–2:40, hard limit 2:59. Every shot is real gameplay captured in Meta XR Simulator. The voice says nothing the footage doesn't show.

- **Disclosure:** an AI voice or AI music makes the YouTube "Altered or synthetic content" answer **Yes**.
- **Credits:** add "Google AI Studio" to Devpost's "Built with" and to the video description.
- **Licence:** check that the account's terms allow commercial use of the output, and save a copy of those terms with the files.

## 1. Voice-over

### Style note (paste above the script)

> Single narrator. A calm, warm rescue coordinator talking to one volunteer at a kitchen table: quietly confident, gently wry, never dramatic. Mid-low voice, clear neutral English. About 130 words a minute, a little slower on the place names. Leave one full second of silence between lines. A small smile in the last line. No shouting, no movie-trailer growl, no music or effects in the voice file.
>
> Pronunciation: RESQ is said "rescue". Emberwood = EM-ber-wood. Frostline = FROST-line. Tidemark = TIDE-mark. Cinder Reach = SIN-der reech.

### Script (18 lines, 168 words: about 80 seconds of voice, leaving room for game sound)

| # | Picture (shot) | Line |
|---|---|---|
| 1 | A calm valley on the table, residents in the village | Every flood starts small. |
| 2 | Rain begins; the river rises toward the houses | This one starts on your table. |
| 3 | Title card | This is RESQ. |
| 4 | A hand pinches a tool card from the tray | Pinch a tool from the tray. A barrier. A bridge. A pump. |
| 5 | Aiming at a spot: the forecast colours dry and flooded cells | Before you commit, RESQ runs the real simulation ahead, and shows you what stays dry, and what floods. |
| 6 | Placed; Start pressed; the water turns at the barrier | Then you start the clock, and watch your plan hold. |
| 7 | The bus crosses; residents reach safety and cheer | Every resident counts. |
| 8 | Alder Valley: a skiff ferries residents | In Alder Valley, the water always finds the low road. |
| 9 | Emberwood: fire and a firebreak | In Emberwood, fire follows the wind. |
| 10 | Frostline: snow slope and a brace | On Frostline, the snow remembers every load. |
| 11 | Tidemark: the tide rises around the piers | At Tidemark, the tide keeps its own clock. |
| 12 | Cinder Reach: coolant crusts the lava front | And in Cinder Reach, you cool the lava to open a way through. |
| 13 | Mission select across the five worlds | Five worlds. Twenty-five missions. Every one with more than one way to win. |
| 14 | Results and the debrief card | When it's over, the debrief shows you why it worked, or why it didn't. |
| 15 | Aftercare: the water drains, the valley recovers | Then you watch the valley recover. |
| 16 | Settings: one-hand mirroring, text size, Command mode with head dwell | Play with two hands, one hand, or just a steady look. Bigger text, higher contrast, your own pace. |
| 17 | The board card with "Use my table" over the virtual table | Start on a virtual table, or bring the valley to your own. |
| 18 | End card: RESQ, Small worlds. Big saves., Meta Quest 3 and 3S | RESQ. Small worlds. Big saves. |

Plain text for pasting, one line per paragraph:

```
Every flood starts small.
This one starts on your table.
This is RESQ.
Pinch a tool from the tray. A barrier. A bridge. A pump.
Before you commit, RESQ runs the real simulation ahead, and shows you what stays dry, and what floods.
Then you start the clock, and watch your plan hold.
Every resident counts.
In Alder Valley, the water always finds the low road.
In Emberwood, fire follows the wind.
On Frostline, the snow remembers every load.
At Tidemark, the tide keeps its own clock.
And in Cinder Reach, you cool the lava to open a way through.
Five worlds. Twenty-five missions. Every one with more than one way to win.
When it's over, the debrief shows you why it worked, or why it didn't.
Then you watch the valley recover.
Play with two hands, one hand, or just a steady look. Bigger text, higher contrast, your own pace.
Start on a virtual table, or bring the valley to your own.
RESQ. Small worlds. Big saves.
```

Download the result as WAV, 48 kHz if offered. The edit splits it into lines and times the captions (`.srt`) to the real voice.

## 2. Music prompt

Paste as one prompt:

> Instrumental background score for a 2 minute 40 second video game trailer. A cozy, hopeful miniature-world adventure with gentle tension, like a tabletop diorama during a storm. 96 BPM, 4/4, D major, with a turn to B minor in the middle section, returning to D major. Instruments: plucked acoustic strings and harp, marimba, soft glass bells, a warm string pad, light hand percussion and a soft kick. No vocals, no choir, no lyrics, no spoken words, no sound effects. Leave the midrange sparse so a narrator can sit on top: melody mostly high (bells, plucks), bass low and round. Structure: 0:00–0:15 sparse intro, plucks and a soft shimmer like distant rain; 0:15–0:45 a gentle groove builds; 0:45–1:40 a steady, slightly tense pulse in B minor with a small fill every 8 bars; 1:40–2:15 a warm lift back to D major, fuller and brighter; 2:15–2:40 resolve and end on a sustained D major chord that rings out for about three seconds. Same tempo and key family throughout, no sudden drops, no loud hits, mixed for dialogue.

- **Why these settings:** the game's win chime and success chord are D major (D, F#, A), and its world instruments are plucked strings, marimba and glass bells, so the trailer bed matches the game.
- **Cutting to the music:** at 96 BPM, 8 bars last 20 seconds, so cuts can land on the fills.
- **If it comes back short:** repeat whole 8-bar phrases with a crossfade, and let the music's own ending land on the end card.
