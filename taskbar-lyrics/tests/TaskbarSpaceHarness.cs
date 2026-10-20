using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

// 只创建自己的隐藏宿主；空位由测试提供，不改动用户任务栏或鼠标。
class TaskbarSpaceHarness
{
    static BindingFlags flags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static Assembly assembly;
    static int checks;
    static object Field(object target,string name){return target.GetType().GetField(name,flags).GetValue(target);}
    static void Set(object target,string name,object value){target.GetType().GetField(name,flags).SetValue(target,value);}
    static object Call(object target,string name,params object[] args){return target.GetType().GetMethod(name,flags).Invoke(target,args);}
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static object Resolve(Rect bar,Rect preferred,Rect[] blocked,double scale,double minimum,bool cover){return assembly.GetType("TaskbarSpaceLayout").GetMethod("Resolve",flags).Invoke(null,new object[]{bar,preferred,blocked,scale,minimum,cover});}
    static Rect Area(object fit){return (Rect)Field(fit,"Area");}
    static object Aligned(Rect bar,Rect preferred,Rect[] blocked,double scale,double minimum,bool cover,string alignment){return assembly.GetType("TaskbarSpaceLayout").GetMethod("ResolveAligned",flags).Invoke(null,new object[]{bar,preferred,blocked,scale,minimum,cover,alignment});}
    static Rect Preferred(Rect bar,double scale,string alignment,double offset,double width)
    {
        Type native=assembly.GetType("TaskbarLyrics");object bounds=Activator.CreateInstance(native.GetNestedType("Rect",flags));
        Set(bounds,"Left",(int)bar.Left);Set(bounds,"Top",(int)bar.Top);Set(bounds,"Right",(int)bar.Right);Set(bounds,"Bottom",(int)bar.Bottom);
        var settings=new Dictionary<string,object>{{"placementAlignment",alignment},{"offset",offset},{"width",width}};
        object placed=native.GetMethod("Placement",flags).Invoke(null,new object[]{bounds,scale,settings});
        return new Rect((int)Field(placed,"Left"),(int)Field(placed,"Top"),(int)Field(placed,"Right")-(int)Field(placed,"Left"),(int)Field(placed,"Bottom")-(int)Field(placed,"Top"));
    }
    static void Alignment()
    {
        Rect bar=new Rect(0,0,1000,48),middle=new Rect(320,0,360,48),right=new Rect(628,0,360,48);
        Check(Preferred(bar,1,"left",12,360)==new Rect(12,0,360,48),"旧版左侧距离改变");
        Check(Preferred(bar,1,"center",0,360)==middle,"居中初始位置错误");
        Check(Preferred(bar,1,"right",12,360)==right,"居右初始偏移错误");
        foreach(double scale in new[]{1.0,1.25,1.5,2.0})foreach(double origin in new[]{0.0,-2560.0})foreach(string alignment in new[]{"left","center","right"})foreach(double offset in new[]{-2000.0,-20,0,12,2000}){
            Rect bounds=new Rect(origin,-100,1000*scale,48*scale);
            Rect wanted=Preferred(bounds,scale,alignment,offset,360);
            Check(bounds.Contains(wanted),"偏移或缩放导致初始区域越出屏幕");
            Check(wanted.Width<=360*scale,"偏移放大了最大显示宽度");
            if(alignment=="center" && offset==-20)Check(Math.Abs(wanted.Left+wanted.Width/2-(bounds.Left+bounds.Width/2-20*scale))<.51,"居中负偏移没有按各屏 DPI 换算");
            Check(bounds.Contains(Preferred(bounds,scale,alignment,offset,2000)),"大宽度或极端偏移越界");
        }
        Check(Area(Aligned(bar,middle,new Rect[0],1,90,true,"center"))==middle,"居中没有保持任务栏中心");
        Rect shifted=Area(Aligned(bar,middle,new[]{new Rect(440,0,160,48)},1,90,true,"center"));
        Check(shifted.Left==604 && shifted.Width==360,"中心被占用后没有向右寻找空位");
        Check(Area(Aligned(bar,middle,new[]{new Rect(440,0,160,48),new Rect(860,0,140,48)},1,90,true,"center")).Right==856,"向右避让后没有收缩宽度");
        var cover=Aligned(bar,middle,new[]{new Rect(0,0,800,48),new Rect(900,0,100,48)},1,90,true,"center");
        Check((bool)Field(cover,"Cover") && !(bool)Field(cover,"Lyrics"),"中心向右无控件空间时未保留封面");
        Check(Area(Aligned(bar,middle,new[]{new Rect(480,0,520,48)},1,90,true,"center")).Width==0,"居中向右无空间时跳回了左侧");
        Check(Area(Aligned(bar,right,new Rect[0],1,90,true,"right"))==right,"居右没有保留边缘偏移");
        Rect shrunk=Area(Aligned(bar,right,new[]{new Rect(0,0,780,48)},1,90,true,"right"));
        Check(shrunk.Left==784 && shrunk.Right==988,"居右收缩后右边缘漂移");
        Check(Area(Aligned(bar,right,new[]{new Rect(800,0,200,48)},1,90,true,"right")).Right==796,"居右未向左避让托盘");
        var stability=Activator.CreateInstance(assembly.GetType("TaskbarSpaceStability"),true);
        Call(stability,"Constrain",middle,DateTime.UtcNow);Call(stability,"Reset");
        Check((Rect)Call(stability,"Constrain",right,DateTime.UtcNow)==right,"切换对齐后仍沿用旧位置");
        // 覆盖负坐标、副屏 DPI、窄空位及两端占用，所有方向都不能越界。
        foreach(string alignment in new[]{"left","center","right"})foreach(double scale in new[]{1.0,1.25,1.5,2.0})foreach(double left in new[]{0.0,-2560.0})foreach(int boundary in new[]{40,300,600,800,950})foreach(bool showCover in new[]{true,false}){
            Rect bounds=new Rect(left,-100,1000*scale,48*scale);
            double x=alignment=="left"?12:alignment=="center"?320:628;
            Rect wanted=new Rect(left+x*scale,-100,360*scale,48*scale);
            Rect[] blocked={new Rect(left,-100,boundary*scale,48*scale),new Rect(left+940*scale,-100,60*scale,48*scale)};
            var fit=Aligned(bounds,wanted,blocked,scale,90,showCover,alignment);Rect area=Area(fit);
            if(area.Width<=0)continue;
            Check((bool)assembly.GetType("TaskbarSpaceLayout").GetMethod("IsFree",flags).Invoke(null,new object[]{area,bounds,blocked,scale}),"对齐后的内容遮挡图标或越过屏幕");
            if(alignment=="right")Check(area.Right<=wanted.Right,"居右偏移边界失效");
            if(alignment=="center")Check(area.Left+area.Width/2>=wanted.Left+wanted.Width/2-.51,"居中避让时向左跳动");
            if((bool)Field(fit,"Lyrics"))Check(area.Width/scale-(showCover?44:0)-8>=89.99,"对齐后未遵守控件最小宽度");
        }
    }
    static void Geometry()
    {
        var bar=new Rect(0,0,1000,48);var preferred=new Rect(12,0,360,48);
        var full=Resolve(bar,preferred,new[]{new Rect(500,0,500,48)},1,150,true);
        Check((bool)Field(full,"Lyrics") && Area(full).Width==360,"空位充足未保持原宽度");
        var narrow=Resolve(bar,preferred,new[]{new Rect(280,0,720,48)},1,150,true);
        Check((bool)Field(narrow,"Lyrics") && Area(narrow).Width==264,"空位减少未缩小歌词视口");
        var cover=Resolve(bar,preferred,new[]{new Rect(210,0,790,48)},1,150,true);
        Check(!(bool)Field(cover,"Lyrics") && (bool)Field(cover,"Cover") && Area(cover).Width==44,"控件放不下未只留封面");
        var hidden=Resolve(bar,preferred,new[]{new Rect(48,0,952,48)},1,150,true);
        Check(Area(hidden).Width==0 && !(bool)Field(hidden,"Cover"),"封面放不下仍占据任务栏");
        var noCover=Resolve(bar,preferred,new[]{new Rect(190,0,810,48)},1,150,false);
        Check((bool)Field(noCover,"Lyrics") && !(bool)Field(noCover,"Cover"),"关闭封面后仍预留封面宽度");
        Check(Area(Resolve(bar,preferred,new[]{new Rect(150,0,850,48)},1,150,false)).Width==0,"无封面且控件放不下应整体隐藏");
        Check((bool)Field(Resolve(bar,preferred,new[]{new Rect(218,0,782,48)},1,150,true),"Lyrics"),"恰好容纳 150px 控件区域时错误收起");
        Check(!(bool)Field(Resolve(bar,preferred,new[]{new Rect(280,0,720,48)},1,220,true),"Lyrics"),"未使用自定义控件最小宽度");
        Check(!(bool)Field(Resolve(bar,new Rect(12,0,120,48),new Rect[0],1,150,true),"Lyrics"),"设置宽度不足时仍显示控件");
        Check((bool)Field(Resolve(bar,new Rect(12,0,142,48),new Rect[0],1,90,true),"Lyrics"),"90px 阈值仍受旧下限限制");
        Check(!(bool)Field(Resolve(bar,new Rect(12,0,141,48),new Rect[0],1,90,true),"Lyrics"),"未计入封面及留白");
        Check((bool)Field(Resolve(bar,new Rect(12,0,60,48),new Rect[0],1,0,true),"Lyrics"),"0px 阈值仍受旧下限限制");
        var start=Resolve(bar,preferred,new[]{new Rect(0,0,60,48),new Rect(120,0,880,48)},1,150,true);
        Check(Area(start).Left==64 && Area(start).Width==44,"未避让开始和搜索按钮");
        var right=Resolve(bar,preferred,new[]{new Rect(0,0,780,48),new Rect(900,0,100,48)},1,150,true);
        Check(!(bool)Field(right,"Lyrics") && Area(right).Right==896,"右侧封面没有贴近空位右边缘");
        // 回放实测托盘按钮回弹的 1～15px 偏移，原生槽位和可见封面必须保持不变。
        Rect trayBar=new Rect(0,1528,2560,72),nativeTray=new Rect(2146,1528,414,72);
        Rect lastFit=Rect.Empty;
        foreach(int overshoot in new[]{0,1,2,5,14,15,0}){
            Rect visual=new Rect(2145-overshoot,1528,415+overshoot,72);
            Rect boundary=(Rect)assembly.GetType("TaskbarSpaceLayout").GetMethod("TrayBoundary",flags).Invoke(null,new object[]{trayBar,nativeTray,visual});
            Check(boundary==nativeTray,"UIA 回弹改变了原生托盘边界");
            var fitted=Resolve(trayBar,new Rect(18,1528,540,72),new[]{new Rect(0,1528,2059,72),boundary},1.5,80,true);
            Check((bool)Field(fitted,"Cover") && !(bool)Field(fitted,"Lyrics"),"托盘动画导致仅封面隐藏");
            if(!lastFit.IsEmpty)Check(Area(fitted)==lastFit,"托盘动画导致右侧封面移动");
            lastFit=Area(fitted);
        }
        // 多种 DPI、负坐标、重叠应用按钮及不同控件阈值，验证可见区域绝不越界。
        foreach(double scale in new[]{1.0,1.1,1.25,1.5,2.0})foreach(int offset in new[]{0,12,90})foreach(int occupiedAt in new[]{40,100,200,300,550})foreach(bool showCover in new[]{false,true}){
            var bounds=new Rect(-1600,-100,1000*scale,48*scale);
            var wanted=new Rect(bounds.Left+offset*scale,bounds.Top,360*scale,bounds.Height);
            var blocked=new[]{new Rect(bounds.Left+occupiedAt*scale,bounds.Top,(1000-occupiedAt)*scale,bounds.Height),new Rect(bounds.Left+600*scale,bounds.Top,100*scale,bounds.Height)};
            var fit=Resolve(bounds,wanted,blocked,scale,150,showCover);Rect area=Area(fit);
            if(area.Width>0){
                Check(bounds.Contains(area),"可见区域越过屏幕任务栏边界");
                foreach(Rect obstacle in blocked){Rect guarded=obstacle;guarded.Inflate(4*scale,0);Rect overlap=Rect.Intersect(area,guarded);Check(overlap.IsEmpty || overlap.Width<.01,"可见区域遮挡应用或系统按钮");}
                if((bool)Field(fit,"Lyrics"))Check(area.Width/scale-(showCover?44:0)-8>=149.99,"显示控件时宽度不足");
                else Check(showCover && Math.Abs(area.Width-Math.Ceiling(44*scale))<.01,"封面模式改变了封面大小");
            }else Check(!(bool)Field(fit,"Lyrics") && !(bool)Field(fit,"Cover"),"隐藏状态仍标记可见内容");
        }
    }
    static void Stability()
    {
        var stability=Activator.CreateInstance(assembly.GetType("TaskbarSpaceStability"),true);DateTime now=DateTime.UtcNow;
        Rect full=new Rect(12,0,360,48),small=new Rect(12,0,44,48),hidden=new Rect(12,0,0,48);
        Check(((Rect)Call(stability,"Constrain",full,now)).Width==360,"首次布局不正确");
        Check(((Rect)Call(stability,"Constrain",small,now.AddMilliseconds(1))).Width==44,"收缩没有立即生效");
        Check(((Rect)Call(stability,"Constrain",full,now.AddMilliseconds(100))).Width==44,"恢复显示没有等待稳定");
        Check(((Rect)Call(stability,"Constrain",full,now.AddMilliseconds(151))).Width==360,"空间稳定 50ms 后仍等待播放器刷新");
        Check(((Rect)Call(stability,"Constrain",full,now.AddMilliseconds(221))).Width==360,"空位恢复后没有及时重新展开");
        Check(((Rect)Call(stability,"Constrain",hidden,now.AddMilliseconds(600))).Width==0,"无空间时未立即隐藏");
        Check(((Rect)Call(stability,"Constrain",small,now.AddMilliseconds(650))).Width==0,"封面恢复没有防抖");
        Check(((Rect)Call(stability,"Constrain",small,now.AddMilliseconds(1101))).Width==44,"封面未自动恢复");
        var pinned=Activator.CreateInstance(assembly.GetType("TaskbarSpaceStability"),true);
        Rect rightCover=new Rect(852,0,44,48),expanded=new Rect(600,0,296,48),bar=new Rect(0,0,1000,48);
        var occupied=new[]{new Rect(0,0,500,48),new Rect(900,0,100,48)};
        Call(pinned,"ConstrainInSpace",rightCover,now,bar,occupied,1.0);
        Check((Rect)Call(pinned,"ConstrainInSpace",expanded,now.AddMilliseconds(100),bar,occupied,1.0)==rightCover,"扩展等待期间封面先向左跳动");
        Check((Rect)Call(pinned,"ConstrainInSpace",rightCover,now.AddMilliseconds(200),bar,occupied,1.0)==rightCover,"瞬时扩展取消后封面未保持原位");
        Call(pinned,"ConstrainInSpace",expanded,now.AddMilliseconds(300),bar,occupied,1.0);
        Check((Rect)Call(pinned,"ConstrainInSpace",expanded,now.AddMilliseconds(420),bar,occupied,1.0)==expanded,"真实空间恢复后没有及时一次性展开");
        var blocked=new[]{new Rect(850,0,150,48)};
        Check((Rect)Call(pinned,"ConstrainInSpace",new Rect(600,0,44,48),now.AddMilliseconds(510),bar,blocked,1.0)==new Rect(600,0,44,48),"实际按钮占用旧位置后未立即避让");
    }
    static void Interaction()
    {
        Type native=assembly.GetType("TaskbarLyrics");object display=null;
        foreach(object candidate in (IEnumerable)native.GetMethod("Screens",flags).Invoke(null,null)){display=candidate;break;}
        using(var parent=new HwndSource(new HwndSourceParameters("空位测试父窗口") {WindowStyle=unchecked((int)0x86000000),ExtendedWindowStyle=0x8000080,PositionX=-3000,PositionY=-3000,Width=600,Height=48,UsesPerPixelTransparency=true})){
            Set(display,"Taskbar",parent.Handle);
            var surface=Activator.CreateInstance(native,flags,null,new[]{display},null);Call(surface,"Open");((DispatcherTimer)Field(surface,"pointerTimer")).Stop();
            Set(surface,"allowDisplay",true);Set(surface,"hoverEnabled",true);Set(surface,"progressEnabled",true);Set(surface,"hoverInfoEnabled",true);
            var viewport=(Viewbox)Field(surface,"controlsViewport");Set(surface,"lyricAreaHeight",48.0);
            Set(surface,"lyricsAvailable",true);Call(surface,"SetHover",true);
            viewport.MaxWidth=320;Call(surface,"LayoutInteraction");double width=viewport.Width;
            foreach(double available in new[]{250.0,200.0,150.0}){viewport.MaxWidth=available;Call(surface,"LayoutInteraction");Check(Math.Abs(viewport.Width-width)<.01,"按钮随歌词宽度缩小");}
            Set(surface,"lyricsAvailable",false);Set(surface,"hoverAmount",1.0);Call(surface,"SetHover",true);
            Check(!(bool)Field(surface,"hovering") && (double)Field(surface,"hoverAmount")==0,"仅封面时悬停仍展开控件");
            var root=(System.Windows.Controls.UserControl)Field(surface,"window");
            var coverHost=(Grid)Field(surface,"coverHost");var lyricsHost=(Grid)Field(surface,"lyricsHost");
            coverHost.Visibility=lyricsHost.Visibility=Visibility.Visible;
            foreach(string alignment in new[]{"right","left","center","right"}){
                Call(surface,"LayoutOrder",alignment);
                root.Measure(new Size(300,48));root.Arrange(new Rect(0,0,300,48));root.UpdateLayout();
                double coverX=coverHost.TransformToAncestor(root).Transform(new Point()).X;
                double lyricX=lyricsHost.TransformToAncestor(root).Transform(new Point()).X;
                Check(alignment=="right"?coverX>lyricX:coverX<lyricX,"切换整体位置后封面顺序错误");
                Check(coverHost.ActualWidth==36 && lyricsHost.ActualWidth==256,"交换列改变了封面或歌词区域尺寸");
            }
            var primary=(TextBlock)Field(surface,"primary");var secondary=(TextBlock)Field(surface,"secondary");
            var primaryViewport=(Grid)Field(surface,"primaryViewport");var secondaryViewport=(Grid)Field(surface,"secondaryViewport");
            primaryViewport.Width=secondaryViewport.Width=248;
            secondary.Visibility=secondaryViewport.Visibility=Visibility.Visible;
            Set(surface,"scrolling",true);Set(surface,"scrollingTranslation",true);
            foreach(string alignment in new[]{"right","left","center"})foreach(string lyric in new[]{"短歌词","这是非常长的一段歌词需要从头正常滚动而且必须能看到最后的文字"}){
                Call(surface,"LayoutOrder",alignment);primary.Text=lyric;secondary.Text="下一句";
                Set(surface,"primaryTextWidth",Call(surface,"FitLine",primary,248.0,true));Set(surface,"secondaryTextWidth",Call(surface,"FitLine",secondary,248.0,true));Call(surface,"AlignLyrics");
                root.Measure(new Size(300,48));root.Arrange(new Rect(0,0,300,48));root.UpdateLayout();
                bool rightShort=alignment=="right" && primary.Width<=248;
                Check(primary.HorizontalAlignment==(rightShort?HorizontalAlignment.Right:HorizontalAlignment.Left),"短歌词未靠近封面，或长歌词起点被裁切");
                if(rightShort){
                    double end=primary.TransformToAncestor(root).Transform(new Point(primary.ActualWidth,0)).X;
                    double coverX=coverHost.TransformToAncestor(root).Transform(new Point()).X;
                    Check(Math.Abs(coverX-end-8)<.51,"居右短歌词与封面间残留整块空白");
                }
                Check(secondary.HorizontalAlignment==(alignment=="right"?HorizontalAlignment.Right:HorizontalAlignment.Left),"第二行没有同步靠近封面");
            }
            Call(surface,"LayoutOrder","right");primary.Text="不滚动的短句";
            Set(surface,"primaryTextWidth",Call(surface,"FitLine",primary,248.0,false));Set(surface,"scrolling",false);Call(surface,"AlignLyrics");
            Check(primary.TextAlignment==TextAlignment.Right,"关闭滚动后短句仍靠左");
            Check(Math.Abs(primary.Width-(double)Field(surface,"primaryTextWidth"))<.01,"居右短句字框留白错开进度遮罩坐标");
            var controlsSurface=(Border)Field(surface,"controlsSurface");
            var timeViewport=(Viewbox)Field(surface,"progressTimeViewport");
            viewport.Visibility=timeViewport.Visibility=Visibility.Visible;viewport.MaxWidth=248;
            foreach(string placement in new[]{"left","center","right"})foreach(string lyric in new[]{"短句","这是一段长度超过控件宽度的歌词，用于验证控件不会远离封面"}){
                Set(surface,"hovering",false);Call(surface,"LayoutOrder",placement);
                primary.Text=lyric;Set(surface,"primaryTextWidth",Call(surface,"FitLine",primary,248.0,true));Call(surface,"AlignLyrics");
                Call(surface,"LayoutControls",248.0);
                root.Measure(new Size(300,48));root.Arrange(new Rect(0,0,300,48));root.UpdateLayout();
                double first=controlsSurface.TransformToAncestor(root).Transform(new Point()).X;
                double last=controlsSurface.TransformToAncestor(root).Transform(new Point(controlsSurface.ActualWidth,0)).X;
                var expected=placement=="right"?HorizontalAlignment.Right:HorizontalAlignment.Left;
                Check(viewport.HorizontalAlignment==expected && timeViewport.HorizontalAlignment==expected,"按钮和播放时间未随封面方向同步对齐");
                double coverX=coverHost.TransformToAncestor(root).Transform(new Point()).X;
                double gap=placement=="right"?coverX-last:first-coverX-coverHost.ActualWidth;
                Check(Math.Abs(gap-11)<.51,"边缘对齐的控件被歌词留白推离封面");
                double before=first;
                Set(surface,"hovering",true);primary.Text="另一句";Call(surface,"LayoutControls",248.0);
                root.Measure(new Size(300,48));root.Arrange(new Rect(0,0,300,48));root.UpdateLayout();
                Check(Math.Abs(controlsSurface.TransformToAncestor(root).Transform(new Point()).X-before)<.51,"悬停中换句使控件横向跳动");
                Check(last<=lyricsHost.TransformToAncestor(root).Transform(new Point(lyricsHost.ActualWidth,0)).X,"控件遮挡封面或越出歌词区域");
            }
            Call(surface,"LayoutOrder","right");
            lyricsHost.Visibility=Visibility.Collapsed;
            root.Measure(new Size(44,48));root.Arrange(new Rect(0,0,44,48));root.UpdateLayout();
            Check(Math.Abs(coverHost.TransformToAncestor(root).Transform(new Point()).X-4)<.01,"居右仅封面模式超出显示范围");
            lyricsHost.Visibility=Visibility.Visible;Call(surface,"LayoutOrder","left");
            root.Measure(new Size(300,48));root.Arrange(new Rect(0,0,300,48));
            Call(surface,"ClipSpace",new Rect(0,0,300,48),new Rect(0,0,1000,48),new[]{new Rect(120,0,100,48)},1.0);
            var clip=(System.Windows.Media.GeometryGroup)root.Clip;
            Check(clip.FillContains(new Point(20,24)) && clip.FillContains(new Point(250,24)),"等待计算时未保留两侧仍可显示的歌词");
            Check(!clip.FillContains(new Point(150,24)),"等待计算时遮挡了新加入的任务栏按钮");
            Check(!(bool)Call(surface,"InSpace",root.PointToScreen(new Point(150,24))),"被裁掉区域仍接收鼠标交互");
            Check((bool)Call(surface,"InSpace",root.PointToScreen(new Point(20,24))),"仍显示的区域不能交互");
            Call(surface,"Close");
        }
        var monitor=Activator.CreateInstance(assembly.GetType("TaskbarSpaceMonitor"),true);Set(monitor,"nextScanUtc",DateTime.MaxValue);
        var bounds=new Rect(0,0,1000,48);
        Check(!(bool)Field(Call(monitor,"Read",new IntPtr(123),bounds),"Valid"),"未知空间被错误视为空白");
        Check((bool)Field(Call(monitor,"Read",IntPtr.Zero,bounds),"Valid"),"无系统任务栏的屏幕未保持底边回退");
        var snapshot=Activator.CreateInstance(assembly.GetType("TaskbarSpaceSnapshot"),true);
        Set(snapshot,"Taskbar",new IntPtr(123));Set(snapshot,"Bounds",bounds);Set(snapshot,"Captured",DateTime.UtcNow);Set(snapshot,"Valid",true);Set(monitor,"snapshot",snapshot);
        Check((bool)Field(Call(monitor,"Read",new IntPtr(123),bounds),"Valid"),"新鲜空位快照不可用");
        Check(!(bool)Field(Call(monitor,"Read",new IntPtr(124),bounds),"Valid"),"旧任务栏快照被用于新任务栏");
        Check(!(bool)Field(Call(monitor,"Read",new IntPtr(123),new Rect(0,0,800,48)),"Valid"),"任务栏缩放后继续使用旧坐标");
        Set(snapshot,"Captured",DateTime.UtcNow.AddSeconds(-4));
        Check((bool)Field(Call(monitor,"Read",new IntPtr(123),bounds),"Valid"),"短暂扫描失败丢失上次有效布局");
        var history=Activator.CreateInstance(assembly.GetType("TaskbarSpaceHistory"),true);
        DateTime captured=DateTime.UtcNow;Rect button=new Rect(100,0,60,48),tray=new Rect(900,0,100,48);
        Set(snapshot,"Captured",captured);Set(snapshot,"Occupied",new[]{button,tray});Call(history,"Accept",snapshot);
        Set(snapshot,"Captured",captured.AddMilliseconds(40));Set(snapshot,"Occupied",new[]{tray});
        var missing=Call(history,"Accept",snapshot);
        Check(Array.IndexOf((Rect[])Field(missing,"Occupied"),button)>=0,"按钮短暂漏报产生假空位");
        Set(snapshot,"Captured",captured.AddMilliseconds(130));
        Check(Array.IndexOf((Rect[])Field(Call(history,"Accept",snapshot),"Occupied"),button)<0,"应用关闭后的空间没有及时释放");
        Set(snapshot,"Captured",captured.AddMilliseconds(400));Set(snapshot,"Occupied",new[]{button,tray});
        Check(Array.IndexOf((Rect[])Field(Call(history,"Accept",snapshot),"Occupied"),button)>=0,"新按钮没有立即避让");
        var trayHistory=Activator.CreateInstance(assembly.GetType("TaskbarSpaceHistory"),true);
        Set(snapshot,"Captured",captured);Set(snapshot,"Occupied",new[]{button,tray});Set(snapshot,"TrayBounds",tray);
        Call(trayHistory,"Accept",snapshot);
        Rect changedTray=new Rect(940,0,60,48);
        Set(snapshot,"Captured",captured.AddSeconds(1));Set(snapshot,"OverflowVisible",true);Set(snapshot,"TrayBounds",changedTray);Set(snapshot,"Occupied",new[]{button,changedTray});
        var opened=Call(trayHistory,"Accept",snapshot);
        Check((Rect)Field(opened,"TrayBounds")==tray,"展开托盘改变了固定避让边界");
        Check(Array.IndexOf((Rect[])Field(opened,"Occupied"),changedTray)<0,"展开状态的临时边界参与了布局");
        Set(snapshot,"Captured",captured.AddSeconds(2));Set(snapshot,"OverflowVisible",false);
        var closed=Call(trayHistory,"Accept",snapshot);
        Check((Rect)Field(closed,"TrayBounds")==changedTray,"关闭托盘后没有恢复实际边界检测");
        Call(monitor,"Dispose");
        Check(!(bool)Field(Call(monitor,"Read",new IntPtr(123),bounds),"Valid"),"已关闭的监视器仍返回可用布局");
    }
    static void Events()
    {
        var gate=Activator.CreateInstance(assembly.GetType("TaskbarSpaceRefresh"),true);
        DateTime now=DateTime.UtcNow;
        Check(!(bool)Call(gate,"Take",now),"无变化时仍扫描任务栏");
        Check(!(bool)Call(gate,"Take",now.AddHours(1)),"空闲时恢复了周期扫描");
        for(int i=0;i<100;i++)Call(gate,"Changed");
        Check((bool)Call(gate,"Take",now),"事件到达后没有立即扫描");
        Check(!(bool)Call(gate,"Take",now.AddMilliseconds(20)),"同批事件重复触发扫描");
        Check((bool)Call(gate,"Take",now.AddMilliseconds(40)),"事件结束 40ms 后没有收尾确认");
        Check(!(bool)Call(gate,"Take",now.AddMilliseconds(500)),"收尾确认变成了持续轮询");
        Check(!(bool)Call(gate,"Take",now.AddHours(2)),"长时间空闲仍产生扫描");
        Call(gate,"Changed");Check((bool)Call(gate,"Take",now.AddHours(2)),"空闲后的新事件丢失");
        Call(gate,"Changed");Check((bool)Call(gate,"Take",now.AddHours(2).AddMilliseconds(60)),"扫描期间到达的新事件丢失");
        Check((bool)Call(gate,"Take",now.AddHours(2).AddMilliseconds(310)),"新一轮事件没有收尾确认");
        Check(!(bool)Call(gate,"Take",now.AddHours(3)),"事件完成后没有回到空闲");
        Call(gate,"Changed");
        Check((DateTime)gate.GetType().GetProperty("Next",flags).GetValue(gate,null)==DateTime.MinValue,"新事件未抢占收尾等待，限频时可能丢失唤醒");
        Call(gate,"Take",now.AddHours(3));
        Check((DateTime)gate.GetType().GetProperty("Next",flags).GetValue(gate,null)==now.AddHours(3).AddMilliseconds(40),"收尾时间不正确");
        Call(gate,"Take",now.AddHours(3).AddMilliseconds(100));
        Check((DateTime)gate.GetType().GetProperty("Next",flags).GetValue(gate,null)==DateTime.MaxValue,"完成确认后仍安排刷新");
        // 不调用 Read：模拟后台扫描完成，必须直接通知布局并合并重复唤醒。
        var monitor=Activator.CreateInstance(assembly.GetType("TaskbarSpaceMonitor"),true);
        var snapshot=Activator.CreateInstance(assembly.GetType("TaskbarSpaceSnapshot"),true);
        Rect bounds=new Rect(0,0,1000,48);IntPtr bar=new IntPtr(123);
        Set(snapshot,"Taskbar",bar);Set(snapshot,"Bounds",bounds);Set(snapshot,"Captured",DateTime.UtcNow);Set(snapshot,"Valid",true);
        Set(monitor,"observedTaskbar",bar);Set(monitor,"observedBounds",bounds);Set(monitor,"nextScanUtc",DateTime.MaxValue);
        var completed=typeof(System.Threading.Tasks.Task).GetMethod("FromResult").MakeGenericMethod(snapshot.GetType()).Invoke(null,new[]{snapshot});
        Set(monitor,"pending",completed);
        int updates=0;Action changed=()=>updates++;
        monitor.GetType().GetEvent("Updated",flags).GetAddMethod(true).Invoke(monitor,new object[]{changed});
        for(int i=0;i<100;i++)Call(monitor,"Wake");
        DrainDispatcher();
        Check(updates==1,"后台扫描完成没有直接刷新，或重复刷新未合并");
        Check((bool)Field(Field(monitor,"snapshot"),"Valid"),"未调用播放器 Read 时扫描结果没有应用");
        Check(!(bool)((DispatcherTimer)Field(monitor,"wakeup")).IsEnabled,"空闲仍保持刷新定时器");
        Call(monitor,"Wake");Call(monitor,"Dispose");DrainDispatcher();
        Check(updates==1,"关闭后仍执行排队的布局通知");
    }
    static void DrainDispatcher()
    {
        var frame=new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));
        Dispatcher.PushFrame(frame);
    }
    static object Snapshot(Rect bar,DateTime time,Rect tray,Dictionary<string,Rect> controls)
    {
        var snapshot=Activator.CreateInstance(assembly.GetType("TaskbarSpaceSnapshot"),true);
        var occupied=new List<Rect>(controls.Values);if(!tray.IsEmpty)occupied.Add(tray);
        Set(snapshot,"Taskbar",new IntPtr(123));Set(snapshot,"Bounds",bar);Set(snapshot,"Captured",time);Set(snapshot,"Valid",true);
        Set(snapshot,"TrayBounds",tray);Set(snapshot,"NativeTray",!tray.IsEmpty);Set(snapshot,"Controls",controls);Set(snapshot,"Occupied",occupied.ToArray());
        return snapshot;
    }
    static void TrayChanges()
    {
        DateTime now=DateTime.UtcNow;Rect bar=new Rect(0,0,1000,48),preferred=new Rect(12,0,360,48);
        var history=Activator.CreateInstance(assembly.GetType("TaskbarSpaceHistory"),true);
        var controls=new Dictionary<string,Rect>{{"app",new Rect(0,0,850,48)}};
        Call(history,"Accept",Snapshot(bar,now,new Rect(900,0,100,48),controls));
        var updated=Call(history,"Accept",Snapshot(bar,now.AddMilliseconds(10),new Rect(940,0,60,48),controls));
        var obstacles=(Rect[])Field(updated,"Occupied");
        Check(Array.IndexOf(obstacles,new Rect(900,0,100,48))<0,"托盘收缩后旧槽位仍堵住封面");
        var fit=Resolve(bar,preferred,obstacles,1,90,true);
        Check((bool)Field(fit,"Cover") && Area(fit).Right==936,"图标移入托盘后封面没有立即出现在当前右边界");
        updated=Call(history,"Accept",Snapshot(bar,now.AddMilliseconds(20),new Rect(880,0,120,48),controls));
        fit=Resolve(bar,preferred,(Rect[])Field(updated,"Occupied"),1,90,true);
        Check(Area(fit).Width==0,"图标移出托盘后仍遮盖真实托盘区域");
        controls=new Dictionary<string,Rect>{{"app",new Rect(0,0,700,48)}};
        updated=Call(history,"Accept",Snapshot(bar,now.AddMilliseconds(30),new Rect(900,0,100,48),controls));
        Check(Array.IndexOf((Rect[])Field(updated,"Occupied"),new Rect(0,0,850,48))<0,"同一应用按钮移动后残留旧坐标");
        fit=Resolve(bar,preferred,(Rect[])Field(updated,"Occupied"),1,90,true);
        Check((bool)Field(fit,"Lyrics") && Area(fit).Left==704 && Area(fit).Width==192,"按钮重排后的歌词宽度或位置错误");
        // 最后一帧正好遇到短暂漏报时，过期清理仍需独立完成。
        updated=Call(history,"Accept",Snapshot(bar,now.AddMilliseconds(40),new Rect(900,0,100,48),new Dictionary<string,Rect>()));
        Check(((Rect[])Field(updated,"Occupied")).Length==2,"短暂漏报没有保留按钮边界");
        Check(!(bool)Call(history,"Expire",now.AddMilliseconds(119)),"按钮边界过早释放");
        Check((bool)Call(history,"Expire",now.AddMilliseconds(120)),"没有新事件时旧边界未过期");
        updated=Call(history,"Snapshot");
        Check(((Rect[])Field(updated,"Occupied")).Length==1,"过期后仍使用旧按钮计算宽度");
        var monitor=Activator.CreateInstance(assembly.GetType("TaskbarSpaceMonitor"),true);
        Set(monitor,"observedTaskbar",new IntPtr(123));Set(monitor,"observedBounds",bar);Set(monitor,"nextScanUtc",DateTime.MaxValue);
        var cachedHistory=Field(monitor,"history");DateTime live=DateTime.UtcNow;
        Call(cachedHistory,"Accept",Snapshot(bar,live,Rect.Empty,controls));
        Set(monitor,"snapshot",Call(cachedHistory,"Accept",Snapshot(bar,live,Rect.Empty,new Dictionary<string,Rect>())));
        int notifications=0;Action notify=()=>notifications++;
        monitor.GetType().GetEvent("Updated",flags).GetAddMethod(true).Invoke(monitor,new object[]{notify});
        Call(monitor,"Pump",false);
        var wait=new DispatcherFrame();var stop=new DispatcherTimer(DispatcherPriority.ApplicationIdle){Interval=TimeSpan.FromMilliseconds(150)};
        stop.Tick+=(sender,e)=>{stop.Stop();wait.Continue=false;};stop.Start();Dispatcher.PushFrame(wait);
        Check(notifications==1 && ((Rect[])Field(Field(monitor,"snapshot"),"Occupied")).Length==0,"停止扫描后缺失边界没有自行释放并通知布局");
        Check(!(bool)((DispatcherTimer)Field(monitor,"wakeup")).IsEnabled,"过期清理结束后仍在空闲刷新");
        Call(monitor,"Dispose");
        var stability=Activator.CreateInstance(assembly.GetType("TaskbarSpaceStability"),true);
        Call(stability,"ConstrainInSpace",new Rect(704,0,192,48),now,bar,new Rect[0],1.0);
        Rect movedBar=new Rect(0,4,1000,48),movedFit=new Rect(12,4,360,48);
        Check((Rect)Call(stability,"ConstrainInSpace",movedFit,now.AddMilliseconds(1),movedBar,new Rect[0],1.0)==movedFit,"任务栏移动后仍沿用旧位置的防抖");
        var gate=Activator.CreateInstance(assembly.GetType("TaskbarSpaceRefresh"),true);
        Call(gate,"ApplicationsChanged");
        for(int i=0;i<=4;i++)Check((bool)Call(gate,"Take",now.AddMilliseconds(i*100)),"未持续捕获新软件稍后加入的任务栏按钮");
        Check(!(bool)Call(gate,"Take",now.AddMilliseconds(500)),"新软件布局稳定后仍继续扫描");
        Call(gate,"Settle",now.AddMilliseconds(500));
        Check((bool)Call(gate,"Take",now.AddMilliseconds(600)),"最后一次扫描仍变化时没有继续确认");
        Call(gate,"Settle",now.AddMilliseconds(700));
        Check(!(bool)Call(gate,"Take",now.AddMilliseconds(800)),"布局确认超过了单批事件的上限");
    }
    static void AnimationCommit()
    {
        DateTime now=DateTime.UtcNow;Rect bar=new Rect(0,0,1000,48),original=new Rect(852,0,44,48);
        var stability=Activator.CreateInstance(assembly.GetType("TaskbarSpaceStability"),true);
        Call(stability,"ConstrainInSpace",original,now,bar,new Rect[0],1.0);
        Set(stability,"Confirmed",false);
        Rect final=original;
        // 托盘持续扩展时中间位置都不可提交，最后只移动一次。
        for(int i=1;i<=5;i++){
            double edge=900-i*8;final=new Rect(edge-48,0,44,48);
            var blocked=new[]{new Rect(edge,0,1000-edge,48)};
            Rect shown=(Rect)Call(stability,"ConstrainInSpace",final,now.AddMilliseconds(i*30),bar,blocked,1.0);
            Check(shown==original,"计算期间隐藏或移动了整个歌词窗口");
            Check((Rect)Field(stability,"accepted")==original,"动画中间位置覆盖了最终位置记录");
        }
        Set(stability,"Confirmed",true);
        Check((Rect)Call(stability,"ConstrainInSpace",final,now.AddMilliseconds(300),bar,new[]{new Rect(860,0,140,48)},1.0)==final,"边界确认后没有一次性采用最终位置");
        Set(stability,"Confirmed",false);
        Rect target=new Rect(100,0,200,48);
        for(int i=0;i<5;i++)Check((Rect)Call(stability,"ConstrainInSpace",new Rect(target.X+i*8,0,200,48),now.AddMilliseconds(350+i*20),bar,new Rect[0],1.0)==final,"旧位置仍安全时没有保持原位");
        Check((Rect)Call(stability,"ConstrainInSpace",final,now.AddMilliseconds(460),bar,new Rect[0],1.0)==final,"动画取消后没有直接保留原位");
        var startup=Activator.CreateInstance(assembly.GetType("TaskbarSpaceStability"),true);Set(startup,"Confirmed",false);
        Check(((Rect)Call(startup,"ConstrainInSpace",target,now,bar,new Rect[0],1.0)).Width==0,"启动时展示了未确认的位置");
        Set(startup,"Confirmed",true);
        Check((Rect)Call(startup,"ConstrainInSpace",target,now.AddMilliseconds(150),bar,new Rect[0],1.0)==target,"启动位置确认后没有恢复显示");
        var monitor=Activator.CreateInstance(assembly.GetType("TaskbarSpaceMonitor"),true);
        Set(monitor,"observedTaskbar",new IntPtr(123));Set(monitor,"observedBounds",bar);Set(monitor,"nextScanUtc",DateTime.MaxValue);
        var snapshot=Snapshot(bar,now,Rect.Empty,new Dictionary<string,Rect>{{"app",new Rect(500,0,100,48)}});
        var fromResult=typeof(System.Threading.Tasks.Task).GetMethod("FromResult").MakeGenericMethod(snapshot.GetType());
        Set(monitor,"pending",fromResult.Invoke(null,new[]{snapshot}));Call(monitor,"Pump",false);
        Set(monitor,"geometryChangedAt",DateTime.UtcNow.AddMilliseconds(-500));
        for(int i=0;i<10;i++)Call(monitor,"Pump",false);
        Check(!(bool)monitor.GetType().GetProperty("PlacementReady",flags).GetValue(monitor,null),"播放器重复刷新错误地代替了新一轮边界测量");
        Set(monitor,"pending",fromResult.Invoke(null,new[]{snapshot}));Call(monitor,"Pump",false);
        Set(monitor,"geometryChangedAt",DateTime.UtcNow.AddMilliseconds(-70));
        Check((bool)monitor.GetType().GetProperty("PlacementReady",flags).GetValue(monitor,null),"两次一致测量后仍不能提交稳定布局");
        snapshot=Snapshot(bar,now.AddMilliseconds(10),Rect.Empty,new Dictionary<string,Rect>{{"app",new Rect(480,0,120,48)}});
        Set(monitor,"pending",fromResult.Invoke(null,new[]{snapshot}));Call(monitor,"Pump",false);
        Check(!(bool)monitor.GetType().GetProperty("PlacementReady",flags).GetValue(monitor,null),"边界继续移动后仍允许提交中间布局");
        Call(monitor,"Dispose");
    }
    static void ContinuousDisplay()
    {
        var method=assembly.GetType("TaskbarSpaceLayout").GetMethod("VisibleParts",flags);
        foreach(double scale in new[]{1.0,1.25,1.5,2.0})foreach(double left in new[]{0.0,-1600.0}){
            Rect bar=new Rect(left,-100,1000*scale,48*scale),area=new Rect(left+12*scale,-100,360*scale,48*scale);
            var blocked=new[]{new Rect(left+200*scale,-100,120*scale,48*scale),new Rect(left+500*scale,-100,100*scale,48*scale)};
            var parts=(Rect[])method.Invoke(null,new object[]{area,bar,blocked,scale});
            Check(parts.Length==2,"部分区域被占用时没有保留其余显示区域");
            foreach(Rect part in parts){
                Check(area.Contains(part) && bar.Contains(part),"过渡显示超出原窗口或任务栏");
                Check((bool)assembly.GetType("TaskbarSpaceLayout").GetMethod("IsFree",flags).Invoke(null,new object[]{part,bar,blocked,scale}),"过渡显示遮挡了任务栏控件");
            }
            Check(((Rect[])method.Invoke(null,new object[]{area,bar,new[]{bar},scale})).Length==0,"实际完全被占用时仍覆盖其他图标");
            Check(((Rect[])method.Invoke(null,new object[]{area,bar,new Rect[0],scale}))[0]==area,"空位恢复后裁切没有完全恢复");
        }
    }
    [STAThread] static int Main(string[] args)
    {
        try {assembly=Assembly.LoadFrom(args[0]);Geometry();Alignment();Stability();Interaction();Events();TrayChanges();AnimationCommit();ContinuousDisplay();File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(new{passed=true,checks=checks}));return 0;}
        catch(Exception error){File.WriteAllText(args[1]+".error",error.ToString());return 1;}
    }
}
