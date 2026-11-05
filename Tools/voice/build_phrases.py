"""Builds Tools/voice/phrases.json: what the natural voice says, recorded once by render.py (in the Voice workflow) into assets/voice/<voice>/.

- every class skill's English name + " ready" (the skills' cooldowns coming back, Ui/CooldownVoice);
- every named monster skill (a boss cast, Ui/CastSpeech) and the words said around it ("on you", "on everyone", "lethal", "Attention"), recorded on
  their own and played one after the other;
- the attack shapes said for a skill without a name ("Attack to the front" ...) and the samples of the settings' test buttons.
Names the client keeps for its own use (with digits, underscores, brackets, percent signs or longer than 40 letters) are left out.
Run from the repository: python3 Tools/voice/build_phrases.py
"""
import json
import re

SKILLS = "Client/assets/aion2/skills/"
names = json.load(open(SKILLS + "skill_names.json", encoding="utf-8"))
i18n = json.load(open(SKILLS + "skill_names_i18n.json", encoding="utf-8"))
ui = json.load(open("Client/assets/i18n/ui_strings.json", encoding="utf-8"))


def english(skill_id):
    v = i18n.get(skill_id, {}).get("en")
    return (v if v and v.strip() else names.get(skill_id) or "").strip()


def speakable(name):
    return (0 < len(name) <= 40 and not re.search(r"[0-9_()%\[\]{}<>/\\:;=+*#@&]", name) and name != "Attack" and any(c.isalpha() for c in name)
            and " - " not in name and not re.search(r"\b[A-Z]{3,}\b", name) and not re.search(r"[a-z][A-Z]", name))


ready = ui["Assist.VoiceReady"]["en"]  # "{0} ready"
classes, monsters = set(), set()
for skill_id in set(names) | set(i18n):
    i = int(skill_id)
    name = english(skill_id)
    if not speakable(name):
        continue
    if 11_000_000 <= i <= 19_999_999 or 110_000_000 <= i <= 199_999_999:
        classes.add(name)
    elif i < 11_000_000 and skill_id in i18n:
        monsters.add(name)

phrases = sorted(ready.format(n) for n in classes)
words = [ui[k]["en"] for k in ("Speech.OnYou", "Speech.OnAll", "Speech.Lethal", "Speech.Warn", "Speech.SampleIce", "Speech.SampleDeath")]
shapes = [v["en"] for k, v in ui.items() if k.startswith("Combat.Shape.")]
phrases += sorted(set(monsters) | set(words) | set(shapes) | {ready.format(ui["Assist.VoiceSample"]["en"])} - set(phrases))
json.dump({"voice": "af_heart", "speed": 1.0, "phrases": phrases}, open("Tools/voice/phrases.json", "w", encoding="utf-8"), ensure_ascii=False, indent=0)
print(len(classes), "class skills,", len(monsters), "monster skills,", len(phrases), "phrases")
