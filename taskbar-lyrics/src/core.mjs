// 所有可调数值由 EchoMusic 插件设置保存。
// 默认值采用当前已保存的配置，“恢复默认”使用同一套设置。
export const DEFAULTS = Object.freeze({
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
  "rotateCover": false
});
export function normalize(value = {}) {
  const s = { ...DEFAULTS, ...value };
  // 旧版字号迁移到独立行设置，保留用户原来的视觉比例。
  s.primaryFontSize = value.primaryFontSize ?? value.fontSize ?? DEFAULTS.primaryFontSize;
  s.secondaryFontSize = value.secondaryFontSize ?? (value.fontSize ? Math.round(value.fontSize * .8) : DEFAULTS.secondaryFontSize);
  if (s.theme==='light' || s.theme==='dark') {
    if (s.theme==='light') { s.primaryColor=value.primaryColor || '#202D35';s.secondaryColor=value.secondaryColor || '#62737B'; }
    s.theme='custom';
  }
  for (const [key, min, max] of [['width',120,900],['fontSize',10,26],['primaryFontSize',10,36],['secondaryFontSize',8,36],['offset',0,2000],['verticalOffset',-20,20],['opacity',30,100],['controlsBackgroundOpacity',0,100]]) {
    s[key] = Math.min(max, Math.max(min, Number.isFinite(Number(s[key])) ? Number(s[key]) : DEFAULTS[key]));
  }
  for (const [key, values] of [['layout',['single','double']],['secondary',['next','translation']],['theme',['auto','light','dark','custom']],['coverShape',['rectangle','circle']],['controlsAlignment',['left','center']],['playedColorSource',['custom','cover']],['controlsBackgroundSource',['custom','cover']],['controlsIconColorSource',['custom','cover']]]) {
    if (!values.includes(s[key])) s[key] = DEFAULTS[key];
  }
  for (const key of ['enabled','hidePaused','hideFullscreen','scrollLyrics','progressMask','hoverControls','showCover','rotateCover','buttonHoverBackground','buttonPressedBackground']) s[key] = Boolean(s[key]);
  for (const key of ['primaryColor','secondaryColor','primaryPlayedColor','secondaryPlayedColor','controlsBackgroundColor','controlsIconColor','buttonHoverColor','buttonPressedColor']) {
    if (!/^#[\da-f]{6}$/i.test(s[key])) s[key]=DEFAULTS[key];
  }
  // Windows 显示设备标识用于跨重启保存屏幕选择。
  if (!['primary','all'].includes(s.display) && !/^\\\\\.\\DISPLAY\d+$/i.test(String(s.display))) s.display='primary';
  s.fontFamily = String(s.fontFamily || 'follow').trim();
  return s;
}
export function lineStart(line) {
  const first = line.characters?.[0]?.startTime;
  return Number.isFinite(first) ? first : Number(line.time) * 1000;
}
export function nativeFontFamily(font) {
  // 将播放器 CSS 字体列表转换为 WPF 字体列表，保留字体顺序。
  const generic = new Set(['system-ui','sans-serif','-apple-system','BlinkMacSystemFont']);
  return String(font || '').split(',').map(name => name.trim().replace(/^['"]|['"]$/g,'')).filter(name => name && !generic.has(name)).concat(['Microsoft YaHei UI','Segoe UI']).join(', ');
}
export function coverAccentFromPixels(pixels) {
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
export function playedPalette(settings,coverAccent='') {
  const automatic=settings.playedColorSource==='cover';
  const color=automatic && /^#[\da-f]{6}$/i.test(coverAccent)?coverAccent:'';
  return {primary:color||settings.primaryPlayedColor,secondary:color||settings.secondaryPlayedColor};
}
export function controlsPalette(settings,coverAccent='') {
  // 背景和图标分别选色，背景透明度不影响图标。
  const background=settings.controlsBackgroundSource==='cover' && /^#[\da-f]{6}$/i.test(coverAccent)?coverAccent:settings.controlsBackgroundColor;
  const foreground=settings.controlsIconColorSource==='cover' && /^#[\da-f]{6}$/i.test(coverAccent)?coverAccent:settings.controlsIconColor;
  const opacity=settings.controlsBackgroundOpacity;
  return {background,foreground,opacity,backgroundCss:background+Math.round(opacity*255/100).toString(16).padStart(2,'0')};
}
export function makeFrame(snapshot, settings, now = Date.now(), coverAccent='') {
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
    settings, text, secondary, playing: Boolean(p.isPlaying), advancing:Boolean(advancing), hasTrack: Boolean(p.trackId || p.title),
    trackId:String(p.trackId || ''), isFavorite:Boolean(p.isFavorite), coverUrl:String(p.coverUrl || ''), coverTimeMs,
    lyricKey: `${p.trackId || ''}:${index}`, timelineMs: ms,
    lineStartMs: canScroll ? startMs : 0, lineEndMs: canScroll ? endMs : 0,
    playbackRate,
    fontFamily:nativeFontFamily(settings.fontFamily === 'follow' ? snapshot.appearance?.fontFamily : settings.fontFamily),
    characters: canScroll ? (current.characters || []) : [],
    secondaryCharacters: canScroll && settings.secondary === 'translation' ? (current.translated ? current.translatedCharacters || [] : current.romanizedCharacters || []) : []
  };
}
