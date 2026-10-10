import test from 'node:test';
import assert from 'node:assert/strict';
import { DEFAULTS, normalize, makeFrame, nativeFontFamily, coverAccentFromPixels, playedPalette, controlsPalette } from '../src/core.mjs';
const snapshot = {
  playback:{trackId:'1',title:'歌曲',artist:'歌手',currentTime:1,updatedAt:1000,isPlaying:true,playbackRate:1,duration:10},
  lyric:{trackId:'1',timeOffset:0,lines:[{time:0,text:'第一句',translated:'one'},{time:2,text:'第二句',translated:'two'},{time:5,text:'第三句'}]}
};
test('单行与双行、翻译来自同一时钟', () => {
  assert.equal(makeFrame(snapshot,DEFAULTS,2100).text,'第二句');
  assert.equal(makeFrame(snapshot,normalize({layout:'single'}),2100).secondary,'');
  assert.equal(makeFrame(snapshot,normalize({layout:'double'}),2100).secondary,'第三句');
  assert.equal(makeFrame(snapshot,normalize({layout:'double',secondary:'translation'}),2100).secondary,'two');
});
test('暂停停止推算，歌词偏移仍生效', () => {
  const paused=structuredClone(snapshot); paused.playback.isPlaying=false;
  assert.equal(makeFrame(paused,DEFAULTS,20000).text,'第一句');
  paused.lyric.timeOffset=1500;
  assert.equal(makeFrame(paused,DEFAULTS,20000).text,'第二句');
});
test('恢复播放沿用宿主重置的时钟，不累计暂停时长', () => {
  const s=structuredClone(snapshot);
  s.playback.currentTime=3;s.playback.updatedAt=1000;
  s.playback.clock={trackId:'1',positionMs:3000,sampledAt:1000,isPlaying:false,isAdvancing:false,playbackRate:1};
  s.playback.isPlaying=false;
  assert.equal(makeFrame(s,DEFAULTS,31000).timelineMs,3000);
  s.playback.isPlaying=true;
  Object.assign(s.playback.clock,{sampledAt:31000,isPlaying:true,isAdvancing:true});
  const resumed=makeFrame(s,DEFAULTS,31000);
  assert.equal(resumed.timelineMs,3000);assert.equal(resumed.text,'第二句');
  assert.equal(makeFrame(s,DEFAULTS,31100).timelineMs,3100);
  s.playback.currentTime=3.1;s.playback.updatedAt=31100;
  Object.assign(s.playback.clock,{positionMs:3100,sampledAt:31100});
  assert.equal(makeFrame(s,DEFAULTS,31100).timelineMs,3100);
});
test('倍速恢复与暂停中跳转使用宿主时钟位置', () => {
  const s=structuredClone(snapshot);
  s.playback.currentTime=1;s.playback.updatedAt=1000;s.playback.isPlaying=false;
  s.playback.clock={trackId:'1',positionMs:4200,sampledAt:25000,isPlaying:false,isAdvancing:false,playbackRate:2};
  s.lyric.timeOffset=200;
  assert.equal(makeFrame(s,DEFAULTS,50000).timelineMs,4400);
  s.playback.isPlaying=true;
  Object.assign(s.playback.clock,{sampledAt:50000,isPlaying:true,isAdvancing:true});
  assert.equal(makeFrame(s,DEFAULTS,50100).timelineMs,4600);
  assert.equal(makeFrame(s,DEFAULTS,50100).coverTimeMs,4400);
});
test('播放按钮已开启但时钟尚未推进时，遮罩继续冻结', () => {
  const s=structuredClone(snapshot);
  s.playback.clock={trackId:'1',positionMs:2500,sampledAt:1000,isPlaying:true,isAdvancing:false,playbackRate:1};
  const held=makeFrame(s,DEFAULTS,25000);
  assert.equal(held.playing,true);assert.equal(held.advancing,false);assert.equal(held.timelineMs,2500);
  s.playback.isPlaying=false;s.playback.clock.isAdvancing=true;
  assert.equal(makeFrame(s,DEFAULTS,25000).timelineMs,2500);
});
test('切歌时不使用上一首残留的宿主时钟', () => {
  const s=structuredClone(snapshot);
  s.playback.clock={trackId:'old',positionMs:9000,sampledAt:1000,isPlaying:true,isAdvancing:true};
  assert.equal(makeFrame(s,DEFAULTS,1100).timelineMs,1100);
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
  assert.equal(s.width,DEFAULTS.width);assert.equal(s.fontSize,26);assert.equal(s.offset,0);assert.equal(s.layout,DEFAULTS.layout);
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

test('旧设置补齐封面与快捷操作，关闭状态和圆形选项可保存',()=>{
  const old=normalize({fontSize:18});
  assert.equal(old.hoverControls,true);assert.equal(old.showCover,true);
  assert.equal(old.coverShape,'rectangle');assert.equal(old.rotateCover,false);
  const changed=normalize({hoverControls:false,showCover:false,coverShape:'circle',rotateCover:true});
  assert.equal(changed.hoverControls,false);assert.equal(changed.showCover,false);
  assert.equal(changed.coverShape,'circle');assert.equal(changed.rotateCover,true);
  assert.equal(normalize({coverShape:'invalid'}).coverShape,'rectangle');
});

test('收藏、封面和旋转时间来自当前歌曲，歌词偏移不会改变旋转时钟',()=>{
  const song=structuredClone(snapshot);
  song.playback.isFavorite=true;song.playback.coverUrl='https://example.test/cover.png';song.lyric.timeOffset=1500;
  const frame=makeFrame(song,DEFAULTS,2000);
  assert.equal(frame.trackId,'1');assert.equal(frame.isFavorite,true);assert.equal(frame.coverUrl,song.playback.coverUrl);
  assert.equal(frame.coverTimeMs,2000);assert.equal(frame.timelineMs,3500);
  song.playback.isPlaying=false;song.playback.isFavorite=false;
  assert.equal(makeFrame(song,DEFAULTS,99999).coverTimeMs,1000);
  assert.equal(makeFrame(song,DEFAULTS,99999).isFavorite,false);
  assert.equal(makeFrame({playback:null},DEFAULTS).trackId,'');
});

test('快捷按钮保留靠左和居中，旧靠右及无效选项恢复默认',()=>{
  assert.equal(normalize({hoverControls:true}).controlsAlignment,DEFAULTS.controlsAlignment);
  for(const alignment of ['left','center'])assert.equal(normalize({controlsAlignment:alignment}).controlsAlignment,alignment);
  assert.equal(normalize({controlsAlignment:'right'}).controlsAlignment,DEFAULTS.controlsAlignment);
  assert.equal(normalize({controlsAlignment:'invalid'}).controlsAlignment,DEFAULTS.controlsAlignment);
});
test('封面取色排除透明和黑白背景，切换封面产生不同主色',()=>{
  const red=coverAccentFromPixels(new Uint8ClampedArray([255,255,255,255,0,0,0,255,220,30,20,255,1,220,1,0]));
  const blue=coverAccentFromPixels(new Uint8ClampedArray([20,50,210,255]));
  assert.match(red,/^#[A-F\d]{6}$/);assert.match(blue,/^#[A-F\d]{6}$/);assert.notEqual(red,blue);
  assert.ok(parseInt(red.slice(1,3),16)>parseInt(red.slice(5,7),16));
  assert.ok(parseInt(blue.slice(5,7),16)>parseInt(blue.slice(1,3),16));
  assert.equal(coverAccentFromPixels(new Uint8ClampedArray([30,40,50,0])), '');
  assert.equal(coverAccentFromPixels(new Uint8ClampedArray([255,255,255,255])), '#C8C8C8');
});

test('封面优先选择稳定的鲜艳色块，忽略大面积灰色和单像素噪点',()=>{
  const pixels=new Uint8ClampedArray([
    ...Array(900).fill([120,120,120,255]).flat(),
    ...Array(90).fill([140,120,115,255]).flat(),
    ...Array(30).fill([230,25,40,255]).flat(),
    0,255,0,255
  ]);
  assert.equal(coverAccentFromPixels(pixels),'#E61928');
  assert.equal(coverAccentFromPixels(new Uint8ClampedArray(Array(12).fill([230,210,20,255]).flat())),'#E6D214');
  const dark=coverAccentFromPixels(new Uint8ClampedArray(Array(16).fill([10,20,80,255]).flat()));
  const rgb=dark.slice(1).match(/../g).map(value=>parseInt(value,16));
  assert.equal(Math.max(...rgb),140);assert.ok((Math.max(...rgb)-Math.min(...rgb))/Math.max(...rgb)>.8);
});

test('按钮反馈开关独立且隐藏时保留配色，旧设置保持悬停关闭',()=>{
  const old=normalize();assert.equal(old.buttonHoverBackground,false);assert.equal(old.buttonPressedBackground,DEFAULTS.buttonPressedBackground);
  const s=normalize({buttonHoverBackground:true,buttonPressedBackground:false,buttonHoverColor:'#123456',buttonPressedColor:'#654321'});
  assert.equal(s.buttonHoverBackground,true);assert.equal(s.buttonPressedBackground,false);
  s.buttonHoverBackground=false;assert.equal(normalize(s).buttonHoverColor,'#123456');assert.equal(normalize(s).buttonPressedColor,'#654321');
  assert.equal(normalize({buttonHoverColor:'bad'}).buttonHoverColor,DEFAULTS.buttonHoverColor);
});
test('遮罩封面配色只改变歌词两行，失败回退且保留手动设置',()=>{
  const s=normalize({playedColorSource:'cover',controlsBackgroundSource:'custom',primaryPlayedColor:'#112233',secondaryPlayedColor:'#445566',showCover:false});
  assert.deepEqual(playedPalette(s,'#AABBCC'),{primary:'#AABBCC',secondary:'#AABBCC'});
  assert.deepEqual(playedPalette(s,''),{primary:'#112233',secondary:'#445566'});
  assert.equal(makeFrame(snapshot,s,1000,'#AABBCC').controlsPalette.background,'#202831');
  assert.equal(s.primaryPlayedColor,'#112233');s.playedColorSource='custom';
  assert.deepEqual(playedPalette(s,'#AABBCC'),{primary:'#112233',secondary:'#445566'});
  assert.equal(normalize({playedColorSource:'invalid'}).playedColorSource,DEFAULTS.playedColorSource);
});
test('按钮背景独立选色与透明度，图标不受背景透明度影响',()=>{
  const s=normalize({playedColorSource:'custom',controlsBackgroundSource:'cover',controlsIconColorSource:'custom',controlsBackgroundOpacity:40});
  const palette=controlsPalette(s,'#FFFFFF');
  assert.equal(palette.background,'#FFFFFF');assert.equal(palette.foreground,'#F1F7F6');assert.equal(palette.opacity,40);assert.equal(palette.backgroundCss,'#FFFFFF66');
  assert.equal(playedPalette(s,'#FFFFFF').primary,DEFAULTS.primaryPlayedColor);
  s.controlsBackgroundSource='custom';s.controlsBackgroundColor='#123456';s.controlsBackgroundOpacity=0;
  assert.equal(controlsPalette(s,'#FFFFFF').backgroundCss,'#12345600');assert.equal(controlsPalette(s).foreground,'#F1F7F6');
  s.controlsBackgroundOpacity=100;assert.equal(controlsPalette(s).backgroundCss,'#123456ff');
  assert.equal(normalize({controlsBackgroundOpacity:120}).controlsBackgroundOpacity,100);
  assert.equal(normalize({controlsBackgroundOpacity:-1}).controlsBackgroundOpacity,0);
  assert.equal(normalize({controlsBackgroundColor:'invalid'}).controlsBackgroundColor,'#202831');
});

test('垂直偏移使用默认值，支持负数并限制范围',()=>{
  assert.equal(normalize().verticalOffset,DEFAULTS.verticalOffset);
  assert.equal(normalize({verticalOffset:-3}).verticalOffset,-3);
  assert.equal(normalize({verticalOffset:'4'}).verticalOffset,4);
  assert.equal(normalize({verticalOffset:-50}).verticalOffset,-20);
  assert.equal(normalize({verticalOffset:50}).verticalOffset,20);
  assert.equal(normalize({verticalOffset:NaN}).verticalOffset,DEFAULTS.verticalOffset);
});

test('按钮图标单独选色，封面失败回退且不改变背景和遮罩',()=>{
  const s=normalize({playedColorSource:'custom',controlsBackgroundSource:'custom',controlsIconColorSource:'cover',controlsIconColor:'#334455',controlsBackgroundColor:'#123456'});
  const red=controlsPalette(s,'#E65F52'),blue=controlsPalette(s,'#76ACED');
  assert.equal(red.foreground,'#E65F52');assert.equal(blue.foreground,'#76ACED');
  assert.equal(red.background,'#123456');assert.equal(blue.background,'#123456');
  assert.equal(controlsPalette(s,'').foreground,'#334455');
  assert.equal(playedPalette(s,'#76ACED').primary,DEFAULTS.primaryPlayedColor);
  s.controlsIconColorSource='custom';assert.equal(controlsPalette(s,'#76ACED').foreground,'#334455');
  assert.equal(normalize({controlsIconColor:'invalid'}).controlsIconColor,'#F1F7F6');
  assert.equal(normalize({controlsIconColorSource:'invalid'}).controlsIconColorSource,DEFAULTS.controlsIconColorSource);
});
