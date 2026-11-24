"""Makes a tutorial's narration with ElevenLabs (the account's "Protobot Narrator" voice) from its script
(Tools/Video/tutorials/<name>.txt: "N words" rows), one mp3 per line in Video/tutorials/<folder>/voice/NN.mp3, and
durations.txt (line, seconds) beside the demo script there. Lines already made are kept unless --redo N[,N...].

    python -I Tools/Video/tutorial_voice.py "01 Getting started" 01-getting-started --take    (all lines in one take)
    python -I Tools/Video/tutorial_voice.py "01 Getting started" 01-getting-started --redo 3,5   (single lines again)
    python -I Tools/Video/tutorial_voice.py "01 Getting started" 01-getting-started --recut   (cut take.mp3 again)

The API key is read from C:/Dev/ProtobotRebuiltV2/ElevenLabs/key.txt (outside the repo; never printed)."""
import json
import os
import subprocess
import sys
import urllib.request

ROOT = "C:/Dev/ProtobotRebuiltV2"
VOICE = "PFJFkCtyPbVgkjpxLvfS"   # Protobot Narrator
# As the trailer's narration (the website's settings then: stability 50, similarity 75, the v4 model, every line in one
# take): multilingual v2 with a style boost brought back the noise of the videos the voice was cloned from.
MODEL = "eleven_v4"
SETTINGS = {"stability": 0.5, "similarity_boost": 0.75}


def lines_of(name):
    here = os.path.dirname(os.path.abspath(__file__))
    rows = {}
    with open(os.path.join(here, "tutorials", name + ".txt"), encoding="utf-8") as f:
        for row in f:
            row = row.strip()
            if not row or row.startswith("#"):
                continue
            n, words = row.split(" ", 1)
            rows[int(n)] = words
    return rows


def speak(key, text, before, after):
    body = {"text": text, "model_id": MODEL, "voice_settings": SETTINGS}
    if before:
        body["previous_text"] = before
    if after:
        body["next_text"] = after
    req = urllib.request.Request(
        "https://api.elevenlabs.io/v1/text-to-speech/" + VOICE + "?output_format=mp3_44100_128",
        data=json.dumps(body).encode(), headers={"xi-api-key": key, "Content-Type": "application/json"}, method="POST")
    with urllib.request.urlopen(req, timeout=120) as r:
        return r.read()


def duration(path):
    out = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", path],
                         capture_output=True, text=True).stdout.strip()
    return float(out)


def silences(path, noise=-38, least=0.45):
    log = subprocess.run(["ffmpeg", "-hide_banner", "-i", path, "-af", "silencedetect=noise=%ddB:d=%.2f" % (noise, least),
                          "-f", "null", "-"], capture_output=True, text=True).stderr
    out, start = [], None
    for row in log.splitlines():
        if "silence_start:" in row:
            start = float(row.split("silence_start:")[1].split()[0])
        elif "silence_end:" in row and start is not None:
            out.append((start, float(row.split("silence_end:")[1].split()[0])))
            start = None
    return out


def one_take(key, rows, out):
    """Every line in one request (one even delivery, as the trailer), cut between the lines by the character times
    ElevenLabs returns with the audio (/with-timestamps)."""
    import base64
    order = sorted(rows)
    sep = "\n\n"
    text = sep.join(rows[n] for n in order)
    body = {"text": text, "model_id": MODEL, "voice_settings": SETTINGS}
    req = urllib.request.Request(
        "https://api.elevenlabs.io/v1/text-to-speech/" + VOICE + "/with-timestamps?output_format=mp3_44100_128",
        data=json.dumps(body).encode(), headers={"xi-api-key": key, "Content-Type": "application/json"}, method="POST")
    with urllib.request.urlopen(req, timeout=300) as r:
        answer = json.loads(r.read())
    take = os.path.join(out, "take.mp3")
    with open(take, "wb") as f:
        f.write(base64.b64decode(answer["audio_base64"]))
    with open(os.path.join(out, "take_alignment.json"), "w", encoding="utf-8") as f:
        json.dump(answer.get("alignment"), f)
    cut(rows, out)
    return len(text)


