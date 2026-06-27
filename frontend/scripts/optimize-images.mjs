import { readdir, stat, writeFile } from "node:fs/promises";
import path from "node:path";
import sharp from "sharp";

const root = path.resolve("public/images");

async function optimizeFile(filePath) {
  const info = await stat(filePath);
  const before = info.size;
  const ext = path.extname(filePath).toLowerCase();
  const base = filePath.slice(0, -ext.length);
  const outPath = `${base}-sm${ext}`;

  let pipeline = sharp(filePath).rotate();
  if (ext === ".webp") {
    pipeline = pipeline.resize({ width: 960, withoutEnlargement: true }).webp({ quality: 72 });
  } else if (ext === ".jpg" || ext === ".jpeg") {
    pipeline = pipeline.resize({ width: 1600, withoutEnlargement: true }).jpeg({ quality: 78, mozjpeg: true });
  } else {
    return;
  }

  const buffer = await pipeline.toBuffer();
  await writeFile(outPath, buffer);
  console.log(`${path.basename(outPath)}: ${(before / 1024).toFixed(0)}KB -> ${(buffer.length / 1024).toFixed(0)}KB`);
}

async function walk(dir) {
  const entries = await readdir(dir, { withFileTypes: true });
  for (const entry of entries) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) await walk(full);
    else if (/\.(webp|jpe?g)$/i.test(entry.name) && !entry.name.includes("-sm.")) await optimizeFile(full);
  }
}

await walk(root);
