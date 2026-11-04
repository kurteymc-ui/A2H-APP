// Builds Client/assets/aion2/pets/route.json from aion2helper.com's Pet Soul Map Atlas (content/public/guides/petmap/data.js and the
// pets list of guides/pets-route-map.html): the dense spawn spots of every pet per map (the route's stops are planned from them), the
// teleports, the start camps and the windstream islands, so the meter's route (Client/Aion2/PetRoute.cs) is the website's route.
// Usage: node make-route.mjs <path to data.js> <path to pets-route-map.html> <path to the meter's pets.json> <out route.json>
import fs from 'node:fs';
import vm from 'node:vm';

const [dataJs, pageHtml, petsJson, out] = process.argv.slice(2);
const sandbox = {window: {}};
vm.runInNewContext(fs.readFileSync(dataJs, 'utf8'), sandbox);
const PET3D = sandbox.window.PET3D;
const html = fs.readFileSync(pageHtml, 'utf8');
const PETS = JSON.parse(html.match(/const PETS=(\[.*?\]);\n/s)[1]);
const islandsSrc = html.match(/const ISLANDS=(\{.*?\n \}\});/s)[1];
const ISLANDS = vm.runInNewContext('(' + islandsSrc + ')');
const meter = JSON.parse(fs.readFileSync(petsJson, 'utf8'));
const idOf = new Map(Object.entries(meter.pets).map(([id, p]) => [p.names.en.toLowerCase(), Number(id)]));

const maps = {};
const keyOf = {verteron: 'verteron', altgard: 'altgard', reshanta: 'abyss'};
for (const [site, m] of Object.entries(PET3D.maps)) {
  // in the site's order (it decides between equally good stops): pet id, name, spots [x, z, monsters, zone] in metres
  const clusters = [];
  let missing = 0;
  for (const [name, spots] of Object.entries(m.clusters)) {
    const id = idOf.get(name.toLowerCase());
    if (!id) { missing++; continue; }
    clusters.push([id, name, spots.map(c => [c[0], c[1], c[2], c[3] || ''])]);
  }
  if (missing) console.error(`${site}: ${missing} pets without a meter id`);
  const tps = m.tps.map(t => [t[0], t[1], t[2], t[4]]);
  const starts = {elyos: [m.start[1], m.start[2], m.start[4]], asmodian: [m.start[1], m.start[2], m.start[4]]};
  if (site === 'reshanta') {
    const east = m.tps.find(t => t[0] === 't' && t[1] === 'Latesran Eastern Root');
    if (east) starts.asmodian = [east[1], east[2], east[4]];
  }
  const islands = {};
  for (const [name, i] of Object.entries(ISLANDS[site] || {})) islands[name] = {tp: i.tp, kibelisk: i.kib, how: i.how, leave: i.leave || '', unchecked: !!i.unchecked};
  maps[keyOf[site]] = {site, name: m.name, faction: m.faction, starts, tps, islands, clusters};
}
const genus = {};
for (const p of PETS) { const id = idOf.get(p.name.toLowerCase()); if (id) genus[id] = p.genus; }
// the planner's numbers, as the page has them
const num = (name) => { const m = html.match(new RegExp('const ' + name + '=([0-9.]+)')) || html.match(new RegExp('[,\\s]' + name + '=([0-9.]+)')); if (!m) throw new Error('the page has no ' + name); return Number(m[1]); };
fs.writeFileSync(out, JSON.stringify({version: 2, stopRadius: num('STOP_R'), goodSpot: num('GOOD_SPOT'), teleportCost: num('TP_COST'),
  covers: num('ROUTE_COVERS'), nodes: num('ROUTE_NODES'), islandStop: num('ISLAND_STOP'), genus, maps}));
console.log('wrote', out, Object.fromEntries(Object.entries(maps).map(([k, v]) => [k, v.clusters.length])));
