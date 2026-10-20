using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;

// 几何计算只使用物理坐标，负坐标屏幕和各屏 DPI 沿用同一套规则。
sealed class TaskbarSpacePlacement
{
    public Rect Area;
    public bool Lyrics,Cover;
}

static class TaskbarSpaceLayout
{
    internal static Rect[] VisibleParts(Rect area,Rect bar,Rect[] occupied,double scale)
    {
        Rect clipped=Rect.Intersect(area,bar);
        if(clipped.IsEmpty || clipped.Width<=0 || clipped.Height<=0)return new Rect[0];
        var parts=new List<Rect>{clipped};
        foreach(Rect item in occupied){
            Rect blocked=item;blocked.Inflate(4*scale,0);
            for(int i=parts.Count-1;i>=0;i--){
                Rect part=parts[i],overlap=Rect.Intersect(part,blocked);
                if(overlap.IsEmpty || overlap.Width<=0 || overlap.Height<=0)continue;
                parts.RemoveAt(i);
                if(overlap.Left>part.Left)parts.Add(new Rect(part.Left,part.Top,overlap.Left-part.Left,part.Height));
                if(overlap.Right<part.Right)parts.Add(new Rect(overlap.Right,part.Top,part.Right-overlap.Right,part.Height));
            }
        }
        return parts.ToArray();
    }
    internal static Rect TrayBoundary(Rect bar,Rect native,Rect visual)
    {
        // 原生容器是布局槽位，UIA 子按钮的边界会随按下、回弹动画缩放。
        Rect tray=Rect.Intersect(bar,native.IsEmpty?visual:native);
        return tray.IsEmpty?Rect.Empty:new Rect(tray.Left,bar.Top,bar.Right-tray.Left,bar.Height);
    }
    internal static bool IsFree(Rect area,Rect bar,Rect[] occupied,double scale)
    {
        if(area.IsEmpty || area.Width<=0 || !bar.Contains(area))return false;
        foreach(Rect item in occupied){
            Rect guarded=item;guarded.Inflate(4*scale,0);
            Rect overlap=Rect.Intersect(area,guarded);
            if(!overlap.IsEmpty && overlap.Width>0 && overlap.Height>0)return false;
        }
        return true;
    }
    internal static TaskbarSpacePlacement Resolve(Rect bar,Rect preferred,Rect[] occupied,double scale,double minimum,bool cover)
    {
        var empty=new TaskbarSpacePlacement {Area=new Rect(preferred.X,preferred.Y,0,preferred.Height)};
        if(bar.IsEmpty || preferred.IsEmpty || bar.Width<=0 || bar.Height<=0)return empty;
        bool horizontal=bar.Width>=bar.Height;
        double start=horizontal?preferred.Left:preferred.Top,end=horizontal?bar.Right:bar.Bottom;
        double padding=4*scale,coverWidth=Math.Ceiling((Math.Min(36,Math.Max(16,preferred.Height/scale-10))+8)*scale);
        double required=Math.Ceiling((Math.Max(0,minimum)+8)*scale+(cover?coverWidth:0));
        var blocks=new List<Rect>();
        foreach(Rect item in occupied){
            Rect clipped=Rect.Intersect(bar,item);
            if(!clipped.IsEmpty && clipped.Width>0 && clipped.Height>0)blocks.Add(clipped);
        }
        blocks.Sort((a,b)=>(horizontal?a.Left:a.Top).CompareTo(horizontal?b.Left:b.Top));
        var gaps=new List<Rect>();
        foreach(Rect item in blocks){
            double first=(horizontal?item.Left:item.Top)-padding,last=(horizontal?item.Right:item.Bottom)+padding;
            if(last<=start)continue;
            if(first>start)gaps.Add(horizontal?new Rect(start,bar.Top,first-start,bar.Height):new Rect(bar.Left,start,bar.Width,first-start));
            start=Math.Max(start,last);
            if(start>=end)break;
        }
        if(start<end)gaps.Add(horizontal?new Rect(start,bar.Top,end-start,bar.Height):new Rect(bar.Left,start,bar.Width,end-start));
        // 先找能容纳完整控件的空位；不足时只保留原尺寸封面。
        foreach(Rect candidate in gaps){
            Rect gap=PixelAligned(candidate);
            double width=Math.Min(preferred.Width,gap.Width),height=Math.Min(preferred.Height,gap.Height);
            if(width+0.01>=required && (horizontal || height+0.01>=preferred.Height))
                return new TaskbarSpacePlacement {Area=new Rect(gap.X,gap.Y,width,height),Lyrics=true,Cover=cover};
        }
        if(cover)foreach(Rect candidate in gaps){
            Rect gap=PixelAligned(candidate);
            if(Math.Min(preferred.Width,gap.Width)+0.01>=coverWidth && gap.Height+0.01>=preferred.Height){
                bool rightSide=!horizontal || gap.Left+gap.Width/2>=bar.Left+bar.Width/2;
                double x=rightSide?gap.Right-coverWidth:gap.Left;
                return new TaskbarSpacePlacement {Area=new Rect(x,gap.Y,coverWidth,preferred.Height),Cover=true};
            }
        }
        return empty;
    }
    static Rect PixelAligned(Rect area)
    {
        double x=Math.Ceiling(area.Left),y=Math.Ceiling(area.Top);
        return new Rect(x,y,Math.Max(0,Math.Floor(area.Right)-x),Math.Max(0,Math.Floor(area.Bottom)-y));
    }
}

