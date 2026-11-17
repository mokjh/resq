#!/usr/bin/env python3
"""Final trailer mix: the picture from edit.py, the voice-over and the music.
  - voice starts at VO_AT seconds; music runs from 0, ducked under the voice (sidechain), fades out at the end;
  - two-pass loudnorm to -14 LUFS integrated, true peak <= -1.5 dBTP; AAC 48 kHz;
  - writes video/exports/resq-trailer-final.mp4 and the full narration as .srt/.vtt timed to the real voice.
    python3 tools/video/mix.py"""
import json, re, subprocess, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
V = ROOT / "video"
PIC = V / "exports" / "resq-trailer-captioned.mp4"   # the picture with the on-screen capture labels ("4x speed")
VO, MUSIC = V / "audio" / "vo.wav", V / "audio" / "music.mp3"
OUT = V / "exports" / "resq-trailer-final.mp4"
VO_AT = 2.0
# the 18 script lines with their start and end in vo.wav (silencedetect at -38 dB, 0.45 s; pauses inside a line merged by matching each line's length to its words)
LINES = [
    (0.00, 1.89, "Every flood starts small."),
    (2.62, 4.23, "This one starts on your table."),
    (5.15, 6.30, "This is RESQ."),
    (7.32, 11.89, "Pinch a tool from the tray. A barrier. A bridge. A pump."),
    (12.92, 18.79, "Before you commit, RESQ runs the real simulation ahead, and shows you what stays dry, and what floods."),
    (19.77, 22.19, "Then you start the clock, and watch your plan hold."),
    (22.94, 24.05, "Every resident counts."),
    (25.05, 28.32, "In Alder Valley, the water always finds the low road."),
    (29.08, 31.56, "In Emberwood, fire follows the wind."),
    (32.39, 35.18, "On Frostline, the snow remembers every load."),
    (36.00, 38.59, "At Tidemark, the tide keeps its own clock."),
    (39.35, 42.75, "And in Cinder Reach, you cool the lava to open a way through."),
    (43.62, 48.59, "Five worlds. Twenty-five missions. Every one with more than one way to win."),
    (49.42, 53.28, "When it's over, the debrief shows you why it worked, or why it didn't."),
    (54.28, 55.63, "Then you watch the valley recover."),
    (56.51, 63.23, "Play with two hands, one hand, or just a steady look. Bigger text, higher contrast, your own pace."),
    (64.09, 66.95, "Start on a virtual table, or bring the valley to your own."),
    (67.78, 71.36, "RESQ. Small worlds. Big saves."),
]

def run(cmd):
    r = subprocess.run(cmd, capture_output=True, text=True)
    if r.returncode: sys.exit(" ".join(map(str, cmd))[:300] + "\n" + r.stderr[-1500:])
    return r.stderr

def dur(p): return float(subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", str(p)], capture_output=True, text=True).stdout)

def main():
    D = dur(PIC); fade = 3.0
    graph = (f"[1:a]aresample=48000,aformat=channel_layouts=stereo,adelay={int(VO_AT*1000)}|{int(VO_AT*1000)},apad,atrim=0:{D},asplit=2[vo][key];"
             f"[2:a]aresample=48000,aformat=channel_layouts=stereo,atrim=0:{D},volume=0.55,afade=t=in:st=0:d=1.0,afade=t=out:st={D-fade}:d={fade}[mus];"
             f"[mus][key]sidechaincompress=threshold=0.03:ratio=8:attack=20:release=450:makeup=1[duck];"
             f"[vo][duck]amix=inputs=2:duration=first:normalize=0[mix]")
    pre = V / "exports" / "mix-pre.wav"
    run(["ffmpeg", "-y", "-loglevel", "error", "-i", str(PIC), "-i", str(VO), "-i", str(MUSIC), "-filter_complex", graph, "-map", "[mix]", "-ar", "48000", str(pre)])
    stats = run(["ffmpeg", "-y", "-i", str(pre), "-af", "loudnorm=I=-14:TP=-1.5:LRA=11:print_format=json", "-f", "null", "-"])
    m = json.loads(stats[stats.rindex("{"):stats.rindex("}") + 1])
    ln = (f"loudnorm=I=-14:TP=-1.5:LRA=11:measured_I={m['input_i']}:measured_TP={m['input_tp']}:measured_LRA={m['input_lra']}:"
          f"measured_thresh={m['input_thresh']}:offset={m['target_offset']}:linear=true")
    run(["ffmpeg", "-y", "-loglevel", "error", "-i", str(PIC), "-i", str(pre), "-map", "0:v", "-map", "1:a", "-c:v", "copy",
         "-af", ln + ",aresample=48000", "-c:a", "aac", "-b:a", "256k", "-ar", "48000", "-movflags", "+faststart", "-shortest", str(OUT)])
    pre.unlink()
    ts = lambda s, sep: f"{int(s//3600):02d}:{int(s%3600//60):02d}:{int(s%60):02d}{sep}{int(round((s%1)*1000)):03d}"
    srt = "".join(f"{n}\n{ts(a+VO_AT, ',')} --> {ts(b+VO_AT+0.3, ',')}\n{t}\n\n" for n, (a, b, t) in enumerate(LINES, 1))
    (V / "exports" / "resq-trailer-final.srt").write_text(srt)
    (V / "exports" / "resq-trailer-final.vtt").write_text("WEBVTT\n\n" + "".join(f"{ts(a+VO_AT, '.')} --> {ts(b+VO_AT+0.3, '.')}\n{t}\n\n" for a, b, t in LINES))
    print(f"mix: {OUT.name} {dur(OUT):.1f} s")

if __name__ == "__main__":
    main()
