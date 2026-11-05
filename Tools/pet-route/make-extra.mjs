// Builds Client/assets/aion2/pets/spawns-extra.json: the spawn points of atreia.app's Pet Soul Map Atlas (content/public/guides/petmap/data.js)
// that the meter's own spawn table (spawns.json) does not have. The atlas has every point of the meter's table
// (all but 48 of 5,269 on Verteron, 74 of 5,069 on Altgard, none of 990 in Lower Reshanta, within 15 m) and about 15 % more - monsters like the
// Ellun River Kailin or the Search Party Tayga, whose spawns are not in it - so with these the map shows a dot at every spot the route leads to.
// Usage: node make-extra.mjs <path to data.js> <the meter's assets/aion2 folder> <out spawns-extra.json>
import fs from 'node:fs';
import vm from 'node:vm';
import path from 'node:path';

const [dataJs, assets, out] = process.argv.slice(2);
const sandbox = {window: {}};
vm.runInNewContext(fs.readFileSync(dataJs, 'utf8'), sandbox);
const PET3D = sandbox.window.PET3D;
const read = f => JSON.parse(fs.readFileSync(path.join(assets, f), 'utf8'));
const pets = read('pets/pets.json'), spawns = read('pets/spawns.json').maps, maps = read('maps/maps.json').maps;
const idOf = new Map(Object.entries(pets.pets).map(([id, p]) => [p.names.en.toLowerCase(), Number(id)]));
const petsOf = new Map(Object.entries(pets.monsters).map(([npc, v]) => [Number(npc), Array.isArray(v) ? v : [v]]));
const keyOf = {verteron: 'verteron', altgard: 'altgard', reshanta: 'abyss'};
const result = {};
for (const [site, m] of Object.entries(PET3D.maps)) {
  const key = keyOf[site];
  // the meter's points of every pet on this map, in metres
  const own = new Map();
  for (const table of maps[key].tables ?? [maps[key].table]) {
    for (const [npc, points] of Object.entries(spawns[table] ?? {})) {
      for (const pet of petsOf.get(Number(npc)) ?? []) {
        if (!own.has(pet)) own.set(pet, []);
        for (const q of points) own.get(pet).push([q[0] / 100, q[1] / 100]);
      }
    }
  }
  const extra = {};
  let added = 0;
  for (const [name, points] of Object.entries(m.spawns)) {
    const id = idOf.get(name.toLowerCase());
    if (!id) continue;
    const mine = own.get(id) ?? [];
    for (const [x, , z] of points) {
      if (mine.some(([mx, mz]) => Math.abs(mx - x) < 15 && Math.abs(mz - z) < 15)) continue;
      (extra[id] ??= []).push(Math.round(x * 100), Math.round(z * 100)); // world units, x and y as the meter's own table
      added++;
    }
  }
  result[key] = extra;
  console.log(key, 'points added', added);
}
fs.writeFileSync(out, JSON.stringify({about: 'Spawn points of the Pet Soul Map Atlas (atreia.app) that the meter's spawn table lacks; world units, flat x,y pairs per pet id.', maps: result}));
