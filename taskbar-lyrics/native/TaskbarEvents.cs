using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

// 事件立即扫描，布局动画期间短暂跟进，空闲时不产生扫描任务。
sealed class TaskbarSpaceRefresh
{
    long requested,observed;
    long applications,observedApplications;
    int remaining;
    DateTime confirmAt;
    DateTime settleUntil;
    DateTime watchUntil;
    internal DateTime Next {get {return Interlocked.Read(ref requested)!=observed?DateTime.MinValue:remaining==0?DateTime.MaxValue:confirmAt;}}
    internal void Changed(){Interlocked.Increment(ref requested);}
    internal void ApplicationsChanged(){Interlocked.Increment(ref applications);Changed();}
    internal bool Take(DateTime now)
    {
        long current=Interlocked.Read(ref requested);
        if(current!=observed){observed=current;remaining=2;confirmAt=now;settleUntil=now.AddMilliseconds(650);}
        long windows=Interlocked.Read(ref applications);
        if(windows!=observedApplications){observedApplications=windows;watchUntil=now.AddMilliseconds(400);}
        if(remaining==0 || now<confirmAt)return false;
        remaining--;if(remaining==0 && now<watchUntil)remaining=1;
        confirmAt=now.AddMilliseconds(40);return true;
    }
    internal void Settle(DateTime now)
    {
        // 扫描中布局仍在变化时继续确认，稳定即停止，单批事件最多跟进 650ms。
        if(now<settleUntil && remaining==0){remaining=1;confirmAt=now.AddMilliseconds(40);}
    }
}

sealed class TaskbarChangeListener : IDisposable
{
    delegate void WinEvent(IntPtr hook,uint evt,IntPtr hwnd,int objectId,int childId,uint threadId,uint time);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,WinEvent callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] static extern bool IsChild(IntPtr parent,IntPtr child);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint process);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd,int index);
    readonly IntPtr taskbar,hook,applicationsHook;
    readonly int ownProcess=System.Diagnostics.Process.GetCurrentProcess().Id;
    readonly Action changed;
    readonly Action applicationsChanged;
    readonly WinEvent callback,applicationsCallback;
    readonly uint shellProcess;
    readonly object subscriptions=new object();
    readonly AutomationPropertyChangedEventHandler propertyChanged;
    readonly StructureChangedEventHandler structureChanged;
    AutomationElement root;
    int disposed;

    internal TaskbarChangeListener(IntPtr bar,Action notify,Action notifyApplications)
    {
        taskbar=bar;changed=notify;callback=OnNativeEvent;applicationsChanged=notifyApplications;applicationsCallback=OnApplicationsEvent;
        propertyChanged=(sender,e)=>OnAutomationEvent(sender);
        structureChanged=(sender,e)=>OnAutomationEvent(sender);
        uint shell;GetWindowThreadProcessId(bar,out shell);shellProcess=shell;
        // 在有消息循环的宿主线程注册；自身窗口的显示与歌词动画不会反馈到这里。
        hook=SetWinEventHook(0x8000,0x800F,IntPtr.Zero,callback,shellProcess,0,2);
        // 新应用的窗口先出现，Explorer 的按钮可能稍后才加入任务栏。
        applicationsHook=SetWinEventHook(0x8002,0x8003,IntPtr.Zero,applicationsCallback,0,0,2);
        Task.Run(()=>{
            lock(subscriptions){
                if(Volatile.Read(ref disposed)!=0)return;
                try {
                    root=AutomationElement.FromHandle(taskbar);
                    var cache=new CacheRequest();cache.Add(AutomationElement.ProcessIdProperty);
                    using(cache.Activate()){
                        Automation.AddStructureChangedEventHandler(root,TreeScope.Subtree,structureChanged);
                        Automation.AddAutomationPropertyChangedEventHandler(root,TreeScope.Subtree,propertyChanged,AutomationElement.BoundingRectangleProperty,AutomationElement.IsOffscreenProperty,AutomationElement.NameProperty);
                    }
                    Notify();
                } catch(ElementNotAvailableException) {} catch(COMException) {} catch(InvalidOperationException) {} catch(UnauthorizedAccessException) {} catch(ArgumentException) {}
            }
        });
    }
    void Notify(){if(Volatile.Read(ref disposed)==0)changed();}
    void OnNativeEvent(IntPtr subscription,uint evt,IntPtr hwnd,int objectId,int childId,uint threadId,uint time)
    {
        if(hwnd!=taskbar && !IsChild(taskbar,hwnd))return;
        if(evt<=0x8004 || evt==0x800B || evt==0x800C || evt==0x800F)Notify();
    }
    void OnAutomationEvent(object sender)
    {
        if(Volatile.Read(ref disposed)!=0)return;
        try {
            var element=(AutomationElement)sender;
            object process=element.GetCachedPropertyValue(AutomationElement.ProcessIdProperty,true);
            if(process is int && (int)process==ownProcess)return;
            Notify();
        } catch(ElementNotAvailableException) {} catch(COMException) {} catch(InvalidOperationException) {}
    }
    void OnApplicationsEvent(IntPtr subscription,uint evt,IntPtr hwnd,int objectId,int childId,uint threadId,uint time)
    {
        if(Volatile.Read(ref disposed)!=0 || objectId!=0 || childId!=0 || hwnd==IntPtr.Zero || GetAncestor(hwnd,2)!=hwnd)return;
        uint process;GetWindowThreadProcessId(hwnd,out process);
        if(hwnd==taskbar || process==(uint)ownProcess || (GetWindowLong(hwnd,-20)&0x80)!=0)return;
        applicationsChanged();
    }
    public void Dispose()
    {
        if(Interlocked.Exchange(ref disposed,1)!=0)return;
        if(hook!=IntPtr.Zero)UnhookWinEvent(hook);
        if(applicationsHook!=IntPtr.Zero)UnhookWinEvent(applicationsHook);
        // COM 退订在后台串行完成，关闭插件不等待 Explorer 响应。
        Task.Run(()=>{
            lock(subscriptions){
                if(root==null)return;
                try {Automation.RemoveStructureChangedEventHandler(root,structureChanged);}catch(ElementNotAvailableException){}catch(COMException){}catch(InvalidOperationException){}
                try {Automation.RemoveAutomationPropertyChangedEventHandler(root,propertyChanged);}catch(ElementNotAvailableException){}catch(COMException){}catch(InvalidOperationException){}
                root=null;
            }
        });
        GC.KeepAlive(callback);
        GC.KeepAlive(applicationsCallback);
    }
}
