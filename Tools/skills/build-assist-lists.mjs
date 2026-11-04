// The two lists of skills the assist leaves off its cooldown list by default (assets/aion2/skills/assist_skill_lists.json):
//  noCooldown: skills with a cooldown of at most 2 s in the site's class skill list (basic attacks such as Rending Blow and Keen Strike), and
//              for the classes whose list has no cooldowns the skill its text calls the basic attack (Vicious Strike, Quick Slice ...); others
//              with no cooldown are learned by the meter from use (see Aion2Cooldowns). The assist never lists any of them;
//  chain:      the follow-ups of the client's chain table (skill_chains.json), but not those the class list has as a skill of their own.
// Usage: node build-assist-lists.mjs <site content/public/armory/assets/class-skills.js> <Client/assets/aion2/skills/skill_chains.json> <out.json>
import fs from 'node:fs';
import vm from 'node:vm';

const [skillsJs, chainsJson, out] = process.argv.slice(2);
const sandbox = {window: {}};
vm.runInNewContext(fs.readFileSync(skillsJs, 'utf8'), sandbox);
const classes = sandbox.window.AION2_CLASS_SKILLS;
const base = (id) => (id < 1e7 || Math.floor(id / 1e4) % 100 === 0 ? id : Math.floor(id / 1e4) * 1e4);
const noCooldown = new Set(), standalone = new Set();
for (const skills of Object.values(classes)) {
  for (const s of Object.values(skills)) {
    if (s.t === 'a' || s.t === 's') standalone.add(s.id);
    if ((s.t === 'a' || s.t === 'c') && typeof s.cd === 'number' && s.cd <= 2) noCooldown.add(s.id);
    if (s.t === 'a' && /^basic attack\b/i.test(s.d || '')) noCooldown.add(s.id); // the classes without cooldowns in their list: their basic attack by its text
  }
}
const chain = new Set();
for (const follow of Object.values(JSON.parse(fs.readFileSync(chainsJson, 'utf8')))) {
  for (const [next] of follow) {
    if (next >= 1e7 && !standalone.has(base(next))) chain.add(base(next));
  }
}
const sorted = (s) => [...s].sort((a, b) => a - b);
fs.writeFileSync(out, JSON.stringify({noCooldown: sorted(noCooldown), chain: sorted(chain)}) + '\n');
console.log(noCooldown.size, 'skills with no cooldown,', chain.size, 'chain skills; follow-ups kept as skills of their own:',
  sorted(new Set(Object.values(JSON.parse(fs.readFileSync(chainsJson, 'utf8'))).flat().map(([n]) => base(n)).filter((n) => n >= 1e7 && standalone.has(n)))).join(', ') || 'none');
