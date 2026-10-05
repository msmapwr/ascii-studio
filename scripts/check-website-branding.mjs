import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
const code=fs.readFileSync('website/dist/main.js','utf8').split('let fluent;')[0];
for (const [stored,expected] of [[{'asciistudio-theme':'dark'},'dark'],[{'charloom-theme':'light','asciistudio-theme':'dark'},'light'],[{},'system'],[{'charloom-theme':'invalid'},'system']]) {
 const context=vm.createContext({document:{documentElement:{}},matchMedia:()=>({matches:false}),localStorage:{getItem:key=>stored[key]??null}});
 vm.runInContext(code,context);
 assert.equal(vm.runInContext('preference',context),expected);
}
const unavailable=vm.createContext({document:{documentElement:{}},matchMedia:()=>({matches:false}),localStorage:{getItem:()=>{throw new Error('blocked')}}});
vm.runInContext(code,unavailable);
assert.equal(vm.runInContext('preference',unavailable),'system');
for (const name of fs.readdirSync('website/dist').filter(n=>/\.(html|js)$/.test(n))) {
 const text=fs.readFileSync('website/dist/'+name,'utf8');
 assert(!text.includes('msmapwr/ascii-studio'),`Old repository URL in ${name}`);
 assert(!text.includes('msmapwr.github.io/ascii-studio'),`Old Pages URL in ${name}`);
}
console.log('PASS brand URLs, legacy theme fallback, modern preference priority and blocked storage');
