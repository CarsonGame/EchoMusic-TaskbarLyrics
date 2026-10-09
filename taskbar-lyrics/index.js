// 所有可调数值由 EchoMusic 插件设置保存。
const DEFAULTS = Object.freeze({ enabled: true, display:'primary', layout: 'single', secondary: 'next', width: 360, fontSize: 16, primaryFontSize:16, secondaryFontSize:12, fontFamily:'follow', primaryColor:'#F1F7F6', secondaryColor:'#B4C3C1', primaryPlayedColor:'#85DCD0', secondaryPlayedColor:'#85DCD0', progressMask:false, offset: 12, opacity: 100, theme: 'auto', hidePaused: false, hideFullscreen: true, scrollLyrics: true });
function normalize(value = {}) {
  const s = { ...DEFAULTS, ...value };
  // 旧版字号迁移到独立行设置，保留用户原来的视觉比例。
  s.primaryFontSize = value.primaryFontSize ?? value.fontSize ?? DEFAULTS.primaryFontSize;
  s.secondaryFontSize = value.secondaryFontSize ?? (value.fontSize ? Math.round(value.fontSize * .8) : DEFAULTS.secondaryFontSize);
  if (s.theme==='light' || s.theme==='dark') {
    if (s.theme==='light') { s.primaryColor=value.primaryColor || '#202D35';s.secondaryColor=value.secondaryColor || '#62737B'; }
    s.theme='custom';
  }
  for (const [key, min, max] of [['width',120,900],['fontSize',10,26],['primaryFontSize',10,36],['secondaryFontSize',8,36],['offset',0,2000],['opacity',30,100]]) {
    s[key] = Math.min(max, Math.max(min, Number.isFinite(Number(s[key])) ? Number(s[key]) : DEFAULTS[key]));
  }
  for (const [key, values] of [['layout',['single','double']],['secondary',['next','translation']],['theme',['auto','light','dark','custom']]]) {
    if (!values.includes(s[key])) s[key] = DEFAULTS[key];
  }
  for (const key of ['enabled','hidePaused','hideFullscreen','scrollLyrics','progressMask']) s[key] = Boolean(s[key]);
  for (const key of ['primaryColor','secondaryColor','primaryPlayedColor','secondaryPlayedColor']) {
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
function makeFrame(snapshot, settings, now = Date.now()) {
  const p = snapshot.playback ?? {};
  const lyric = snapshot.lyric ?? {};
  const lines = lyric.lines ?? [];
  let ms = Number(p.currentTime || 0) * 1000;
  // 用宿主时间戳推算进度，暂停和跳转由下一份快照重新校准。
  if (p.isPlaying) ms += Math.max(0, now - Number(p.updatedAt || now)) * Number(p.playbackRate || 1);
  if (p.duration > 0) ms = Math.min(ms, p.duration * 1000);
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
    settings, text, secondary, playing: Boolean(p.isPlaying), hasTrack: Boolean(p.trackId || p.title),
    lyricKey: `${p.trackId || ''}:${index}`, timelineMs: ms,
    lineStartMs: canScroll ? startMs : 0, lineEndMs: canScroll ? endMs : 0,
    playbackRate: Number(p.playbackRate || 1),
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
const _hoisted_3 = { class: "tl-preview-card" }
const _hoisted_4 = { class: "tl-caption" }
const _hoisted_5 = {
  key: 0,
  class: "tl-preview-scrub"
}
const _hoisted_6 = { class: "tl-section" }
const _hoisted_7 = { class: "tl-grid" }
const _hoisted_8 = { class: "tl-field tl-span" }
const _hoisted_9 = { class: "tl-field" }
const _hoisted_10 = { class: "tl-field" }
const _hoisted_11 = { class: "tl-field tl-span" }
const _hoisted_12 = { class: "tl-field tl-span" }
const _hoisted_13 = { class: "tl-section" }
const _hoisted_14 = { class: "tl-type-grid" }
const _hoisted_15 = { class: "tl-type-card" }
const _hoisted_16 = { class: "tl-card-label" }
const _hoisted_17 = { class: "tl-color-row" }
const _hoisted_18 = { class: "tl-color-control" }
const _hoisted_19 = ["value", "onInput"]
const _hoisted_20 = {
  key: 0,
  class: "tl-color-row"
}
const _hoisted_21 = { class: "tl-color-control" }
const _hoisted_22 = ["value", "onInput"]
const _hoisted_23 = { class: "tl-card-label" }
const _hoisted_24 = { class: "tl-color-row" }
const _hoisted_25 = { class: "tl-color-control" }
const _hoisted_26 = ["value", "onInput"]
const _hoisted_27 = {
  key: 0,
  class: "tl-color-row"
}
const _hoisted_28 = { class: "tl-color-control" }
const _hoisted_29 = ["value", "onInput"]
const _hoisted_30 = { class: "tl-section" }
const _hoisted_31 = { class: "tl-grid" }
const _hoisted_32 = { class: "tl-field" }
const _hoisted_33 = { class: "tl-field" }
const _hoisted_34 = { class: "tl-field" }
const _hoisted_35 = { class: "tl-section" }
const _hoisted_36 = { class: "tl-toggle" }
const _hoisted_37 = { class: "tl-toggle" }
const _hoisted_38 = { class: "tl-toggle" }
const _hoisted_39 = { class: "tl-toggle" }
const _hoisted_40 = { class: "tl-footer" }
const _hoisted_41 = {
  role: "status",
  class: "tl-status"
}

return function render(_ctx, _cache) {

    const { createElementVNode: _createElementVNode, resolveComponent: _resolveComponent, createVNode: _createVNode, toDisplayString: _toDisplayString, normalizeStyle: _normalizeStyle, openBlock: _openBlock, createElementBlock: _createElementBlock, createCommentVNode: _createCommentVNode, normalizeClass: _normalizeClass, createTextVNode: _createTextVNode, withCtx: _withCtx } = _Vue

    const _component_Switch = _resolveComponent("Switch")
    const _component_Slider = _resolveComponent("Slider")
    const _component_Select = _resolveComponent("Select")
    const _component_InputNumber = _resolveComponent("InputNumber")
    const _component_Button = _resolveComponent("Button")

    return (_openBlock(), _createElementBlock("section", _hoisted_1, [
      _createElementVNode("header", _hoisted_2, [_cache[0] || (_cache[0] = _createElementVNode("div", null, [_createElementVNode("span", { class: "tl-eyebrow" }, "TASKBAR LYRICS"), _createElementVNode("h2", null, "微光 · 任务栏歌词")], -1)), _createVNode(_component_Switch, {
        modelValue: _ctx.draft.enabled,
        "onUpdate:modelValue": $event => ((_ctx.draft.enabled) = $event)
      }, null, 8, ["modelValue", "onUpdate:modelValue"])]),
      _createElementVNode("section", _hoisted_3, [_createElementVNode("div", _hoisted_4, [_cache[1] || (_cache[1] = _createElementVNode("span", null, "实时预览", -1)), _createElementVNode("span", null, _toDisplayString(_ctx.draft.layout === 'double' ? '双行' : '单行') + " · " + _toDisplayString(_ctx.draft.progressMask ? '进度遮罩' : '纯色文字'), 1)]), _createElementVNode("div", {
        class: _normalizeClass(["tl-preview", { 'tl-preview-light': _ctx.previewLight }]),
        style: _normalizeStyle({ fontFamily:_ctx.previewFont, opacity:_ctx.draft.opacity / 100 })
      }, [_createElementVNode("div", {
        class: "tl-preview-line",
        style: _normalizeStyle({ fontSize:_ctx.draft.primaryFontSize + 'px', color:_ctx.previewColors[0] })
      }, [_cache[2] || (_cache[2] = _createElementVNode("span", null, "让每一句，留在此刻", -1)), (_ctx.draft.progressMask)
        ? (_openBlock(), _createElementBlock("span", {
            key: 0,
            class: "tl-preview-played",
            style: _normalizeStyle({ color:_ctx.draft.primaryPlayedColor, clipPath:'inset(0 ' + (100-_ctx.previewProgress) + '% 0 0)' })
          }, "让每一句，留在此刻", 4))
        : _createCommentVNode("", true)], 4), (_ctx.draft.layout === 'double')
        ? (_openBlock(), _createElementBlock("div", {
            key: 0,
            class: "tl-preview-line tl-preview-secondary",
            style: _normalizeStyle({ fontSize:_ctx.draft.secondaryFontSize + 'px', color:_ctx.previewColors[1] })
          }, [_createElementVNode("span", null, _toDisplayString(_ctx.draft.secondary === 'next' ? '下一段旋律，正在靠近' : 'Keep this moment in a melody'), 1), (_ctx.draft.progressMask && _ctx.draft.secondary === 'translation')
            ? (_openBlock(), _createElementBlock("span", {
                key: 0,
                class: "tl-preview-played",
                style: _normalizeStyle({ color:_ctx.draft.secondaryPlayedColor, clipPath:'inset(0 ' + (100-_ctx.previewProgress) + '% 0 0)' })
              }, "Keep this moment in a melody", 4))
            : _createCommentVNode("", true)], 4))
        : _createCommentVNode("", true)], 6), (_ctx.draft.progressMask)
        ? (_openBlock(), _createElementBlock("div", _hoisted_5, [_cache[3] || (_cache[3] = _createElementVNode("span", null, "预览进度", -1)), _createVNode(_component_Slider, {
            modelValue: _ctx.previewProgress,
            "onUpdate:modelValue": $event => ((_ctx.previewProgress) = $event),
            min: 0,
            max: 100,
            step: 1
          }, null, 8, ["modelValue", "onUpdate:modelValue", "min", "max", "step"]), _createElementVNode("span", null, _toDisplayString(_ctx.previewProgress) + "%", 1)]))
        : _createCommentVNode("", true)]),
      _createElementVNode("section", _hoisted_6, [_cache[10] || (_cache[10] = _createElementVNode("h3", null, "显示方式", -1)), _createElementVNode("div", _hoisted_7, [
        _createElementVNode("div", _hoisted_8, [_cache[4] || (_cache[4] = _createElementVNode("label", null, "显示屏幕", -1)), _createVNode(_component_Select, {
          modelValue: _ctx.draft.display,
          "onUpdate:modelValue": $event => ((_ctx.draft.display) = $event),
          options: _ctx.displayOptions
        }, null, 8, ["modelValue", "onUpdate:modelValue", "options"]), _cache[5] || (_cache[5] = _createElementVNode("p", { class: "tl-note" }, "默认主屏幕；全部屏幕同步显示。指定屏幕断开时自动使用主屏幕。", -1))]),
        _createElementVNode("div", _hoisted_9, [_cache[6] || (_cache[6] = _createElementVNode("label", null, "歌词行数", -1)), _createVNode(_component_Select, {
          modelValue: _ctx.draft.layout,
          "onUpdate:modelValue": $event => ((_ctx.draft.layout) = $event),
          options: _ctx.layoutOptions
        }, null, 8, ["modelValue", "onUpdate:modelValue", "options"])]),
        _createElementVNode("div", _hoisted_10, [_cache[7] || (_cache[7] = _createElementVNode("label", null, "第二行内容", -1)), _createVNode(_component_Select, {
          modelValue: _ctx.draft.secondary,
          "onUpdate:modelValue": $event => ((_ctx.draft.secondary) = $event),
          options: _ctx.secondaryOptions,
          disabled: _ctx.draft.layout === 'single'
        }, null, 8, ["modelValue", "onUpdate:modelValue", "options", "disabled"])]),
        _createElementVNode("div", _hoisted_11, [_cache[8] || (_cache[8] = _createElementVNode("label", null, "歌词字体", -1)), _createVNode(_component_Select, {
          modelValue: _ctx.draft.fontFamily,
          "onUpdate:modelValue": $event => ((_ctx.draft.fontFamily) = $event),
          options: _ctx.fontOptions,
          filterable: ""
        }, null, 8, ["modelValue", "onUpdate:modelValue", "options"])]),
        _createElementVNode("div", _hoisted_12, [_cache[9] || (_cache[9] = _createElementVNode("label", null, "配色方式", -1)), _createVNode(_component_Select, {
          modelValue: _ctx.draft.theme,
          "onUpdate:modelValue": $event => ((_ctx.draft.theme) = $event),
          options: _ctx.themeOptions
        }, null, 8, ["modelValue", "onUpdate:modelValue", "options"])])
      ])]),
      _createElementVNode("section", _hoisted_13, [_cache[19] || (_cache[19] = _createElementVNode("h3", null, "每行文字", -1)), _createElementVNode("div", _hoisted_14, [_createElementVNode("div", _hoisted_15, [
        _createElementVNode("div", _hoisted_16, [_cache[11] || (_cache[11] = _createElementVNode("span", { class: "tl-dot" }, "1", -1)), _cache[12] || (_cache[12] = _createElementVNode("h4", null, "第一行", -1)), _createElementVNode("span", null, _toDisplayString(_ctx.draft.primaryFontSize) + " px", 1)]),
        _createVNode(_component_Slider, {
          modelValue: _ctx.draft.primaryFontSize,
          "onUpdate:modelValue": $event => ((_ctx.draft.primaryFontSize) = $event),
          min: 10,
          max: 36,
          step: 1
        }, null, 8, ["modelValue", "onUpdate:modelValue", "min", "max", "step"]),
        _createElementVNode("div", _hoisted_17, [_cache[13] || (_cache[13] = _createElementVNode("label", null, "文字颜色", -1)), _createElementVNode("label", _hoisted_18, [_createElementVNode("input", {
          type: "color",
          value: _ctx.draft.primaryColor,
          onInput: $event => (_ctx.setColor('primaryColor', $event))
        }, null, 40, _hoisted_19), _createElementVNode("span", null, _toDisplayString(_ctx.draft.primaryColor), 1)])]),
        (_ctx.draft.progressMask)
          ? (_openBlock(), _createElementBlock("div", _hoisted_20, [_cache[14] || (_cache[14] = _createElementVNode("label", null, "已播放颜色", -1)), _createElementVNode("label", _hoisted_21, [_createElementVNode("input", {
              type: "color",
              value: _ctx.draft.primaryPlayedColor,
              onInput: $event => (_ctx.setColor('primaryPlayedColor', $event))
            }, null, 40, _hoisted_22), _createElementVNode("span", null, _toDisplayString(_ctx.draft.primaryPlayedColor), 1)])]))
          : _createCommentVNode("", true)
      ]), _createElementVNode("div", { class: _normalizeClass(["tl-type-card", { 'tl-muted': _ctx.draft.layout === 'single' }]) }, [
        _createElementVNode("div", _hoisted_23, [_cache[15] || (_cache[15] = _createElementVNode("span", { class: "tl-dot" }, "2", -1)), _cache[16] || (_cache[16] = _createElementVNode("h4", null, "第二行", -1)), _createElementVNode("span", null, _toDisplayString(_ctx.draft.secondaryFontSize) + " px", 1)]),
        _createVNode(_component_Slider, {
          modelValue: _ctx.draft.secondaryFontSize,
          "onUpdate:modelValue": $event => ((_ctx.draft.secondaryFontSize) = $event),
          min: 8,
          max: 36,
          step: 1
        }, null, 8, ["modelValue", "onUpdate:modelValue", "min", "max", "step"]),
        _createElementVNode("div", _hoisted_24, [_cache[17] || (_cache[17] = _createElementVNode("label", null, "文字颜色", -1)), _createElementVNode("label", _hoisted_25, [_createElementVNode("input", {
          type: "color",
          value: _ctx.draft.secondaryColor,
          onInput: $event => (_ctx.setColor('secondaryColor', $event))
        }, null, 40, _hoisted_26), _createElementVNode("span", null, _toDisplayString(_ctx.draft.secondaryColor), 1)])]),
        (_ctx.draft.progressMask)
          ? (_openBlock(), _createElementBlock("div", _hoisted_27, [_cache[18] || (_cache[18] = _createElementVNode("label", null, "已播放颜色", -1)), _createElementVNode("label", _hoisted_28, [_createElementVNode("input", {
              type: "color",
              value: _ctx.draft.secondaryPlayedColor,
              onInput: $event => (_ctx.setColor('secondaryPlayedColor', $event))
            }, null, 40, _hoisted_29), _createElementVNode("span", null, _toDisplayString(_ctx.draft.secondaryPlayedColor), 1)])]))
          : _createCommentVNode("", true)
      ], 2)]), _cache[20] || (_cache[20] = _createElementVNode("p", { class: "tl-note" }, "预览实时反映字号、字体与颜色。任务栏高度不足时，两行按比例缩小。", -1))]),
      _createElementVNode("section", _hoisted_30, [_cache[24] || (_cache[24] = _createElementVNode("h3", null, "位置与透明度", -1)), _createElementVNode("div", _hoisted_31, [_createElementVNode("div", _hoisted_32, [_createElementVNode("label", null, [_cache[21] || (_cache[21] = _createTextVNode("显示宽度 ", -1)), _createElementVNode("span", null, _toDisplayString(_ctx.draft.width) + " px", 1)]), _createVNode(_component_Slider, {
        modelValue: _ctx.draft.width,
        "onUpdate:modelValue": $event => ((_ctx.draft.width) = $event),
        min: 120,
        max: 900,
        step: 10
      }, null, 8, ["modelValue", "onUpdate:modelValue", "min", "max", "step"])]), _createElementVNode("div", _hoisted_33, [_createElementVNode("label", null, [_cache[22] || (_cache[22] = _createTextVNode("不透明度 ", -1)), _createElementVNode("span", null, _toDisplayString(_ctx.draft.opacity) + "%", 1)]), _createVNode(_component_Slider, {
        modelValue: _ctx.draft.opacity,
        "onUpdate:modelValue": $event => ((_ctx.draft.opacity) = $event),
        min: 30,
        max: 100,
        step: 1
      }, null, 8, ["modelValue", "onUpdate:modelValue", "min", "max", "step"])]), _createElementVNode("div", _hoisted_34, [_cache[23] || (_cache[23] = _createElementVNode("label", null, "距任务栏左侧", -1)), _createVNode(_component_InputNumber, {
        modelValue: _ctx.draft.offset,
        "onUpdate:modelValue": $event => ((_ctx.draft.offset) = $event),
        min: 0,
        max: 2000,
        step: 1
      }, null, 8, ["modelValue", "onUpdate:modelValue", "min", "max", "step"])])])]),
      _createElementVNode("section", _hoisted_35, [
        _cache[29] || (_cache[29] = _createElementVNode("h3", null, "播放与行为", -1)),
        _createElementVNode("div", _hoisted_36, [_cache[25] || (_cache[25] = _createElementVNode("div", null, [_createElementVNode("span", null, "歌词进度遮罩"), _createElementVNode("p", null, "已播放文字逐渐显色，有逐字时间时按逐字进度显示。")], -1)), _createVNode(_component_Switch, {
          modelValue: _ctx.draft.progressMask,
          "onUpdate:modelValue": $event => ((_ctx.draft.progressMask) = $event)
        }, null, 8, ["modelValue", "onUpdate:modelValue"])]),
        _createElementVNode("div", _hoisted_37, [_cache[26] || (_cache[26] = _createElementVNode("div", null, [_createElementVNode("span", null, "长歌词随进度滚动"), _createElementVNode("p", null, "暂停时停止，跳转后重新对齐。")], -1)), _createVNode(_component_Switch, {
          modelValue: _ctx.draft.scrollLyrics,
          "onUpdate:modelValue": $event => ((_ctx.draft.scrollLyrics) = $event)
        }, null, 8, ["modelValue", "onUpdate:modelValue"])]),
        _createElementVNode("div", _hoisted_38, [_cache[27] || (_cache[27] = _createElementVNode("span", null, "暂停时隐藏", -1)), _createVNode(_component_Switch, {
          modelValue: _ctx.draft.hidePaused,
          "onUpdate:modelValue": $event => ((_ctx.draft.hidePaused) = $event)
        }, null, 8, ["modelValue", "onUpdate:modelValue"])]),
        _createElementVNode("div", _hoisted_39, [_cache[28] || (_cache[28] = _createElementVNode("span", null, "全屏应用时隐藏", -1)), _createVNode(_component_Switch, {
          modelValue: _ctx.draft.hideFullscreen,
          "onUpdate:modelValue": $event => ((_ctx.draft.hideFullscreen) = $event)
        }, null, 8, ["modelValue", "onUpdate:modelValue"])])
      ]),
      _createElementVNode("footer", _hoisted_40, [_createVNode(_component_Button, {
        onClick: _ctx.save,
        disabled: _ctx.busy
      }, {
        default: _withCtx(() => [_createTextVNode(_toDisplayString(_ctx.busy ? '正在应用…' : '保存并应用'), 1)]),
        _: 1
      }, 8, ["onClick", "disabled"]), _createVNode(_component_Button, {
        variant: "outline",
        onClick: _ctx.restart,
        disabled: _ctx.busy
      }, {
        default: _withCtx(() => [...(_cache[30] || (_cache[30] = [_createTextVNode("重新连接", -1)]))]),
        _: 1
      }, 8, ["onClick", "disabled"]), _createVNode(_component_Button, {
        variant: "ghost",
        onClick: _ctx.reset,
        disabled: _ctx.busy
      }, {
        default: _withCtx(() => [...(_cache[31] || (_cache[31] = [_createTextVNode("恢复默认", -1)]))]),
        _: 1
      }, 8, ["onClick", "disabled"])]),
      _createElementVNode("p", _hoisted_41, _toDisplayString(_ctx.status), 1)
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
  const displays = ctx.vue.ref([]);
  const token = crypto.randomUUID();
  ctx.dispose(ctx.nowPlaying.onSnapshot(next => { snapshot = next; }));
  const server = await ctx.webServer.listen(request => {
    if (!alive || request.query.token !== token) return { status: 403, body: '' };
    // 原生程序报告实际屏幕，设置页使用相同的播放器下拉组件。
    if (request.path === '/displays') {
      try { const list=JSON.parse(request.query.data); if (!Array.isArray(list) || list.length>32) throw new Error('屏幕列表无效'); displays.value=list; }
      catch { return { status:400,body:'' }; }
      return { body:'ok' };
    }
    if (request.path !== '/state') return { status:404,body:'' };
    return { headers: { 'content-type':'application/json; charset=utf-8', 'cache-control':'no-store' }, body: JSON.stringify(makeFrame(snapshot, settings)) };
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
  ctx.dispose(async () => { alive = false; if (pid) await ctx.process.terminate(pid); });
  const panel = ctx.vue.defineComponent({
    name:'TaskbarLyricsSettings',
    components: Object.fromEntries(['Select','Slider','Switch','Button','InputNumber'].map(name => [name,ctx.vue.defineAsyncComponent(ctx.ui.components[name])])),
    render:createPanelRender(ctx.vue),
    setup() {
      const draft = ctx.vue.reactive({ ...settings });
      const busy = ctx.vue.ref(false);
      const status = ctx.vue.ref('');
      const fontOptions = ctx.vue.ref([]);
      const previewProgress = ctx.vue.ref(55);
      ctx.fonts.getOptions({ includeFollow:true }).then(options => { fontOptions.value=options; }).catch(error => { status.value=error.message; });
      const displayOptions=ctx.vue.computed(() => [{label:'主屏幕（默认）',value:'primary'},{label:'全部屏幕',value:'all'},...displays.value.map(item => ({label:item.label,value:item.id})),...(!['primary','all'].includes(draft.display) && !displays.value.some(item => item.id===draft.display) ? [{label:'已断开的屏幕 · 自动使用主屏幕',value:draft.display}] : [])]);
      const layoutOptions=[{label:'单行歌词',value:'single'},{label:'双行歌词',value:'double'}];
      const secondaryOptions=[{label:'下一句歌词',value:'next'},{label:'翻译／音译',value:'translation'}];
      const themeOptions=[{label:'跟随 Windows 任务栏',value:'auto'},{label:'自定义文字颜色',value:'custom'}];
      const previewFont = ctx.vue.computed(() => draft.fontFamily === 'follow' ? snapshot.appearance?.fontFamily || ctx.fonts.buildFamily('system-ui') : ctx.fonts.buildFamily(draft.fontFamily));
      const previewLight = ctx.vue.computed(() => draft.theme === 'custom' ? (parseInt(draft.primaryColor.slice(1,3),16)+parseInt(draft.primaryColor.slice(3,5),16)+parseInt(draft.primaryColor.slice(5,7),16))/3 < 110 : matchMedia('(prefers-color-scheme: light)').matches);
      const previewColors = ctx.vue.computed(() => draft.theme === 'custom' ? [draft.primaryColor,draft.secondaryColor] : previewLight.value ? ['#202D35','#62737B'] : ['#F1F7F6','#B4C3C1']);
      const setColor = (key,event) => { draft[key]=event.target.value; if (!key.includes('Played')) draft.theme='custom'; };
      const run = async action => {
        busy.value = true;
        try { await action(); status.value = '已应用'; }
        catch (error) { status.value = error.message; }
        finally { busy.value = false; }
      };
      const save = () => run(async () => {
        settings = normalize(draft);
        Object.assign(draft, settings);
        await ctx.storage.set('settings', settings);
        if (settings.enabled && !pid) await start();
      });
      const reset = () => { Object.assign(draft, DEFAULTS); return save(); };
      return { draft, busy, status, save, reset, fontOptions, previewProgress, previewFont, previewColors, previewLight, layoutOptions, secondaryOptions, themeOptions, displayOptions, setColor, restart: () => run(start) };
    }
  });
  ctx.ui.settings.define({ title:'任务栏歌词 · 微光', component:panel });
  if (settings.enabled) {
    try { await start(); }
    catch (error) { ctx.toast.warning(error.message); }
  }
}
