import test from 'node:test';
import assert from 'node:assert/strict';
import {createMaintenance} from '../src/maintenance.mjs';

function fixture(overrides={}) {
  const calls=[],timers=new Map();let sequence=0;
  const controller=createMaintenance({
    stop:async()=>calls.push('stop'),start:async()=>calls.push('start'),enabled:()=>true,
    resolveToggle:async()=>async enabled=>calls.push(enabled?'enable':'disable'),
    uuid:()=>`lease-${++sequence}`,schedule:(callback,delay)=>{const id=++sequence;timers.set(id,{callback,delay});return id;},cancel:id=>timers.delete(id),
    warn:message=>calls.push(message),...overrides
  });
  const request=async(action,ticket='',mode='native',method='POST')=>controller.handle({method,query:{action,ticket,mode}});
  const prepare=async(mode='native')=>JSON.parse((await request('prepare','',mode)).body);
  return {controller,calls,timers,request,prepare};
}

test('更新先释放程序，租约提交后恢复，拒绝并发及错误票据',async()=>{
  const f=fixture();const session=await f.prepare();
  assert.deepEqual(f.calls,['stop']);assert.equal(f.controller.active,true);
  assert.equal((await f.request('prepare')).status,409);
  assert.equal((await f.request('commit','wrong')).status,409);
  assert.equal((await f.request('commit',session.ticket)).status,202);
  assert.deepEqual(f.calls,['stop']);assert.equal(f.controller.active,true);
  await [...f.timers.values()].find(timer=>timer.delay===150).callback();
  assert.deepEqual(f.calls,['stop','start']);assert.equal(f.controller.active,false);assert.equal(f.timers.size,0);
});
test('GET 不能停止或恢复进程，非法模式和未知操作不执行',async()=>{
  const f=fixture();assert.equal((await f.request('prepare','','native','GET')).status,405);
  assert.equal((await f.request('prepare','','unknown')).status,400);
  assert.equal((await f.request('execute')).status,400);assert.deepEqual(f.calls,[]);
});
test('文件复制失败可取消；控制端退出后租约自动恢复歌词',async()=>{
  const f=fixture();const session=await f.prepare();await f.request('abort',session.ticket);
  assert.deepEqual(f.calls,['stop','start']);await f.prepare();
  const timer=[...f.timers.values()].find(timer=>timer.delay===90000);timer.callback();
  await new Promise(resolve=>setImmediate(resolve));
  assert.deepEqual(f.calls,['stop','start','stop','start']);assert.equal(f.controller.active,false);
});
test('插件被用户关闭后，旧租约不得自动启动它',async()=>{
  const f=fixture();await f.prepare();f.controller.dispose();
  assert.equal(f.timers.size,0);assert.equal((await f.request('prepare')).status,403);assert.deepEqual(f.calls,['stop']);
});
test('完整重载先确认播放器支持，失败时不停止原程序',async()=>{
  const f=fixture({resolveToggle:async()=>{throw new Error('unsupported');}});
  assert.equal((await f.request('prepare','','plugin')).status,502);assert.deepEqual(f.calls,[]);assert.equal(f.controller.active,false);
});
test('完整重载在响应后执行；停用异常仍尝试重新启用',async()=>{
  const calls=[];const f=fixture({resolveToggle:async()=>async enabled=>{calls.push(enabled);if(!enabled)throw new Error('stop error');}});
  const session=await f.prepare('plugin');assert.equal((await f.request('commit',session.ticket,'plugin')).status,202);
  assert.deepEqual(calls,[]);
  await [...f.timers.values()].find(timer=>timer.delay===150).callback();
  assert.deepEqual(calls,[false,true]);
});
test('恢复失败保留租约，允许回滚后再次恢复',async()=>{
  let failing=true;const f=fixture({start:async()=>{if(failing)throw new Error('launch failed');}});
  const session=await f.prepare();assert.equal((await f.request('commit',session.ticket)).status,202);
  await [...f.timers.values()].find(timer=>timer.delay===150).callback();
  assert.equal(f.controller.active,true);failing=false;assert.equal((await f.request('abort',session.ticket)).status,200);assert.equal(f.controller.active,false);
});

test('实际打包入口校验令牌，自动暂停恢复不会调用播放器启停或影响播放',async()=>{
  const {activate}=await import('../index.js');let handler,resumed;let pid=100;const launches=[],stops=[],disposals=[];const ref=value=>({value});
  const restarted=new Promise(resolve=>{resumed=resolve;});
  const ctx={electron:{platform:'win32'},storage:{get:async()=>({})},nowPlaying:{getSnapshot:async()=>({playback:{trackId:'song'}}),onSnapshot:()=>()=>{}},
    vue:{ref,shallowRef:ref,unref:value=>value?.value??value,computed:fn=>({get value(){return fn();}}),defineComponent:value=>value,defineAsyncComponent:value=>value},
    player:{},dispose:fn=>disposals.push(fn),ui:{components:{},settings:{define(){}}},toast:{warning(){}},
    process:{launch:async options=>{launches.push(options);if(launches.length===2)resumed();return{ok:true,pid:++pid};},terminate:async id=>{stops.push(id);return{ok:true};}},
    webServer:{listen:async callback=>{handler=callback;return{ok:true,origin:'http://127.0.0.1:11111'};}}};
  await activate(ctx);const token=new URL(launches[0].args[0]).searchParams.get('token');
  const request=(action,ticket='',valid=token)=>handler({method:'POST',path:'/maintenance',query:{token:valid,action,ticket,mode:'native'}});
  assert.equal((await request('prepare','','wrong')).status,403);assert.deepEqual(stops,[]);
  const session=JSON.parse((await request('prepare')).body);assert.deepEqual(stops,[101]);assert.equal(launches.length,1);
  assert.equal((await request('commit',session.ticket)).status,202);assert.equal(launches.length,1);
  await restarted;await new Promise(resolve=>setImmediate(resolve));assert.equal(launches.length,2);
  for(const dispose of disposals.reverse())await dispose();assert.deepEqual(stops,[101,102]);
});

test('启动暂时无响应也不阻塞更新请求，重新加载可取消排队的旧恢复',async()=>{
  const f=fixture({start:()=>new Promise(()=>{})});const session=await f.prepare();
  assert.equal((await f.request('commit',session.ticket)).status,202);
  assert.equal((await f.request('status','','native','GET')).status,200);
  f.controller.dispose();assert.equal(f.timers.size,0);assert.deepEqual(f.calls,['stop']);
});
