"""Edits the Protobot v2.0 Beta 1 trailer from in-game footage (Builds/FrameRecorder frames in Video/shots/<name>),
title cards made here, an original music track (make_music.py) and, when present, narration lines
(Video/voice/vo01..vo09 .wav/.mp3, placed at the times in Tools/Video/narration.md).

    python -I Tools/Video/make_trailer.py

Writes Video/out/Protobot v2.0 Beta 1 trailer.mp4 (1080p) and a smaller 720p copy for Discord (under 10 MB)."""
import glob
import math
import os
import shutil
import subprocess
import sys

from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageFont

VIDEO = "C:/Dev/ProtobotRebuiltV2/Video"
SHOTS = os.path.join(VIDEO, "shots")
WORK = os.path.join(VIDEO, "work")
OUT = os.path.join(VIDEO, "out")
INTRO = "C:/Dev/ProtobotRebuiltV2/ProtobotRebuiltV2/Assets/ProtobotRebuilt/Resources/Intro"
W, H, FPS = 1920, 1080, 30
XF = 0.5  # crossfade seconds
BLUE = (30, 150, 220)
FONT_BLACK = "C:/Windows/Fonts/seguibl.ttf"
FONT_BOLD = "C:/Windows/Fonts/segoeuib.ttf"
FONT = "C:/Windows/Fonts/segoeui.ttf"


def font(path, size):
    return ImageFont.truetype(path, size)


def ease(x):
    x = max(0.0, min(1.0, x))
    return 1 - (1 - x) ** 3


# ------------------------------------------------------------------ grading and captions

VIGNETTE = None


