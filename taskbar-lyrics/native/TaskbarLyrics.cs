using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
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
    [StructLayout(LayoutKind.Sequential)] struct NativePoint { public int X,Y; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int awareness);

    Window window;
    Grid coverHost,coverVisual,lyricsHost,controlsAnchor;
    StackPanel lyricsText;
    Image coverImage;
    Border coverFallback,controlsSurface;
    Viewbox controlsViewport;
    RotateTransform coverRotation;
    RectangleGeometry coverRectangle;
    EllipseGeometry coverCircle;
    Button previousButton,playButton,nextButton,favoriteButton;
    System.Windows.Shapes.Path playGlyph,pauseGlyph,favoriteGlyph;
    DispatcherTimer pointerTimer;
    bool hovering,hoverEnabled,coverEnabled,coverSpinning,closed,frameBusy,hasControlTrack,favorite;
    string controlTrackId="",coverKey="";
    string controlsAlignment="center";
    double coverTimeMs;
    static Uri stateEndpoint;
    static bool actionPending;
    static readonly HashSet<TaskbarLyrics> instances=new HashSet<TaskbarLyrics>();
    static readonly Dictionary<string,Task<BitmapSource>> coverCache=new Dictionary<string,Task<BitmapSource>>();
    TextBlock primary, secondary;
    TextBlock primaryPlayed, secondaryPlayed;
    RectangleGeometry primaryMask, secondaryMask;
    Grid primaryViewport, secondaryViewport;
    TranslateTransform primaryShift, secondaryShift;
    TranslateTransform verticalShift;
    readonly Stopwatch lyricClock = new Stopwatch();
    double timelineMs, lineStartMs, lineEndMs, playbackRate;
    bool clockPlaying, clockAdvancing, scrolling, scrollingTranslation;
    bool masking, allowDisplay;
    Brush controlsColor;
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
    static readonly HttpClient actions = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(12) };
    static readonly HttpClient images = new HttpClient { Timeout = TimeSpan.FromSeconds(6), MaxResponseContentBufferSize=4*1024*1024 };
    static readonly JavaScriptSerializer json = new JavaScriptSerializer();
    static readonly Dictionary<string,object> defaults = new Dictionary<string,object> {
        {"enabled",true},
        {"display","primary"},
        {"layout","double"},
        {"secondary","next"},
        {"width",360},
        {"fontSize",16},
        {"primaryFontSize",16},
        {"secondaryFontSize",12},
        {"fontFamily","follow"},
        {"primaryColor","#ffffff"},
        {"secondaryColor","#ffffff"},
        {"primaryPlayedColor","#85DCD0"},
        {"secondaryPlayedColor","#85DCD0"},
        {"playedColorSource","cover"},
        {"controlsBackgroundSource","cover"},
        {"controlsIconColorSource","cover"},
        {"controlsIconColor","#F1F7F6"},
        {"controlsBackgroundColor","#202831"},
        {"controlsBackgroundOpacity",0},
        {"buttonHoverBackground",false},
        {"buttonHoverColor","#304449"},
        {"buttonPressedBackground",false},
        {"buttonPressedColor","#3B585C"},
        {"progressMask",true},
        {"offset",12},
        {"verticalOffset",2},
        {"opacity",100},
        {"theme","custom"},
        {"hidePaused",false},
        {"hideFullscreen",true},
        {"scrollLyrics",true},
        {"hoverControls",true},
        {"controlsAlignment","left"},
        {"showCover",true},
        {"coverShape","rectangle"},
        {"rotateCover",false}
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
            verticalShift = (TranslateTransform)window.FindName("VerticalShift");
            primaryPlayed = (TextBlock)window.FindName("PrimaryPlayed");
            secondaryPlayed = (TextBlock)window.FindName("SecondaryPlayed");
            primaryMask = (RectangleGeometry)window.FindName("PrimaryMask");
            secondaryMask = (RectangleGeometry)window.FindName("SecondaryMask");
            coverHost=(Grid)window.FindName("CoverHost");coverVisual=(Grid)window.FindName("CoverVisual");
            lyricsHost=(Grid)window.FindName("LyricsHost");lyricsText=(StackPanel)window.FindName("LyricsText");
            controlsAnchor=(Grid)window.FindName("ControlsAnchor");
            coverImage=(Image)window.FindName("CoverImage");coverFallback=(Border)window.FindName("CoverFallback");
            coverRotation=(RotateTransform)window.FindName("CoverRotation");
            coverRectangle=(RectangleGeometry)coverVisual.Resources["CoverRectangle"];coverCircle=(EllipseGeometry)coverVisual.Resources["CoverCircle"];
            controlsViewport=(Viewbox)window.FindName("ControlsViewport");controlsSurface=(Border)window.FindName("ControlsSurface");
            previousButton=(Button)window.FindName("PreviousButton");playButton=(Button)window.FindName("PlayButton");nextButton=(Button)window.FindName("NextButton");favoriteButton=(Button)window.FindName("FavoriteButton");
            playGlyph=(System.Windows.Shapes.Path)window.FindName("PlayGlyph");pauseGlyph=(System.Windows.Shapes.Path)window.FindName("PauseGlyph");favoriteGlyph=(System.Windows.Shapes.Path)window.FindName("FavoriteGlyph");
            previousButton.Click+=(sender,e)=>SendAction("previousTrack");playButton.Click+=(sender,e)=>SendAction("togglePlayback");nextButton.Click+=(sender,e)=>SendAction("nextTrack");favoriteButton.Click+=(sender,e)=>SendAction("toggleFavorite");

        handle=new WindowInteropHelper(window).EnsureHandle();
        SetWindowLong(handle,-20,GetWindowLong(handle,-20)|0x20|0x80|0x8000000);
        HwndSource.FromHwnd(handle).AddHook(Hook);
        instances.Add(this);
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
        pointerTimer=new DispatcherTimer(DispatcherPriority.Input){Interval=TimeSpan.FromMilliseconds(30)};
        pointerTimer.Tick+=(sender,e)=>UpdateHover();pointerTimer.Start();
        foregroundHook=SetWinEventHook(3,3,IntPtr.Zero,shellEvent,0,0,2);
        reorderHook=SetWinEventHook(0x8002,0x8004,IntPtr.Zero,shellEvent,0,0,2);
    }
    void Close()
    {
        closed=true;pointerTimer.Stop();instances.Remove(this);
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
                            primaryPlayedColor=primaryPlayed.Foreground.ToString(),secondaryPlayedColor=secondaryPlayed.Foreground.ToString(),controlsColor=previousButton.Foreground.ToString(),favoriteColor=favoriteButton.Foreground.ToString(),
                            controlsBackground=controlsSurface.Background.ToString(),
                            buttonHoverBackground=((SolidColorBrush)window.Resources["ButtonHoverBackground"]).Color.ToString(),buttonPressedBackground=((SolidColorBrush)window.Resources["ButtonPressedBackground"]).Color.ToString(),
                            tooltipsEnabled=ToolTipService.GetIsEnabled(previousButton)||ToolTipService.GetIsEnabled(playButton)||ToolTipService.GetIsEnabled(nextButton)||ToolTipService.GetIsEnabled(favoriteButton),
                            hasButtonTooltips=previousButton.ToolTip!=null || playButton.ToolTip!=null || nextButton.ToolTip!=null || favoriteButton.ToolTip!=null,
                            maskVisible=primaryPlayed.Visibility==Visibility.Visible, primaryMaskWidth=primaryMask.Rect.Width,
                            secondaryMaskWidth=secondaryMask.Rect.Width, shellRaises=shellRaises, aboveTaskbar=AboveTaskbar(), shellCoversLyrics=ShellCoversLyrics(),
                            owned=ownerBar!=IntPtr.Zero && GetWindow(handle,4)==ownerBar,owner=GetWindow(handle,4).ToInt64(),taskbar=display.Taskbar.ToInt64(),windowBand=selfBand,taskbarBand=barBand,
                            hoverEnabled=hoverEnabled,controlsVisible=hovering,controlsEnabled=playButton.IsEnabled,isFavorite=favorite,playing=clockPlaying,
                            coverVisible=coverEnabled,coverLoaded=coverImage.Source!=null,coverShape=coverVisual.Clip is EllipseGeometry?"circle":"rectangle",coverAngle=coverRotation.Angle,
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
            stateEndpoint=endpoint;
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
        if (message == 0x84) {
            // 仅按钮区域接收点击；歌词与封面继续穿透到任务栏。
            handled=true;return new IntPtr(hovering && InControls(new Point(unchecked((short)(l.ToInt64()&0xffff)),unchecked((short)((l.ToInt64()>>16)&0xffff))))?1:-1);
        }
        return IntPtr.Zero;
    }

    void SetHover(bool value)
    {
        if(hovering==value)return;
        hovering=value;
        controlsViewport.Visibility=value?Visibility.Visible:Visibility.Collapsed;
        lyricsText.Visibility=value?Visibility.Hidden:Visibility.Visible;
        int style=GetWindowLong(handle,-20);
        SetWindowLong(handle,-20,value?style&~0x20:style|0x20);
    }
    void UpdateHover()
    {
        NativePoint point=new NativePoint();Rect bounds;
        bool inside=allowDisplay && hoverEnabled && IsWindowVisible(handle) && GetCursorPos(out point);
        if(inside) {
            GetWindowRect(handle,out bounds);
            IntPtr hit=WindowFromPoint(point),bar=display.Taskbar;
            var name=new StringBuilder(256);if(bar==IntPtr.Zero)GetClassName(hit,name,name.Capacity);
            inside=point.X>=bounds.Left && point.X<bounds.Right && point.Y>=bounds.Top && point.Y<bounds.Bottom &&
                (hit==handle || hit==bar || (bar!=IntPtr.Zero && (IsChild(bar,hit) || GetAncestor(hit,2)==bar)) || (bar==IntPtr.Zero && (name.ToString()=="Progman" || name.ToString()=="WorkerW")));
        }
        SetHover(inside);
    }
    bool InControls(Point screen)
    {
        if(controlsViewport.Visibility!=Visibility.Visible || controlsSurface.ActualWidth<=0 || controlsSurface.ActualHeight<=0)return false;
        Point first=controlsSurface.PointToScreen(new Point(0,0)),last=controlsSurface.PointToScreen(new Point(controlsSurface.ActualWidth,controlsSurface.ActualHeight));
        return new System.Windows.Rect(first,last).Contains(screen);
    }
    void RefreshControls()
    {
        bool enabled=hasControlTrack && !frameBusy && !actionPending;
        previousButton.IsEnabled=playButton.IsEnabled=nextButton.IsEnabled=favoriteButton.IsEnabled=enabled;
        playGlyph.Visibility=clockPlaying?Visibility.Collapsed:Visibility.Visible;
        pauseGlyph.Visibility=clockPlaying?Visibility.Visible:Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetName(playButton,clockPlaying?"暂停":"播放");
        System.Windows.Automation.AutomationProperties.SetName(favoriteButton,favorite?"取消收藏":"收藏");
        favoriteButton.Foreground=controlsColor;
        favoriteGlyph.Fill=favorite?favoriteButton.Foreground:Brushes.Transparent;
    }
    async void SendAction(string action)
    {
        if(actionPending || !hasControlTrack || !hoverEnabled || !allowDisplay)return;
        // 所有屏幕共享操作锁，等待播放器确认后统一更新图标。
        actionPending=true;foreach(var surface in instances)surface.RefreshControls();
        try {
            var url=new UriBuilder(stateEndpoint);url.Path="/command";
            url.Query=stateEndpoint.Query.TrimStart('?')+"&action="+Uri.EscapeDataString(action)+"&trackId="+Uri.EscapeDataString(controlTrackId);
            using(var response=await actions.PostAsync(url.Uri,new ByteArrayContent(new byte[0]))) {
                var result=json.Deserialize<Dictionary<string,object>>(await response.Content.ReadAsStringAsync());
                if(response.IsSuccessStatusCode && result.ContainsKey("frame")) {
                    foreach(var surface in new List<TaskbarLyrics>(instances))surface.Apply((Dictionary<string,object>)result["frame"]);
                } else {
                    string error=result.ContainsKey("error")?Convert.ToString(result["error"]):"歌曲操作失败，请重试";
                    Log(error);
                }
            }
        } catch(Exception error) {
            Log("歌曲操作失败："+error.Message);
        } finally {actionPending=false;foreach(var surface in instances)surface.RefreshControls();}
    }
    static async Task<BitmapSource> ReadCover(string source)
    {
        byte[] bytes;
        if(source.StartsWith("data:image/",StringComparison.OrdinalIgnoreCase)) {
            int comma=source.IndexOf(',');
            if(comma<0 || !source.Substring(0,comma).EndsWith(";base64",StringComparison.OrdinalIgnoreCase))throw new IOException("图片格式无效");
            bytes=Convert.FromBase64String(source.Substring(comma+1));
        } else {
            Uri uri=new Uri(source);
            if(uri.IsFile)bytes=await Task.Run(()=>File.ReadAllBytes(uri.LocalPath));
            else if(uri.Scheme=="http" || uri.Scheme=="https")bytes=await images.GetByteArrayAsync(uri);
            else throw new IOException("图片协议不支持");
        }
        if(bytes.Length>4*1024*1024)throw new IOException("图片过大");
        using(var stream=new MemoryStream(bytes)) {
            var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth=128;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();return bitmap;
        }
    }
    static async Task<BitmapSource> LoadCover(string source,string fallback)
    {
        try {return await ReadCover(source);} catch(Exception) { }
        if(fallback!="" && fallback!=source)try {return await ReadCover(fallback);}catch(Exception) { }
        return null;
    }
    async void ApplyCover(string source,string fallback)
    {
        string key=source+"|"+fallback;if(key==coverKey)return;
        coverKey=key;coverImage.Source=null;coverImage.Visibility=Visibility.Collapsed;coverFallback.Visibility=Visibility.Visible;
        if(source=="")return;
        Task<BitmapSource> pending;
        if(!coverCache.TryGetValue(key,out pending)) {
            // 同一封面只读取一次，所有屏幕共享冻结后的图片。
            if(coverCache.Count>=8)coverCache.Remove(new List<string>(coverCache.Keys)[0]);
            pending=LoadCover(source,fallback);coverCache[key]=pending;
        }
        BitmapSource image=await pending;
        if(closed || coverKey!=key || image==null)return;
        coverImage.Source=image;coverImage.Visibility=Visibility.Visible;coverFallback.Visibility=Visibility.Collapsed;
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
            if(!GetWindowRect(taskbar,out bar) || bar.Height<4 || bar.Width<4 || !IsWindowVisible(taskbar)){allowDisplay=false;SetHover(false);ShowWindow(handle,0);return;}
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
        if (hide) { SetHover(false);ShowWindow(handle,0); return; }
        OwnTaskbar(taskbar);
        Rect area=Placement(bar,scale,s);
        // WPF 使用逻辑像素，偏移自动适配每块屏幕的 DPI。
        verticalShift.Y=s.ContainsKey("verticalOffset")?Math.Max(-20,Math.Min(20,Convert.ToDouble(s["verticalOffset"]))):0;
        primary.FontFamily=secondary.FontFamily=new FontFamily(Convert.ToString(frame["fontFamily"]));
        Style(s,area.Height/scale,frame);
        ButtonBackground("ButtonHoverBackground",s,"buttonHoverBackground","buttonHoverColor");
        ButtonBackground("ButtonPressedBackground",s,"buttonPressedBackground","buttonPressedColor");
        primary.Text=Convert.ToString(frame["text"]);
        secondary.Text=Convert.ToString(frame["secondary"]);
        hoverEnabled=s.ContainsKey("hoverControls") && Convert.ToBoolean(s["hoverControls"]);
        if(!hoverEnabled)SetHover(false);
        controlTrackId=frame.ContainsKey("trackId")?Convert.ToString(frame["trackId"]):"";
        hasControlTrack=controlTrackId!="";favorite=frame.ContainsKey("isFavorite") && Convert.ToBoolean(frame["isFavorite"]);
        frameBusy=frame.ContainsKey("commandBusy") && Convert.ToBoolean(frame["commandBusy"]);
        coverEnabled=s.ContainsKey("showCover") && Convert.ToBoolean(s["showCover"]);
        bool circle=s.ContainsKey("coverShape") && Convert.ToString(s["coverShape"])=="circle";
        coverSpinning=coverEnabled && circle && s.ContainsKey("rotateCover") && Convert.ToBoolean(s["rotateCover"]);
        coverTimeMs=frame.ContainsKey("coverTimeMs")?Convert.ToDouble(frame["coverTimeMs"]):Convert.ToDouble(frame["timelineMs"]);
        double coverSize=Math.Min(36,Math.Max(16,area.Height/scale-10));
        coverHost.Width=coverHost.Height=coverSize;coverHost.Visibility=coverEnabled?Visibility.Visible:Visibility.Collapsed;
        coverRectangle.Rect=new System.Windows.Rect(0,0,coverSize,coverSize);
        coverCircle.Center=new Point(coverSize/2,coverSize/2);coverCircle.RadiusX=coverCircle.RadiusY=coverSize/2;
        coverVisual.Clip=circle?(Geometry)coverCircle:coverRectangle;
        ApplyCover(coverEnabled && frame.ContainsKey("coverUrl")?Convert.ToString(frame["coverUrl"]):"",coverEnabled && frame.ContainsKey("coverSourceUrl")?Convert.ToString(frame["coverSourceUrl"]):"");
        double contentWidth=Math.Max(0,area.Width/scale-8-(coverEnabled?coverSize+8:0));
        primaryViewport.Width=secondaryViewport.Width=contentWidth;
        controlsViewport.MaxWidth=contentWidth;controlsViewport.MaxHeight=Math.Max(1,area.Height/scale-4);
        scrolling=Convert.ToBoolean(s["scrollLyrics"]);
        scrollingTranslation=scrolling && Convert.ToString(s["secondary"]) == "translation";
        primaryTextWidth=FitLine(primary,primaryViewport.Width,scrolling);
        secondaryTextWidth=FitLine(secondary,secondaryViewport.Width,scrollingTranslation);
        // 只按文字改变位置；为按钮保留宽度，短句不会触发缩放。
        double visibleTextWidth=Math.Min(contentWidth,Math.Max(primaryTextWidth,secondaryViewport.Visibility==Visibility.Visible?secondaryTextWidth:0));
        string alignment=s.ContainsKey("controlsAlignment") && Convert.ToString(s["controlsAlignment"])=="left"?"left":"center";
        double buttonsWidth=controlsSurface.Width+controlsViewport.Margin.Left+controlsViewport.Margin.Right;
        double anchorWidth=alignment=="center"?Math.Min(contentWidth,Math.Max(visibleTextWidth,buttonsWidth)):contentWidth;
        controlsAnchor.Width=hovering && alignment==controlsAlignment?Math.Min(controlsAnchor.Width,contentWidth):anchorWidth;
        controlsAlignment=alignment;
        controlsViewport.HorizontalAlignment=alignment=="left"?HorizontalAlignment.Left:HorizontalAlignment.Center;
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
        clockAdvancing=clockPlaying && (!frame.ContainsKey("advancing") || Convert.ToBoolean(frame["advancing"]));
        RefreshControls();
        // 暂停或缓冲时停止计时，恢复只从新的宿主锚点开始。
        if(clockAdvancing)lyricClock.Restart();else lyricClock.Reset();
        RenderScroll(null,EventArgs.Empty);
        Rect actual;GetWindowRect(handle,out actual);
        // WPF 切换 DPI 会调整窗口尺寸，按实际边界校正，避免沿用旧屏幕尺寸。
        bool moved=area.Left!=actual.Left || area.Top!=actual.Top || area.Width!=actual.Width || area.Height!=actual.Height;
        // 定期恢复置顶层级，同时避免每一帧重排任务栏窗口。
        if (moved || !IsWindowVisible(handle) || (DateTime.UtcNow-raised).TotalSeconds >= 2) {
            Position(area);
        }
        UpdateHover();
    }

    void Style(Dictionary<string,object> s,double height,Dictionary<string,object> frame)
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
        var palette=frame.ContainsKey("playedPalette")?(Dictionary<string,object>)frame["playedPalette"]:new Dictionary<string,object>{{"primary",s["primaryPlayedColor"]},{"secondary",s["secondaryPlayedColor"]},{"controls",""}};
        primaryPlayed.Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString(Convert.ToString(palette["primary"])));
        secondaryPlayed.Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString(Convert.ToString(palette["secondary"])));
        if(theme=="custom") {
            Color color=((SolidColorBrush)primary.Foreground).Color;light=(color.R+color.G+color.B)/3<110;
        }
        // 背景 alpha 单独调节，图标保持清晰，不跟随歌词遮罩配色。
        if(frame.ContainsKey("controlsPalette")) {
            var controls=(Dictionary<string,object>)frame["controlsPalette"];
            Color background=(Color)ColorConverter.ConvertFromString(Convert.ToString(controls["background"]));
            background.A=(byte)Math.Round(Convert.ToDouble(controls["opacity"])*255/100);
            controlsSurface.Background=new SolidColorBrush(background);
            controlsColor=new SolidColorBrush((Color)ColorConverter.ConvertFromString(Convert.ToString(controls["foreground"])));
        }else {
            controlsSurface.Background=new SolidColorBrush((Color)ColorConverter.ConvertFromString(light?"#EDE8EDEF":"#ED202831"));
            controlsColor=primary.Foreground;
        }
        previousButton.Foreground=playButton.Foreground=nextButton.Foreground=controlsColor;
        favoriteButton.Foreground=controlsColor;
    }

    void ButtonBackground(string resource,Dictionary<string,object> settings,string enabled,string color)
    {
        Color desired=settings.ContainsKey(enabled) && Convert.ToBoolean(settings[enabled])?(Color)ColorConverter.ConvertFromString(Convert.ToString(settings[color])):Colors.Transparent;
        // 仅替换变化的画刷，XAML 动态资源同步更新四个按钮。
        if(((SolidColorBrush)window.Resources[resource]).Color!=desired)window.Resources[resource]=new SolidColorBrush(desired);
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
        double current=timelineMs+(clockAdvancing ? lyricClock.Elapsed.TotalMilliseconds*playbackRate : 0);
        coverRotation.Angle=coverSpinning?(coverTimeMs+(clockAdvancing?lyricClock.Elapsed.TotalMilliseconds*playbackRate:0))*.018%360:0;
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
            Style(s,48,new Dictionary<string,object>());
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
