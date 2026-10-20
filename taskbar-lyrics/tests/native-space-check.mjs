import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {spawnSync} from 'node:child_process';

if(process.platform!=='win32')console.log('Native taskbar-space check requires Windows.');
else {
  const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
  const temporary=fs.mkdtempSync(path.join(os.tmpdir(),'echomusic-taskbar-space-'));
  const framework=path.join(process.env.WINDIR,'Microsoft.NET','Framework64','v4.0.30319');
  const executable=path.join(temporary,'TaskbarSpaceHarness.exe'),report=path.join(temporary,'report.json');
  const references=['System.Web.Extensions.dll','System.Xaml.dll',...['PresentationFramework','PresentationCore','WindowsBase'].map(name=>path.join(framework,'WPF',`${name}.dll`))];
  const compile=spawnSync(path.join(framework,'csc.exe'),['/nologo','/target:winexe','/platform:x64',`/out:${executable}`,`/win32manifest:${path.join(root,'native','app.manifest')}`,...references.map(reference=>`/reference:${reference}`),path.join(root,'tests','TaskbarSpaceHarness.cs')],{windowsHide:true,encoding:'utf8'});
  assert.equal(compile.status,0,compile.stderr||compile.stdout||String(compile.error));
  const test=spawnSync(executable,[path.join(root,'bin','TaskbarLyrics.exe'),report],{windowsHide:true,timeout:20000});
  assert.equal(test.status,0,fs.existsSync(report+'.error')?fs.readFileSync(report+'.error','utf8'):String(test.error));
  const result=JSON.parse(fs.readFileSync(report,'utf8'));assert.equal(result.passed,true);
  console.log(`Native taskbar space: ${result.checks} checks passed; free gaps, cover-only, hiding, DPI, hover and fixed button size.`);
}