def vignette():
    global VIGNETTE
    if VIGNETTE is None:
        v = Image.new("L", (W // 4, H // 4), 0)
        px = v.load()
        for y in range(H // 4):
            for x in range(W // 4):
                dx, dy = (x - W / 8) / (W / 8), (y - H / 8) / (H / 8)
                d = math.sqrt(dx * dx * 0.8 + dy * dy)
                px[x, y] = int(max(0, min(255, (d - 0.55) * 210)))
        VIGNETTE = v.resize((W, H), Image.BICUBIC).filter(ImageFilter.GaussianBlur(30))
    return VIGNETTE


def grade(im, cinematic=True):
    im = ImageEnhance.Contrast(im).enhance(1.10)
    im = ImageEnhance.Color(im).enhance(1.18 if cinematic else 1.05)
    black = Image.new("RGB", (W, H), (0, 0, 0))
    return Image.composite(black, im, vignette().point(lambda v: int(v * (0.85 if cinematic else 0.45))))


def caption(im, title, sub, t, dur):
    """A lower-left title: accent bar, title and subtitle, sliding in and fading out."""
    if not title:
        return im
    a_in = ease(t / 0.6)
    a_out = 1 - ease((t - (dur - 0.7)) / 0.6) if t > dur - 0.7 else 1.0
    alpha = min(a_in, a_out)
    if alpha <= 0:
        return im
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    x = 110 - int((1 - a_in) * 60)
    y = H - 250
    tf, sf = font(FONT_BLACK, 68), font(FONT_BOLD, 36)
    tw = d.textlength(title, font=tf)
    sw = d.textlength(sub, font=sf) if sub else 0
    box_w = int(max(tw, sw) + 90)
    # soft dark band behind for legibility
    band = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    bd = ImageDraw.Draw(band)
    bd.rectangle([x - 40, y - 30, x + box_w + 60, y + (150 if sub else 100)], fill=(5, 10, 20, int(150 * alpha)))
    band = band.filter(ImageFilter.GaussianBlur(28))
    layer = Image.alpha_composite(layer, band)
    d = ImageDraw.Draw(layer)
    d.rectangle([x, y + 6, x + 10, y + (130 if sub else 80)], fill=BLUE + (int(255 * alpha),))
    d.text((x + 34, y - 6), title, font=tf, fill=(255, 255, 255, int(255 * alpha)))
    if sub:
        d.text((x + 36, y + 80), sub, font=sf, fill=(190, 220, 245, int(235 * alpha)))
    return Image.alpha_composite(im.convert("RGBA"), layer).convert("RGB")


# ------------------------------------------------------------------ title cards

def background(t, base=None):
    if base is not None:
        im = base.filter(ImageFilter.GaussianBlur(18))
        im = ImageEnhance.Brightness(im).enhance(0.45)
        tint = Image.new("RGB", (W, H), (8, 30, 60))
        return Image.blend(im, tint, 0.45)
    im = Image.new("RGB", (W, H))
    d = ImageDraw.Draw(im)
    for yy in range(0, H, 4):
        k = yy / H
        d.rectangle([0, yy, W, yy + 4], fill=(int(8 + 10 * k), int(28 + 30 * (1 - k)), int(58 + 50 * (1 - k))))
    return im


GEAR = None
WORD = None


def logo_parts():
    global GEAR, WORD
    if GEAR is None:
        g = Image.open(os.path.join(INTRO, "Gear.png")).convert("RGBA")
        GEAR = g.crop(g.getbbox())
        w = Image.open(os.path.join(INTRO, "Wordmark.png")).convert("RGBA")
        WORD = w.crop(w.getbbox())
    return GEAR, WORD


def card_intro(t, dur, base):
    im = background(t, base).convert("RGBA")
    gear, word = logo_parts()
    s = 0.32 + 0.06 * ease(t / 1.5)
    g = gear.resize((int(gear.width * s), int(gear.height * s)), Image.LANCZOS).rotate(-40 * ease(t / 2.0) - 8 * t, resample=Image.BICUBIC, expand=True)
    ga = min(1.0, t / 0.6)
    g.putalpha(g.split()[3].point(lambda v: int(v * ga)))
    im.alpha_composite(g, (W // 2 - g.width // 2, int(H * 0.40) - g.height // 2))
    if t > 1.2:
        wa = ease((t - 1.2) / 0.8)
        ws = 0.85
        wm = word.resize((int(word.width * ws), int(word.height * ws)), Image.LANCZOS)
        wm.putalpha(wm.split()[3].point(lambda v: int(v * wa)))
        im.alpha_composite(wm, (W // 2 - wm.width // 2, int(H * 0.66) + int((1 - wa) * 30)))
    if t > 2.2:
        a = ease((t - 2.2) / 0.7)
        layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        d = ImageDraw.Draw(layer)
        txt = "v2.0 Beta 1"
        f = font(FONT_BOLD, 40)
        d.text((W // 2 - d.textlength(txt, font=f) // 2, int(H * 0.66) + 210), txt, font=f, fill=(170, 215, 245, int(255 * a)))
        im = Image.alpha_composite(im, layer)
    return fade_edges(im.convert("RGB"), t, dur)


def card_new(t, dur, base):
    im = background(t, base).convert("RGBA")
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    a = ease(t / 0.6)
    head = "NEW IN v2.0 BETA 1"
    f = font(FONT_BLACK, 84)
    d.text((W // 2 - d.textlength(head, font=f) // 2, 170 - int((1 - a) * 30)), head, font=f, fill=(255, 255, 255, int(255 * a)))
    items = [
        ("Safer online play", "a name filter, reports that go to real moderators, and robot checks in the lobby"),
        ("Built for students", "players under 13 play online with no voice chat and a made-up name"),
        ("Your data, your call", "a clear privacy policy, and delete your online data anytime"),
        ("The official Discord", "guides, help, bug reports and builds from other players"),
    ]
    for i, (title, sub) in enumerate(items):
        ia = ease((t - 0.6 - i * 0.45) / 0.5)
        if ia <= 0:
            continue
        y = 360 + i * 140
        x = 360 + int((1 - ia) * 50)
        d.rounded_rectangle([x - 40, y - 18, W - 360, y + 104], radius=18, fill=(255, 255, 255, int(16 * ia)), outline=(80, 170, 235, int(120 * ia)), width=2)
        d.ellipse([x - 12, y + 30, x + 8, y + 50], fill=BLUE + (int(255 * ia),))
        d.text((x + 30, y - 6), title, font=font(FONT_BOLD, 46), fill=(255, 255, 255, int(255 * ia)))
        d.text((x + 32, y + 52), sub, font=font(FONT, 32), fill=(185, 215, 240, int(235 * ia)))
    im = Image.alpha_composite(im, layer)
    return fade_edges(im.convert("RGB"), t, dur)


def card_outro(t, dur, base):
    im = background(t, base).convert("RGBA")
    gear, word = logo_parts()
    a = ease(t / 0.8)
    g = gear.resize((int(gear.width * 0.22), int(gear.height * 0.22)), Image.LANCZOS).rotate(-6 * t, resample=Image.BICUBIC, expand=True)
    g.putalpha(g.split()[3].point(lambda v: int(v * a)))
    im.alpha_composite(g, (W // 2 - g.width // 2, 130))
    wm = word.resize((int(word.width * 0.75), int(word.height * 0.75)), Image.LANCZOS)
    wm.putalpha(wm.split()[3].point(lambda v: int(v * a)))
    im.alpha_composite(wm, (W // 2 - wm.width // 2, 400))
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    b = ease((t - 0.6) / 0.6)
    line = "Free on Windows"
    f = font(FONT_BLACK, 58)
    d.text((W // 2 - d.textlength(line, font=f) // 2, 600), line, font=f, fill=(255, 255, 255, int(255 * b)))
    c = ease((t - 1.2) / 0.6)
    for i, (label, value) in enumerate([("Download", "github.com/Cranezz/Protobot-Rebuilt"), ("Discord", "discord.gg/Q7dfw9DqXD")]):
        y = 720 + i * 70
        lf, vf = font(FONT_BOLD, 36), font(FONT, 36)
        lw = d.textlength(label + "   ", font=lf)
        tw = lw + d.textlength(value, font=vf)
        x = W // 2 - tw // 2
        d.text((x, y), label, font=lf, fill=(110, 190, 240, int(255 * c)))
        d.text((x + lw, y), value, font=vf, fill=(235, 245, 255, int(255 * c)))
    small = "Protobot is a fan-made project and isn't affiliated with VEX Robotics or the REC Foundation."
    sf = font(FONT, 22)
    d.text((W // 2 - d.textlength(small, font=sf) // 2, H - 70), small, font=sf, fill=(160, 180, 200, int(200 * c)))
    im = Image.alpha_composite(im, layer)
    return fade_edges(im.convert("RGB"), t, dur, end_black=True)


def fade_edges(im, t, dur, end_black=False):
    if t < 0.25:
        im = Image.blend(Image.new("RGB", (W, H)), im, t / 0.25)
    if end_black and t > dur - 1.0:
        im = Image.blend(im, Image.new("RGB", (W, H)), ease((t - (dur - 1.0)) / 1.0))
    return im


# ------------------------------------------------------------------ the cut

# (kind, shot or card, first frame, seconds, title, subtitle, cinematic grade)
CUT = [
    ("card", "intro", "home", 5.0, None, None),
    ("shot", "field", 0, 7.0, "Build with real VEX parts", "Every part in the library, down to the last screw", True),
    ("shot", "motion", 0, 7.0, "Make it move", "Motors, gears, chains and pistons", True),
    ("shot", "multi_wide", 0, 9.0, "Drive it on the field", "Real physics on this season's game", True),
    ("shot", "multi_follow", 0, 8.0, "Play matches online", "2v2 with your friends, just like competition", True),
    ("shot", "code", 0, 7.0, "Program it", "Python, C++ or Blocks", False),
    ("shot", "match_reveal", 0, 12.0, None, None, False),
    ("shot", "lobby", 0, 5.0, "Invite your team", "Share a code and you're in", False),
    ("shot", "home", 0, 5.0, "Join the community", "Help, guides and builds on our Discord", False),
    ("card", "outro", "field", 7.0, None, None),
]


def frames_of(shot):
    return sorted(glob.glob(os.path.join(SHOTS, shot, "*.jpg")))


def render_segment(index, item):
    folder = os.path.join(WORK, "seg%02d" % index)
    if os.path.exists(folder):
        shutil.rmtree(folder)
    os.makedirs(folder)
    kind = item[0]
    if kind == "card":
        _, name, base_shot, dur, _, _ = item
        bf = frames_of(base_shot)
        base = Image.open(bf[len(bf) // 2]).convert("RGB") if bf else None
        make = {"intro": card_intro, "new": card_new, "outro": card_outro}[name]
        for i in range(int(dur * FPS)):
            make(i / FPS, dur, base).save(os.path.join(folder, "%05d.jpg" % i), quality=92)
    else:
        _, shot, first, dur, title, sub, cinematic = item
        src = frames_of(shot)
        n = int(dur * FPS)
        for i in range(n):
            j = min(len(src) - 1, first + i)
            im = Image.open(src[j]).convert("RGB")
            if im.size != (W, H):
                im = im.resize((W, H), Image.LANCZOS)
            im = grade(im, cinematic)
            im = caption(im, title, sub, i / FPS, dur)
            im.save(os.path.join(folder, "%05d.jpg" % i), quality=92)
    clip = os.path.join(WORK, "seg%02d.mp4" % index)
    run(["ffmpeg", "-y", "-v", "error", "-framerate", str(FPS), "-i", os.path.join(folder, "%05d.jpg"),
         "-c:v", "libx264", "-preset", "medium", "-crf", "16", "-pix_fmt", "yuv420p", clip])
    return clip, item[3]


def run(args):
    r = subprocess.run(args, capture_output=True, text=True)
    if r.returncode != 0:
        sys.exit("ffmpeg failed: " + r.stderr[-1500:])


def main():
    os.makedirs(WORK, exist_ok=True)
    os.makedirs(OUT, exist_ok=True)
    only = [int(a) for a in sys.argv[1:] if a.isdigit()]
    clips = []
    for i, item in enumerate(CUT):
        clip = os.path.join(WORK, "seg%02d.mp4" % i)
        if not only or i in only or not os.path.exists(clip):
            print("segment", i, item[1], flush=True)
            render_segment(i, item)
        clips.append((clip, item[3]))
    # crossfade chain
    args = ["ffmpeg", "-y", "-v", "error"]
    for c, _ in clips:
        args += ["-i", c]
    chain, offset, last = [], 0.0, "[0:v]"
    for k in range(1, len(clips)):
        offset += clips[k - 1][1] - XF
        out = "[v%d]" % k
        chain.append(f"{last}[{k}:v]xfade=transition=fade:duration={XF}:offset={offset:.3f}{out}")
        last = out
    total = sum(d for _, d in clips) - XF * (len(clips) - 1)
    video = os.path.join(WORK, "picture.mp4")
    args += ["-filter_complex", ";".join(chain), "-map", last, "-c:v", "libx264", "-preset", "slow", "-crf", "17", "-pix_fmt", "yuv420p", video]
    run(args)
    # sound: the music (faded out), plus narration lines if they exist
    music = os.path.join(VIDEO, "music.wav")
    audio_in = ["-i", music]
    mix = [f"[1:a]atrim=0:{total:.2f},afade=t=out:st={total - 2.5:.2f}:d=2.5,volume=0.9[m]"]
    starts = [0.8, 5.0, 11.5, 18.0, 26.5, 34.0, 41.0, 52.5, 61.0]   # = narration.md
    voices = []
    for n in range(1, 10):
        files = glob.glob(os.path.join(VIDEO, "voice", "vo%02d.*" % n))
        if files:
            voices.append((files[0], starts[n - 1]))
    for k, (f, st) in enumerate(voices):
        audio_in += ["-i", f]
        mix.append(f"[{2 + k}:a]aresample=48000,aformat=channel_layouts=stereo,adelay={int(st * 1000)}|{int(st * 1000)},volume=1.8[vo{k}]")
    if voices:
        # the narration together, then the music ducks under it (sidechain) and comes back between lines
        vlabels = "".join(f"[vo{k}]" for k in range(len(voices)))
        mix.append(f"{vlabels}amix=inputs={len(voices)}:normalize=0,asplit=2[vox][key]")
        mix.append("[m][key]sidechaincompress=threshold=0.02:ratio=10:attack=15:release=450:makeup=1[mduck]")
        mix.append("[mduck][vox]amix=inputs=2:normalize=0,loudnorm=I=-14:TP=-1.5[a]")
    else:
        mix.append("[m]loudnorm=I=-14:TP=-1.5[a]")
    final = os.path.join(OUT, "Protobot v2.0 Beta 1 trailer.mp4")
    run(["ffmpeg", "-y", "-v", "error", "-i", video] + audio_in + ["-filter_complex", ";".join(mix), "-map", "0:v", "-map", "[a]",
         "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-shortest", "-movflags", "+faststart", final])
    # a 720p copy under 10 MB for Discord (two-pass at the bitrate that fits)
    small = os.path.join(OUT, "Protobot v2.0 Beta 1 trailer (720p).mp4")
    kbps = int(9.3 * 8 * 1000 / total) - 96
    log = os.path.join(WORK, "pass")
    run(["ffmpeg", "-y", "-v", "error", "-i", final, "-vf", "scale=1280:720", "-c:v", "libx264", "-preset", "slow", "-b:v", f"{kbps}k",
         "-pass", "1", "-passlogfile", log, "-an", "-f", "mp4", "NUL"])
    run(["ffmpeg", "-y", "-v", "error", "-i", final, "-vf", "scale=1280:720", "-c:v", "libx264", "-preset", "slow", "-b:v", f"{kbps}k",
         "-pass", "2", "-passlogfile", log, "-c:a", "aac", "-b:a", "96k", "-movflags", "+faststart", small])
    print(f"done: {total:.1f} s, {os.path.getsize(final) / 1e6:.1f} MB and {os.path.getsize(small) / 1e6:.1f} MB (720p)")


if __name__ == "__main__":
    main()
