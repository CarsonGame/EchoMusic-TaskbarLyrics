// 所有可调数值由 EchoMusic 插件设置保存。
// 默认值采用当前已保存的配置，“恢复默认”使用同一套设置。
const DEFAULTS = Object.freeze({
  "enabled": true,
  "display": "primary",
  "layout": "double",
  "secondary": "next",
  "width": 360,
  "fontSize": 16,
  "primaryFontSize": 16,
  "secondaryFontSize": 12,
  "fontFamily": "follow",
  "primaryColor": "#ffffff",
  "secondaryColor": "#ffffff",
  "primaryPlayedColor": "#85DCD0",
  "secondaryPlayedColor": "#85DCD0",
  "playedColorSource": "cover",
  "controlsBackgroundSource": "cover",
  "controlsIconColorSource": "cover",
  "controlsIconColor": "#F1F7F6",
  "controlsBackgroundColor": "#202831",
  "controlsBackgroundOpacity": 0,
  "buttonHoverBackground": false,
  "buttonHoverColor": "#304449",
  "buttonPressedBackground": false,
  "buttonPressedColor": "#3B585C",
  "progressMask": true,
  "offset": 12,
  "verticalOffset": 2,
  "opacity": 100,
  "theme": "custom",
  "hidePaused": false,
  "hideFullscreen": true,
  "scrollLyrics": true,
  "hoverControls": true,
  "controlsAlignment": "left",
  "showCover": true,
  "coverShape": "rectangle",
  "rotateCover": false,
  "hoverProgress": true,
  "smartContrast": true,
  "trackTransitions": true,
  "transitionDuration": 280,
  "noticeEnabled": true,
  "noticeOnlyTaskbarSwitch": true,
  "noticeDuration": 2,
  "noticeFadeDuration": 180,
  "noticeHoldOnHover": true,
  "noticeBackgroundStyle": "glass",
  "noticeBackgroundColor": "#ffffff",
  "noticeTransparency": 100,
  "noticeWidth": 180,
  "noticeRadius": 15,
  "noticeGap": 8,
  "noticeShowArtist": true,
  "noticeTitleFontSize": 15,
  "noticeArtistFontSize": 12,
  "noticeTextSource": "auto",
  "noticeTextAlignment": "center",
  "noticeTitleColor": "#F1F7F6",
  "noticeArtistColor": "#BFC5CA",
  "noticeScrollText": true
});
function normalize(value = {}) {
  const s = { ...DEFAULTS, ...value };
  // 旧版字号迁移到独立行设置，保留用户原来的视觉比例。
  s.primaryFontSize = value.primaryFontSize ?? value.fontSize ?? DEFAULTS.primaryFontSize;
  s.secondaryFontSize = value.secondaryFontSize ?? (value.fontSize ? Math.round(value.fontSize * .8) : DEFAULTS.secondaryFontSize);
  if (s.theme==='light' || s.theme==='dark') {
    if (s.theme==='light') { s.primaryColor=value.primaryColor || '#202D35';s.secondaryColor=value.secondaryColor || '#62737B'; }
    s.theme='custom';
  }
  for (const [key, min, max] of [['width',120,900],['fontSize',10,26],['primaryFontSize',10,36],['secondaryFontSize',8,36],['offset',0,2000],['verticalOffset',-20,20],['opacity',0,100],['controlsBackgroundOpacity',0,100],['transitionDuration',100,1000],['noticeDuration',1,30],['noticeFadeDuration',0,1000],['noticeTransparency',0,100],['noticeWidth',180,480],['noticeRadius',0,24],['noticeGap',0,40],['noticeTitleFontSize',10,24],['noticeArtistFontSize',8,20]]) {
    s[key] = Math.min(max, Math.max(min, Number.isFinite(Number(s[key])) ? Number(s[key]) : DEFAULTS[key]));
  }
  for (const [key, values] of [['layout',['single','double']],['secondary',['next','translation']],['theme',['auto','light','dark','custom']],['coverShape',['rectangle','circle']],['controlsAlignment',['left','center']],['playedColorSource',['custom','cover']],['controlsBackgroundSource',['custom','cover']],['controlsIconColorSource',['custom','cover']],['noticeBackgroundStyle',['custom','cover','glass']],['noticeTextSource',['auto','custom']],['noticeTextAlignment',['left','center']]]) {
    if (!values.includes(s[key])) s[key] = DEFAULTS[key];
  }
  for (const key of ['enabled','hidePaused','hideFullscreen','scrollLyrics','progressMask','hoverControls','showCover','rotateCover','buttonHoverBackground','buttonPressedBackground','hoverProgress','smartContrast','trackTransitions','noticeEnabled','noticeOnlyTaskbarSwitch','noticeHoldOnHover','noticeShowArtist','noticeScrollText']) s[key] = Boolean(s[key]);
  for (const key of ['primaryColor','secondaryColor','primaryPlayedColor','secondaryPlayedColor','controlsBackgroundColor','controlsIconColor','buttonHoverColor','buttonPressedColor','noticeBackgroundColor','noticeTitleColor','noticeArtistColor']) {
    if (!/^#[\da-f]{6}$/i.test(s[key])) s[key]=DEFAULTS[key];
  }
  // Windows 显示设备标识用于跨重启保存屏幕选择。
  if (!['primary','all'].includes(s.display) && !/^\\\\\.\\DISPLAY\d+$/i.test(String(s.display))) s.display='primary';
  s.fontFamily = String(s.fontFamily || 'follow').trim();
  return s;
}
function lineStart(line) {
  const first = line.characters?.[0]?.startTime;
  return Number.isFinite(first) ? first : Number(line.time) * 1000;
}
function nativeFontFamily(font) {
  // 将播放器 CSS 字体列表转换为 WPF 字体列表，保留字体顺序。
  const generic = new Set(['system-ui','sans-serif','-apple-system','BlinkMacSystemFont']);
  return String(font || '').split(',').map(name => name.trim().replace(/^['"]|['"]$/g,'')).filter(name => name && !generic.has(name)).concat(['Microsoft YaHei UI','Segoe UI']).join(', ');
}
function coverAccentFromPixels(pixels) {
  // 鲜艳色块优先，忽略透明像素和零星噪点，避免灰色背景占据取色结果。
  const bins=new Map();let total=0;
  for(let i=0;i+3<pixels.length;i+=4){
    const [r,g,b,a]=pixels.slice(i,i+4);if(a<128)continue;
    total++;
    const key=((r>>4)<<8)|((g>>4)<<4)|(b>>4);
    const bin=bins.get(key)||[0,0,0,0];
    bin[0]+=r;bin[1]+=g;bin[2]+=b;bin[3]++;bins.set(key,bin);
  }
  if(!total)return '';
  const minimum=total<32?1:Math.max(2,Math.ceil(total*.004));
  const supported=[...bins.values()].filter(bin=>bin[3]>=minimum);
  const candidates=(supported.length?supported:[...bins.values()]).map(bin=>{
    const rgb=bin.slice(0,3).map(c=>c/bin[3]),max=Math.max(...rgb),min=Math.min(...rgb);
    return {rgb,count:bin[3],value:max/255,saturation:max?(max-min)/max:0};
  });
  // 有鲜艳区域时只在其中选择，面积作为次要权重，保留封面原有色相与饱和度。
  const vivid=candidates.filter(color=>color.saturation>=.5 && color.value>=.4);
  const chromatic=candidates.filter(color=>color.saturation>=.15 && color.value>=.2);
  const pool=vivid.length?vivid:chromatic.length?chromatic:candidates;
  if(!pool.length)return '';
  let selected=pool[0],score=-1;
  for(const color of pool){
    const weight=chromatic.length?Math.pow(color.saturation,2)*(.4+.6*color.value)*Math.pow(color.count/total,.25):color.count;
    if(weight>score){selected=color;score=weight;}
  }
  const multiplier=selected.value>0 && selected.value<.55?.55/selected.value:1;
  const rgb=selected.rgb.map(c=>Math.min(255,c*multiplier));
  // 黑白封面回退中性灰，不凭空生成彩色；彩色不再混白导致褪色。
  if(!chromatic.length){const gray=Math.min(200,Math.max(140,rgb[0]*.299+rgb[1]*.587+rgb[2]*.114));rgb.fill(gray);}
  return '#'+rgb.map(c=>Math.round(c).toString(16).padStart(2,'0')).join('').toUpperCase();
}
function playedPalette(settings,coverAccent='') {
  const automatic=settings.playedColorSource==='cover';
  const color=automatic && /^#[\da-f]{6}$/i.test(coverAccent)?coverAccent:'';
  return {primary:color||settings.primaryPlayedColor,secondary:color||settings.secondaryPlayedColor};
}
function controlsPalette(settings,coverAccent='') {
  // 背景和图标分别选色，背景透明度不影响图标。
  const background=settings.controlsBackgroundSource==='cover' && /^#[\da-f]{6}$/i.test(coverAccent)?coverAccent:settings.controlsBackgroundColor;
  const foreground=settings.controlsIconColorSource==='cover' && /^#[\da-f]{6}$/i.test(coverAccent)?coverAccent:settings.controlsIconColor;
  const opacity=settings.controlsBackgroundOpacity;
  return {background,foreground,opacity,backgroundCss:background+Math.round(opacity*255/100).toString(16).padStart(2,'0')};
}
function noticePalette(settings,coverAccent='',light=false) {
  const background=settings.noticeBackgroundStyle==='cover' && /^#[\da-f]{6}$/i.test(coverAccent)?coverAccent:settings.noticeBackgroundColor;
  const opacity=100-settings.noticeTransparency;
  const base=blendColor(background,light?'#E8EDEF':'#202831',opacity);
  const white='#F1F7F6',dark='#202D35';
  const foreground=contrastRatio(white,base)>=contrastRatio(dark,base)?white:dark;
  const automatic=settings.noticeTextSource==='auto';
  return {background,opacity,backgroundCss:background+Math.round(opacity*255/100).toString(16).padStart(2,'0'),
    title:automatic?readableCoverColor(foreground,[base],4.5):settings.noticeTitleColor,
    artist:automatic?readableCoverColor(foreground===white?'#BFC5CA':'#4B5561',[base],4.5):settings.noticeArtistColor};
}
function contrastRatio(first,second) {
  const luminance=color=>{
    const channels=[1,3,5].map(i=>parseInt(color.slice(i,i+2),16)/255).map(v=>v<=.04045?v/12.92:((v+.055)/1.055)**2.4);
    return channels[0]*.2126+channels[1]*.7152+channels[2]*.0722;
  };
  const a=luminance(first),b=luminance(second);
  return (Math.max(a,b)+.05)/(Math.min(a,b)+.05);
}
function readableCoverColor(color,backgrounds,minimum=3) {
  // 保留色相，寻找达到可读对比度的最小亮度调整；手动颜色由调用方保留。
  const score=value=>Math.min(...backgrounds.map(background=>contrastRatio(value,background)));
  if(score(color)>=minimum)return color;
  const rgb=[1,3,5].map(i=>parseInt(color.slice(i,i+2),16));
  let best=color,bestScore=score(color);
  for(let step=1;step<=100;step++)for(const endpoint of [255,0]){
    const candidate='#'+rgb.map(v=>Math.round(v+(endpoint-v)*step/100).toString(16).padStart(2,'0')).join('').toUpperCase();
    const value=score(candidate);if(value>=minimum)return candidate;
    if(value>bestScore){best=candidate;bestScore=value;}
  }
  return best;
}
function blendColor(color,background,opacity) {
  const alpha=Math.max(0,Math.min(1,opacity/100));
  return '#'+[1,3,5].map(i=>Math.round(parseInt(color.slice(i,i+2),16)*alpha+parseInt(background.slice(i,i+2),16)*(1-alpha)).toString(16).padStart(2,'0')).join('').toUpperCase();
}
function previewPalettes(settings,accent,light=false) {
  const played=playedPalette(settings,accent),controls=controlsPalette(settings,accent);
  if(!settings.smartContrast)return {played,controls};
  const background=light?'#E8EDEF':'#202831';
  if(settings.playedColorSource==='cover')for(const key of ['primary','secondary'])played[key]=readableCoverColor(played[key],[background]);
  if(settings.controlsIconColorSource==='cover'){
    const base=blendColor(controls.background,background,controls.opacity),backgrounds=[base];
    if(settings.buttonHoverBackground)backgrounds.push(settings.buttonHoverColor);
    if(settings.buttonPressedBackground)backgrounds.push(settings.buttonPressedColor);
    controls.foreground=readableCoverColor(controls.foreground,backgrounds);
  }
  return {played,controls};
}
function formatPlaybackTime(milliseconds) {
  const value=Number(milliseconds),total=Math.floor(Math.max(0,Number.isFinite(value)?value:0)/1000),seconds=String(total%60).padStart(2,'0');
  return total>=3600?`${Math.floor(total/3600)}:${String(Math.floor(total/60)%60).padStart(2,'0')}:${seconds}`:`${String(Math.floor(total/60)).padStart(2,'0')}:${seconds}`;
}
function makeFrame(snapshot, settings, now = Date.now(), coverAccent='') {
  const p = snapshot.playback ?? {};
  const lyric = snapshot.lyric ?? {};
  const lines = lyric.lines ?? [];
  // 宿主时钟在恢复播放时重设采样时间，旧 updatedAt 可能仍停在暂停前。
  const clock = Number.isFinite(p.clock?.positionMs) && (!p.clock.trackId || String(p.clock.trackId) === String(p.trackId)) ? p.clock : undefined;
  const rate = Number(clock?.playbackRate ?? p.playbackRate ?? 1);
  const playbackRate = Number.isFinite(rate) && rate > 0 ? rate : 1;
  const advancing = Boolean(p.isPlaying) && (clock?.isPlaying !== false) && (clock?.isAdvancing ?? p.isAdvancing ?? true);
  let ms = clock ? clock.positionMs : Number(p.currentTime || 0) * 1000;
  const sampledAt = Number(clock?.sampledAt ?? p.updatedAt ?? now);
  if (advancing) ms += Math.max(0, now - (Number.isFinite(sampledAt) && sampledAt > 0 ? sampledAt : now)) * playbackRate;
  const durationMs = Number(clock?.durationMs ?? Number(p.duration || 0) * 1000);
  ms = Math.max(0, durationMs > 0 ? Math.min(ms, durationMs) : ms);
  const coverTimeMs=ms;
  ms += Number(lyric.timeOffset || 0);
  let index = -1;
  // Now Playing 的歌词 trackId 实际对应 lyricHash，优先按宿主歌词标识匹配。
  const lyricIdentity = p.lyricHash || p.trackId;
  const sameTrack = !lyric.trackId || String(lyric.trackId) === String(lyricIdentity);
  if (sameTrack) {
    for (let i = 0; i < lines.length; i++) {
      if (Number.isFinite(lineStart(lines[i])) && lineStart(lines[i]) <= ms) index = i;
    }
    if (lines.length && !lines.some(l => Number.isFinite(lineStart(l)))) index = Number(lyric.currentIndex ?? -1);
  }
  const current = lines[index];
  const text = current?.text?.trim() || String(p.title || 'EchoMusic');
  let secondary = '';
  if (settings.layout === 'double') {
    secondary = settings.secondary === 'translation' ? String(current?.translated || current?.romanized || '') : String(lines[index + 1]?.text || '');
    if (!current || !sameTrack) secondary = String(p.artist || '');
  }
  // 下一句起点作为本句结束；末句优先用逐字结束时间，再用歌曲时长。
  const startMs = current ? lineStart(current) : 0;
  const nextLine = lines.slice(index + 1).find(line => lineStart(line) > startMs);
  const lastCharacter = current?.characters?.at(-1);
  const characterEnd = Number(lastCharacter?.endTime);
  const endMs = nextLine ? lineStart(nextLine) : (Number.isFinite(characterEnd) && characterEnd > startMs ? characterEnd : Number(p.duration || 0) * 1000 + Number(lyric.timeOffset || 0));
  const canScroll = Boolean(current && sameTrack && Number.isFinite(startMs) && endMs > startMs);
  return {
    playedPalette:playedPalette(settings,coverAccent),
    controlsPalette:controlsPalette(settings,coverAccent),
    noticePalette:noticePalette(settings,coverAccent),
    settings, text, secondary, playing: Boolean(p.isPlaying), advancing:Boolean(advancing), hasTrack: Boolean(p.trackId || p.title),
    trackId:String(p.trackId || ''), title:String(p.title || ''), artist:String(p.artist || ''), isFavorite:Boolean(p.isFavorite), coverUrl:String(p.coverUrl || ''), coverTimeMs,
    durationMs:Number.isFinite(durationMs) && durationMs>0?durationMs:0,
    lyricKey: `${p.trackId || ''}:${index}`, timelineMs: ms,
    lineStartMs: canScroll ? startMs : 0, lineEndMs: canScroll ? endMs : 0,
    playbackRate,
    fontFamily:nativeFontFamily(settings.fontFamily === 'follow' ? snapshot.appearance?.fontFamily : settings.fontFamily),
    characters: canScroll ? (current.characters || []) : [],
    secondaryCharacters: canScroll && settings.secondary === 'translation' ? (current.translated ? current.translatedCharacters || [] : current.romanizedCharacters || []) : []
  };
}

// 设置界面由 SettingsPanel.html 预编译，勿手工编辑。
function createPanelRender(Vue) {
const _Vue = Vue
const { createVNode: _createVNode, createElementVNode: _createElementVNode, createCommentVNode: _createCommentVNode, createTextVNode: _createTextVNode } = _Vue

const _hoisted_1 = { class: "tl-settings" }
const _hoisted_2 = { class: "tl-header" }
const _hoisted_3 = { class: "tl-enable-control" }
const _hoisted_4 = { class: "tl-preview-card" }
const _hoisted_5 = { class: "tl-caption" }
const _hoisted_6 = ["onPointerenter"]
const _hoisted_7 = ["src", "onLoad", "onError"]
const _hoisted_8 = {
  key: 1,
  viewBox: "0 0 24 24",
  "aria-hidden": "true"
}
const _hoisted_9 = ["src"]
const _hoisted_10 = { class: "tl-preview-content" }
const _hoisted_11 = {
  key: 0,
  class: "tl-preview-songinfo"
}
const _hoisted_12 = { class: "tl-preview-lyrics" }
const _hoisted_13 = {
  type: "button",
  class: "tl-preview-button",
  "aria-label": "预览播放状态"
}
const _hoisted_14 = { viewBox: "0 0 24 24" }
const _hoisted_15 = {
  key: 0,
  d: "M6 4h4v16H6zM14 4h4v16h-4z"
}
const _hoisted_16 = {
  key: 1,
  d: "M7 3v18l15-9z"
}
const _hoisted_17 = {
  type: "button",
  class: "tl-preview-button",
  "aria-label": "预览收藏状态"
}
const _hoisted_18 = {
  key: 2,
  class: "tl-preview-progress-time"
}
const _hoisted_19 = {
  key: 3,
  class: "tl-preview-timeline"
}
const _hoisted_20 = ["onUpdate:modelValue"]
const _hoisted_21 = {
  key: 0,
  class: "tl-notice-preview-row"
}
const _hoisted_22 = { class: "tl-notice-preview-caption" }
const _hoisted_23 = { class: "tl-notice-preview-scene" }
const _hoisted_24 = ["onPointerenter"]
const _hoisted_25 = {
  key: 1,
  class: "tl-preview-scrub"
}
const _hoisted_26 = {
  key: 0,
  class: "tl-options"
}
const _hoisted_27 = { class: "tl-section" }
const _hoisted_28 = { class: "tl-grid" }
const _hoisted_29 = { class: "tl-field tl-span" }
const _hoisted_30 = { class: "tl-field" }
const _hoisted_31 = { class: "tl-field" }
const _hoisted_32 = { class: "tl-field" }
const _hoisted_33 = { class: "tl-field" }
const _hoisted_34 = { class: "tl-section" }
const _hoisted_35 = { class: "tl-grid" }
const _hoisted_36 = { class: "tl-field" }
const _hoisted_37 = { class: "tl-field" }
const _hoisted_38 = {
  key: 0,
  class: "tl-field tl-span"
}
const _hoisted_39 = { class: "tl-field tl-span" }
const _hoisted_40 = { class: "tl-toggle tl-mask-toggle" }
const _hoisted_41 = {
  key: 0,
  class: "tl-field tl-palette-field"
}
const _hoisted_42 = {
  key: 0,
  class: "tl-note tl-auto-color"
}
const _hoisted_43 = { class: "tl-type-card" }
const _hoisted_44 = { class: "tl-card-label" }
const _hoisted_45 = {
  key: 0,
  class: "tl-color-row"
}
const _hoisted_46 = { class: "tl-color-control" }
const _hoisted_47 = ["value", "onInput"]
const _hoisted_48 = {
  key: 1,
  class: "tl-color-row"
}
const _hoisted_49 = { class: "tl-color-control" }
const _hoisted_50 = ["value", "onInput"]
const _hoisted_51 = {
  key: 0,
  class: "tl-type-card"
}
const _hoisted_52 = { class: "tl-card-label" }
const _hoisted_53 = {
  key: 0,
  class: "tl-color-row"
}
const _hoisted_54 = { class: "tl-color-control" }
const _hoisted_55 = ["value", "onInput"]
const _hoisted_56 = {
  key: 1,
  class: "tl-color-row"
}
const _hoisted_57 = { class: "tl-color-control" }
const _hoisted_58 = ["value", "onInput"]
const _hoisted_59 = {
  key: 1,
  class: "tl-toggle"
}
const _hoisted_60 = { class: "tl-toggle" }
const _hoisted_61 = {
  key: 2,
  class: "tl-dependent tl-field"
}
const _hoisted_62 = { class: "tl-section" }
const _hoisted_63 = { class: "tl-toggle" }
const _hoisted_64 = {
  key: 0,
  class: "tl-dependent"
}
const _hoisted_65 = { class: "tl-field" }
const _hoisted_66 = {
  key: 0,
  class: "tl-toggle tl-inner-toggle"
}
const _hoisted_67 = { class: "tl-section" }
const _hoisted_68 = { class: "tl-toggle" }
const _hoisted_69 = { class: "tl-toggle" }
const _hoisted_70 = {
  key: 0,
  class: "tl-dependent tl-field"
}
const _hoisted_71 = {
  key: 1,
  class: "tl-dependent tl-controls-settings"
}
const _hoisted_72 = { class: "tl-field" }
const _hoisted_73 = {
  key: 0,
  class: "tl-note tl-auto-color"
}
const _hoisted_74 = {
  key: 0,
  class: "tl-color-row"
}
const _hoisted_75 = { class: "tl-color-control" }
const _hoisted_76 = ["value", "onInput"]
const _hoisted_77 = { class: "tl-field" }
const _hoisted_78 = {
  key: 0,
  class: "tl-note tl-auto-color"
}
const _hoisted_79 = {
  key: 1,
  class: "tl-color-row"
}
const _hoisted_80 = { class: "tl-color-control" }
const _hoisted_81 = ["value", "onInput"]
const _hoisted_82 = { class: "tl-field" }
const _hoisted_83 = { class: "tl-feedback-settings" }
const _hoisted_84 = { class: "tl-toggle tl-inner-toggle" }
const _hoisted_85 = {
  key: 0,
  class: "tl-color-row"
}
const _hoisted_86 = { class: "tl-color-control" }
const _hoisted_87 = ["value", "onInput"]
const _hoisted_88 = { class: "tl-toggle tl-inner-toggle" }
const _hoisted_89 = {
  key: 0,
  class: "tl-color-row"
}
const _hoisted_90 = { class: "tl-color-control" }
const _hoisted_91 = ["value", "onInput"]
const _hoisted_92 = { class: "tl-section" }
const _hoisted_93 = { class: "tl-toggle" }
const _hoisted_94 = {
  key: 0,
  class: "tl-dependent tl-notice-settings"
}
const _hoisted_95 = { class: "tl-toggle tl-inner-toggle" }
const _hoisted_96 = { class: "tl-grid" }
const _hoisted_97 = { class: "tl-field" }
const _hoisted_98 = { class: "tl-field" }
const _hoisted_99 = { class: "tl-toggle tl-inner-toggle" }
const _hoisted_100 = { class: "tl-grid" }
const _hoisted_101 = { class: "tl-field tl-span" }
const _hoisted_102 = {
  key: 0,
  class: "tl-color-row tl-span"
}
const _hoisted_103 = { class: "tl-color-control" }
const _hoisted_104 = ["value", "onInput"]
const _hoisted_105 = {
  key: 1,
  class: "tl-note tl-auto-color tl-span"
}
const _hoisted_106 = {
  key: 2,
  class: "tl-note tl-span"
}
const _hoisted_107 = { class: "tl-field tl-span" }
const _hoisted_108 = { class: "tl-note" }
const _hoisted_109 = { class: "tl-field" }
const _hoisted_110 = { class: "tl-field" }
const _hoisted_111 = { class: "tl-field tl-span" }
const _hoisted_112 = { class: "tl-toggle tl-inner-toggle" }
const _hoisted_113 = { class: "tl-grid" }
const _hoisted_114 = { class: "tl-field tl-span" }
const _hoisted_115 = { class: "tl-field" }
const _hoisted_116 = {
  key: 0,
  class: "tl-field"
}
const _hoisted_117 = { class: "tl-field tl-span" }
const _hoisted_118 = {
  key: 1,
  class: "tl-color-row"
}
const _hoisted_119 = { class: "tl-color-control" }
const _hoisted_120 = ["value", "onInput"]
const _hoisted_121 = {
  key: 2,
  class: "tl-color-row"
}
const _hoisted_122 = { class: "tl-color-control" }
const _hoisted_123 = ["value", "onInput"]
const _hoisted_124 = { class: "tl-toggle tl-inner-toggle" }
const _hoisted_125 = { class: "tl-section" }
const _hoisted_126 = { class: "tl-toggle" }
const _hoisted_127 = { class: "tl-toggle" }
const _hoisted_128 = { class: "tl-toggle" }
const _hoisted_129 = {
  key: 1,
  class: "tl-empty-settings"
}
const _hoisted_130 = { class: "tl-footer" }
const _hoisted_131 = { class: "tl-actions" }
const _hoisted_132 = {
  role: "status",
  class: "tl-status"
}

return function render(_ctx, _cache) {

    const { createElementVNode: _createElementVNode, toDisplayString: _toDisplayString, resolveComponent: _resolveComponent, createVNode: _createVNode, normalizeClass: _normalizeClass, openBlock: _openBlock, createElementBlock: _createElementBlock, createCommentVNode: _createCommentVNode, normalizeStyle: _normalizeStyle, vModelText: _vModelText, withDirectives: _withDirectives, createTextVNode: _createTextVNode, withCtx: _withCtx, createBlock: _createBlock } = _Vue

    const _component_Switch = _resolveComponent("Switch")
    const _component_Slider = _resolveComponent("Slider")
    const _component_Select = _resolveComponent("Select")
    const _component_InputNumber = _resolveComponent("InputNumber")
    const _component_Button = _resolveComponent("Button")

    return (_openBlock(), _createElementBlock("section", _hoisted_1, [
      _createElementVNode("header", _hoisted_2, [_cache[0] || (_cache[0] = _createElementVNode("div", null, [_createElementVNode("span", { class: "tl-eyebrow" }, "TASKBAR LYRICS"), _createElementVNode("h2", null, "微光 · 任务栏歌词"), _createElementVNode("p", { class: "tl-header-note" }, "让歌词留在任务栏，让操作更顺手。")], -1)), _createElementVNode("div", _hoisted_3, [_createElementVNode("span", null, _toDisplayString(_ctx.draft.enabled ? '已开启' : '已关闭'), 1), _createVNode(_component_Switch, {
        modelValue: _ctx.draft.enabled,
        "onUpdate:modelValue": $event => ((_ctx.draft.enabled) = $event)
      }, null, 8, ["modelValue", "onUpdate:modelValue"])])]),
      _createElementVNode("section", _hoisted_4, [
        _createElementVNode("div", _hoisted_5, [_cache[1] || (_cache[1] = _createElementVNode("span", null, "实时预览", -1)), _createElementVNode("span", null, _toDisplayString(_ctx.draft.layout === 'double' ? '双行' : '单行') + " · " + _toDisplayString(_ctx.draft.progressMask ? '进度遮罩' : '纯色文字'), 1)]),
        _createElementVNode("div", {
          class: _normalizeClass(["tl-preview", { 'tl-preview-light': _ctx.previewLight, 'tl-preview-hover': _ctx.draft.hoverControls, 'tl-controls-left': _ctx.draft.controlsAlignment === 'left', 'tl-preview-progress-enabled':_ctx.draft.hoverProgress, 'tl-preview-animated':_ctx.draft.trackTransitions }]),
          style: _normalizeStyle({ fontFamily:_ctx.previewFont, opacity:_ctx.draft.opacity / 100, '--tl-transition-duration':_ctx.draft.transitionDuration + 'ms' })
        }, [_createElementVNode("div", {
          class: "tl-preview-body",
          style: _normalizeStyle({ transform:'translateY(' + _ctx.draft.verticalOffset + 'px)' })
        }, [(_ctx.draft.showCover)
          ? (_openBlock(), _createElementBlock("div", {
              key: 0,
              class: _normalizeClass(["tl-preview-cover", { 'tl-cover-circle': _ctx.draft.coverShape === 'circle', 'tl-cover-spin': _ctx.draft.coverShape === 'circle' && _ctx.draft.rotateCover }]),
              onPointerenter: _ctx.onPreviewCoverHover,
              style: _normalizeStyle({ animationPlayState:_ctx.previewPlaying ? 'running':'paused' })
            }, [(_ctx.previewCover && _ctx.coverFailed !== _ctx.previewCover)
              ? (_openBlock(), _createElementBlock("img", {
                  key: _ctx.previewCover,
                  src: _ctx.previewCover,
                  class: _normalizeClass({ 'tl-cover-loaded':_ctx.previewCoverLoaded === _ctx.previewCover }),
                  alt: "",
                  onLoad: _ctx.onPreviewCoverLoad,
                  onError: _ctx.onPreviewCoverError
                }, null, 42, _hoisted_7))
              : (_openBlock(), _createElementBlock("svg", _hoisted_8, [...(_cache[2] || (_cache[2] = [_createElementVNode("path", { d: "M9 5v12a3 3 0 1 1-2-2.83V7l12-3v10a3 3 0 1 1-2-2.83V2z" }, null, -1)]))])), (_ctx.previewCoverPrevious)
              ? (_openBlock(), _createElementBlock("img", {
                  key: 2,
                  class: _normalizeClass(["tl-cover-previous", { 'tl-cover-fading':_ctx.previewCoverLoaded === _ctx.previewCover }]),
                  src: _ctx.previewCoverPrevious,
                  alt: ""
                }, null, 10, _hoisted_9))
              : _createCommentVNode("", true)], 46, _hoisted_6))
          : _createCommentVNode("", true), _createElementVNode("div", _hoisted_10, [
          (_ctx.draft.showCover)
            ? (_openBlock(), _createElementBlock("div", _hoisted_11, [_createElementVNode("div", {
                class: "tl-preview-info-line",
                style: _normalizeStyle({ color:_ctx.previewColors[0] })
              }, [_createElementVNode("span", null, _toDisplayString(_ctx.previewTitle), 1)], 4), _createElementVNode("div", {
                class: "tl-preview-info-line tl-preview-info-artist",
                style: _normalizeStyle({ color:_ctx.previewColors[1] })
              }, [_createElementVNode("span", null, _toDisplayString(_ctx.previewArtist), 1)], 4)]))
            : _createCommentVNode("", true),
          _createElementVNode("div", _hoisted_12, [_createElementVNode("div", {
            class: "tl-preview-line",
            style: _normalizeStyle({ fontSize:_ctx.draft.primaryFontSize + 'px' })
          }, [_createElementVNode("span", {
            class: _normalizeClass(["tl-preview-glyph", { 'tl-preview-progress':_ctx.draft.progressMask }]),
            style: _normalizeStyle({ '--tl-lyric-waiting':_ctx.previewColors[0], '--tl-lyric-played':_ctx.previewPlayed.primary, '--tl-lyric-position':_ctx.previewProgress + '%' })
          }, "让每一句，留在此刻", 6)], 4), (_ctx.draft.layout === 'double')
            ? (_openBlock(), _createElementBlock("div", {
                key: 0,
                class: "tl-preview-line tl-preview-secondary",
                style: _normalizeStyle({ fontSize:_ctx.draft.secondaryFontSize + 'px' })
              }, [_createElementVNode("span", {
                class: _normalizeClass(["tl-preview-glyph", { 'tl-preview-progress':_ctx.draft.progressMask && _ctx.draft.secondary === 'translation' }]),
                style: _normalizeStyle({ '--tl-lyric-waiting':_ctx.previewColors[1], '--tl-lyric-played':_ctx.previewPlayed.secondary, '--tl-lyric-position':_ctx.previewProgress + '%' })
              }, _toDisplayString(_ctx.draft.secondary === 'next' ? '下一段旋律，正在靠近' : 'Keep this moment in a melody'), 7)], 4))
            : _createCommentVNode("", true)]),
          (_ctx.draft.hoverControls)
            ? (_openBlock(), _createElementBlock("div", {
                key: 1,
                class: "tl-preview-controls",
                style: _normalizeStyle({ color:_ctx.previewControls.foreground, backgroundColor:_ctx.previewControls.backgroundCss, '--tl-button-hover':_ctx.draft.buttonHoverBackground ? _ctx.draft.buttonHoverColor : 'transparent', '--tl-button-pressed':_ctx.draft.buttonPressedBackground ? _ctx.draft.buttonPressedColor : 'transparent' })
              }, [
                _cache[4] || (_cache[4] = _createElementVNode("button", {
                  type: "button",
                  class: "tl-preview-button",
                  "aria-label": "预览上一首"
                }, [_createElementVNode("svg", { viewBox: "0 0 24 24" }, [_createElementVNode("path", { d: "M5 5h3v14H5zM20 5v14L9 12z" })])], -1)),
                _createElementVNode("button", _hoisted_13, [(_openBlock(), _createElementBlock("svg", _hoisted_14, [(_ctx.previewPlaying)
                  ? (_openBlock(), _createElementBlock("path", _hoisted_15))
                  : (_openBlock(), _createElementBlock("path", _hoisted_16))]))]),
                _cache[5] || (_cache[5] = _createElementVNode("button", {
                  type: "button",
                  class: "tl-preview-button",
                  "aria-label": "预览下一首"
                }, [_createElementVNode("svg", { viewBox: "0 0 24 24" }, [_createElementVNode("path", { d: "M16 5h3v14h-3zM4 5v14l11-7z" })])], -1)),
                _createElementVNode("button", _hoisted_17, [(_openBlock(), _createElementBlock("svg", {
                  viewBox: "0 0 24 24",
                  class: _normalizeClass(["tl-preview-favorite", { 'tl-favorited': _ctx.previewFavorite }])
                }, [...(_cache[3] || (_cache[3] = [_createElementVNode("path", { d: "M12 21C7 17 2 13 2 8a5 5 0 0 1 10-1A5 5 0 0 1 22 8c0 5-5 9-10 13Z" }, null, -1)]))], 2))])
              ], 4))
            : _createCommentVNode("", true),
          (_ctx.draft.hoverProgress)
            ? (_openBlock(), _createElementBlock("div", _hoisted_18, _toDisplayString(_ctx.previewElapsed) + " / " + _toDisplayString(_ctx.previewDuration), 1))
            : _createCommentVNode("", true),
          (_ctx.draft.hoverProgress)
            ? (_openBlock(), _createElementBlock("div", _hoisted_19, [_withDirectives(_createElementVNode("input", {
                type: "range",
                min: "0",
                max: "100",
                step: "0.1",
                "onUpdate:modelValue": $event => ((_ctx.previewProgress) = $event),
                "aria-label": "预览歌曲进度",
                style: _normalizeStyle({ '--tl-progress-color':_ctx.previewPlayed.primary, '--tl-progress-position':_ctx.previewProgress + '%' })
              }, null, 12, _hoisted_20), [[_vModelText, _ctx.previewProgress]])]))
            : _createCommentVNode("", true)
        ])], 4)], 6),
        (_ctx.draft.enabled && _ctx.draft.noticeEnabled)
          ? (_openBlock(), _createElementBlock("div", _hoisted_21, [_createElementVNode("span", _hoisted_22, "切歌信息浮层 · " + _toDisplayString(_ctx.draft.noticeDuration) + " 秒", 1), _createElementVNode("div", _hoisted_23, [_createElementVNode("div", {
              class: _normalizeClass(["tl-notice-preview", { 'tl-notice-glass':_ctx.draft.noticeBackgroundStyle === 'glass', 'tl-notice-scroll':_ctx.draft.noticeScrollText, 'tl-notice-center':_ctx.draft.noticeTextAlignment === 'center' }]),
              onPointerenter: _ctx.onPreviewNoticeHover,
              style: _normalizeStyle({ width:_ctx.draft.noticeWidth + 'px', borderRadius:_ctx.draft.noticeRadius + 'px', backgroundColor:_ctx.previewNotice.backgroundCss, fontFamily:_ctx.previewFont })
            }, [_createElementVNode("div", {
              class: "tl-notice-line",
              style: _normalizeStyle({ color:_ctx.previewNotice.title, fontSize:_ctx.draft.noticeTitleFontSize + 'px' })
            }, [_createElementVNode("span", null, _toDisplayString(_ctx.previewTitle), 1)], 4), (_ctx.draft.noticeShowArtist)
              ? (_openBlock(), _createElementBlock("div", {
                  key: 0,
                  class: "tl-notice-line tl-notice-artist",
                  style: _normalizeStyle({ color:_ctx.previewNotice.artist, fontSize:_ctx.draft.noticeArtistFontSize + 'px' })
                }, [_createElementVNode("span", null, _toDisplayString(_ctx.previewArtist), 1)], 4))
              : _createCommentVNode("", true)], 46, _hoisted_24)])]))
          : _createCommentVNode("", true),
        (_ctx.draft.enabled && _ctx.draft.progressMask)
          ? (_openBlock(), _createElementBlock("div", _hoisted_25, [_cache[6] || (_cache[6] = _createElementVNode("span", null, "预览进度", -1)), _createVNode(_component_Slider, {
              modelValue: _ctx.previewProgress,
              "onUpdate:modelValue": $event => ((_ctx.previewProgress) = $event),
              min: 0,
              max: 100,
              step: 1
            }, null, 8, ["modelValue", "onUpdate:modelValue", "min", "max", "step"]), _createElementVNode("span", null, _toDisplayString(_ctx.previewProgress) + "%", 1)]))
          : _createCommentVNode("", true)
      ]),
      (_ctx.draft.enabled)
        ? (_openBlock(), _createElementBlock("div", _hoisted_26, [
            _createElementVNode("section", _hoisted_27, [_cache[15] || (_cache[15] = _createElementVNode("div", { class: "tl-section-heading" }, [_createElementVNode("h3", null, "显示与位置"), _createElementVNode("p", null, "选择屏幕，调整歌词占用的空间。")], -1)), _createElementVNode("div", _hoisted_28, [
              _createElementVNode("div", _hoisted_29, [_cache[7] || (_cache[7] = _createElementVNode("label", null, "显示屏幕", -1)), _createVNode(_component_Select, {
                modelValue: _ctx.draft.display,
                "onUpdate:modelValue": $event => ((_ctx.draft.display) = $event),
                options: _ctx.displayOptions
              }, null, 8, ["modelValue", "onUpdate:modelValue", "options"]), _cache[8] || (_cache[8] = _createElementVNode("p", { class: "tl-note" }, "指定屏幕断开时，自动回到主屏幕。", -1))]),
              _createElementVNode("div", _hoisted_30, [_createElementVNode("label", null, [_cache[9] || (_cache[9] = _createTextVNode("显示宽度 ", -1)), _createElementVNode("span", null, _toDisplayString(_ctx.draft.width) + " px", 1)]), _createVNode(_component_Slider, {
                modelValue: _ctx.draft.width,
                "onUpdate:modelValue": $event => ((_ctx.draft.width) = $event),
                min: 120,
                max: 900,
                step: 10
              }, null, 8, ["modelValue", "onUpdate:modelValue", "min", "max", "step"])]),
              _createElementVNode("div", _hoisted_31, [_createElementVNode("label", null, [_cache[10] || (_cache[10] = _createTextVNode("透明度 ", -1)), _createElementVNode("span", null, _toDisplayString(_ctx.transparency) + "%", 1)]), _createVNode(_component_Slider, {
                modelValue: _ctx.transparency,
                "onUpdate:modelValue": $event => ((_ctx.transparency) = $event),
                min: 0,
                max: 100,
                step: 1
              }, null, 8, ["modelValue", "onUpdate:modelValue", "min", "max", "step"]), _cache[11] || (_cache[11] = _createElementVNode("p", { class: "tl-note" }, "0% 不透明，100% 完全透明。影响歌词、封面、按钮和进度条。", -1))]),
              _createElementVNode("div", _hoisted_32, [_cache[12] || (_cache[12] = _createElementVNode("label", null, "距任务栏左侧", -1)), _createVNode(_component_InputNumber, {
                modelValue: _ctx.draft.offset,
                "onUpdate:modelValue": $event => ((_ctx.draft.offset) = $event),
                min: 0,
                max: 2000,
                step: 1
              }, null, 8, ["modelValue", "onUpdate:modelValue", "min", "max", "step"])]),
              _createElementVNode("div", _hoisted_33, [_cache[13] || (_cache[13] = _createElementVNode("label", null, [_createTextVNode("垂直偏移 "), _createElementVNode("span", null, "px")], -1)), _createVNode(_component_InputNumber, {
                modelValue: _ctx.draft.verticalOffset,
                "onUpdate:modelValue": $event => ((_ctx.draft.verticalOffset) = $event),
                min: -20,
                max: 20,
                step: 1
              }, null, 8, ["modelValue", "onUpdate:modelValue", "min", "max", "step"]), _cache[14] || (_cache[14] = _createElementVNode("p", { class: "tl-note" }, "负数向上，正数向下，0 为居中。封面、歌词和按钮一起移动；偏移过大时边缘可能被裁切。", -1))])
            ])]),
            _createElementVNode("section", _hoisted_34, [
              _cache[34] || (_cache[34] = _createElementVNode("div", { class: "tl-section-heading" }, [_createElementVNode("h3", null, "歌词外观"), _createElementVNode("p", null, "字体、行数与颜色，在上方实时预览。")], -1)),
              _createElementVNode("div", _hoisted_35, [
                _createElementVNode("div", _hoisted_36, [_cache[16] || (_cache[16] = _createElementVNode("label", null, "歌词行数", -1)), _createVNode(_component_Select, {
                  modelValue: _ctx.draft.layout,
                  "onUpdate:modelValue": $event => ((_ctx.draft.layout) = $event),
                  options: _ctx.layoutOptions
                }, null, 8, ["modelValue", "onUpdate:modelValue", "options"])]),
                _createElementVNode("div", _hoisted_37, [_cache[17] || (_cache[17] = _createElementVNode("label", null, "歌词字体", -1)), _createVNode(_component_Select, {
                  modelValue: _ctx.draft.fontFamily,
                  "onUpdate:modelValue": $event => ((_ctx.draft.fontFamily) = $event),
                  options: _ctx.fontOptions,
                  filterable: ""
                }, null, 8, ["modelValue", "onUpdate:modelValue", "options"])]),
                (_ctx.draft.layout === 'double')
                  ? (_openBlock(), _createElementBlock("div", _hoisted_38, [_cache[18] || (_cache[18] = _createElementVNode("label", null, "第二行内容", -1)), _createVNode(_component_Select, {
                      modelValue: _ctx.draft.secondary,
                      "onUpdate:modelValue": $event => ((_ctx.draft.secondary) = $event),
                      options: _ctx.secondaryOptions
                    }, null, 8, ["modelValue", "onUpdate:modelValue", "options"])]))
                  : _createCommentVNode("", true),
                _createElementVNode("div", _hoisted_39, [_cache[19] || (_cache[19] = _createElementVNode("label", null, "待歌词颜色", -1)), _createVNode(_component_Select, {
                  modelValue: _ctx.draft.theme,
                  "onUpdate:modelValue": $event => ((_ctx.draft.theme) = $event),
                  options: _ctx.themeOptions
                }, null, 8, ["modelValue", "onUpdate:modelValue", "options"])])
              ]),
              _createElementVNode("div", _hoisted_40, [_cache[20] || (_cache[20] = _createElementVNode("div", null, [_createElementVNode("span", null, "歌词进度遮罩"), _createElementVNode("p", null, "同一层文字随进度变色，支持逐字进度。")], -1)), _createVNode(_component_Switch, {
                modelValue: _ctx.draft.progressMask,
                "onUpdate:modelValue": $event => ((_ctx.draft.progressMask) = $event)
              }, null, 8, ["modelValue", "onUpdate:modelValue"])]),
              (_ctx.draft.progressMask)
                ? (_openBlock(), _createElementBlock("div", _hoisted_41, [_cache[22] || (_cache[22] = _createElementVNode("label", null, "歌词进度遮罩颜色", -1)), _createVNode(_component_Select, {
                    modelValue: _ctx.draft.playedColorSource,
                    "onUpdate:modelValue": $event => ((_ctx.draft.playedColorSource) = $event),
                    options: _ctx.playedColorOptions
                  }, null, 8, ["modelValue", "onUpdate:modelValue", "options"]), (_ctx.draft.playedColorSource === 'cover')
                    ? (_openBlock(), _createElementBlock("p", _hoisted_42, [_createElementVNode("span", { style: _normalizeStyle({ background:_ctx.previewPlayed.primary }) }, null, 4), _cache[21] || (_cache[21] = _createTextVNode("优先使用当前封面的鲜艳颜色作为歌词遮罩颜色，切歌取色期间保留上一颜色，成功后更新；首次取色前使用手动颜色。", -1))]))
                    : _createCommentVNode("", true)]))
                : _createCommentVNode("", true),
              _createElementVNode("div", { class: _normalizeClass(["tl-type-grid", { 'tl-one-line': _ctx.draft.layout === 'single' }]) }, [_createElementVNode("div", _hoisted_43, [
                _createElementVNode("div", _hoisted_44, [_cache[23] || (_cache[23] = _createElementVNode("span", { class: "tl-dot" }, "1", -1)), _cache[24] || (_cache[24] = _createElementVNode("h4", null, "第一行", -1)), _createElementVNode("span", null, _toDisplayString(_ctx.draft.primaryFontSize) + " px", 1)]),
                _createVNode(_component_Slider, {
                  modelValue: _ctx.draft.primaryFontSize,
                  "onUpdate:modelValue": $event => ((_ctx.draft.primaryFontSize) = $event),
                  min: 10,
                  max: 36,
                  step: 1
                }, null, 8, ["modelValue", "onUpdate:modelValue", "min", "max", "step"]),
                (_ctx.draft.theme === 'custom')
                  ? (_openBlock(), _createElementBlock("div", _hoisted_45, [_cache[25] || (_cache[25] = _createElementVNode("label", null, "文字颜色", -1)), _createElementVNode("label", _hoisted_46, [_createElementVNode("input", {
                      type: "color",
                      "aria-label": "第一行文字颜色",
                      value: _ctx.draft.primaryColor,
                      onInput: $event => (_ctx.setColor('primaryColor', $event))
                    }, null, 40, _hoisted_47), _createElementVNode("span", null, _toDisplayString(_ctx.draft.primaryColor), 1)])]))
                  : _createCommentVNode("", true),
                (_ctx.draft.progressMask && _ctx.draft.playedColorSource === 'custom')
                  ? (_openBlock(), _createElementBlock("div", _hoisted_48, [_cache[26] || (_cache[26] = _createElementVNode("label", null, "已播放颜色", -1)), _createElementVNode("label", _hoisted_49, [_createElementVNode("input", {
                      type: "color",
                      "aria-label": "第一行已播放颜色",
                      value: _ctx.draft.primaryPlayedColor,
                      onInput: $event => (_ctx.setColor('primaryPlayedColor', $event))
                    }, null, 40, _hoisted_50), _createElementVNode("span", null, _toDisplayString(_ctx.draft.primaryPlayedColor), 1)])]))
                  : _createCommentVNode("", true)
              ]), (_ctx.draft.layout === 'double')
                ? (_openBlock(), _createElementBlock("div", _hoisted_51, [
                    _createElementVNode("div", _hoisted_52, [_cache[27] || (_cache[27] = _createElementVNode("span", { class: "tl-dot" }, "2", -1)), _cache[28] || (_cache[28] = _createElementVNode("h4", null, "第二行", -1)), _createElementVNode("span", null, _toDisplayString(_ctx.draft.secondaryFontSize) + " px", 1)]),
                    _createVNode(_component_Slider, {
                      modelValue: _ctx.draft.secondaryFontSize,
                      "onUpdate:modelValue": $event => ((_ctx.draft.secondaryFontSize) = $event),
                      min: 8,
                      max: 36,
                      step: 1
                    }, null, 8, ["modelValue", "onUpdate:modelValue", "min", "max", "step"]),
                    (_ctx.draft.theme === 'custom')
                      ? (_openBlock(), _createElementBlock("div", _hoisted_53, [_cache[29] || (_cache[29] = _createElementVNode("label", null, "文字颜色", -1)), _createElementVNode("label", _hoisted_54, [_createElementVNode("input", {
                          type: "color",
                          "aria-label": "第二行文字颜色",
                          value: _ctx.draft.secondaryColor,
                          onInput: $event => (_ctx.setColor('secondaryColor', $event))
                        }, null, 40, _hoisted_55), _createElementVNode("span", null, _toDisplayString(_ctx.draft.secondaryColor), 1)])]))
                      : _createCommentVNode("", true),
                    (_ctx.draft.progressMask && _ctx.draft.secondary === 'translation' && _ctx.draft.playedColorSource === 'custom')
                      ? (_openBlock(), _createElementBlock("div", _hoisted_56, [_cache[30] || (_cache[30] = _createElementVNode("label", null, "已播放颜色", -1)), _createElementVNode("label", _hoisted_57, [_createElementVNode("input", {
                          type: "color",
                          "aria-label": "第二行已播放颜色",
                          value: _ctx.draft.secondaryPlayedColor,
                          onInput: $event => (_ctx.setColor('secondaryPlayedColor', $event))
                        }, null, 40, _hoisted_58), _createElementVNode("span", null, _toDisplayString(_ctx.draft.secondaryPlayedColor), 1)])]))
                      : _createCommentVNode("", true)
                  ]))
                : _createCommentVNode("", true)], 2),
              _cache[35] || (_cache[35] = _createElementVNode("p", { class: "tl-note" }, "任务栏高度不足时，字号按比例适配。", -1)),
              ((_ctx.draft.playedColorSource === 'cover' && (_ctx.draft.progressMask || _ctx.draft.hoverProgress)) || (_ctx.draft.controlsIconColorSource === 'cover' && _ctx.draft.hoverControls))
                ? (_openBlock(), _createElementBlock("div", _hoisted_59, [_cache[31] || (_cache[31] = _createElementVNode("div", null, [_createElementVNode("span", null, "智能颜色对比度"), _createElementVNode("p", null, "根据任务栏明暗及按钮背景，改善封面自动配色的可读性；适用于歌词遮罩、进度条和按钮图标，自定义颜色保持不变。")], -1)), _createVNode(_component_Switch, {
                    modelValue: _ctx.draft.smartContrast,
                    "onUpdate:modelValue": $event => ((_ctx.draft.smartContrast) = $event)
                  }, null, 8, ["modelValue", "onUpdate:modelValue"])]))
                : _createCommentVNode("", true),
              _createElementVNode("div", _hoisted_60, [_cache[32] || (_cache[32] = _createElementVNode("div", null, [_createElementVNode("span", null, "切歌过渡动画"), _createElementVNode("p", null, "新封面加载成功后淡入，自动配色平滑过渡；歌词进度仍按实际播放时间推进。")], -1)), _createVNode(_component_Switch, {
                modelValue: _ctx.draft.trackTransitions,
                "onUpdate:modelValue": $event => ((_ctx.draft.trackTransitions) = $event)
              }, null, 8, ["modelValue", "onUpdate:modelValue"])]),
              (_ctx.draft.trackTransitions)
                ? (_openBlock(), _createElementBlock("div", _hoisted_61, [_createElementVNode("label", null, [_cache[33] || (_cache[33] = _createTextVNode("过渡时长 ", -1)), _createElementVNode("span", null, _toDisplayString(_ctx.draft.transitionDuration) + " ms", 1)]), _createVNode(_component_Slider, {
                    modelValue: _ctx.draft.transitionDuration,
                    "onUpdate:modelValue": $event => ((_ctx.draft.transitionDuration) = $event),
                    min: 100,
                    max: 1000,
                    step: 20
                  }, null, 8, ["modelValue", "onUpdate:modelValue", "min", "max", "step"])]))
                : _createCommentVNode("", true)
            ]),
            _createElementVNode("section", _hoisted_62, [_cache[39] || (_cache[39] = _createElementVNode("div", { class: "tl-section-heading" }, [_createElementVNode("h3", null, "歌曲封面"), _createElementVNode("p", null, "为任务栏加上一点专辑的色彩。")], -1)), _createElementVNode("div", _hoisted_63, [_cache[36] || (_cache[36] = _createElementVNode("div", null, [_createElementVNode("span", null, "显示歌曲封面"), _createElementVNode("p", null, "封面显示在歌词左侧，鼠标放在封面上显示歌名与演唱者，过长时自动滚动。")], -1)), _createVNode(_component_Switch, {
              modelValue: _ctx.draft.showCover,
              "onUpdate:modelValue": $event => ((_ctx.draft.showCover) = $event)
            }, null, 8, ["modelValue", "onUpdate:modelValue"])]), (_ctx.draft.showCover)
              ? (_openBlock(), _createElementBlock("div", _hoisted_64, [_createElementVNode("div", _hoisted_65, [_cache[37] || (_cache[37] = _createElementVNode("label", null, "封面形状", -1)), _createVNode(_component_Select, {
                  modelValue: _ctx.draft.coverShape,
                  "onUpdate:modelValue": $event => ((_ctx.draft.coverShape) = $event),
                  options: _ctx.coverShapeOptions
                }, null, 8, ["modelValue", "onUpdate:modelValue", "options"])]), (_ctx.draft.coverShape === 'circle')
                  ? (_openBlock(), _createElementBlock("div", _hoisted_66, [_cache[38] || (_cache[38] = _createElementVNode("div", null, [_createElementVNode("span", null, "圆形封面旋转"), _createElementVNode("p", null, "播放时旋转，暂停时停留。")], -1)), _createVNode(_component_Switch, {
                      modelValue: _ctx.draft.rotateCover,
                      "onUpdate:modelValue": $event => ((_ctx.draft.rotateCover) = $event)
                    }, null, 8, ["modelValue", "onUpdate:modelValue"])]))
                  : _createCommentVNode("", true)]))
              : _createCommentVNode("", true)]),
            _createElementVNode("section", _hoisted_67, [
              _cache[57] || (_cache[57] = _createElementVNode("div", { class: "tl-section-heading" }, [_createElementVNode("h3", null, "快捷操作"), _createElementVNode("p", null, "鼠标移入歌词，即可操作当前歌曲。")], -1)),
              _createElementVNode("div", _hoisted_68, [_cache[40] || (_cache[40] = _createElementVNode("div", null, [_createElementVNode("span", null, "鼠标悬浮显示快捷按钮"), _createElementVNode("p", null, "上一首、播放／暂停、下一首、收藏／取消收藏。")], -1)), _createVNode(_component_Switch, {
                modelValue: _ctx.draft.hoverControls,
                "onUpdate:modelValue": $event => ((_ctx.draft.hoverControls) = $event)
              }, null, 8, ["modelValue", "onUpdate:modelValue"])]),
              _createElementVNode("div", _hoisted_69, [_cache[41] || (_cache[41] = _createElementVNode("div", null, [_createElementVNode("span", null, "悬浮播放进度条"), _createElementVNode("p", null, "与按钮栏等宽；移入进度条时加粗，上方显示播放时间并暂时隐藏按钮，移开后恢复。点击或拖动后松手跳转，可独立于快捷按钮开启。")], -1)), _createVNode(_component_Switch, {
                modelValue: _ctx.draft.hoverProgress,
                "onUpdate:modelValue": $event => ((_ctx.draft.hoverProgress) = $event)
              }, null, 8, ["modelValue", "onUpdate:modelValue"])]),
              (_ctx.draft.hoverControls || _ctx.draft.hoverProgress)
                ? (_openBlock(), _createElementBlock("div", _hoisted_70, [_cache[42] || (_cache[42] = _createElementVNode("label", null, "快捷操作位置", -1)), _createVNode(_component_Select, {
                    modelValue: _ctx.draft.controlsAlignment,
                    "onUpdate:modelValue": $event => ((_ctx.draft.controlsAlignment) = $event),
                    options: _ctx.controlsAlignmentOptions
                  }, null, 8, ["modelValue", "onUpdate:modelValue", "options"]), _cache[43] || (_cache[43] = _createElementVNode("p", { class: "tl-note" }, "按钮与进度条一起对齐。居中位置随当前歌词长度调整，尺寸保持不变；短句会预留空间，避免遮住封面。", -1))]))
                : _createCommentVNode("", true),
              (_ctx.draft.hoverControls)
                ? (_openBlock(), _createElementBlock("div", _hoisted_71, [
                    _createElementVNode("div", _hoisted_72, [_cache[45] || (_cache[45] = _createElementVNode("label", null, "快捷按钮图标颜色", -1)), _createVNode(_component_Select, {
                      modelValue: _ctx.draft.controlsIconColorSource,
                      "onUpdate:modelValue": $event => ((_ctx.draft.controlsIconColorSource) = $event),
                      options: _ctx.controlsBackgroundOptions
                    }, null, 8, ["modelValue", "onUpdate:modelValue", "options"]), (_ctx.draft.controlsIconColorSource === 'cover')
                      ? (_openBlock(), _createElementBlock("p", _hoisted_73, [_createElementVNode("span", { style: _normalizeStyle({ background:_ctx.previewControls.foreground }) }, null, 4), _cache[44] || (_cache[44] = _createTextVNode("图标使用当前封面的鲜艳颜色，切歌自动更新；与按钮背景独立设置。", -1))]))
                      : _createCommentVNode("", true)]),
                    (_ctx.draft.controlsIconColorSource === 'custom')
                      ? (_openBlock(), _createElementBlock("div", _hoisted_74, [_cache[46] || (_cache[46] = _createElementVNode("label", null, "自定义图标颜色", -1)), _createElementVNode("label", _hoisted_75, [_createElementVNode("input", {
                          type: "color",
                          "aria-label": "快捷按钮自定义图标颜色",
                          value: _ctx.draft.controlsIconColor,
                          onInput: $event => (_ctx.setColor('controlsIconColor', $event))
                        }, null, 40, _hoisted_76), _createElementVNode("span", null, _toDisplayString(_ctx.draft.controlsIconColor), 1)])]))
                      : _createCommentVNode("", true),
                    _createElementVNode("div", _hoisted_77, [_cache[48] || (_cache[48] = _createElementVNode("label", null, "快捷按钮背景颜色", -1)), _createVNode(_component_Select, {
                      modelValue: _ctx.draft.controlsBackgroundSource,
                      "onUpdate:modelValue": $event => ((_ctx.draft.controlsBackgroundSource) = $event),
                      options: _ctx.controlsBackgroundOptions
                    }, null, 8, ["modelValue", "onUpdate:modelValue", "options"]), (_ctx.draft.controlsBackgroundSource === 'cover')
                      ? (_openBlock(), _createElementBlock("p", _hoisted_78, [_createElementVNode("span", { style: _normalizeStyle({ background:_ctx.previewControls.background }) }, null, 4), _cache[47] || (_cache[47] = _createTextVNode("背景使用当前封面的鲜艳颜色，切歌自动更新。", -1))]))
                      : _createCommentVNode("", true)]),
                    (_ctx.draft.controlsBackgroundSource === 'custom')
                      ? (_openBlock(), _createElementBlock("div", _hoisted_79, [_cache[49] || (_cache[49] = _createElementVNode("label", null, "自定义背景颜色", -1)), _createElementVNode("label", _hoisted_80, [_createElementVNode("input", {
                          type: "color",
                          "aria-label": "快捷按钮自定义背景颜色",
                          value: _ctx.draft.controlsBackgroundColor,
                          onInput: $event => (_ctx.setColor('controlsBackgroundColor', $event))
                        }, null, 40, _hoisted_81), _createElementVNode("span", null, _toDisplayString(_ctx.draft.controlsBackgroundColor), 1)])]))
                      : _createCommentVNode("", true),
                    _createElementVNode("div", _hoisted_82, [_createElementVNode("label", null, [_cache[50] || (_cache[50] = _createTextVNode("背景透明度 ", -1)), _createElementVNode("span", null, _toDisplayString(_ctx.backgroundTransparency) + "%", 1)]), _createVNode(_component_Slider, {
                      modelValue: _ctx.backgroundTransparency,
                      "onUpdate:modelValue": $event => ((_ctx.backgroundTransparency) = $event),
                      min: 0,
                      max: 100,
                      step: 1
                    }, null, 8, ["modelValue", "onUpdate:modelValue", "min", "max", "step"]), _cache[51] || (_cache[51] = _createElementVNode("p", { class: "tl-note" }, "0% 不透明，100% 完全透明。仅影响按钮栏背景，鼠标移入上方预览查看效果。", -1))]),
                    _createElementVNode("div", _hoisted_83, [_createElementVNode("div", null, [_createElementVNode("div", _hoisted_84, [_cache[52] || (_cache[52] = _createElementVNode("div", null, [_createElementVNode("span", null, "按钮悬停背景"), _createElementVNode("p", null, "鼠标放在单个按钮上时显示背景。")], -1)), _createVNode(_component_Switch, {
                      modelValue: _ctx.draft.buttonHoverBackground,
                      "onUpdate:modelValue": $event => ((_ctx.draft.buttonHoverBackground) = $event)
                    }, null, 8, ["modelValue", "onUpdate:modelValue"])]), (_ctx.draft.buttonHoverBackground)
                      ? (_openBlock(), _createElementBlock("div", _hoisted_85, [_cache[53] || (_cache[53] = _createElementVNode("label", null, "悬停背景颜色", -1)), _createElementVNode("label", _hoisted_86, [_createElementVNode("input", {
                          type: "color",
                          "aria-label": "按钮悬停背景颜色",
                          value: _ctx.draft.buttonHoverColor,
                          onInput: $event => (_ctx.setColor('buttonHoverColor', $event))
                        }, null, 40, _hoisted_87), _createElementVNode("span", null, _toDisplayString(_ctx.draft.buttonHoverColor), 1)])]))
                      : _createCommentVNode("", true)]), _createElementVNode("div", null, [_createElementVNode("div", _hoisted_88, [_cache[54] || (_cache[54] = _createElementVNode("div", null, [_createElementVNode("span", null, "按钮按下背景"), _createElementVNode("p", null, "按住单个按钮时显示背景，优先于悬停状态。")], -1)), _createVNode(_component_Switch, {
                      modelValue: _ctx.draft.buttonPressedBackground,
                      "onUpdate:modelValue": $event => ((_ctx.draft.buttonPressedBackground) = $event)
                    }, null, 8, ["modelValue", "onUpdate:modelValue"])]), (_ctx.draft.buttonPressedBackground)
                      ? (_openBlock(), _createElementBlock("div", _hoisted_89, [_cache[55] || (_cache[55] = _createElementVNode("label", null, "按下背景颜色", -1)), _createElementVNode("label", _hoisted_90, [_createElementVNode("input", {
                          type: "color",
                          "aria-label": "按钮按下背景颜色",
                          value: _ctx.draft.buttonPressedColor,
                          onInput: $event => (_ctx.setColor('buttonPressedColor', $event))
                        }, null, 40, _hoisted_91), _createElementVNode("span", null, _toDisplayString(_ctx.draft.buttonPressedColor), 1)])]))
                      : _createCommentVNode("", true)]), _cache[56] || (_cache[56] = _createElementVNode("p", { class: "tl-note" }, "上方预览按钮仅演示外观，不操作歌曲。", -1))])
                  ]))
                : _createCommentVNode("", true)
            ]),
            _createElementVNode("section", _hoisted_92, [_cache[82] || (_cache[82] = _createElementVNode("div", { class: "tl-section-heading" }, [_createElementVNode("h3", null, "切歌信息浮层"), _createElementVNode("p", null, "切歌后在快捷按钮上方显示歌曲信息，不占用歌词和按钮区域。")], -1)), _createElementVNode("div", _hoisted_93, [_cache[58] || (_cache[58] = _createElementVNode("div", null, [_createElementVNode("span", null, "启用切歌信息浮层"), _createElementVNode("p", null, "收到新歌曲信息后立即展示，多个屏幕同步，不等待封面加载。")], -1)), _createVNode(_component_Switch, {
              modelValue: _ctx.draft.noticeEnabled,
              "onUpdate:modelValue": $event => ((_ctx.draft.noticeEnabled) = $event)
            }, null, 8, ["modelValue", "onUpdate:modelValue"])]), (_ctx.draft.noticeEnabled)
              ? (_openBlock(), _createElementBlock("div", _hoisted_94, [
                  _cache[78] || (_cache[78] = _createElementVNode("div", { class: "tl-subheading" }, "展示行为", -1)),
                  _createElementVNode("div", _hoisted_95, [_cache[59] || (_cache[59] = _createElementVNode("div", null, [_createElementVNode("span", null, "仅任务栏切歌时显示"), _createElementVNode("p", null, "开启后，仅点击任务栏的上一首、下一首显示；播放器内切歌和自动播放下一首不显示。关闭后所有切歌都显示。")], -1)), _createVNode(_component_Switch, {
                    modelValue: _ctx.draft.noticeOnlyTaskbarSwitch,
                    "onUpdate:modelValue": $event => ((_ctx.draft.noticeOnlyTaskbarSwitch) = $event)
                  }, null, 8, ["modelValue", "onUpdate:modelValue"])]),
                  _createElementVNode("div", _hoisted_96, [_createElementVNode("div", _hoisted_97, [_createElementVNode("label", null, [_cache[60] || (_cache[60] = _createTextVNode("展示后淡出 ", -1)), _createElementVNode("span", null, _toDisplayString(_ctx.draft.noticeDuration) + " 秒", 1)]), _createVNode(_component_InputNumber, {
                    modelValue: _ctx.draft.noticeDuration,
                    "onUpdate:modelValue": $event => ((_ctx.draft.noticeDuration) = $event),
                    min: 1,
                    max: 30,
                    step: 0.5
                  }, null, 8, ["modelValue", "onUpdate:modelValue", "min", "max", "step"])]), _createElementVNode("div", _hoisted_98, [_createElementVNode("label", null, [_cache[61] || (_cache[61] = _createTextVNode("淡出时长 ", -1)), _createElementVNode("span", null, _toDisplayString(_ctx.draft.noticeFadeDuration) + " ms", 1)]), _createVNode(_component_Slider, {
                    modelValue: _ctx.draft.noticeFadeDuration,
                    "onUpdate:modelValue": $event => ((_ctx.draft.noticeFadeDuration) = $event),
                    min: 0,
                    max: 1000,
                    step: 20
                  }, null, 8, ["modelValue", "onUpdate:modelValue", "min", "max", "step"]), _cache[62] || (_cache[62] = _createElementVNode("p", { class: "tl-note" }, "0 为直接收起。", -1))])]),
                  _createElementVNode("div", _hoisted_99, [_cache[63] || (_cache[63] = _createElementVNode("div", null, [_createElementVNode("span", null, "悬停时保持显示"), _createElementVNode("p", null, "移开后重新计时，任一屏幕悬停都会延长所有屏幕的展示。")], -1)), _createVNode(_component_Switch, {
                    modelValue: _ctx.draft.noticeHoldOnHover,
                    "onUpdate:modelValue": $event => ((_ctx.draft.noticeHoldOnHover) = $event)
                  }, null, 8, ["modelValue", "onUpdate:modelValue"])]),
                  _cache[79] || (_cache[79] = _createElementVNode("div", { class: "tl-subheading" }, "背景与形状", -1)),
                  _createElementVNode("div", _hoisted_100, [
                    _createElementVNode("div", _hoisted_101, [_cache[64] || (_cache[64] = _createElementVNode("label", null, "背景样式", -1)), _createVNode(_component_Select, {
                      modelValue: _ctx.draft.noticeBackgroundStyle,
                      "onUpdate:modelValue": $event => ((_ctx.draft.noticeBackgroundStyle) = $event),
                      options: _ctx.noticeBackgroundOptions
                    }, null, 8, ["modelValue", "onUpdate:modelValue", "options"])]),
                    (_ctx.draft.noticeBackgroundStyle !== 'cover')
                      ? (_openBlock(), _createElementBlock("div", _hoisted_102, [_createElementVNode("label", null, _toDisplayString(_ctx.draft.noticeBackgroundStyle === 'glass' ? '玻璃叠色' : '背景颜色'), 1), _createElementVNode("label", _hoisted_103, [_createElementVNode("input", {
                          type: "color",
                          "aria-label": "切歌浮层背景颜色",
                          value: _ctx.draft.noticeBackgroundColor,
                          onInput: $event => (_ctx.setColor('noticeBackgroundColor', $event))
                        }, null, 40, _hoisted_104), _createElementVNode("span", null, _toDisplayString(_ctx.draft.noticeBackgroundColor), 1)])]))
                      : _createCommentVNode("", true),
                    (_ctx.draft.noticeBackgroundStyle === 'cover')
                      ? (_openBlock(), _createElementBlock("p", _hoisted_105, [_createElementVNode("span", { style: _normalizeStyle({ background:_ctx.previewNotice.background }) }, null, 4), _cache[65] || (_cache[65] = _createTextVNode("使用封面鲜艳颜色，切歌取色期间保留上次颜色。", -1))]))
                      : _createCommentVNode("", true),
                    (_ctx.draft.noticeBackgroundStyle === 'glass')
                      ? (_openBlock(), _createElementBlock("p", _hoisted_106, "模糊浮层背后的内容，并叠加所选颜色；透明度越高，背景越通透。"))
                      : _createCommentVNode("", true),
                    _createElementVNode("div", _hoisted_107, [_createElementVNode("label", null, [_createTextVNode(_toDisplayString(_ctx.draft.noticeBackgroundStyle === 'glass' ? '玻璃叠色透明度' : '背景透明度') + " ", 1), _createElementVNode("span", null, _toDisplayString(_ctx.draft.noticeTransparency) + "%", 1)]), _createVNode(_component_Slider, {
                      modelValue: _ctx.draft.noticeTransparency,
                      "onUpdate:modelValue": $event => ((_ctx.draft.noticeTransparency) = $event),
                      min: 0,
                      max: 100,
                      step: 1
                    }, null, 8, ["modelValue", "onUpdate:modelValue", "min", "max", "step"]), _createElementVNode("p", _hoisted_108, _toDisplayString(_ctx.draft.noticeBackgroundStyle === 'glass' ? '0% 为不透明叠色，100% 为无叠色，保留背景模糊；文字保持清晰。' : '0% 不透明，100% 完全透明。只影响背景，不影响文字。'), 1)]),
                    _createElementVNode("div", _hoisted_109, [_createElementVNode("label", null, [_cache[66] || (_cache[66] = _createTextVNode("浮层宽度 ", -1)), _createElementVNode("span", null, _toDisplayString(_ctx.draft.noticeWidth) + " px", 1)]), _createVNode(_component_Slider, {
                      modelValue: _ctx.draft.noticeWidth,
                      "onUpdate:modelValue": $event => ((_ctx.draft.noticeWidth) = $event),
                      min: 180,
                      max: 480,
                      step: 10
                    }, null, 8, ["modelValue", "onUpdate:modelValue", "min", "max", "step"])]),
                    _createElementVNode("div", _hoisted_110, [_createElementVNode("label", null, [_cache[67] || (_cache[67] = _createTextVNode("圆角 ", -1)), _createElementVNode("span", null, _toDisplayString(_ctx.draft.noticeRadius) + " px", 1)]), _createVNode(_component_Slider, {
                      modelValue: _ctx.draft.noticeRadius,
                      "onUpdate:modelValue": $event => ((_ctx.draft.noticeRadius) = $event),
                      min: 0,
                      max: 24,
                      step: 1
                    }, null, 8, ["modelValue", "onUpdate:modelValue", "min", "max", "step"])]),
                    _createElementVNode("div", _hoisted_111, [_createElementVNode("label", null, [_cache[68] || (_cache[68] = _createTextVNode("距任务栏间距 ", -1)), _createElementVNode("span", null, _toDisplayString(_ctx.draft.noticeGap) + " px", 1)]), _createVNode(_component_Slider, {
                      modelValue: _ctx.draft.noticeGap,
                      "onUpdate:modelValue": $event => ((_ctx.draft.noticeGap) = $event),
                      min: 0,
                      max: 40,
                      step: 1
                    }, null, 8, ["modelValue", "onUpdate:modelValue", "min", "max", "step"])])
                  ]),
                  _cache[80] || (_cache[80] = _createElementVNode("div", { class: "tl-subheading" }, "文字样式", -1)),
                  _createElementVNode("div", _hoisted_112, [_cache[69] || (_cache[69] = _createElementVNode("span", null, "显示歌手", -1)), _createVNode(_component_Switch, {
                    modelValue: _ctx.draft.noticeShowArtist,
                    "onUpdate:modelValue": $event => ((_ctx.draft.noticeShowArtist) = $event)
                  }, null, 8, ["modelValue", "onUpdate:modelValue"])]),
                  _createElementVNode("div", _hoisted_113, [
                    _createElementVNode("div", _hoisted_114, [_cache[70] || (_cache[70] = _createElementVNode("label", null, "文字对齐", -1)), _createVNode(_component_Select, {
                      modelValue: _ctx.draft.noticeTextAlignment,
                      "onUpdate:modelValue": $event => ((_ctx.draft.noticeTextAlignment) = $event),
                      options: _ctx.noticeAlignmentOptions
                    }, null, 8, ["modelValue", "onUpdate:modelValue", "options"]), _cache[71] || (_cache[71] = _createElementVNode("p", { class: "tl-note" }, "歌名与歌手一起对齐。长文字溢出时从开头显示，再按滚动设置处理。", -1))]),
                    _createElementVNode("div", _hoisted_115, [_createElementVNode("label", null, [_cache[72] || (_cache[72] = _createTextVNode("歌名字号 ", -1)), _createElementVNode("span", null, _toDisplayString(_ctx.draft.noticeTitleFontSize) + " px", 1)]), _createVNode(_component_Slider, {
                      modelValue: _ctx.draft.noticeTitleFontSize,
                      "onUpdate:modelValue": $event => ((_ctx.draft.noticeTitleFontSize) = $event),
                      min: 10,
                      max: 24,
                      step: 1
                    }, null, 8, ["modelValue", "onUpdate:modelValue", "min", "max", "step"])]),
                    (_ctx.draft.noticeShowArtist)
                      ? (_openBlock(), _createElementBlock("div", _hoisted_116, [_createElementVNode("label", null, [_cache[73] || (_cache[73] = _createTextVNode("歌手字号 ", -1)), _createElementVNode("span", null, _toDisplayString(_ctx.draft.noticeArtistFontSize) + " px", 1)]), _createVNode(_component_Slider, {
                          modelValue: _ctx.draft.noticeArtistFontSize,
                          "onUpdate:modelValue": $event => ((_ctx.draft.noticeArtistFontSize) = $event),
                          min: 8,
                          max: 20,
                          step: 1
                        }, null, 8, ["modelValue", "onUpdate:modelValue", "min", "max", "step"])]))
                      : _createCommentVNode("", true),
                    _createElementVNode("div", _hoisted_117, [_cache[74] || (_cache[74] = _createElementVNode("label", null, "文字配色", -1)), _createVNode(_component_Select, {
                      modelValue: _ctx.draft.noticeTextSource,
                      "onUpdate:modelValue": $event => ((_ctx.draft.noticeTextSource) = $event),
                      options: _ctx.noticeTextOptions
                    }, null, 8, ["modelValue", "onUpdate:modelValue", "options"])]),
                    (_ctx.draft.noticeTextSource === 'custom')
                      ? (_openBlock(), _createElementBlock("div", _hoisted_118, [_cache[75] || (_cache[75] = _createElementVNode("label", null, "歌名颜色", -1)), _createElementVNode("label", _hoisted_119, [_createElementVNode("input", {
                          type: "color",
                          "aria-label": "切歌浮层歌名颜色",
                          value: _ctx.draft.noticeTitleColor,
                          onInput: $event => (_ctx.setColor('noticeTitleColor', $event))
                        }, null, 40, _hoisted_120), _createElementVNode("span", null, _toDisplayString(_ctx.draft.noticeTitleColor), 1)])]))
                      : _createCommentVNode("", true),
                    (_ctx.draft.noticeTextSource === 'custom' && _ctx.draft.noticeShowArtist)
                      ? (_openBlock(), _createElementBlock("div", _hoisted_121, [_cache[76] || (_cache[76] = _createElementVNode("label", null, "歌手颜色", -1)), _createElementVNode("label", _hoisted_122, [_createElementVNode("input", {
                          type: "color",
                          "aria-label": "切歌浮层歌手颜色",
                          value: _ctx.draft.noticeArtistColor,
                          onInput: $event => (_ctx.setColor('noticeArtistColor', $event))
                        }, null, 40, _hoisted_123), _createElementVNode("span", null, _toDisplayString(_ctx.draft.noticeArtistColor), 1)])]))
                      : _createCommentVNode("", true)
                  ]),
                  _cache[81] || (_cache[81] = _createElementVNode("p", { class: "tl-note" }, "字体跟随“歌词字体”，宽度、字号、圆角与间距会适配屏幕缩放。", -1)),
                  _createElementVNode("div", _hoisted_124, [_cache[77] || (_cache[77] = _createElementVNode("div", null, [_createElementVNode("span", null, "长文字自动滚动"), _createElementVNode("p", null, "关闭后显示省略号；只在文字溢出时滚动，暂停播放也可查看。")], -1)), _createVNode(_component_Switch, {
                    modelValue: _ctx.draft.noticeScrollText,
                    "onUpdate:modelValue": $event => ((_ctx.draft.noticeScrollText) = $event)
                  }, null, 8, ["modelValue", "onUpdate:modelValue"])])
                ]))
              : _createCommentVNode("", true)]),
            _createElementVNode("section", _hoisted_125, [
              _cache[86] || (_cache[86] = _createElementVNode("div", { class: "tl-section-heading" }, [_createElementVNode("h3", null, "播放行为"), _createElementVNode("p", null, "让歌词显示适应你的使用习惯。")], -1)),
              _createElementVNode("div", _hoisted_126, [_cache[83] || (_cache[83] = _createElementVNode("div", null, [_createElementVNode("span", null, "长歌词随进度滚动"), _createElementVNode("p", null, "暂停时停止，跳转后重新对齐。")], -1)), _createVNode(_component_Switch, {
                modelValue: _ctx.draft.scrollLyrics,
                "onUpdate:modelValue": $event => ((_ctx.draft.scrollLyrics) = $event)
              }, null, 8, ["modelValue", "onUpdate:modelValue"])]),
              _createElementVNode("div", _hoisted_127, [_cache[84] || (_cache[84] = _createElementVNode("span", null, "暂停时隐藏", -1)), _createVNode(_component_Switch, {
                modelValue: _ctx.draft.hidePaused,
                "onUpdate:modelValue": $event => ((_ctx.draft.hidePaused) = $event)
              }, null, 8, ["modelValue", "onUpdate:modelValue"])]),
              _createElementVNode("div", _hoisted_128, [_cache[85] || (_cache[85] = _createElementVNode("span", null, "全屏应用时隐藏", -1)), _createVNode(_component_Switch, {
                modelValue: _ctx.draft.hideFullscreen,
                "onUpdate:modelValue": $event => ((_ctx.draft.hideFullscreen) = $event)
              }, null, 8, ["modelValue", "onUpdate:modelValue"])])
            ])
          ]))
        : (_openBlock(), _createElementBlock("p", _hoisted_129, "开启上方开关后，即可调整歌词、封面和快捷操作。")),
      _createElementVNode("footer", _hoisted_130, [_createElementVNode("div", _hoisted_131, [_createVNode(_component_Button, {
        class: "tl-action-button",
        size: "sm",
        variant: "outline",
        onClick: _ctx.save,
        disabled: _ctx.busy
      }, {
        default: _withCtx(() => [_createTextVNode(_toDisplayString(_ctx.busy ? '正在应用…' : '保存并应用'), 1)]),
        _: 1
      }, 8, ["onClick", "disabled"]), (_ctx.draft.enabled)
        ? (_openBlock(), _createBlock(_component_Button, {
            key: 0,
            class: "tl-action-button",
            size: "sm",
            variant: "outline",
            onClick: _ctx.restart,
            disabled: _ctx.busy
          }, {
            default: _withCtx(() => [...(_cache[87] || (_cache[87] = [_createTextVNode("重新连接", -1)]))]),
            _: 1
          }, 8, ["onClick", "disabled"]))
        : _createCommentVNode("", true), _createVNode(_component_Button, {
        class: "tl-action-button",
        size: "sm",
        variant: "outline",
        onClick: _ctx.reset,
        disabled: _ctx.busy
      }, {
        default: _withCtx(() => [...(_cache[88] || (_cache[88] = [_createTextVNode("恢复默认", -1)]))]),
        _: 1
      }, 8, ["onClick", "disabled"])]), _createElementVNode("p", _hoisted_132, _toDisplayString(_ctx.status), 1)])
    ]))
}
}

export async function activate(ctx) {
  if (ctx.electron.platform !== 'win32') throw new Error('此插件仅支持 Windows。');
  let settings = normalize(await ctx.storage.get('settings') || DEFAULTS);
  let snapshot = await ctx.nowPlaying.getSnapshot();
  let alive = true;
  let pid = 0;
  let launchPending = false;
  let commandBusy = false;
  let taskbarSwitchRequest;
  let noticeTaskbarTrackId='';
  let coverRevision = 1;
  let coverIdentity = `${snapshot.playback?.trackId || ''}:${snapshot.playback?.coverUrl || ''}`;
  let cachedCover;
  let accentRevision=0;
  const coverAccent=ctx.vue.ref('');
  const commandWaiters = new Set();
  const snapshotRef = ctx.vue.shallowRef(snapshot);
  const displays = ctx.vue.ref([]);
  const token = crypto.randomUUID();
  const updateSnapshot = next => {
    const trackId=String(next.playback?.trackId || ''),previousId=String(snapshot.playback?.trackId || '');
    if(trackId!==previousId){
      // 只标记任务栏切歌命令引起的首个新歌曲，后续自动切歌不会沿用。
      noticeTaskbarTrackId=taskbarSwitchRequest?.trackId===previousId && Date.now()<=taskbarSwitchRequest.expires ? trackId : '';
      taskbarSwitchRequest=undefined;
    }
    const identity=`${next.playback?.trackId || ''}:${next.playback?.coverUrl || ''}`;
    // 切歌先保留上次成功颜色，等新封面取色完成后再替换。
    if (identity!==coverIdentity) { coverIdentity=identity;coverRevision++;cachedCover=undefined; }
    snapshot = next; snapshotRef.value = next;
    ensureCoverAccent();
    for (const waiter of commandWaiters) waiter(next);
  };
  const currentFrame = () => {
    const frame={...makeFrame(snapshot,settings,Date.now(),coverAccent.value),commandBusy,noticeTaskbarTrackId};
    if (frame.coverUrl) {
      frame.coverSourceUrl=frame.coverUrl;
      frame.coverUrl=`${server.origin}/cover?token=${token}&key=${coverRevision}`;
    }
    return frame;
  };
  const readCover = () => {
    const url=snapshot.playback?.coverUrl;
    if (!url) return Promise.resolve({status:404,body:''});
    // 在播放器内读取封面，原生端同时保留普通图片 URL 作为跨域失败时的兜底。
    cachedCover ||= fetch(url).then(async response => {
      if (!response.ok) throw new Error('封面读取失败');
      const bytes=await response.arrayBuffer();
      if (bytes.byteLength>4*1024*1024) throw new Error('封面过大');
      return {headers:{'content-type':response.headers.get('content-type') || 'image/png'},body:bytes};
    }).catch(() => ({status:502,body:''}));
    return cachedCover;
  };
  const ensureCoverAccent = async (preview=false) => {
    if((settings.playedColorSource!=='cover' && settings.controlsBackgroundSource!=='cover' && settings.controlsIconColorSource!=='cover' && !(settings.noticeEnabled && settings.noticeBackgroundStyle==='cover') && !preview) || accentRevision===coverRevision || !snapshot.playback?.coverUrl)return;
    const revision=coverRevision;accentRevision=revision;
    let bitmap;
    try {
      const image=await readCover();if(!(image.body instanceof ArrayBuffer))return;
      bitmap=await createImageBitmap(new Blob([image.body]));
      // 离屏采样只处理图片数据，不创建或插入界面组件。
      const canvas=new OffscreenCanvas(32,32),context=canvas.getContext('2d');
      context.drawImage(bitmap,0,0,32,32);
      const color=coverAccentFromPixels(context.getImageData(0,0,32,32).data);
      // 切歌后的旧请求不能覆盖新歌颜色，所有屏幕共用同一结果。
      if(alive && revision===coverRevision && color)coverAccent.value=color;
    }catch { /* 读取失败保留上次颜色，首次取色前使用手动颜色。 */ }
    finally { if(bitmap)bitmap.close(); }
  };
  const cover = request => request.query.key===String(coverRevision) && settings.showCover ? readCover() : {status:404,body:''};
  ensureCoverAccent();
  ctx.dispose(ctx.nowPlaying.onSnapshot(updateSnapshot));
  const control = async request => {
    if (request.method !== 'POST') return { status:405,body:'' };
    const action = request.query.action;
    if (!['previousTrack','togglePlayback','nextTrack','toggleFavorite','seek'].includes(action)) return { status:400,body:'' };
    const seeking=action==='seek';
    if (!settings.enabled || !(seeking?settings.hoverProgress:settings.hoverControls)) return { status:403,body:'' };
    if (commandBusy) return { status:409,body:JSON.stringify({error:'歌曲操作正在处理中'}) };
    updateSnapshot(await ctx.nowPlaying.getSnapshot());
    if (commandBusy) return { status:409,body:JSON.stringify({error:'歌曲操作正在处理中'}) };
    const before = snapshot.playback;
    if (!before?.trackId || String(before.trackId) !== request.query.trackId) return { status:409,body:JSON.stringify({error:'歌曲已切换，请重试'}) };
    const duration=makeFrame(snapshot,settings).durationMs/1000;
    const requested=Number(request.query.value);
    if(seeking && (request.query.value===undefined || String(request.query.value).trim()==='' || !Number.isFinite(requested)))return {status:400,body:JSON.stringify({error:'播放位置无效'})};
    if(seeking && (!Number.isFinite(duration) || duration<=0 || before.isLoading))return {status:409,body:JSON.stringify({error:'当前歌曲暂时无法跳转'})};
    const target=seeking?Math.min(duration,Math.max(0,requested)):0;
    commandBusy = true;
    const switchRequest=action==='previousTrack' || action==='nextTrack' ? {trackId:String(before.trackId),expires:Date.now()+7000} : undefined;
    if(switchRequest)taskbarSwitchRequest=switchRequest;
    let finish, timer;
    // 等待宿主确认结果，所有屏幕共享忙碌状态，收藏按钮不自行猜测状态。
    const confirmed = new Promise(resolve => {
      finish = next => {
        const after=next.playback;
        if(seeking){
          if(String(after?.trackId || '')!==String(before.trackId)){resolve(false);return;}
          const position=makeFrame(next,settings).coverTimeMs/1000;
          if(Math.abs(position-target)<=1.5)resolve(true);
          return;
        }
        const changed=String(after?.trackId || '')!==String(before.trackId) || (action==='toggleFavorite' ? Boolean(after?.isFavorite)!==Boolean(before.isFavorite) : action==='togglePlayback' ? Boolean(after?.isPlaying)!==Boolean(before.isPlaying) : Number(after?.currentTime)<Number(before.currentTime)-.5);
        if (changed) resolve(true);
      };
      commandWaiters.add(finish);
      timer=setTimeout(() => resolve(false),7000);
    });
    try {
      await ctx.nowPlaying.command(seeking?{type:'seek',value:target}:action);
      updateSnapshot(await ctx.nowPlaying.getSnapshot());
      const ok=action==='previousTrack' || action==='nextTrack' ? await Promise.race([confirmed,new Promise(resolve => setTimeout(() => resolve(true),250))]) : await confirmed;
      if (!ok) return { status:String(snapshot.playback?.trackId)!==String(before.trackId)?409:504,body:JSON.stringify({error:'播放器尚未确认操作或歌曲已切换，请重试'}) };
      return { headers:{'content-type':'application/json'},body:JSON.stringify({ok:true,frame:{...currentFrame(),commandBusy:false}}) };
    } catch(error) {
      if(switchRequest && taskbarSwitchRequest===switchRequest)taskbarSwitchRequest=undefined;
      ctx.toast.warning(error.message);
      return { status:502,body:JSON.stringify({error:error.message}) };
    } finally { clearTimeout(timer);commandWaiters.delete(finish);commandBusy=false; }
  };
  const server = await ctx.webServer.listen(request => {
    if (!alive || request.query.token !== token) return { status: 403, body: '' };
    if (request.path === '/command') return control(request);
    if (request.path === '/cover') return cover(request);
    // 原生程序报告实际屏幕，设置页使用相同的播放器下拉组件。
    if (request.path === '/displays') {
      try { const list=JSON.parse(request.query.data); if (!Array.isArray(list) || list.length>32) throw new Error('屏幕列表无效'); displays.value=list; }
      catch { return { status:400,body:'' }; }
      return { body:'ok' };
    }
    if (request.path !== '/state') return { status:404,body:'' };
    return { headers: { 'content-type':'application/json; charset=utf-8', 'cache-control':'no-store' }, body: JSON.stringify(currentFrame()) };
  });
  if (!server.ok) throw new Error(server.error);
  const start = async () => {
    if (launchPending || !alive) return;
    launchPending = true;
    try {
      if (pid) { await ctx.process.terminate(pid); pid = 0; }
      const result = await ctx.process.launch({ executable:'bin/TaskbarLyrics.exe', args:[server.origin + '/state?token=' + token] });
      if (!result.ok) throw new Error(result.canceled ? '启动授权已取消。点击“启动／重新连接”可重试。' : result.error);
      if (!alive) await ctx.process.terminate(result.pid);
      else pid = result.pid;
    } finally { launchPending = false; }
  };
  ctx.dispose(async () => { alive = false; for (const waiter of commandWaiters) waiter({playback:null});if (pid) await ctx.process.terminate(pid); });
  const panel = ctx.vue.defineComponent({
    name:'TaskbarLyricsSettings',
    components: Object.fromEntries(['Select','Slider','Switch','Button','InputNumber'].map(name => [name,ctx.vue.defineAsyncComponent(ctx.ui.components[name])])),
    render:createPanelRender(ctx.vue),
    setup() {
      const draft = ctx.vue.reactive({ ...settings });
      // 界面显示透明度，存储仍用原有 alpha，保留已有配置的视觉效果。
      const transparency=ctx.vue.computed({get:()=>100-draft.opacity,set:value=>{draft.opacity=100-Number(value);}});
      const backgroundTransparency=ctx.vue.computed({get:()=>100-draft.controlsBackgroundOpacity,set:value=>{draft.controlsBackgroundOpacity=100-Number(value);}});
      const busy = ctx.vue.ref(false);
      const status = ctx.vue.ref('');
      const fontOptions = ctx.vue.ref([]);
      const previewProgress = ctx.vue.ref(55);
      ctx.fonts.getOptions({ includeFollow:true }).then(options => { fontOptions.value=options; }).catch(error => { status.value=error.message; });
      const displayOptions=ctx.vue.computed(() => [{label:'主屏幕（默认）',value:'primary'},{label:'全部屏幕',value:'all'},...displays.value.map(item => ({label:item.label,value:item.id})),...(!['primary','all'].includes(draft.display) && !displays.value.some(item => item.id===draft.display) ? [{label:'已断开的屏幕 · 自动使用主屏幕',value:draft.display}] : [])]);
      const layoutOptions=[{label:'单行歌词',value:'single'},{label:'双行歌词',value:'double'}];
      const secondaryOptions=[{label:'下一句歌词',value:'next'},{label:'翻译／音译',value:'translation'}];
      const themeOptions=[{label:'跟随 Windows 任务栏',value:'auto'},{label:'自定义文字颜色',value:'custom'}];
      const coverShapeOptions=[{label:'矩形封面',value:'rectangle'},{label:'圆形封面',value:'circle'}];
      const controlsAlignmentOptions=[{label:'靠左',value:'left'},{label:'随当前歌词长度居中',value:'center'}];
      const playedColorOptions=[{label:'手动设置',value:'custom'},{label:'封面自动取色',value:'cover'}];
      const controlsBackgroundOptions=[{label:'自定义颜色',value:'custom'},{label:'使用封面配色',value:'cover'}];
      const noticeBackgroundOptions=[{label:'自定义颜色',value:'custom'},{label:'使用封面配色',value:'cover'},{label:'毛玻璃',value:'glass'}];
      const noticeTextOptions=[{label:'自动适配背景',value:'auto'},{label:'自定义文字颜色',value:'custom'}];
      const noticeAlignmentOptions=[{label:'居左',value:'left'},{label:'居中',value:'center'}];
      const previewNotice=ctx.vue.computed(()=>noticePalette(draft,coverAccent.value,previewLight.value));
      const previewPalette=ctx.vue.computed(() => previewPalettes(draft,coverAccent.value,previewLight.value));
      const previewPlayed=ctx.vue.computed(() => previewPalette.value.played);
      const previewControls=ctx.vue.computed(() => previewPalette.value.controls);
      // 未保存的取色选项也可立即预览，关闭时保留已有手动颜色。
      ctx.vue.watch(()=>[draft.playedColorSource,draft.controlsBackgroundSource,draft.controlsIconColorSource,draft.noticeBackgroundStyle,snapshotRef.value.playback?.trackId,snapshotRef.value.playback?.coverUrl],([lyric,background,icons,notice])=>{if(lyric==='cover' || background==='cover' || icons==='cover' || notice==='cover')ensureCoverAccent(true);});
      const previewCover=ctx.vue.computed(() => snapshotRef.value.playback?.coverUrl || '');
      const previewCoverPrevious=ctx.vue.ref(''),previewCoverLoaded=ctx.vue.ref('');
      let previewCoverTimer;
      ctx.vue.watch(previewCover,()=>{
        clearTimeout(previewCoverTimer);
        if(previewCoverLoaded.value)previewCoverPrevious.value=previewCoverLoaded.value;
        previewCoverLoaded.value='';
        if(!previewCover.value || !draft.trackTransitions)previewCoverPrevious.value='';
      });
      const onPreviewCoverLoad=event=>{
        // 旧图片的延迟事件不能结束当前歌曲的过渡。
        if(event.target.getAttribute('src')!==previewCover.value)return;
        previewCoverLoaded.value=previewCover.value;
        clearTimeout(previewCoverTimer);
        previewCoverTimer=setTimeout(()=>{previewCoverPrevious.value='';},draft.trackTransitions?draft.transitionDuration:0);
      };
      const onPreviewCoverError=event=>{
        if(event.target.getAttribute('src')!==previewCover.value)return;
        clearTimeout(previewCoverTimer);coverFailed.value=previewCover.value;previewCoverPrevious.value='';
      };
      ctx.vue.watch(()=>draft.trackTransitions,enabled=>{if(!enabled){clearTimeout(previewCoverTimer);previewCoverPrevious.value='';}});
      ctx.dispose(()=>clearTimeout(previewCoverTimer));
      const previewDuration=ctx.vue.computed(()=>formatPlaybackTime((Number(snapshotRef.value.playback?.duration)||260)*1000));
      const previewElapsed=ctx.vue.computed(()=>formatPlaybackTime((Number(snapshotRef.value.playback?.duration)||260)*10*previewProgress.value));
      const previewPlaying=ctx.vue.computed(() => Boolean(snapshotRef.value.playback?.isPlaying));
      const previewFavorite=ctx.vue.computed(() => Boolean(snapshotRef.value.playback?.isFavorite));
      const previewTitle=ctx.vue.computed(()=>snapshotRef.value.playback?.title || '让每一句，留在此刻');
      const previewArtist=ctx.vue.computed(()=>snapshotRef.value.playback?.artist || 'EchoMusic');
      function onPreviewCoverHover(event) {
        // 按实际溢出长度设置滚动距离，短文字保持静止。
        for(const text of event.currentTarget.parentElement.querySelectorAll('.tl-preview-info-line span')) {
          const distance=Math.max(0,text.scrollWidth-text.parentElement.clientWidth);
          text.style.setProperty('--tl-info-distance',-distance+'px');
          text.style.setProperty('--tl-info-duration',(distance/26*2+1.6)+'s');
        }
      }
      function onPreviewNoticeHover(event) {
        for(const text of event.currentTarget.querySelectorAll('.tl-notice-line span')) {
          const distance=Math.max(0,text.scrollWidth-text.parentElement.clientWidth);
          text.style.setProperty('--tl-info-distance',-distance+'px');
          text.style.setProperty('--tl-info-duration',(distance/26*2+1.6)+'s');
        }
      }
      const coverFailed=ctx.vue.ref('');
      const previewFont = ctx.vue.computed(() => draft.fontFamily === 'follow' ? snapshot.appearance?.fontFamily || ctx.fonts.buildFamily('system-ui') : ctx.fonts.buildFamily(draft.fontFamily));
      const previewLight = ctx.vue.computed(() => {
        const display=displays.value.find(item=>item.id===draft.display) || displays.value.find(item=>item.primary) || displays.value[0];
        return display?.taskbarLight ?? (draft.theme === 'custom' ? (parseInt(draft.primaryColor.slice(1,3),16)+parseInt(draft.primaryColor.slice(3,5),16)+parseInt(draft.primaryColor.slice(5,7),16))/3 < 110 : matchMedia('(prefers-color-scheme: light)').matches);
      });
      const previewColors = ctx.vue.computed(() => draft.theme === 'custom' ? [draft.primaryColor,draft.secondaryColor] : previewLight.value ? ['#202D35','#62737B'] : ['#F1F7F6','#B4C3C1']);
      const setColor = (key,event) => { draft[key]=event.target.value; if (key==='primaryColor' || key==='secondaryColor') draft.theme='custom'; };
      const run = async action => {
        busy.value = true;
        try { await action(); status.value = '';ctx.toast.success('已应用'); }
        catch (error) { status.value = error.message; }
        finally { busy.value = false; }
      };
      const save = () => run(async () => {
        settings = normalize(draft);
        ensureCoverAccent();
        Object.assign(draft, settings);
        await ctx.storage.set('settings', settings);
        if (settings.enabled && !pid) await start();
      });
      const reset = () => { Object.assign(draft, DEFAULTS); return save(); };
      return { draft, transparency, backgroundTransparency, busy, status, save, reset, fontOptions, previewProgress, previewElapsed, previewDuration, previewFont, previewColors, previewLight, previewPlayed, previewControls, previewNotice, noticeBackgroundOptions, noticeTextOptions, noticeAlignmentOptions, onPreviewNoticeHover, playedColorOptions, controlsBackgroundOptions, layoutOptions, secondaryOptions, themeOptions, displayOptions, coverShapeOptions, controlsAlignmentOptions, previewCover, previewCoverPrevious, previewCoverLoaded, onPreviewCoverLoad, onPreviewCoverError, onPreviewCoverHover, previewTitle, previewArtist, previewPlaying, previewFavorite, coverFailed, setColor, restart: () => run(start) };
    }
  });
  ctx.ui.settings.define({ title:'任务栏歌词 · 微光', component:panel });
  if (settings.enabled) {
    try { await start(); }
    catch (error) { ctx.toast.warning(error.message); }
  }
}
