using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

// UI 在 XAML 中声明；这里只绑定数据和 Windows 窗口行为。
sealed class TaskbarLyrics
{
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; public int Width { get { return Right-Left; } } public int Height { get { return Bottom-Top; } } }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string Device; }
    delegate bool MonitorCallback(IntPtr monitor,IntPtr dc,IntPtr rect,IntPtr data);
    delegate bool WindowCallback(IntPtr hwnd,IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc,IntPtr clip,MonitorCallback callback,IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback callback,IntPtr data);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor,int type,out uint x,out uint y);
    sealed class Display { public IntPtr Monitor,Taskbar; public MonitorInfo Info; public double Scale; }
    Display display;
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string name, string title);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string cls,string title);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW",SetLastError=true)] static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] static extern bool GetWindowBand(IntPtr hwnd,out uint band);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    delegate void WinEvent(IntPtr hook,uint evt,IntPtr hwnd,int objectId,int childId,uint threadId,uint time);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,WinEvent callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint process);
    [DllImport("user32.dll")] static extern bool IsChild(IntPtr parent,IntPtr child);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd,uint command);
    [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int awareness);

    Window window;
    TextBlock primary, secondary;
    TextBlock primaryPlayed, secondaryPlayed;
    RectangleGeometry primaryMask, secondaryMask;
    Grid primaryViewport, secondaryViewport;
    TranslateTransform primaryShift, secondaryShift;
    readonly Stopwatch lyricClock = new Stopwatch();
    double timelineMs, lineStartMs, lineEndMs, playbackRate;
    bool clockPlaying, scrolling, scrollingTranslation;
    bool masking, allowDisplay;
    double primaryTextWidth, secondaryTextWidth;
    IntPtr foregroundHook, reorderHook;
    readonly WinEvent shellEvent;
    int shellRaises;
    bool restoreQueued;
    sealed class MaskStop { public double Start,End,Left,Right; }
    List<MaskStop> primaryStops=new List<MaskStop>(), secondaryStops=new List<MaskStop>();
    string primaryProfileKey="",secondaryProfileKey="";
    IntPtr handle,ownerBar;
    DateTime raised = DateTime.MinValue;
    static readonly HttpClient http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };
    static readonly JavaScriptSerializer json = new JavaScriptSerializer();
    static readonly Dictionary<string,object> defaults = new Dictionary<string,object> {
        {"enabled",true},{"layout","single"},{"width",360},{"fontSize",16},{"offset",12},
        {"opacity",100},{"theme","auto"},{"hidePaused",false},{"hideFullscreen",true},{"scrollLyrics",true},{"secondary","next"},
        {"primaryFontSize",16},{"secondaryFontSize",12},{"progressMask",false},
        {"primaryColor","#F1F7F6"},{"secondaryColor","#B4C3C1"},{"primaryPlayedColor","#85DCD0"},{"secondaryPlayedColor","#85DCD0"}
    };


    TaskbarLyrics(Display target)
    {
        display=target;shellEvent=OnShellEvent;
            // 嵌入 XAML，发布时无需额外 UI 文件或运行时安装。
            using (Stream stream = typeof(TaskbarLyrics).Assembly.GetManifestResourceStream("LyricsWindow.xaml"))
                window = (Window)System.Windows.Markup.XamlReader.Load(stream);
            primary = (TextBlock)window.FindName("Primary");
            secondary = (TextBlock)window.FindName("Secondary");
            primaryViewport = (Grid)window.FindName("PrimaryViewport");
            secondaryViewport = (Grid)window.FindName("SecondaryViewport");
            primaryShift = (TranslateTransform)window.FindName("PrimaryShift");
            secondaryShift = (TranslateTransform)window.FindName("SecondaryShift");
            primaryPlayed = (TextBlock)window.FindName("PrimaryPlayed");
            secondaryPlayed = (TextBlock)window.FindName("SecondaryPlayed");
            primaryMask = (RectangleGeometry)window.FindName("PrimaryMask");
            secondaryMask = (RectangleGeometry)window.FindName("SecondaryMask");

        handle=new WindowInteropHelper(window).EnsureHandle();
        SetWindowLong(handle,-20,GetWindowLong(handle,-20)|0x20|0x80|0x8000000);
        HwndSource.FromHwnd(handle).AddHook(Hook);
    }
    void Open()
    {
        // 首次显示前移到目标屏，避免 WPF 沿用当前活动屏的绘制坐标。
        Rect initial=display.Info.Monitor;
        if(display.Taskbar!=IntPtr.Zero)GetWindowRect(display.Taskbar,out initial);
        else initial.Top=initial.Bottom-(int)Math.Round(48*display.Scale);
        SetWindowPos(handle,IntPtr.Zero,initial.Left,initial.Top,(int)(360*display.Scale),initial.Height,0x14);
        window.Opacity=0;window.Show();ShowWindow(handle,0);
        CompositionTarget.Rendering+=RenderScroll;
        foregroundHook=SetWinEventHook(3,3,IntPtr.Zero,shellEvent,0,0,2);
        reorderHook=SetWinEventHook(0x8002,0x8004,IntPtr.Zero,shellEvent,0,0,2);
    }
    void Close()
    {
        CompositionTarget.Rendering-=RenderScroll;
        UnhookWinEvent(foregroundHook);UnhookWinEvent(reorderHook);window.Close();
    }
    object Report()
    {
                        Rect actual; GetWindowRect(handle,out actual);uint selfBand,barBand;GetWindowBand(handle,out selfBand);GetWindowBand(display.Taskbar,out barBand);
                        return new {
                            text=primary.Text, secondary=secondary.Text, secondaryVisible=secondary.Visibility==Visibility.Visible,
                            fontSize=primary.FontSize, visualScale=VisualTreeHelper.GetDpi(primary).DpiScaleX, windowDpi=GetDpiForWindow(handle), screenScale=display.Scale, visible=IsWindowVisible(handle),
                            primaryOffset=primaryShift.X, secondaryOffset=secondaryShift.X,
                            textWidth=double.IsNaN(primary.Width) ? 0 : primary.Width,
                            viewportWidth=double.IsNaN(primaryViewport.Width) ? 0 : primaryViewport.Width,
                            secondaryFontSize=secondary.FontSize, fontFamily=primary.FontFamily.Source,
                            primaryColor=primary.Foreground.ToString(), secondaryColor=secondary.Foreground.ToString(),
                            maskVisible=primaryPlayed.Visibility==Visibility.Visible, primaryMaskWidth=primaryMask.Rect.Width,
                            secondaryMaskWidth=secondaryMask.Rect.Width, shellRaises=shellRaises, aboveTaskbar=AboveTaskbar(), shellCoversLyrics=ShellCoversLyrics(),
                            owned=ownerBar!=IntPtr.Zero && GetWindow(handle,4)==ownerBar,owner=GetWindow(handle,4).ToInt64(),taskbar=display.Taskbar.ToInt64(),windowBand=selfBand,taskbarBand=barBand,
                            bounds=new[]{actual.Left,actual.Top,actual.Width,actual.Height},
                            mouseTransparent=(GetWindowLong(handle,-20)&0x20)!=0,
                            noActivate=(GetWindowLong(handle,-20)&0x8000000)!=0
                        };

    }
    static List<Display> Screens()
    {
        var result=new List<Display>();
        EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,(monitor,dc,rect,data)=>{
            var info=new MonitorInfo { Size=Marshal.SizeOf(typeof(MonitorInfo)) };
            if(GetMonitorInfo(monitor,ref info)) {
                uint x,y;GetDpiForMonitor(monitor,0,out x,out y);
                result.Add(new Display {Monitor=monitor,Info=info,Scale=Math.Max(.5,x/96.0)});
            }
            return true;
        },IntPtr.Zero);
        // 开始菜单打开时，主任务栏进入特殊层级，不再出现在 EnumWindows 中。
        var bars=new List<IntPtr>();IntPtr mainBar=FindWindow("Shell_TrayWnd",null);
        if(mainBar!=IntPtr.Zero)bars.Add(mainBar);
        for(IntPtr bar=FindWindowEx(IntPtr.Zero,IntPtr.Zero,"Shell_SecondaryTrayWnd",null);bar!=IntPtr.Zero;bar=FindWindowEx(IntPtr.Zero,bar,"Shell_SecondaryTrayWnd",null))bars.Add(bar);
        foreach(IntPtr bar in bars)foreach(var item in result)if(item.Monitor==MonitorFromWindow(bar,2))item.Taskbar=bar;
        result.Sort((a,b)=>{int primary=(b.Info.Flags&1).CompareTo(a.Info.Flags&1);return primary!=0?primary:string.CompareOrdinal(a.Info.Device,b.Info.Device);});
        return result;
    }
    static object[] Inventory(List<Display> screens)
    {
        var result=new List<object>();
        // 界面按当前连接屏幕连续编号；设备标识仅用于保存选择和窗口匹配。
        int number=0;
        foreach(var item in screens) result.Add(new {id=item.Info.Device,label="屏幕 "+(++number)+" · "+item.Info.Monitor.Width+" × "+item.Info.Monitor.Height+((item.Info.Flags&1)!=0?"（主屏幕）":""),primary=(item.Info.Flags&1)!=0,bounds=new[]{item.Info.Monitor.Left,item.Info.Monitor.Top,item.Info.Monitor.Width,item.Info.Monitor.Height},scale=item.Scale});
        return result.ToArray();
    }
    [STAThread] static int Main(string[] args)
    {
        try {
            // 旧编译器默认使用系统 DPI；开启 WPF 每屏缩放以避免副屏文字放大。
            AppContext.SetSwitch("Switch.System.Windows.DoNotScaleForDpiChanges",false);
            SetProcessDpiAwareness(2);
            if(args.Length==1 && args[0]=="--raise-taskbar") {SetWindowPos(FindWindow("Shell_TrayWnd",null),new IntPtr(-1),0,0,0,0,0x13);return 0;}
            if(args.Length==2 && args[0]=="--screens") {File.WriteAllText(args[1],json.Serialize(Inventory(Screens())),Encoding.UTF8);return 0;}
            if(args.Length==2 && (args[0]=="--preview" || args[0]=="--probe")) {
                var preview=new TaskbarLyrics(Screens()[0]);
                if(args[0]=="--preview") preview.Preview(args[1]);else preview.Probe(args[1]);return 0;
            }
            Uri endpoint;
            if(args.Length<1 || args.Length>3 || !Uri.TryCreate(args[0],UriKind.Absolute,out endpoint) || endpoint.Scheme!="http" || endpoint.Host!="127.0.0.1") return 2;
            var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
            var surfaces=new Dictionary<string,TaskbarLyrics>();
            var clock=Stopwatch.StartNew();var reorderClock=new Stopwatch();bool polling=false,reordered=false;int failures=0;
            string inventory="";var screens=new List<Display>();DateTime checkedAt=DateTime.MinValue;
            var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(100)};
            timer.Tick+=async(sender,e)=>{
                if(polling)return;polling=true;
                try {
                    var frame=json.Deserialize<Dictionary<string,object>>(await http.GetStringAsync(endpoint));
                    // 周期刷新设备与任务栏，支持接入、断开和 Explorer 重启。
                    if((DateTime.UtcNow-checkedAt).TotalSeconds>=1) {
                        screens=Screens();checkedAt=DateTime.UtcNow;
                        string next=json.Serialize(Inventory(screens));
                        if(next!=inventory) {
                            var url=new UriBuilder(endpoint);url.Path="/displays";url.Query=endpoint.Query.TrimStart('?')+"&data="+Uri.EscapeDataString(next);
                            await http.GetStringAsync(url.Uri);inventory=next;
                        }
                    }
                    if(screens.Count==0)return;
                    var settings=(Dictionary<string,object>)frame["settings"];
                    string selection=settings.ContainsKey("display")?Convert.ToString(settings["display"]):"primary";
                    var targets=screens.FindAll(item=>selection=="all" || (selection=="primary"?(item.Info.Flags&1)!=0:item.Info.Device==selection));
                    if(targets.Count==0) targets.Add(screens[0]);
                    var wanted=new HashSet<string>();foreach(var target in targets)wanted.Add(target.Info.Device);
                    foreach(string id in new List<string>(surfaces.Keys))if(!wanted.Contains(id)){surfaces[id].Close();surfaces.Remove(id);}
                    foreach(var target in targets) {
                        TaskbarLyrics surface;
                        if(surfaces.TryGetValue(target.Info.Device,out surface) && !IsWindow(surface.handle)){surface.Close();surfaces.Remove(target.Info.Device);}
                        if(!surfaces.TryGetValue(target.Info.Device,out surface)){surface=new TaskbarLyrics(target);surface.Open();surfaces.Add(target.Info.Device,surface);}
                        surface.display=target;
                        surface.Apply(frame);
                    }
                    failures=0;
                    if(args.Length==3 && !reordered && clock.ElapsedMilliseconds>=200){Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,"--raise-taskbar"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});reordered=true;reorderClock.Restart();}
                    if(args.Length==2 || (args.Length==3 && reordered && reorderClock.ElapsedMilliseconds>=400)) {
                        var reports=new Dictionary<string,object>();foreach(var item in surfaces)reports.Add(item.Key,item.Value.Report());
                        // 保留旧验证报告字段，并附上各屏幕窗口以验证选择结果。
                        var first=json.Deserialize<Dictionary<string,object>>(json.Serialize(surfaces[targets[0].Info.Device].Report()));
                        first["screens"]=Inventory(screens);first["surfaces"]=reports;
                        File.WriteAllText(args[1],json.Serialize(first),Encoding.UTF8);app.Shutdown();
                    }
                } catch(Exception ex) {
                    failures++;if(failures==3)foreach(var surface in surfaces.Values)ShowWindow(surface.handle,0);
                    if(failures>=12){Log(ex.ToString());app.Shutdown();}
                } finally {polling=false;}
            };
            timer.Start();app.Run();timer.Stop();foreach(var surface in surfaces.Values)surface.Close();return 0;
        }catch(Exception ex){Log(ex.ToString());return 1;}
    }

    IntPtr Hook(IntPtr hwnd,int message,IntPtr w,IntPtr l,ref bool handled)
    {
        if (message == 0x21) { handled=true; return new IntPtr(3); }
        if (message == 0x84) { handled=true; return new IntPtr(-1); }
        return IntPtr.Zero;
    }

    bool Fullscreen(IntPtr taskbar)
    {
        IntPtr active = GetForegroundWindow();
        if (active == IntPtr.Zero || active == handle || active == taskbar) return false;
        if (IsShellSurface(active,taskbar)) return false;
        var name = new StringBuilder(256);
        GetClassName(active,name,name.Capacity);
        if (name.ToString() == "Progman" || name.ToString() == "WorkerW") return false;
        if (MonitorFromWindow(active,2) != display.Monitor) return false;
        var monitor = new MonitorInfo { Size=Marshal.SizeOf(typeof(MonitorInfo)) };
        Rect bounds;
        if (!GetMonitorInfo(display.Monitor,ref monitor) || !GetWindowRect(active,out bounds)) return false;
        return bounds.Left <= monitor.Monitor.Left && bounds.Top <= monitor.Monitor.Top && bounds.Right >= monitor.Monitor.Right && bounds.Bottom >= monitor.Monitor.Bottom;
    }

    static bool IsShellSurface(IntPtr hwnd,IntPtr taskbar)
    {
        if (hwnd==IntPtr.Zero) return false;
        uint process,barProcess;GetWindowThreadProcessId(hwnd,out process);GetWindowThreadProcessId(taskbar,out barProcess);
        if(process==(uint)Process.GetCurrentProcess().Id)return false;
        if (hwnd==taskbar || (process==barProcess && (IsChild(taskbar,hwnd) || GetAncestor(hwnd,3)==taskbar))) return true;
        var name=new StringBuilder(256);GetClassName(hwnd,name,name.Capacity);
        if (process==barProcess && name.ToString()!="CabinetWClass") return true;
        try {
            string executable=Process.GetProcessById((int)process).ProcessName;
            return executable=="StartMenuExperienceHost" || executable=="ShellExperienceHost" || executable=="SearchHost" || executable=="ShellHost" || executable=="TextInputHost";
        } catch(ArgumentException) { return false; } catch(System.ComponentModel.Win32Exception) { return false; }
    }

    void OnShellEvent(IntPtr hook,uint evt,IntPtr hwnd,int objectId,int childId,uint threadId,uint time)
    {
        IntPtr bar=display.Taskbar;
        // 合并 Shell 事件并检查实际遮挡，避免反复重排挤占歌词绘制。
        if ((evt!=3 && (objectId!=0 || childId!=0)) || restoreQueued || !allowDisplay || !IsShellSurface(hwnd,bar)) return;
        restoreQueued=true;
        window.Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(() => {
            restoreQueued=false;
            if (allowDisplay && IsWindowVisible(handle) && (!AboveTaskbar() || ShellCoversLyrics())) RestoreLayer();
        }));
    }

    void OwnTaskbar(IntPtr bar)
    {
        if(ownerBar==bar && GetWindow(handle,4)==bar)return;
        // 关联任务栏所有者，保持透明浮窗高于任务栏，避免跨进程子窗口绘制异常。
        SetWindowLongPtr(handle,-8,bar);ownerBar=GetWindow(handle,4);
    }
    void Position(Rect area)
    {
        SetWindowPos(handle,new IntPtr(-1),area.Left,area.Top,area.Width,area.Height,0x10|0x40);
        raised=DateTime.UtcNow;
    }
    void RestoreLayer()
    {
        if((DateTime.UtcNow-raised).TotalMilliseconds<50)return;
        SetWindowPos(handle,new IntPtr(-1),0,0,0,0,0x13);
        shellRaises++;raised=DateTime.UtcNow;
    }

    bool AboveTaskbar()
    {
        IntPtr bar=display.Taskbar;
        if(bar==IntPtr.Zero || GetParent(handle)==bar)return true;
        for(IntPtr item=GetWindow(handle,2);item!=IntPtr.Zero;item=GetWindow(item,2)) if(item==bar) return true;
        return false;
    }

    bool ShellCoversLyrics()
    {
        // 开始菜单透明宿主也可能覆盖任务栏区域，检查实际 Shell 层级。
        Rect lyrics;GetWindowRect(handle,out lyrics);
        for(IntPtr item=GetWindow(handle,3);item!=IntPtr.Zero;item=GetWindow(item,3)) {
            Rect bounds;
            if(!IsWindowVisible(item) || !GetWindowRect(item,out bounds))continue;
            if(bounds.Left>=lyrics.Right || bounds.Right<=lyrics.Left || bounds.Top>=lyrics.Bottom || bounds.Bottom<=lyrics.Top)continue;
            if(IsShellSurface(item,display.Taskbar))return true;
        }
        return false;
    }

    static Rect Placement(Rect bar, double scale, Dictionary<string,object> s)
    {
        // 以物理像素定位，再按当前任务栏 DPI 换算用户设置。
        bool horizontal=bar.Width >= bar.Height;
        int offset=(int)Math.Round(Convert.ToDouble(s["offset"])*scale);
        int available=horizontal ? bar.Width : bar.Height;
        offset=Math.Min(Math.Max(0,offset),Math.Max(0,available-40));
        int width=horizontal ? Math.Min((int)Math.Round(Convert.ToDouble(s["width"])*scale),available-offset) : bar.Width;
        int height=horizontal ? bar.Height : Math.Min((int)(48*scale),bar.Height-offset);
        return new Rect { Left=bar.Left+(horizontal?offset:0), Top=bar.Top+(horizontal?0:offset), Right=bar.Left+(horizontal?offset:0)+width, Bottom=bar.Top+(horizontal?0:offset)+height };
    }

    void Apply(Dictionary<string,object> frame)
    {
        var s=(Dictionary<string,object>)frame["settings"];
        IntPtr taskbar=display.Taskbar;
        Rect bar=display.Info.Monitor;
        double scale=display.Scale;
        if(taskbar!=IntPtr.Zero) {
            if(!GetWindowRect(taskbar,out bar) || bar.Height<4 || bar.Width<4 || !IsWindowVisible(taskbar)){allowDisplay=false;ShowWindow(handle,0);return;}
            scale=Math.Max(.5,GetDpiForWindow(taskbar)/96.0);
        }else {
            // 副屏未启用系统任务栏时，在该屏底边保留歌词显示。
            bar.Top=bar.Bottom-(int)Math.Round(48*scale);
        }
        var monitor=new MonitorInfo { Size=Marshal.SizeOf(typeof(MonitorInfo)) };
        GetMonitorInfo(display.Monitor,ref monitor);
        bool outside=bar.Top >= monitor.Monitor.Bottom-2 || bar.Bottom <= monitor.Monitor.Top+2 || bar.Left >= monitor.Monitor.Right-2 || bar.Right <= monitor.Monitor.Left+2;
        bool hide=!Convert.ToBoolean(s["enabled"]) || !Convert.ToBoolean(frame["hasTrack"]) || outside ||
            (Convert.ToBoolean(s["hidePaused"]) && !Convert.ToBoolean(frame["playing"])) ||
            (Convert.ToBoolean(s["hideFullscreen"]) && Fullscreen(taskbar));
        allowDisplay=!hide;
        if (hide) { ShowWindow(handle,0); return; }
        OwnTaskbar(taskbar);
        Rect area=Placement(bar,scale,s);
        primary.FontFamily=secondary.FontFamily=new FontFamily(Convert.ToString(frame["fontFamily"]));
        Style(s,area.Height/scale);
        primary.Text=Convert.ToString(frame["text"]);
        secondary.Text=Convert.ToString(frame["secondary"]);
        primaryViewport.Width=secondaryViewport.Width=Math.Max(0,area.Width/scale-8);
        scrolling=Convert.ToBoolean(s["scrollLyrics"]);
        scrollingTranslation=scrolling && Convert.ToString(s["secondary"]) == "translation";
        primaryTextWidth=FitLine(primary,primaryViewport.Width,scrolling);
        secondaryTextWidth=FitLine(secondary,secondaryViewport.Width,scrollingTranslation);
        masking=Convert.ToBoolean(s["progressMask"]);
        primaryPlayed.Visibility=masking ? Visibility.Visible : Visibility.Collapsed;
        secondaryPlayed.Visibility=masking && Convert.ToString(s["secondary"])=="translation" ? Visibility.Visible : Visibility.Collapsed;
        UpdateStops(primary,frame["characters"],ref primaryProfileKey,ref primaryStops);
        UpdateStops(secondary,frame["secondaryCharacters"],ref secondaryProfileKey,ref secondaryStops);
        timelineMs=Convert.ToDouble(frame["timelineMs"]);
        lineStartMs=Convert.ToDouble(frame["lineStartMs"]);
        lineEndMs=Convert.ToDouble(frame["lineEndMs"]);
        playbackRate=Convert.ToDouble(frame["playbackRate"]);
        clockPlaying=Convert.ToBoolean(frame["playing"]);
        lyricClock.Restart();
        RenderScroll(null,EventArgs.Empty);
        Rect actual;GetWindowRect(handle,out actual);
        // WPF 切换 DPI 会调整窗口尺寸，按实际边界校正，避免沿用旧屏幕尺寸。
        bool moved=area.Left!=actual.Left || area.Top!=actual.Top || area.Width!=actual.Width || area.Height!=actual.Height;
        // 定期恢复置顶层级，同时避免每一帧重排任务栏窗口。
        if (moved || !IsWindowVisible(handle) || (DateTime.UtcNow-raised).TotalSeconds >= 2) {
            Position(area);
        }
    }

    void Style(Dictionary<string,object> s,double height)
    {
        bool dual=Convert.ToString(s["layout"]) == "double";
        double first=Convert.ToDouble(s["primaryFontSize"]),second=Convert.ToDouble(s["secondaryFontSize"]);
        double lineSpacing=primary.FontFamily.LineSpacing;
        double fit=Math.Min(1,Math.Max(1,height-4)/(lineSpacing*(first+(dual?second:0))+(dual?1:0)));
        primary.FontSize=first*fit;secondary.FontSize=second*fit;
        secondary.Visibility=dual ? Visibility.Visible : Visibility.Collapsed;
        secondaryViewport.Visibility=secondary.Visibility;
        window.Opacity=Convert.ToDouble(s["opacity"])/100;
        string theme=Convert.ToString(s["theme"]);
        bool light=theme == "light";
        if (theme == "auto") {
            object setting=Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","SystemUsesLightTheme",0);
            light=Convert.ToInt32(setting) != 0;
        }
        primary.Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString(theme=="custom" ? Convert.ToString(s["primaryColor"]) : light ? "#202D35" : "#F1F7F6"));
        secondary.Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString(theme=="custom" ? Convert.ToString(s["secondaryColor"]) : light ? "#62737B" : "#B4C3C1"));
        primaryPlayed.Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString(Convert.ToString(s["primaryPlayedColor"])));
        secondaryPlayed.Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString(Convert.ToString(s["secondaryPlayedColor"])));
    }

    static double FitLine(TextBlock text,double viewportWidth,bool scroll)
    {
        // 保留完整文本宽度，由 XAML 中的视口裁切，避免滚动时出现省略号。
        text.Width=double.NaN;
        text.TextTrimming=scroll ? TextTrimming.None : TextTrimming.CharacterEllipsis;
        text.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
        double natural=text.DesiredSize.Width;
        text.Width=scroll ? natural : viewportWidth;
        return natural;
    }

    void RenderScroll(object sender,EventArgs e)
    {
        // Shell 并非每次点击都会发出层级事件，渲染前检查可避免等待定时置顶。
        if (allowDisplay && IsWindowVisible(handle) && (!AboveTaskbar() || ShellCoversLyrics())) RestoreLayer();
        // 渲染帧推算本句进度，暂停冻结；每次宿主快照都会校准跳转与倍速。
        double current=timelineMs+(clockPlaying ? lyricClock.Elapsed.TotalMilliseconds*playbackRate : 0);
        double progress=lineEndMs>lineStartMs ? Math.Max(0,Math.Min(1,(current-lineStartMs)/(lineEndMs-lineStartMs))) : 0;
        primaryShift.X=scrolling ? -Math.Max(0,primary.Width-primaryViewport.Width)*progress : 0;
        secondaryShift.X=scrollingTranslation ? -Math.Max(0,secondary.Width-secondaryViewport.Width)*progress : 0;
        primaryMask.Rect=new System.Windows.Rect(0,0,masking ? MaskWidth(primaryStops,current,progress,primaryTextWidth) : 0,primary.FontSize*2);
        secondaryMask.Rect=new System.Windows.Rect(0,0,masking && secondaryPlayed.Visibility==Visibility.Visible ? MaskWidth(secondaryStops,current,progress,secondaryTextWidth) : 0,secondary.FontSize*2);
    }

    static double TextWidth(string text,TextBlock style)
    {
        return new FormattedText(text,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,
            new Typeface(style.FontFamily,style.FontStyle,style.FontWeight,style.FontStretch),style.FontSize,Brushes.White,VisualTreeHelper.GetDpi(style).PixelsPerDip).WidthIncludingTrailingWhitespace;
    }

    static void UpdateStops(TextBlock text,object characters,ref string cacheKey,ref List<MaskStop> stops)
    {
        string key=text.Text+"|"+text.FontFamily.Source+"|"+text.FontSize+"|"+VisualTreeHelper.GetDpi(text).PixelsPerDip+"|"+json.Serialize(characters);
        if (key==cacheKey) return;
        cacheKey=key;stops=new List<MaskStop>();
        string prefix="";
        foreach(object item in (System.Collections.IEnumerable)characters) {
            var character=(Dictionary<string,object>)item;
            double left=TextWidth(prefix,text);prefix+=Convert.ToString(character["text"]);
            stops.Add(new MaskStop { Start=Convert.ToDouble(character["startTime"]),End=Convert.ToDouble(character["endTime"]),Left=left,Right=TextWidth(prefix,text) });
        }
        if (prefix.Trim()!=text.Text.Trim()) stops.Clear();
    }

    static double MaskWidth(List<MaskStop> stops,double current,double progress,double width)
    {
        // 有逐字数据按字符时间填充；纯 LRC 按本句时间平滑填充。
        if (stops.Count==0) return width*progress;
        foreach(MaskStop stop in stops) {
            if (current<stop.Start) return Math.Min(width,stop.Left);
            if (current<stop.End && stop.End>stop.Start) return Math.Min(width,stop.Left+(stop.Right-stop.Left)*(current-stop.Start)/(stop.End-stop.Start));
        }
        return width;
    }

    void Preview(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach(string layout in new[]{"single","double"}) foreach(string theme in new[]{"dark","light"}) foreach(bool mask in new[]{false,true}) {
            var s=new Dictionary<string,object>(defaults); s["layout"]=layout; s["theme"]=theme;
            Style(s,48);
            window.Width=420; window.Height=48;
            var root=(Grid)window.Content;
            root.Background=new SolidColorBrush((Color)ColorConverter.ConvertFromString(theme=="dark" ? "#202831" : "#E8EDEF"));
            root.Width=420; root.Height=48;
            primaryPlayed.Visibility=mask ? Visibility.Visible : Visibility.Collapsed;
            secondaryPlayed.Visibility=mask && layout=="double" ? Visibility.Visible : Visibility.Collapsed;
            primaryMask.Rect=new System.Windows.Rect(0,0,TextWidth(primary.Text,primary)*.55,48);
            secondaryMask.Rect=new System.Windows.Rect(0,0,TextWidth(secondary.Text,secondary)*.55,48);
            root.Measure(new Size(420,48)); root.Arrange(new System.Windows.Rect(0,0,420,48)); root.UpdateLayout();
            var bmp=new RenderTargetBitmap(840,96,192,192,PixelFormats.Pbgra32); bmp.Render(root);
            var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp));
            using(var file=File.Create(Path.Combine(directory,(mask?"mask-":"")+layout+"-"+theme+".png"))) png.Save(file);
        }
    }

    void Probe(string path)
    {
        IntPtr taskbar=FindWindow("Shell_TrayWnd",null); Rect bar;
        if (taskbar==IntPtr.Zero || !GetWindowRect(taskbar,out bar)) throw new Exception("找不到 Windows 任务栏");
        double scale=GetDpiForWindow(taskbar)/96.0;
        Rect area=Placement(bar,scale,defaults);
        File.WriteAllText(path,json.Serialize(new { taskbar=new[]{bar.Left,bar.Top,bar.Width,bar.Height}, dpi=GetDpiForWindow(taskbar), lyrics=new[]{area.Left,area.Top,area.Width,area.Height}, mouseTransparent=true, noActivate=true }),Encoding.UTF8);
    }

    static void Log(string message)
    {
        try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"TaskbarLyrics.log"),DateTime.Now.ToString("s")+" "+message+Environment.NewLine); } catch(IOException) { }
    }
}
