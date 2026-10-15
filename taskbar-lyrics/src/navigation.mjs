// 优先保留宿主提供的歌手边界与 ID，避免把乐队名称误拆成多人。
export function informationNavigation(snapshot, track) {
  const playback=snapshot.playback || {};
  const sameTrack=track && String(track.id)===String(playback.trackId);
  const source=sameTrack && (track.artists?.length ? track.artists : track.singers);
  const numericId=value=>/^[1-9]\d*$/.test(String(value || ''))?String(value):'';
  const artists=[];
  const add=(name,id='')=>{
    name=String(name || '').trim();id=numericId(id);
    if(name && !artists.some(item=>item.name===name && item.id===id))artists.push({name,id});
  };
  if(Array.isArray(source))for(const item of source)add(item.name,item.id);
  if(!artists.length)for(const name of String(playback.artist || '').split(/\s*[/／,，、;；&＆]\s*|\s+(?:&|＆|feat\.?|ft\.?|featuring|with|×)\s+/i))add(name);
  return {title:String(playback.title || ''),artists};
}

export function createInformationNavigator(ctx, readCurrent) {
  let pending=false;
  return async request=>{
    if(request.method!=='POST')return {status:405,body:''};
    if(!['song','artist'].includes(request.query.kind))return {status:400,body:''};
    if(pending)return {status:409,body:''};
    pending=true;
    try{
      const {snapshot,track,settings}=await readCurrent();
      if(!settings.enabled || !settings.hoverSongInfo)return {status:403,body:''};
      const trackId=String(snapshot.playback?.trackId || '');
      if(!trackId || trackId!==request.query.trackId)return {status:409,body:JSON.stringify({error:'歌曲已切换，请重试'})};
      const info=informationNavigation(snapshot,track);
      let route={name:'search',query:{q:info.title,tab:'song'}};
      if(request.query.kind==='song' && !info.title.trim())return {status:400,body:''};
      if(request.query.kind==='artist'){
        if(!settings.noticeShowArtist)return {status:403,body:''};
        const index=Number(request.query.index),artist=info.artists[index];
        if(!Number.isInteger(index) || !artist || artist.name!==request.query.artist)return {status:409,body:JSON.stringify({error:'歌手信息已更新，请重试'})};
        let id=artist.id;
        if(!id){
          // 无 ID 时只接受唯一的同名结果，不猜测相似姓名或同名歌手。
          try{
            const response=await ctx.kugou.search.search(artist.name,'author',1,30);
            const data=response?.data || response;
            const lists=data?.lists || data?.list || [];
            const exact=lists.filter(item=>String(item.name || item.author_name || item.singername || item.AuthorName || item.SingerName || '').trim().normalize('NFKC').toLocaleLowerCase()===artist.name.normalize('NFKC').toLocaleLowerCase());
            const ids=[...new Set(exact.map(item=>String(item.id || item.author_id || item.singerid || item.AuthorId || item.SingerId || '')).filter(value=>/^[1-9]\d*$/.test(value)))];
            if(ids.length===1)id=ids[0];
          }catch { /* 查询不可用时打开歌手搜索，由用户选择准确结果。 */ }
        }
        route=id?{name:'artist-detail',params:{id}}:{name:'search',query:{q:artist.name,tab:'author'}};
      }
      // 先切换主界面路由，再恢复窗口；歌词页展开时同步收起。
      await ctx.player.toggleLyricView(false);
      await ctx.router.push(route);
      const shown=await ctx.host.showOnTop('main',{focus:true});
      if(!shown.ok)throw new Error(shown.error || '无法显示 EchoMusic 主界面');
      return {headers:{'content-type':'application/json'},body:JSON.stringify({ok:true})};
    }catch(error){
      ctx.toast.warning(error.message);
      return {status:502,body:JSON.stringify({error:error.message})};
    }finally{pending=false;}
  };
}
