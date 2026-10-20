import fs from 'node:fs';
import { compile } from '@vue/compiler-dom';
const root = new URL('./', import.meta.url);
// 固定生成文件换行，避免不同 Windows Git 设置改变构建结果。
const read = name => fs.readFileSync(new URL(name, root), 'utf8').replace(/\r\n/g, '\n');
const compiled = compile(read('src/SettingsPanel.html'), { mode:'function', prefixIdentifiers:true, hoistStatic:true }).code;
const panel = '// 设置界面由 SettingsPanel.html 预编译，勿手工编辑。\nfunction createPanelRender(Vue) {\n' + compiled + '\n}\n';
const core = read('src/core.mjs').replace(/^export /gm, '');
const navigation = read('src/navigation.mjs').replace(/^export /gm, '');
const maintenance = read('src/maintenance.mjs').replace(/^export /gm, '');
const entry = read('src/entry.mjs').replace(/^import .*\r?\n/gm, '').replace('// @compiled-panel', panel);
fs.writeFileSync(new URL('index.js', root), core + '\n' + navigation + '\n' + maintenance + '\n' + entry);
