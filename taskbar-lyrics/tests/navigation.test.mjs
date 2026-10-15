import test from 'node:test';
import assert from 'node:assert/strict';
import {informationNavigation,createInformationNavigator} from '../src/navigation.mjs';
const snapshot={playback:{trackId:'song-1',title:'夜曲 & 星光',artist:'甲 / 乙'}};
const track={id:'song-1',artists:[{id:11,name:'甲'},{id:'22',name:'乙'}]};
const settings={enabled:true,hoverSongInfo:true,noticeShowArtist:true};
const request=(query={})=>({method:'POST',query:{trackId:'song-1',kind:'song',...query}});
function fixture(current={snapshot,track,settings}, search=async()=>({data:{lists:[]}})){
  const routes=[],shows=[],lyrics=[],warnings=[],searches=[];
  const ctx={router:{push:async route=>routes.push(route)},host:{showOnTop:async(...args)=>{shows.push(args);return {ok:true};}},player:{toggleLyricView:async value=>lyrics.push(value)},toast:{warning:value=>warnings.push(value)},kugou:{search:{search:async(...args)=>{searches.push(args);return search(...args);}}}};
  return {navigate:createInformationNavigator(ctx,async()=>current),routes,shows,lyrics,warnings,searches,ctx};
}
test('优先保留独立歌手 ID；乐队名中的分隔符不拆分',()=>{
  const info=informationNavigation(snapshot,{...track,artists:[{id:123,name:'AC/DC'},{id:456,name:'Earth, Wind & Fire'}]});
  assert.deepEqual(info.artists,[{name:'AC/DC',id:'123'},{name:'Earth, Wind & Fire',id:'456'}]);
});
test('旧快照拆分常见合作分隔符，去重并忽略其他歌曲的歌手 ID',()=>{
  for(const artist of ['甲/乙','甲、乙','甲，乙','甲 & 乙','甲 feat. 乙','甲 / 乙 / 甲'])assert.deepEqual(informationNavigation({playback:{...snapshot.playback,artist}},{...track,id:'other'}).artists,[{name:'甲',id:''},{name:'乙',id:''}]);
  assert.deepEqual(informationNavigation(snapshot,{id:'song-1',singers:track.artists}).artists,[{name:'甲',id:'11'},{name:'乙',id:'22'}]);
});
test('歌名进入歌曲搜索并恢复主窗口、收起歌词页，不操作播放',async()=>{
  const f=fixture();assert.equal((await f.navigate(request())).status,undefined);
  assert.deepEqual(f.routes,[{name:'search',query:{q:'夜曲 & 星光',tab:'song'}}]);
  assert.deepEqual(f.shows,[['main',{focus:true}]]);assert.deepEqual(f.lyrics,[false]);assert.equal(f.searches.length,0);
});
test('合作歌曲各位歌手分别跳转自己的详情页',async()=>{
  const f=fixture();for(const [index,artist,id] of [[0,'甲','11'],[1,'乙','22']]){
    await f.navigate(request({kind:'artist',index:String(index),artist}));
    assert.deepEqual(f.routes.at(-1),{name:'artist-detail',params:{id}});
  }assert.equal(f.searches.length,0);
});
test('缺少歌手 ID 时，仅唯一同名搜索结果可跳详情',async()=>{
  const f=fixture({snapshot,track:{id:'song-1'},settings},async()=>({data:{lists:[{author_id:42,author_name:'甲'},{author_id:5,author_name:'甲某'}]}}));
  await f.navigate(request({kind:'artist',index:'0',artist:'甲'}));
  assert.deepEqual(f.routes,[{name:'artist-detail',params:{id:'42'}}]);assert.deepEqual(f.searches,[['甲','author',1,30]]);
});
test('同名歧义、无结果或查询失败回退歌手搜索，不猜测错误歌手',async()=>{
  for(const search of [async()=>({data:{lists:[{id:1,name:'甲'},{id:2,name:'甲'}]}}),async()=>({data:{lists:[]}}),async()=>{throw new Error('offline');}]){
    const f=fixture({snapshot,track:{id:'song-1'},settings},search);
    await f.navigate(request({kind:'artist',index:'0',artist:'甲'}));
    assert.deepEqual(f.routes,[{name:'search',query:{q:'甲',tab:'author'}}]);
  }
});
test('拒绝旧歌曲、被隐藏的信息、错误方法和无效歌手',async()=>{
  const f=fixture();
  for(const [req,status] of [[{...request(),method:'GET'},405],[request({kind:'external'}),400],[request({trackId:'old'}),409],[request({kind:'artist',index:'1',artist:'甲'}),409],[request({kind:'artist',index:'9',artist:'乙'}),409]])assert.equal((await f.navigate(req)).status,status);
  for(const config of [{...settings,enabled:false},{...settings,hoverSongInfo:false},{...settings,noticeShowArtist:false}])assert.equal((await fixture({snapshot,track,settings:config}).navigate(request({kind:'artist',index:'0',artist:'甲'}))).status,403);
  assert.deepEqual(f.routes,[]);assert.deepEqual(f.shows,[]);
});
test('跨屏重复点击合并，失败释放锁且不会改变播放状态',async()=>{
  let release;const f=fixture();f.ctx.router.push=()=>new Promise(resolve=>release=resolve);
  const first=f.navigate(request());await new Promise(resolve=>setImmediate(resolve));
  assert.equal((await f.navigate(request())).status,409);release();await first;
  f.ctx.router.push=async()=>{throw new Error('路由失败');};assert.equal((await f.navigate(request())).status,502);assert.equal((await f.navigate(request())).status,502);
  assert.deepEqual(f.warnings,['路由失败','路由失败']);
});

test('实际打包入口传输独立歌手信息，并校验导航接口令牌',async()=>{
  const {activate}=await import('../index.js');
  let handler,launch;const disposals=[],routes=[];
  const ref=value=>({value});
  const ctx={
    electron:{platform:'win32'},storage:{get:async()=>({...settings})},
    nowPlaying:{getSnapshot:async()=>snapshot,onSnapshot:()=>()=>{}},
    vue:{ref,shallowRef:ref,unref:value=>value?.value??value,computed:read=>({get value(){return read();}}),defineComponent:value=>value,defineAsyncComponent:value=>value},
    player:{currentTrack:ref(track),toggleLyricView:async()=>{}},
    dispose:fn=>disposals.push(fn),
    ui:{components:{},settings:{define:()=>{}}},
    process:{launch:async options=>{launch=options;return {ok:true,pid:123};},terminate:async()=>{}},
    webServer:{listen:async callback=>{handler=callback;return {ok:true,origin:'http://127.0.0.1:11111'};}},
    toast:{warning:message=>{throw new Error(message);}},router:{push:async value=>routes.push(value)},host:{showOnTop:async()=>({ok:true})}
  };
  await activate(ctx);
  const token=new URL(launch.args[0]).searchParams.get('token');
  assert.equal((await handler({method:'GET',path:'/state',query:{token:'wrong'}})).status,403);
  const state=JSON.parse((await handler({method:'GET',path:'/state',query:{token}})).body);
  assert.deepEqual(state.navigation.artists,[{name:'甲',id:'11'},{name:'乙',id:'22'}]);
  await handler({...request({kind:'artist',index:'1',artist:'乙',token}),path:'/navigate-information'});
  assert.deepEqual(routes,[{name:'artist-detail',params:{id:'22'}}]);
  for(const dispose of disposals.reverse())await dispose();
  assert.equal((await handler({...request({token}),path:'/navigate-information'})).status,403);
});