sealed class TaskbarSpaceSnapshot
{
    public IntPtr Taskbar;
    public Rect Bounds;
    public Rect[] Occupied=new Rect[0];
    public DateTime Captured;
    public bool Valid;
    public Rect TrayBounds=Rect.Empty;
    public bool OverflowVisible;
    public bool NativeTray;
    public Dictionary<string,Rect> Controls=new Dictionary<string,Rect>();
}

// 保留刚消失的按钮边界，避免点击期间的 UIA 短暂漏报产生假空位。
sealed class TaskbarSpaceHistory
{
    sealed class Entry {public Rect Area;public DateTime Missing=DateTime.MaxValue;}
    readonly Dictionary<string,Entry> seen=new Dictionary<string,Entry>();
    IntPtr taskbar;
    Rect bounds;
    Rect tray=Rect.Empty;
    TaskbarSpaceSnapshot latestSnapshot=new TaskbarSpaceSnapshot();
    internal DateTime NextExpiry {
        get {
            DateTime next=DateTime.MaxValue;
            foreach(var entry in seen.Values)if(entry.Missing!=DateTime.MaxValue && entry.Missing.AddMilliseconds(80)<next)next=entry.Missing.AddMilliseconds(80);
            return next;
        }
    }
    internal TaskbarSpaceSnapshot Accept(TaskbarSpaceSnapshot latest)
    {
        if(taskbar!=latest.Taskbar || bounds!=latest.Bounds){seen.Clear();taskbar=latest.Taskbar;bounds=latest.Bounds;tray=Rect.Empty;}
        latestSnapshot=latest;
        // 展开托盘时提供程序会临时移除箭头边界，沿用展开前的整个托盘区域。
        if(latest.NativeTray || !latest.OverflowVisible || tray.IsEmpty)tray=latest.TrayBounds;
        var controls=new Dictionary<string,Rect>(latest.Controls);
        if(controls.Count==0)foreach(Rect area in latest.Occupied)if(area!=latest.TrayBounds)controls[area.ToString(System.Globalization.CultureInfo.InvariantCulture)]=area;
        // 同一控件移动时直接替换坐标，不能把新旧位置一起当作占用。
        foreach(var control in controls)seen[control.Key]=new Entry {Area=control.Value};
        foreach(var entry in seen)if(!controls.ContainsKey(entry.Key) && entry.Value.Missing==DateTime.MaxValue)entry.Value.Missing=latest.Captured;
        Expire(latest.Captured);
        return Snapshot();
    }
    internal bool Expire(DateTime now)
    {
        var expired=new List<string>();
        foreach(var entry in seen)if(entry.Value.Missing!=DateTime.MaxValue && now>=entry.Value.Missing.AddMilliseconds(80))expired.Add(entry.Key);
        foreach(string id in expired)seen.Remove(id);
        return expired.Count>0;
    }
    internal TaskbarSpaceSnapshot Snapshot()
    {
        var occupied=new HashSet<Rect>();foreach(var entry in seen.Values)occupied.Add(entry.Area);
        // 托盘采用唯一的当前槽位，宽度改变时不保留旧槽位。
        if(!tray.IsEmpty)occupied.Add(tray);
        var areas=new Rect[occupied.Count];occupied.CopyTo(areas);
        return new TaskbarSpaceSnapshot {Taskbar=taskbar,Bounds=bounds,Occupied=areas,Captured=latestSnapshot.Captured,Valid=latestSnapshot.Valid,TrayBounds=tray,OverflowVisible=latestSnapshot.OverflowVisible,NativeTray=latestSnapshot.NativeTray};
    }
}

