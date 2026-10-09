import fs from 'node:fs';
import { compile } from '@vue/compiler-dom';
const root = new URL('./', import.meta.url);
const read = name => fs.readFileSync(new URL(name, root), 'utf8');
const compiled = compile(read('src/SettingsPanel.html'), { mode:'function', prefixIdentifiers:true, hoistStatic:true }).code;
const panel = '// 设置界面由 SettingsPanel.html 预编译，勿手工编辑。\nfunction createPanelRender(Vue) {\n' + compiled + '\n}\n';
const core = read('src/core.mjs').replace(/^export /gm, '');
const entry = read('src/entry.mjs').replace(/^import .*\n/, '').replace('// @compiled-panel', panel);
fs.writeFileSync(new URL('index.js', root), core + '\n' + entry);
