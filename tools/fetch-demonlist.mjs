/*
 * Тянет первые 200 позиций глобального демонлиста (pointercrate.com) и кладёт их
 * в два места:
 *
 *   1. src/GdTracker.Api/Data/Seed/demonlist-seed.json — сид сервера, которым он
 *      наполняет пустую базу;
 *   2. блок между маркерами DEMONLIST-SEED в docs/demonlist.html — расстановка,
 *      которую страница показывает, пока сервер не подключён.
 *
 *   node tools/fetch-demonlist.mjs
 *
 * Запускается вручную: это стартовая расстановка, дальше список живёт в базе
 * и правится модераторами через API.
 */
import { readFile, writeFile, mkdir } from "node:fs/promises";
import { dirname } from "node:path";

const API = "https://pointercrate.com/api/v2/demons/listed/";
const WANT = 200;
const SEED_FILE = "src/GdTracker.Api/Data/Seed/demonlist-seed.json";
const PAGE_FILE = "docs/demonlist.html";
const BEGIN = "/* DEMONLIST-SEED-BEGIN */";
const END = "/* DEMONLIST-SEED-END */";

async function page(after) {
  const res = await fetch(`${API}?limit=100&after=${after}`, {
    headers: { "User-Agent": "gd-progress-tracker seed script" },
  });
  if (!res.ok) throw new Error(`pointercrate ответил ${res.status}`);
  return res.json();
}

const raw = [];
for (let after = 0; after < WANT; after += 100) raw.push(...(await page(after)));

const entries = raw
  .filter((d) => d.position <= WANT)
  .sort((a, b) => a.position - b.position)
  .map((d) => ({
    position: d.position,
    name: d.name,
    publisher: d.publisher?.name ?? "",
    verifier: d.verifier?.name ?? "",
    levelId: d.level_id ?? null,
    video: d.video ?? null,
    thumbnail: d.thumbnail ?? null,
    requirement: d.requirement ?? 100,
  }));

if (entries.length !== WANT) throw new Error(`ожидалось ${WANT} позиций, пришло ${entries.length}`);

// --- сид сервера: со всеми полями, их хранит база ---
await mkdir(dirname(SEED_FILE), { recursive: true });
await writeFile(
  SEED_FILE,
  JSON.stringify({ source: "pointercrate.com", fetchedAtUtc: new Date().toISOString(), entries }, null, 2) + "\n",
  "utf8",
);
console.log(`записано ${entries.length} позиций → ${SEED_FILE}`);

/* --- расстановка внутри страницы ---
   Строкой на позицию и без превью: превью страница не показывает, а лишние
   тридцать килобайт возить в каждой загрузке незачем. */
const compact = entries
  .map((e) => JSON.stringify([e.position, e.name, e.publisher, e.verifier, e.levelId, e.video, e.requirement]))
  .join(",\n    ");

const html = await readFile(PAGE_FILE, "utf8");
const from = html.indexOf(BEGIN);
const to = html.indexOf(END);
if (from < 0 || to < 0) throw new Error(`в ${PAGE_FILE} нет маркеров ${BEGIN} … ${END}`);

const block = `${BEGIN}\n  var SEED = [\n    ${compact}\n  ];\n  ${END}`;
await writeFile(PAGE_FILE, html.slice(0, from) + block + html.slice(to + END.length), "utf8");
console.log(`записано ${entries.length} позиций → ${PAGE_FILE}`);
