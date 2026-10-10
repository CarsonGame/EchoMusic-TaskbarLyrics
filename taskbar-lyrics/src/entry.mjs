import { DEFAULTS, normalize, makeFrame, coverAccentFromPixels, previewPalettes, formatPlaybackTime } from './core.mjs';
// @compiled-panel
export async function activate(ctx) {
  if (ctx.electron.platform !== 'win32') throw new Error('此插件仅支持 Windows。');
  let settings = normalize(await ctx.storage.get('settings') || DEFAULTS);
  let snapshot = await ctx.nowPlaying.getSnapshot();
  let alive = true;
  let pid = 0;
  let launchPending = false;
  let commandBusy = false;
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
    const identity=`${next.playback?.trackId || ''}:${next.playback?.coverUrl || ''}`;
    // 切歌先保留上次成功颜色，等新封面取色完成后再替换。
    if (identity!==coverIdentity) { coverIdentity=identity;coverRevision++;cachedCover=undefined; }
    snapshot = next; snapshotRef.value = next;
    ensureCoverAccent();
    for (const waiter of commandWaiters) waiter(next);
  };
  const currentFrame = () => {
    const frame={...makeFrame(snapshot,settings,Date.now(),coverAccent.value),commandBusy};
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
    if((settings.playedColorSource!=='cover' && settings.controlsBackgroundSource!=='cover' && settings.controlsIconColorSource!=='cover' && !preview) || accentRevision===coverRevision || !snapshot.playback?.coverUrl)return;
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
      const previewPalette=ctx.vue.computed(() => previewPalettes(draft,coverAccent.value,previewLight.value));
      const previewPlayed=ctx.vue.computed(() => previewPalette.value.played);
      const previewControls=ctx.vue.computed(() => previewPalette.value.controls);
      // 未保存的取色选项也可立即预览，关闭时保留已有手动颜色。
      ctx.vue.watch(()=>[draft.playedColorSource,draft.controlsBackgroundSource,draft.controlsIconColorSource,snapshotRef.value.playback?.trackId,snapshotRef.value.playback?.coverUrl],([lyric,background,icons])=>{if(lyric==='cover' || background==='cover' || icons==='cover')ensureCoverAccent(true);});
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
      return { draft, transparency, backgroundTransparency, busy, status, save, reset, fontOptions, previewProgress, previewElapsed, previewDuration, previewFont, previewColors, previewLight, previewPlayed, previewControls, playedColorOptions, controlsBackgroundOptions, layoutOptions, secondaryOptions, themeOptions, displayOptions, coverShapeOptions, controlsAlignmentOptions, previewCover, previewCoverPrevious, previewCoverLoaded, onPreviewCoverLoad, onPreviewCoverError, onPreviewCoverHover, previewTitle, previewArtist, previewPlaying, previewFavorite, coverFailed, setColor, restart: () => run(start) };
    }
  });
  ctx.ui.settings.define({ title:'任务栏歌词 · 微光', component:panel });
  if (settings.enabled) {
    try { await start(); }
    catch (error) { ctx.toast.warning(error.message); }
  }
}
