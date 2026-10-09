import test from 'node:test';
import assert from 'node:assert/strict';
import { DEFAULTS, normalize, makeFrame, nativeFontFamily } from '../src/core.mjs';
const snapshot = {
  playback:{trackId:'1',title:'歌曲',artist:'歌手',currentTime:1,updatedAt:1000,isPlaying:true,playbackRate:1,duration:10},
  lyric:{trackId:'1',timeOffset:0,lines:[{time:0,text:'第一句',translated:'one'},{time:2,text:'第二句',translated:'two'},{time:5,text:'第三句'}]}
};
test('单行与双行、翻译来自同一时钟', () => {
  assert.equal(makeFrame(snapshot,DEFAULTS,2100).text,'第二句');
  assert.equal(makeFrame(snapshot,DEFAULTS,2100).secondary,'');
  assert.equal(makeFrame(snapshot,normalize({layout:'double'}),2100).secondary,'第三句');
  assert.equal(makeFrame(snapshot,normalize({layout:'double',secondary:'translation'}),2100).secondary,'two');
});
test('暂停停止推算，歌词偏移仍生效', () => {
  const paused=structuredClone(snapshot); paused.playback.isPlaying=false;
  assert.equal(makeFrame(paused,DEFAULTS,20000).text,'第一句');
  paused.lyric.timeOffset=1500;
  assert.equal(makeFrame(paused,DEFAULTS,20000).text,'第二句');
});
test('切歌期间不会泄漏旧歌词', () => {
  const changed=structuredClone(snapshot); changed.playback.trackId='2';changed.playback.title='新歌曲';
  const frame=makeFrame(changed,normalize({layout:'double'}),2100);
  assert.equal(frame.text,'新歌曲'); assert.equal(frame.secondary,'歌手');
});
test('前奏和无歌词显示歌曲，逐字歌词起点用毫秒', () => {
  const intro=structuredClone(snapshot); intro.lyric.lines=[{time:4,text:'开场'}];
  assert.equal(makeFrame(intro,DEFAULTS,1000).text,'歌曲');
  intro.lyric.lines=[];assert.equal(makeFrame(intro,DEFAULTS).text,'歌曲');
  intro.lyric.lines=[{time:9,text:'逐字',characters:[{startTime:500}]}];
  assert.equal(makeFrame(intro,DEFAULTS,1000).text,'逐字');
});
test('倍速、播放结束和跳转按最新快照校准', () => {
  const fast=structuredClone(snapshot);fast.playback.playbackRate=2;
  assert.equal(makeFrame(fast,DEFAULTS,3000).text,'第三句');
  assert.equal(makeFrame(fast,DEFAULTS,999999).text,'第三句');
  fast.playback.currentTime=0;fast.playback.updatedAt=3000;
  assert.equal(makeFrame(fast,DEFAULTS,3000).text,'第一句');
});
test('设置限制范围并恢复未知枚举', () => {
  const s=normalize({width:Infinity,fontSize:90,offset:-1,layout:'bad'});
  assert.equal(s.width,360);assert.equal(s.fontSize,26);assert.equal(s.offset,0);assert.equal(s.layout,'single');
});
test('滚动时钟包含当前句起止、偏移和暂停', () => {
  const frame=makeFrame(snapshot,DEFAULTS,2500);
  assert.equal(frame.lineStartMs,2000);assert.equal(frame.lineEndMs,5000);assert.equal(frame.timelineMs,2500);
  const paused=structuredClone(snapshot);paused.playback.isPlaying=false;paused.playback.currentTime=3;paused.lyric.timeOffset=500;
  const fixed=makeFrame(paused,DEFAULTS,999999);
  assert.equal(fixed.timelineMs,3500);assert.equal(fixed.lineEndMs,5000);assert.equal(fixed.playing,false);
});
test('切歌重置滚动，末句逐字结束不重复加偏移', () => {
  const s=structuredClone(snapshot);s.playback.trackId='2';
  assert.equal(makeFrame(s,DEFAULTS,3000).lineEndMs,0);
  s.playback.trackId='1';s.playback.currentTime=6;s.lyric.timeOffset=500;
  s.lyric.lines[2].characters=[{startTime:5000,endTime:7500}];
  const last=makeFrame(s,DEFAULTS,1000);
  assert.equal(last.lineStartMs,5000);assert.equal(last.lineEndMs,7500);assert.equal(last.timelineMs,6500);
});
test('宿主歌词按 lyricHash 关联，歌曲 ID 不同也应显示并滚动', () => {
  const realShape=structuredClone(snapshot);
  realShape.playback.trackId='920474385';realShape.playback.lyricHash='213D580CA0BDCC28A5FDBA995FFDA106';
  realShape.lyric.trackId=realShape.playback.lyricHash;
  const frame=makeFrame(realShape,normalize({layout:'double',secondary:'translation'}),2500);
  assert.equal(frame.text,'第二句');assert.equal(frame.secondary,'two');
  assert.equal(frame.lineStartMs,2000);assert.equal(frame.lineEndMs,5000);
  realShape.playback.trackId='598028578';realShape.playback.lyricHash='NEW_SONG_HASH';
  assert.equal(makeFrame(realShape,DEFAULTS,2500).text,'歌曲');
  assert.equal(makeFrame(realShape,DEFAULTS,2500).lineEndMs,0);
});
test('旧字号迁移，两行独立设置和颜色校验', () => {
  const old=normalize({fontSize:20});assert.equal(old.primaryFontSize,20);assert.equal(old.secondaryFontSize,16);
  const s=normalize({primaryFontSize:24,secondaryFontSize:10,primaryColor:'#112233',secondaryColor:'bad',progressMask:true});
  assert.equal(s.primaryFontSize,24);assert.equal(s.secondaryFontSize,10);assert.equal(s.primaryColor,'#112233');
  assert.equal(s.secondaryColor,DEFAULTS.secondaryColor);assert.equal(s.progressMask,true);
});
test('播放器字体列表可用于原生字体，逐字时间传入遮罩', () => {
  assert.ok(nativeFontFamily('"Segoe UI", system-ui, sans-serif').startsWith('Segoe UI, '));
  const s=structuredClone(snapshot);s.appearance={fontFamily:'"Arial", system-ui'};
  s.lyric.lines[0].characters=[{text:'第一句',startTime:0,endTime:1500}];
  const frame=makeFrame(s,normalize({fontFamily:'follow'}),1000);
  assert.ok(frame.fontFamily.startsWith('Arial, '));assert.equal(frame.characters[0].endTime,1500);
});

test('屏幕选择默认主屏幕，支持全部与设备标识',()=>{
  assert.equal(normalize().display,'primary');
  assert.equal(normalize({display:'all'}).display,'all');
  assert.equal(normalize({display:String.fromCharCode(92,92)+'.'+String.fromCharCode(92)+'DISPLAY5'}).display,String.fromCharCode(92,92)+'.'+String.fromCharCode(92)+'DISPLAY5');
  assert.equal(normalize({display:'invalid'}).display,'primary');
});