// 未确认的布局只作为候选，避免窗口跟着任务栏动画反复跳动。
sealed class TaskbarSpaceStability
{
    bool initialized;
    Rect accepted,candidate;
    Rect space=Rect.Empty;
    DateTime candidateAt;
    internal bool Confirmed=true;
    internal bool Waiting {get {return initialized && candidate!=accepted;}}
    internal Rect Constrain(Rect safe,DateTime now)
    {
        return Update(safe,now,initialized && safe.Contains(accepted));
    }
    internal Rect ConstrainInSpace(Rect safe,DateTime now,Rect bar,Rect[] occupied,double scale)
    {
        // 任务栏位置或尺寸变化后，旧位置不再参与防抖。
        if(space!=bar){space=bar;initialized=false;}
        return Update(safe,now,initialized && TaskbarSpaceLayout.IsFree(accepted,bar,occupied,scale));
    }
    Rect Update(Rect safe,DateTime now,bool canKeep)
    {
        if(!initialized){
            initialized=true;candidate=safe;candidateAt=now;
            accepted=Confirmed?safe:new Rect(safe.X,safe.Y,0,safe.Height);
            return accepted;
        }
        if(safe==accepted){candidate=safe;candidateAt=now;return accepted;}
        if(candidate!=safe){candidate=safe;candidateAt=now;}
        // 计算期间保留原位和排版，重叠部分由显示裁切处理，不隐藏整块窗口。
        if(!Confirmed)return accepted;
        bool growing=safe.Width>accepted.Width;
        bool moving=safe.X!=accepted.X || safe.Y!=accepted.Y || safe.Height!=accepted.Height;
        // 旧位置仍安全时保留完整位置；不要在等待扩展时先把封面移到左侧。
        if(!(growing || moving) || (!canKeep && accepted.Width>0)){
            accepted=candidate=safe;candidateAt=now;return accepted;
        }
        if(now-candidateAt>=TimeSpan.FromMilliseconds(50))accepted=safe;
        return accepted;
    }
}

