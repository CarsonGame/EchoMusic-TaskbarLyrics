import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {spawnSync} from 'node:child_process';

if(process.platform!=='win32'){
 console.log('Native lifecycle check requires Windows.');
}else{
 const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
 const temporary=fs.mkdtempSync(path.join(os.tmpdir(),'echomusic-host-lifecycle-'));
 const framework=path.join(process.env.WINDIR,'Microsoft.NET','Framework64','v4.0.30319');
 const executable=path.join(temporary,'HostLifecycleHarness.exe'),report=path.join(temporary,'report.json');
 const references=['System.Web.Extensions.dll','System.Xaml.dll',...['PresentationFramework','PresentationCore','WindowsBase'].map(name=>path.join(framework,'WPF',`${name}.dll`))];
 const compile=spawnSync(path.join(framework,'csc.exe'),['/nologo','/target:winexe','/platform:x64',`/out:${executable}`,`/win32manifest:${path.join(root,'native','app.manifest')}`,...references.map(reference=>`/reference:${reference}`),path.join(root,'tests','HostLifecycleHarness.cs')],{windowsHide:true,encoding:'utf8'});
 assert.equal(compile.status,0,compile.stderr||compile.stdout||String(compile.error));
 // 只模拟自己的隐藏父窗口，不重启 Explorer，也不操作播放器。
 const test=spawnSync(executable,[path.join(root,'bin','TaskbarLyrics.exe'),report],{windowsHide:true,timeout:20000});
 assert.equal(test.status,0,fs.existsSync(report+'.error')?fs.readFileSync(report+'.error','utf8'):String(test.error));
 const result=JSON.parse(fs.readFileSync(report,'utf8'));assert.equal(result.passed,true);assert.equal(result.checks,33);
 console.log(`Native lifecycle: ${result.checks} checks passed; parent replacement/destruction, recovery, floating fallback and cleanup. Report: ${report}`);
}
