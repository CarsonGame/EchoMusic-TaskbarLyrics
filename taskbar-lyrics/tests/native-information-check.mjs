import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {spawnSync,spawn} from 'node:child_process';
import http from 'node:http';
if(process.platform!=='win32')console.log('Native information hit check requires Windows.');
else{
 const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
 const temporary=fs.mkdtempSync(path.join(os.tmpdir(),'echomusic-information-hit-'));
 const framework=path.join(process.env.WINDIR,'Microsoft.NET','Framework64','v4.0.30319');
 const executable=path.join(temporary,'InformationHitHarness.exe'),report=path.join(temporary,'report.json');
 const references=['System.Web.Extensions.dll','System.Xaml.dll',...['PresentationFramework','PresentationCore','WindowsBase'].map(name=>path.join(framework,'WPF',`${name}.dll`))];
 const compile=spawnSync(path.join(framework,'csc.exe'),['/nologo','/target:winexe','/platform:x64',`/out:${executable}`,`/win32manifest:${path.join(root,'native','app.manifest')}`,...references.map(reference=>`/reference:${reference}`),path.join(root,'tests','InformationHitHarness.cs')],{windowsHide:true,encoding:'utf8'});
 assert.equal(compile.status,0,compile.stderr||compile.stdout||String(compile.error));
 const requests=[];
 const server=http.createServer((request,response)=>{requests.push(new URL(request.url,'http://127.0.0.1'));response.end(JSON.stringify({ok:true}));});
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
 const endpoint='http://127.0.0.1:'+server.address().port+'/state?token=fixture-token';
 const test=await new Promise(resolve=>{
   const child=spawn(executable,[path.join(root,'bin','TaskbarLyrics.exe'),report,endpoint],{windowsHide:true});
   const timer=setTimeout(()=>{child.kill();},20000);
   child.on('error',error=>{clearTimeout(timer);resolve({status:-1,error});});
   child.on('exit',status=>{clearTimeout(timer);resolve({status});});
 });
 await new Promise(resolve=>server.close(resolve));
 assert.equal(test.status,0,fs.existsSync(report+'.error')?fs.readFileSync(report+'.error','utf8'):String(test.error));
 assert.equal(requests.length,3,fs.existsSync(path.join(temporary,'TaskbarLyrics.log'))?fs.readFileSync(path.join(temporary,'TaskbarLyrics.log'),'utf8'):'No native log');
 for(const [index,url] of requests.entries()){
   assert.equal(url.pathname,'/navigate-information');assert.equal(url.searchParams.get('token'),'fixture-token');
   assert.equal(url.searchParams.get('trackId'),'歌曲 & id');assert.equal(url.searchParams.get('kind'),index===0?'song':'artist');
   assert.equal(url.searchParams.get('index'),String(index-1));
   assert.equal(url.searchParams.get('artist'),index===0?'':index===1?'歌手😀':'最后一位歌手');
 }

 assert.equal(test.status,0,fs.existsSync(report+'.error')?fs.readFileSync(report+'.error','utf8'):String(test.error));
 const result=JSON.parse(fs.readFileSync(report,'utf8'));assert.equal(result.passed,true);
 console.log(`Native information hit: ${result.checks} checks passed; individual artists, title, separators, scrolling, scaling and hidden state. Report: ${report}`);
}