sealed class TaskbarSpaceMonitor : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] struct NativeRect {public int Left,Top,Right,Bottom;}
    delegate bool WindowCallback(IntPtr handle,IntPtr parameter);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent,WindowCallback callback,IntPtr parameter);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr handle,System.Text.StringBuilder name,int count);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr handle,out NativeRect rect);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr handle);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string cls,string title);
    readonly int process=Process.GetCurrentProcess().Id;
    readonly TaskbarSpaceHistory history=new TaskbarSpaceHistory();
    Task<TaskbarSpaceSnapshot> pending;
    TaskbarSpaceSnapshot snapshot=new TaskbarSpaceSnapshot();
    TaskbarSpaceSnapshot measured=new TaskbarSpaceSnapshot();
    int matchingMeasurements;
    DateTime geometryChangedAt;
    internal bool PlacementReady {get {return pending==null && matchingMeasurements>=2 && DateTime.UtcNow-geometryChangedAt>=TimeSpan.FromMilliseconds(60) && SameGeometry(snapshot,measured);}}
    DateTime nextScanUtc;
    readonly TaskbarSpaceRefresh refresh=new TaskbarSpaceRefresh();
    TaskbarChangeListener listener;
    IntPtr observedTaskbar;
    Rect observedBounds=Rect.Empty;
    volatile bool disposed;
    readonly Dispatcher dispatcher=Dispatcher.CurrentDispatcher;
    readonly DispatcherTimer wakeup;
    int queued;
    internal event Action Updated=delegate{};
    internal int ScanCount;

    internal TaskbarSpaceMonitor()
    {
        wakeup=new DispatcherTimer(DispatcherPriority.Render,dispatcher);
        wakeup.Tick+=(sender,e)=>{wakeup.Stop();Pump(true);};
    }
    void Changed(){refresh.Changed();Wake();}
    void ApplicationsChanged(){refresh.ApplicationsChanged();Wake();}
    void Wake()
    {
        // 事件和扫描完成都直接唤醒布局，不等待播放器的 HTTP 下一帧。
        if(disposed || Interlocked.Exchange(ref queued,1)!=0)return;
        dispatcher.BeginInvoke(DispatcherPriority.Render,new Action(()=>{
            Interlocked.Exchange(ref queued,0);if(!disposed)Pump(true);
        }));
    }
    void Pump(bool notify)
    {
        if(disposed)return;
        bool updated=false;
        if(pending!=null && pending.IsCompleted){
            if(pending.Status==TaskStatus.RanToCompletion){
                var latest=pending.Result;
                // 短暂的 UIA 失败不覆盖最后一次有效布局。
                if(latest.Valid && latest.Taskbar==observedTaskbar && latest.Bounds==observedBounds){
                    bool changed=!SameGeometry(measured,latest);
                    matchingMeasurements=changed?1:matchingMeasurements+1;measured=latest;
                    var next=history.Accept(latest);
                    if(!SameGeometry(snapshot,next)){geometryChangedAt=DateTime.UtcNow;updated=true;}
                    snapshot=next;
                    if(changed)refresh.Settle(DateTime.UtcNow);
                }else refresh.Settle(DateTime.UtcNow);
            }
            pending=null;
        }
        // 缺失边界按时间自行释放，不依赖下一次 Explorer 事件。
        if(history.Expire(DateTime.UtcNow)){
            var next=history.Snapshot();
            if(!SameGeometry(snapshot,next)){geometryChangedAt=DateTime.UtcNow;updated=true;}
            snapshot=next;
        }
        if(pending==null && observedTaskbar!=IntPtr.Zero){
            DateTime now=DateTime.UtcNow;
            // 新事件抢占收尾等待；扫描之间只保留一帧的合并窗口。
            if(now>=nextScanUtc && refresh.Take(now)){
                IntPtr bar=observedTaskbar;Rect bounds=observedBounds;
                nextScanUtc=now.AddMilliseconds(16);wakeup.Stop();
                pending=Task.Run(()=>Scan(bar,bounds));ScanCount++;
                pending.ContinueWith(completed=>Wake());
            }else {
                DateTime due=refresh.Next;
                if(due!=DateTime.MaxValue && nextScanUtc!=DateTime.MaxValue){
                    if(due<nextScanUtc)due=nextScanUtc;
                    wakeup.Stop();wakeup.Interval=TimeSpan.FromMilliseconds(Math.Max(1,(due-now).TotalMilliseconds));wakeup.Start();
                }
            }
        }
        DateTime expiry=history.NextExpiry;
        if(expiry!=DateTime.MaxValue && (!wakeup.IsEnabled || expiry<DateTime.UtcNow+wakeup.Interval)){
            wakeup.Stop();wakeup.Interval=TimeSpan.FromMilliseconds(Math.Max(1,(expiry-DateTime.UtcNow).TotalMilliseconds));wakeup.Start();
        }
        if(updated && notify)Updated();
    }
    internal static bool SameGeometry(TaskbarSpaceSnapshot first,TaskbarSpaceSnapshot second)
    {
        if(!first.Valid || first.Taskbar!=second.Taskbar || first.Bounds!=second.Bounds || first.TrayBounds!=second.TrayBounds || first.NativeTray!=second.NativeTray)return false;
        return new HashSet<Rect>(first.Occupied).SetEquals(second.Occupied);
    }

    static bool OverflowVisible(Rect bar)
    {
        foreach(string cls in new[]{"TopLevelWindowForOverflowXamlIsland","NotifyIconOverflowWindow"}){
            IntPtr window=FindWindow(cls,null);NativeRect rect;
            if(window!=IntPtr.Zero && IsWindowVisible(window) && GetWindowRect(window,out rect) && rect.Right>bar.Left && rect.Left<bar.Right)return true;
        }
        return false;
    }

    internal TaskbarSpaceSnapshot Read(IntPtr taskbar,Rect bounds)
    {
        DateTime now=DateTime.UtcNow;
        if(disposed)return new TaskbarSpaceSnapshot {Taskbar=taskbar,Bounds=bounds};
        if(observedTaskbar!=taskbar || observedBounds!=bounds){
            if(listener!=null)listener.Dispose();
            observedTaskbar=taskbar;observedBounds=bounds;
            listener=IsWindow(taskbar)?new TaskbarChangeListener(taskbar,Changed,ApplicationsChanged):null;
            Changed();
        }
        if(taskbar==IntPtr.Zero)return new TaskbarSpaceSnapshot {Taskbar=taskbar,Bounds=bounds,Captured=now,Valid=true};
        Pump(false);
        // Explorer 暂时无响应时不猜测空位，也不在 UI 线程等待它。
        if(!snapshot.Valid || snapshot.Taskbar!=taskbar)
            return new TaskbarSpaceSnapshot {Taskbar=taskbar,Bounds=bounds};
        if(snapshot.Bounds!=bounds)return new TaskbarSpaceSnapshot {Taskbar=taskbar,Bounds=bounds};
        return snapshot;
    }

    public void Dispose()
    {
        if(disposed)return;
        disposed=true;wakeup.Stop();if(listener!=null)listener.Dispose();
    }

    internal TaskbarSpaceSnapshot Scan(IntPtr taskbar,Rect bounds)
    {
        var result=new TaskbarSpaceSnapshot {Taskbar=taskbar,Bounds=bounds};
        try {
            var occupied=new List<Rect>();
            Rect tray=Rect.Empty;
            Rect nativeTray=Rect.Empty;
            var controls=new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Edit),
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.ListItem),
                new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Custom));
            var condition=new AndCondition(controls,new NotCondition(new PropertyCondition(AutomationElement.ProcessIdProperty,process)));
            var cache=new CacheRequest {TreeScope=TreeScope.Element,AutomationElementMode=AutomationElementMode.None};
            foreach(AutomationProperty property in new[]{AutomationElement.BoundingRectangleProperty,AutomationElement.IsOffscreenProperty,AutomationElement.ControlTypeProperty,AutomationElement.ClassNameProperty,AutomationElement.AutomationIdProperty,AutomationElement.RuntimeIdProperty})cache.Add(property);
            int found=0;
            var root=AutomationElement.FromHandle(taskbar);
            using(cache.Activate()){
                var items=root.FindAll(TreeScope.Descendants,condition);
                for(int i=0;i<items.Count;i++){
                    var current=items[i].Cached;
                    if(current.IsOffscreen)continue;
                    if(current.ControlType==ControlType.Custom){
                        string id=current.AutomationId,cls=current.ClassName;
                        // 不把横跨整条任务栏的 XAML 容器误当作应用按钮。
                        if(!(cls.Contains("TaskListButton") || cls.Contains("SearchBox") || cls.Contains("SystemTray") || id=="StartButton" || id=="WidgetsButton"))continue;
                    }
                    Rect rect=current.BoundingRectangle;
                    if(rect.IsEmpty || double.IsNaN(rect.X) || double.IsInfinity(rect.X) || double.IsNaN(rect.Y) || double.IsInfinity(rect.Y) || rect.Width<=0 || rect.Height<=0)continue;
                    Rect clipped=Rect.Intersect(bounds,rect);
                    if(clipped.IsEmpty || clipped.Width<=0 || clipped.Height<=0)continue;
                    // 浮层中的项目即使擦到任务栏，也不属于任务栏按钮。
                    if(!bounds.Contains(new Point(rect.Left+rect.Width/2,rect.Top+rect.Height/2)))continue;
                    if(current.ClassName.Contains("SystemTray"))tray.Union(clipped);
                    else {
                        occupied.Add(clipped);
                        var runtime=items[i].GetCachedPropertyValue(AutomationElement.RuntimeIdProperty,true) as int[];
                        if(runtime!=null && runtime.Length>0)result.Controls["uia:"+String.Join(".",Array.ConvertAll(runtime,value=>value.ToString(System.Globalization.CultureInfo.InvariantCulture)))]=clipped;
                        else result.Controls["rect:"+clipped.ToString(System.Globalization.CultureInfo.InvariantCulture)]=clipped;
                    }
                    found++;
                }
            }
            // Win10 系统托盘和部分外壳替换仍以原生子窗口提供边界。
            EnumChildWindows(taskbar,(handle,parameter)=>{
                var name=new System.Text.StringBuilder(256);GetClassName(handle,name,name.Capacity);
                string cls=name.ToString();NativeRect rect;
                if((cls=="TrayNotifyWnd" || cls=="TrayClockWClass" || cls=="TrayShowDesktopButtonWClass" || cls=="Start" || cls=="SearchPane" || cls=="SearchBox") && IsWindowVisible(handle) && GetWindowRect(handle,out rect) && rect.Right>rect.Left && rect.Bottom>rect.Top){
                    Rect area=Rect.Intersect(bounds,new Rect(rect.Left,rect.Top,rect.Right-rect.Left,rect.Bottom-rect.Top));
                    if(cls=="TrayNotifyWnd")nativeTray.Union(area);
                    else if(cls=="TrayClockWClass" || cls=="TrayShowDesktopButtonWClass")tray.Union(area);
                    else {occupied.Add(area);result.Controls["hwnd:"+handle.ToInt64()]=area;}
                }
                return true;
            },IntPtr.Zero);
            tray=TaskbarSpaceLayout.TrayBoundary(bounds,nativeTray,tray);
            // 托盘内部按钮由整个槽位统一表示，不能在槽位缩小时留下旧图标边界。
            if(!tray.IsEmpty){
                occupied.RemoveAll(area=>tray.Contains(area));
                var inside=new List<string>();foreach(var item in result.Controls)if(tray.Contains(item.Value))inside.Add(item.Key);
                foreach(string id in inside)result.Controls.Remove(id);
            }
            if(!tray.IsEmpty)occupied.Add(tray);
            result.TrayBounds=tray;result.OverflowVisible=OverflowVisible(bounds);
            result.NativeTray=!nativeTray.IsEmpty;
            result.Occupied=occupied.ToArray();result.Valid=found>=2;
        } catch(ElementNotAvailableException) {} catch(System.Runtime.InteropServices.COMException) {} catch(InvalidOperationException) {} catch(UnauthorizedAccessException) {} catch(ArgumentException) {}
        result.Captured=DateTime.UtcNow;
        return result;
    }
}
