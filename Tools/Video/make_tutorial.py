"""Puts a tutorial video together: the frames DemoPlayer recorded (FOLDER/00000.jpg..., 30 fps) and the narration lines
(voice/NN.mp3 beside FOLDER), each line starting at the frame marks.txt gives for it ("N frame"). Writes
FOLDER/../<title>.mp4 (1080p) and a 720p copy under 10 MB for Discord.

    python -I Tools/Video/make_tutorial.py C:/Dev/ProtobotRebuiltV2/Video/tutorials/01-getting-started/take2 "Protobot tutorial 1 - Getting started"
"""
import os
import subprocess
import sys

FPS = 30


def run(args):
    subprocess.run(args, check=True)


def duration(path):
    out = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", path],
                         capture_output=True, text=True).stdout.strip()
    return float(out)


def main():
    folder, title = sys.argv[1], sys.argv[2]
    base = os.path.dirname(os.path.normpath(folder))
    frames = sorted(f for f in os.listdir(folder) if f.endswith(".jpg"))
    total = len(frames) / FPS
    marks = []
    with open(os.path.join(folder, "marks.txt")) as f:
        for row in f:
            p = row.split()
            if len(p) == 2:
                marks.append((int(p[0]), int(p[1]) / FPS))
    args = ["ffmpeg", "-y", "-v", "error", "-framerate", str(FPS), "-i", os.path.join(folder, "%05d.jpg")]
    mix = []
    for k, (n, start) in enumerate(marks):
        args += ["-i", os.path.join(base, "voice", "%02d.mp3" % n)]
        ms = int(start * 1000)
        mix.append(f"[{k + 1}:a]aresample=48000,aformat=channel_layouts=stereo,adelay={ms}|{ms}[v{k}]")
    labels = "".join(f"[v{k}]" for k in range(len(marks)))
    mix.append(f"{labels}amix=inputs={len(marks)}:normalize=0,apad,atrim=0:{total:.3f},loudnorm=I=-16:TP=-1.5[a]")
    out = os.path.join(base, title + ".mp4")
    run(args + ["-filter_complex", ";".join(mix), "-map", "0:v", "-map", "[a]", "-c:v", "libx264", "-preset", "slow",
                "-crf", "18", "-pix_fmt", "yuv420p", "-r", str(FPS), "-c:a", "aac", "-b:a", "192k", "-ar", "48000",
                "-movflags", "+faststart", out])
    small = os.path.join(base, title + " (720p).mp4")
    kbps = int(9.3 * 8 * 1000 / total) - 96
    log = os.path.join(base, "pass")
    run(["ffmpeg", "-y", "-v", "error", "-i", out, "-vf", "scale=1280:720", "-c:v", "libx264", "-preset", "slow",
         "-b:v", f"{kbps}k", "-pass", "1", "-passlogfile", log, "-an", "-f", "mp4", "NUL"])
    run(["ffmpeg", "-y", "-v", "error", "-i", out, "-vf", "scale=1280:720", "-c:v", "libx264", "-preset", "slow",
         "-b:v", f"{kbps}k", "-pass", "2", "-passlogfile", log, "-c:a", "aac", "-b:a", "96k", "-movflags", "+faststart", small])
    for f in os.listdir(base):
        if f.startswith("pass"):
            os.remove(os.path.join(base, f))
    print(f"done: {total:.1f} s, {os.path.getsize(out) / 1e6:.1f} MB and {os.path.getsize(small) / 1e6:.1f} MB (720p)")


if __name__ == "__main__":
    main()
