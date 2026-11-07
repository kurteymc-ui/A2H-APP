// Composes the body of a GitHub release in one fixed shape, so every release reads the same:
//
//   <what changed>          the text already written for the release, or the entries of this version from changelog.json
//   Full changelog: <url>
//
//   ### Installer
//   SHA-256 (<file>): <hash>
//   VirusTotal: <n/m> detections - <report link>
//
// Idempotent: the installer block of an earlier run is cut off and written again, so a re-run of the release workflow changes nothing.
// Usage: node release-notes.mjs --version 1.16.2 --body-file body.txt [--installer NAME --sha HASH --vt-result 1/64 --vt-link URL]
import { readFileSync } from "node:fs";

const MAX_ITEMS = 12;
const CHANGELOG_URL = `https://github.com/${process.env.GITHUB_REPOSITORY || "kurteymc-ui/Atreia-App"}/commits/main`;

const arg = (name) => { const i = process.argv.indexOf(`--${name}`); return i > 0 ? process.argv[i + 1] : undefined; };
const version = arg("version");
const body = arg("body-file") ? readFileSync(arg("body-file"), "utf8") : "";

// What the release says about itself: everything before the installer block (or the old one-line SHA / VirusTotal lines).
const marker = body.search(/(### Installer|SHA-256 of the installer|VirusTotal scan of the installer)/);
let intro = (marker >= 0 ? body.slice(0, marker) : body).replace(/^Full changelog: .*$/m, "").trim();
const sha = arg("sha") ?? /SHA-256 (?:\(([^)]*)\)|of the installer \(([^)]*)\)): (\w{64})/.exec(body)?.[3];
const installer = arg("installer") ?? /SHA-256 (?:\(([^)]*)\)|of the installer \(([^)]*)\)):/.exec(body)?.slice(1, 3).find(Boolean);
const vt = /VirusTotal(?: scan of the installer)?: (\d+\/\d+) detections - (\S+)/.exec(body);
const vtResult = arg("vt-result") ?? vt?.[1];
const vtLink = arg("vt-link") ?? vt?.[2];

// A release without its own text ("Release v1.16.0", or empty) gets its version's update notes, the same ones the app shows (Client/assets/changelog/updates.json).
if (!intro || /^Release v?\d+\.\d+\.\d+$/.test(intro)) {
  const notes = JSON.parse(readFileSync(new URL("../../Client/assets/changelog/updates.json", import.meta.url), "utf8"));
  const release = (notes.versions ?? []).find((r) => r.version === version);
  const items = release?.items ?? [];
  if (items.length > 0) {
    const label = { new: "New", improved: "Improved", fixed: "Fixed" };
    const shown = items.slice(0, MAX_ITEMS).map((i) => `- **${label[i.kind] ?? "Improved"} · ${i.area}: ${i.title}.** ${i.text}` + (i.example ? `\n  *Example: ${i.example}*` : ""));
    if (items.length > MAX_ITEMS) shown.push(`- and ${items.length - MAX_ITEMS} more`);
    intro = (release.headline ? `### ${release.headline}\n` : "### What's changed\n") + shown.join("\n");
  } else {
    intro = `Release v${version}`;
  }
}

if (!intro.startsWith("### ")) intro = `### What's changed\n${intro}`;
const lines = [intro, "", `Full changelog: ${CHANGELOG_URL}`];
if (sha || vtResult) {
  lines.push("", "### Installer");
  if (sha) lines.push(`SHA-256 (${installer ?? "installer"}): ${sha}`);
  if (vtResult) lines.push(`VirusTotal: ${vtResult} detections - ${vtLink}`);
}
process.stdout.write(lines.join("\n") + "\n");
