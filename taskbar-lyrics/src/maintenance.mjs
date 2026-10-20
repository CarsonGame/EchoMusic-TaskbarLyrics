// 只使用已经加载的播放器模块；找不到单插件启停能力时，先失败再停进程。
export async function resolvePluginToggle(ctx, environment = globalThis) {
  if (typeof ctx.electron?.plugins?.setEnabled !== 'function') throw new Error('播放器未提供插件启停接口');
  const base = new URL(environment.document.baseURI);
  const resources = [
    ...environment.performance.getEntriesByType('resource').map(entry => entry.name),
    ...Array.from(environment.document.querySelectorAll('link[rel="modulepreload"],script[src]'), node => node.href || node.src)
  ];
  const urls = [...new Set(resources)].filter(value => {
    try {
      const url = new URL(value, base);
      return url.protocol === base.protocol && url.host === base.host && url.pathname.startsWith(new URL('assets/', base).pathname) && url.pathname.endsWith('.js');
    } catch { return false; }
  }).sort((a,b) => Number(/\/style-/.test(b))-Number(/\/style-/.test(a)));
  for (const url of urls) {
    const module = await import(/* @vite-ignore */ url);
    const candidates = [...new Set(Object.values(module))].filter(value => {
      if (typeof value !== 'function' || value.length !== 2) return false;
      const source = Function.prototype.toString.call(value);
      return /window\.electron\.plugins\??\.setEnabled\(/.test(source) && source.includes('await');
    });
    if (candidates.length === 1) return enabled => candidates[0](ctx.id || 'taskbar-lyrics', enabled);
  }
  throw new Error('当前播放器版本未找到单插件重新加载能力，更新尚未开始');
}

export function createMaintenance({stop, start, enabled, resolveToggle, schedule = setTimeout, cancel = clearTimeout, uuid = () => crypto.randomUUID(), warn = () => {}}) {
  let phase='idle', ticket='', timer, disposed=false, toggle, mode='native';
  const reply=(status,body)=>({status,headers:{'content-type':'application/json','cache-control':'no-store'},body:JSON.stringify(body)});
  const clear=()=>{cancel(timer);timer=undefined;};
  const reset=()=>{clear();phase='idle';ticket='';toggle=undefined;};
  const resume=async()=>{if(!disposed && enabled())await start();};
  const arm=()=>{
    clear();
    timer=schedule(()=>{
      if(disposed || phase!=='prepared')return;
      phase='recovering';
      resume().catch(error=>warn('更新等待超时，歌词恢复失败：'+error.message)).finally(reset);
    },90000);
  };
  const handle=async request=>{
    const action=request.query.action;
    if(action==='status' && request.method==='GET')return reply(200,{ok:true,phase});
    if(request.method!=='POST')return reply(405,{error:'更新操作仅接受 POST'});
    if(disposed)return reply(403,{error:'插件已经关闭'});
    if(action==='prepare'){
      if(phase!=='idle')return reply(409,{error:'已有更新正在进行'});
      if(!['native','plugin'].includes(request.query.mode))return reply(400,{error:'未知更新模式'});
      phase='preparing';mode=request.query.mode;
      let stopped=false;
      try {
        if(mode==='plugin')toggle=await resolveToggle();
        await stop();
        stopped=true;
        ticket=uuid();phase='prepared';arm();
        return reply(200,{ok:true,ticket,mode,leaseMs:90000});
      }catch(error){
        try { if(stopped)await resume(); }catch(recovery){warn(recovery.message);}
        reset();return reply(502,{error:error.message});
      }
    }
    if(!['renew','commit','abort'].includes(action))return reply(400,{error:'未知更新操作'});
    if(phase!=='prepared' || request.query.ticket!==ticket)return reply(409,{error:'更新会话已经失效'});
    if(action==='renew'){arm();return reply(200,{ok:true,leaseMs:90000});}
    clear();phase=action==='commit'?'committing':'recovering';
    if(action==='commit' && mode==='plugin'){
      const reload=toggle;
      // 响应送达后再销毁旧插件上下文；启用逻辑独立完成，不随旧上下文清理。
      schedule(async()=>{
        try { await reload(false); }
        catch(error){warn('停止插件时发生错误：'+error.message);}
        finally {
          for(let attempt=0;attempt<3;attempt++){
            try { await reload(true);return; }
            catch(error){if(attempt===2)warn('插件自动恢复失败：'+error.message);else await new Promise(resolve=>schedule(resolve,500));}
          }
        }
      },150);
      return reply(202,{ok:true,reloading:true});
    }
    if(action==='commit'){
      // 先结束更新请求再启动程序，避免启动 IPC 阻塞当前 Web 请求。
      timer=schedule(async()=>{
        if(disposed)return;
        try {await resume();reset();}
        catch(error){phase='prepared';arm();warn('歌词恢复失败：'+error.message);}
      },150);
      return reply(202,{ok:true,restarting:true});
    }
    try {await resume();reset();return reply(200,{ok:true});}
    catch(error){phase='prepared';arm();return reply(502,{error:error.message});}
  };
  return {handle,get active(){return phase!=='idle';},dispose(){disposed=true;clear();}};
}
