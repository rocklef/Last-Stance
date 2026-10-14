// Downloads the CC0 / OFL assets used by Last Stance into the Unity project.
//   node tools/fetch_assets.mjs
// Poly Haven (https://polyhaven.com, CC0): sky HDRI, courtyard textures, props.
// Google Fonts (OFL): HUD fonts.
import fs from "node:fs/promises";
import path from "node:path";

const OUT = "UnityProject/Assets/External";
const HDRI = "belfast_sunset_puresky";
const TEXTURES = ["stone_tiles_02", "japanese_stone_wall", "grey_roof_tiles", "lacquered_cherry_wood",
  "painted_plaster_wall", "japanese_cedar_bark", "old_wood_floor", "gravel_floor", "monastery_stone_floor"];
const MODELS = ["stone_fire_pit", "wooden_lantern_01", "antique_katana_01", "ceramic_vase_01", "chinese_stool",
  "wooden_barrels_01", "wooden_crate_02", "wooden_bucket_01", "boulder_01"];
const FONTS = {
  "Bangers-Regular.ttf": "https://github.com/google/fonts/raw/main/ofl/bangers/Bangers-Regular.ttf",
  "Anton-Regular.ttf": "https://github.com/google/fonts/raw/main/ofl/anton/Anton-Regular.ttf",
};

async function json(url) {
  const r = await fetch(url);
  if (!r.ok) throw new Error(`${url}: HTTP ${r.status}`);
  return r.json();
}

async function download(url, file) {
  try { await fs.access(file); return 0; } catch {}
  for (let attempt = 1; ; attempt++) {
    try {
      const r = await fetch(url);
      if (!r.ok) throw new Error(`${url}: HTTP ${r.status}`);
      const buf = Buffer.from(await r.arrayBuffer());
      await fs.mkdir(path.dirname(file), { recursive: true });
      await fs.writeFile(file, buf);
      return buf.length;
    } catch (e) {
      if (attempt >= 4) throw e;
      await new Promise((ok) => setTimeout(ok, 1500 * attempt));
    }
  }
}

let total = 0;

const hdri = await json(`https://api.polyhaven.com/files/${HDRI}`);
total += await download(hdri.hdri["2k"].hdr.url, `${OUT}/hdri/${HDRI}_2k.hdr`);
console.log("hdri   ", HDRI);

for (const id of TEXTURES) {
  const files = await json(`https://api.polyhaven.com/files/${id}`);
  for (const map of ["Diffuse", "nor_gl", "Rough"]) {
    const f = files[map]?.["2k"]?.jpg ?? files[map]?.["2k"]?.png;
    if (f) total += await download(f.url, `${OUT}/textures/${id}/${id}_${map}${path.extname(f.url)}`);
  }
  console.log("texture", id);
}

for (const id of MODELS) {
  const files = await json(`https://api.polyhaven.com/files/${id}`);
  const g = files.gltf["1k"].gltf;
  total += await download(g.url, `${OUT}/models/${id}/${id}.gltf`);
  for (const [rel, f] of Object.entries(g.include)) total += await download(f.url, `${OUT}/models/${id}/${rel}`);
  console.log("model  ", id);
}

for (const [name, url] of Object.entries(FONTS)) {
  try {
    total += await download(url, `${OUT}/fonts/${name}`);
    console.log("font   ", name);
  } catch (e) {
    console.log("font   ", name, "FAILED", e.message);
  }
}

console.log(`done, ${(total / 1e6).toFixed(1)} MB downloaded`);
