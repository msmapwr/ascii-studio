// Requires sharp. Uses SVG source artwork for all derived assets.
const fs = require('node:fs');
const path = require('node:path');
const sharp = require('sharp');
const root = path.resolve(__dirname, '..');
const brand = path.join(root, 'assets', 'branding');
const appAssets = path.join(root, 'src', 'Charloom', 'Assets');
async function main() {
    fs.mkdirSync(appAssets, { recursive: true });
    const names = ['primary', 'ribbon', 'grid'];
    const rendered = [];
    for (const name of names) {
        const svg = fs.readFileSync(path.join(brand, `charloom-${name}.svg`));
        const png = await sharp(svg).resize(512,512).png().toBuffer();
        fs.writeFileSync(path.join(brand, `charloom-${name}.png`), png);
        rendered.push(png);
    }
    const primary = fs.readFileSync(path.join(brand, 'charloom-primary.svg'), 'utf8');
    const inner = primary.slice(primary.indexOf('<defs>'), primary.lastIndexOf('</svg>'));
    fs.writeFileSync(path.join(brand, 'charloom-horizontal.svg'), `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1200 320" role="img"><title>Charloom</title><g transform="scale(.625)">${inner}</g><text x="335" y="182" font-family="Segoe UI,Arial,sans-serif" font-size="104" font-weight="600" fill="#33284E">Charloom</text></svg>`);
    fs.writeFileSync(path.join(brand, 'charloom-vertical.svg'), `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 640 700" role="img"><title>Charloom</title><g transform="translate(64 0)">${inner}</g><text x="320" y="626" text-anchor="middle" font-family="Segoe UI,Arial,sans-serif" font-size="76" font-weight="600" fill="#33284E">Charloom</text></svg>`);
    const mono = '<path fill="currentColor" fill-rule="evenodd" d="M110 436 229 94q7-20 27-20t27 20l119 342h-69l-22-68H201l-22 68Zm111-130h70l-35-110Z"/>';
    fs.writeFileSync(path.join(brand,'charloom-monochrome.svg'),`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" role="img"><title>Charloom monochrome</title>${mono}</svg>`);
    for (const size of [32,44,50,64,128,150,256,512,1024]) {
        await sharp(Buffer.from(primary)).resize(size,size).png().toFile(path.join(appAssets, `Logo${size}.png`));
    }
    for (const scale of [100,200,400]) {
        for(const [label,size] of [['Square44x44Logo',44],['Square150x150Logo',150],['StoreLogo',50]])
            await sharp(Buffer.from(primary)).resize(size*scale/100,size*scale/100).png().toFile(path.join(appAssets,`${label}.scale-${scale}.png`));
    }
    const frames = [];
    for(const size of [16,24,32,48,64,128,256]) frames.push({size,data:await sharp(Buffer.from(primary)).resize(size,size).png().toBuffer()});
    const header=Buffer.alloc(6+frames.length*16); header.writeUInt16LE(1,2); header.writeUInt16LE(frames.length,4);
    let offset=header.length;
    frames.forEach(({size,data},i)=>{const o=6+i*16;header[o]=size===256?0:size;header[o+1]=header[o];header.writeUInt16LE(1,o+4);header.writeUInt16LE(32,o+6);header.writeUInt32LE(data.length,o+8);header.writeUInt32LE(offset,o+12);offset+=data.length;});
    fs.writeFileSync(path.join(appAssets,'Charloom.ico'),Buffer.concat([header,...frames.map(f=>f.data)]));
    const pngTags=rendered.map((png,i)=>`<image x="${70+i*410}" y="120" width="340" height="340" href="data:image/png;base64,${png.toString('base64')}"/><text x="${240+i*410}" y="510" text-anchor="middle" font-size="24" fill="#40384E">${['Layered Glyph · 主方案','Folded A · 折叠字母','Character Portal · 字符入口'][i]}</text>`).join('');
    const board=`<svg xmlns="http://www.w3.org/2000/svg" width="1360" height="640" viewBox="0 0 1360 640"><rect width="1360" height="640" rx="32" fill="#F4F2FA"/><text x="70" y="74" font-family="Segoe UI,Arial" font-size="32" font-weight="600" fill="#33284E">Charloom / Icon concepts</text>${pngTags}<text x="70" y="595" font-family="Segoe UI,Arial" font-size="20" fill="#766F83">Violet × Cyan · Sculpted layers · Character grid</text></svg>`;
    await sharp(Buffer.from(board)).png().toFile(path.join(brand,'concept-board.png'));
}
main().catch(error=>{console.error(error);process.exitCode=1;});
