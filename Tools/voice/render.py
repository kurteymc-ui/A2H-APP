"""Records the natural voice's clips (the Voice workflow): every phrase of phrases.json, spoken by Kokoro-82M (Apache-2.0, run through kokoro-onnx),
silence trimmed, as a small MP3, with index.json mapping each phrase (lower case, single spaces, no end punctuation) to its file.

python3 render.py phrases.json kokoro-v1.0.onnx voices-v1.0.bin out-folder
"""
import hashlib
import json
import os
import subprocess
import sys

import numpy as np
import soundfile as sf
from kokoro_onnx import Kokoro


def key(text):
    return " ".join(text.lower().replace(",", " ").split()).strip(" .!?")


src, model, voices, out = sys.argv[1:5]
cfg = json.load(open(src, encoding="utf-8"))
kokoro = Kokoro(model, voices)
os.makedirs(out, exist_ok=True)
wav = os.path.join(out, "_tmp.wav")
index = {}
for n, text in enumerate(cfg["phrases"]):
    k = key(text)
    name = hashlib.sha1(k.encode("utf-8")).hexdigest()[:12] + ".mp3"
    samples, rate = kokoro.create(text + ".", voice=cfg["voice"], speed=cfg.get("speed", 1.0), lang="en-us")
    level = np.abs(samples)
    loud = np.where(level > max(1e-3, level.max() * 0.02))[0]
    if len(loud):
        samples = samples[max(0, loud[0] - int(0.02 * rate)): loud[-1] + int(0.08 * rate)]
    sf.write(wav, samples, rate)
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", wav, "-ac", "1", "-b:a", "40k", os.path.join(out, name)], check=True)
    index[k] = name
    if n % 100 == 0:
        print(n, text, flush=True)

os.remove(wav)
# the short pause played between two announcements
subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-f", "lavfi", "-i", "anullsrc=r=24000:cl=mono", "-t", "0.3", "-ac", "1", "-b:a", "40k",
                os.path.join(out, "pause.mp3")], check=True)
json.dump({"voice": cfg["voice"], "clips": index}, open(os.path.join(out, "index.json"), "w", encoding="utf-8"), ensure_ascii=False, sort_keys=True, indent=0)
print(len(index), "clips")
