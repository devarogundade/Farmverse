// Which English texts have no translation yet. Play the game in the Unity Editor (any language): every text
// shown is collected into Assets/Farmverse/Content/Resources/Localization/en.json when Play stops. Then run
//   node tools/dev/loc-missing.mjs            (writes Claude outputs/loc-missing.json and prints the counts)
// and send that file for translation; the answers are added to fr.json / ja.json.
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
const dir = new URL('../../Assets/Farmverse/Content/Resources/Localization/', import.meta.url);
const read = (iso) => new Set(JSON.parse(readFileSync(new URL(iso + '.json', dir), 'utf8')).entries.map((e) => e.k));
const en = read('en');
const missing = {};
for (const iso of ['fr', 'ja']) {
  const have = read(iso);
  missing[iso] = [...en].filter((k) => !have.has(k));
  console.log(`${iso}: ${missing[iso].length} of ${en.size} texts not translated`);
}
mkdirSync(new URL('../../Claude outputs/', import.meta.url), { recursive: true });
writeFileSync(new URL('../../Claude outputs/loc-missing.json', import.meta.url), JSON.stringify(missing, null, 1));