def cut(rows, out):
    """Cuts take.mp3 into voice/NN.mp3, one per line. The character times put a line's end at the END of the pause
    after it (about 0.1 s into the next line's first word: the next line's start was heard at the end of each clip),
    so each cut goes into the real pause (silencedetect) between the two lines: a line ends 0.12 s into that pause
    with a short fade, the next starts 0.05 s before its end."""
    order = sorted(rows)
    take = os.path.join(out, "take.mp3")
    with open(os.path.join(out, "take_alignment.json"), encoding="utf-8") as f:
        al = json.load(f)
    starts, ends = al["character_start_times_seconds"], al["character_end_times_seconds"]
    sep = "\n\n"
    spans, pos = [], 0
    for n in order:
        first, last = pos, pos + len(rows[n]) - 1
        spans.append((starts[first], ends[last]))
        pos = last + 1 + len(sep)
    total = duration(take)
    quiet = silences(take, noise=-40, least=0.12)
    bounds = []   # (end of line i, start of line i+1)
    for i in range(len(spans) - 1):
        a, b = spans[i][1], spans[i + 1][0]
        near = [q for q in quiet if q[1] > a - 0.8 and q[0] < b + 0.4]
        if near:
            q = max(near, key=lambda q: (q[1] - q[0]) - 0.5 * abs(q[1] - b))
            bounds.append((min(q[0] + 0.12, q[1]), max(q[1] - 0.05, q[0])))
        else:
            m = (a + b) / 2
            bounds.append((m, m))
            print("line %d: no pause found after it, cut at %.2f" % (order[i], m))
    for i, n in enumerate(order):
        path = os.path.join(out, "voice", "%02d.mp3" % n)
        a = max(0.0, spans[i][0] - 0.06) if i == 0 else bounds[i - 1][1]
        b = bounds[i][0] if i < len(bounds) else min(total, spans[i][1] + 0.4)
        fade = "afade=t=in:d=0.02,afade=t=out:st=%.3f:d=0.08" % max(0.0, b - a - 0.08)
        # -ss/-t before -i: the clip's times start at 0, so the fade's start is in the clip's time (after -i the filter
        # saw the take's times and every line after the first was faded to silence).
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-ss", "%.3f" % a, "-t", "%.3f" % (b - a), "-i", take, "-af", fade,
                        "-c:a", "libmp3lame", "-b:a", "160k", path], check=True)
        print("line %d: %.1f s (%.2f-%.2f)" % (n, duration(path), a, b))


def main():
    name, folder = sys.argv[1], sys.argv[2]
    redo = set()
    if "--redo" in sys.argv:
        redo = {int(x) for x in sys.argv[sys.argv.index("--redo") + 1].split(",")}
    with open(os.path.join(ROOT, "ElevenLabs", "key.txt"), encoding="utf-8") as f:
        key = f.read().strip()
    out = os.path.join(ROOT, "Video", "tutorials", folder)
    os.makedirs(os.path.join(out, "voice"), exist_ok=True)
    rows = lines_of(name)
    order = sorted(rows)
    chars = 0
    if "--take" in sys.argv:
        chars = one_take(key, rows, out)
        redo = set()
    elif "--recut" in sys.argv:   # the take again from take.mp3 (no request)
        cut(rows, out)
        redo = set()
    for i, n in enumerate(order):
        path = os.path.join(out, "voice", "%02d.mp3" % n)
        if os.path.exists(path) and n not in redo:
            continue
        before = rows[order[i - 1]] if i > 0 else None
        after = rows[order[i + 1]] if i + 1 < len(order) else None
        try:
            audio = speak(key, rows[n], before, after)
        except urllib.error.HTTPError as e:
            print("line %d failed: %d %s" % (n, e.code, e.read()[:300].decode("utf-8", "replace")))
            sys.exit(1)
        with open(path, "wb") as f:
            f.write(audio)
        chars += len(rows[n])
        print("line %d: %.1f s" % (n, duration(path)))
    with open(os.path.join(out, "durations.txt"), "w") as f:
        for n in order:
            f.write("%d %.3f\n" % (n, duration(os.path.join(out, "voice", "%02d.mp3" % n))))
    print("characters sent:", chars)


if __name__ == "__main__":
    main()
