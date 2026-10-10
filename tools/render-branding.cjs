// Render the editable LightShaper SVG assets and package the Windows icon.
const fs = require('node:fs');
const path = require('node:path');
const args = process.argv.slice(2);
const moduleIndex = args.indexOf('--sharp');
const sharp = require(moduleIndex >= 0 ? args[moduleIndex + 1] : 'sharp');
const assets = path.resolve(__dirname, '..', 'Assets');
const sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];

async function main() {
  const source = fs.readFileSync(path.join(assets, 'icon.svg'), 'utf8');
  const images = [];
  for (const size of sizes) {
    images.push(await sharp(Buffer.from(source))
      .resize(size, size).png().toBuffer());
  }

  // ICO directories can contain PNG images, preserving their full alpha channel.
  const directory = Buffer.alloc(6 + sizes.length * 16);
  directory.writeUInt16LE(1, 2);
  directory.writeUInt16LE(sizes.length, 4);
  let offset = directory.length;
  for (let i = 0; i < sizes.length; i++) {
    const entry = 6 + i * 16;
    directory[entry] = directory[entry + 1] = sizes[i] === 256 ? 0 : sizes[i];
    directory.writeUInt16LE(1, entry + 4);
    directory.writeUInt16LE(32, entry + 6);
    directory.writeUInt32LE(images[i].length, entry + 8);
    directory.writeUInt32LE(offset, entry + 12);
    offset += images[i].length;
  }
  fs.writeFileSync(path.join(assets, 'app.ico'), Buffer.concat([directory, ...images]));
  await sharp(Buffer.from(source)).resize(1024, 1024).png().toFile(path.join(assets, 'logo.png'));
  for (const name of ['lightshaper-logo', 'lightshaper-wordmark']) {
    await sharp(path.join(assets, `${name}.svg`)).png().toFile(path.join(assets, `${name}.png`));
  }
  console.log(`Rendered application logo, ${sizes.length} ICO sizes, and original LightShaper assets.`);
}

main().catch(error => { console.error(error); process.exitCode = 1; });
