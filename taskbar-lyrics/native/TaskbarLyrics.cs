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
using System.Windows.Controls.Primitives;
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
    [StructLayout(LayoutKind.Sequential)] struct AccentPolicy { public int State,Flags,GradientColor,AnimationId; }
    [StructLayout(LayoutKind.Sequential)] struct CompositionAttribute { public int Attribute;public IntPtr Data;public int Size; }
    [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr hwnd,ref CompositionAttribute data);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int left,int top,int right,int bottom,int width,int height);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hwnd,IntPtr region,bool redraw);

    Window window;
    Grid coverHost,coverVisual,coverLayers,lyricsHost,controlsAnchor;
    StackPanel lyricsText;
    StackPanel songInfo;
    Grid songTitleViewport,songArtistViewport;
    TextBlock songTitle,songArtist;
    TranslateTransform songTitleShift,songArtistShift;
    bool coverHovered;
    readonly Stopwatch infoClock=new Stopwatch();
    Popup trackNotice;
    Border trackNoticeSurface;
    Grid noticeTitleViewport,noticeArtistViewport;
    TextBlock noticeTitle,noticeArtist;
    TranslateTransform noticeTitleShift,noticeArtistShift;
    IntPtr noticeHandle;
    bool noticeHovered;
    bool noticeEnabled=true,noticeScrollText=true,noticeGlassApplied;
    string noticeBackgroundStyle="glass",noticeCompositionKey="",noticeGeometryKey="";
    double noticeGap=8;
    SolidColorBrush noticeBackground,noticeTitleInk,noticeArtistInk;
    Rect noticeAnchorBounds;
    double noticeAnchorCenter=double.NaN,noticeTopShift=double.NaN;
    static string observedTrackId="",noticeTrackId="";
    static bool noticePending;
    static readonly Stopwatch noticeClock=new Stopwatch();
    static double noticeHoldUntil;
    static double noticeDisplayMs=3000,noticeFadeMs=180;
    static bool noticeFeatureEnabled=true,noticeHoverHold=true,noticeExpired,noticeOnlyTaskbarSwitch=true;
    Image coverImage,coverPrevious;
    Border coverFallback,controlsSurface;
    Viewbox controlsViewport,progressTimeViewport;
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
    Grid progressHost,progressTrack;
    Border progressFill,progressBed;
    System.Windows.Shapes.Ellipse progressThumb;
    TextBlock progressTime;
    string progressElapsed="00:00",progressDuration="00:00";
    bool progressEnabled,progressHovered,scrubbing,transitionsEnabled,smartColors,autoPlayed,autoIcons;
    double durationMs,scrubRatio,transitionDuration,pendingSeekMs=-1;
    string scrubTrackId="";
    static TaskbarLyrics scrubOwner;
    readonly Stopwatch paletteClock=new Stopwatch(),coverFadeClock=new Stopwatch();
    Color[] paletteFrom,paletteTo,paletteCurrent;
    string paletteRenderKey="";
    Color taskbarBackground;
    static Uri stateEndpoint;
    static bool actionPending;
    static readonly HashSet<TaskbarLyrics> instances=new HashSet<TaskbarLyrics>();
    static readonly Dictionary<string,Task<BitmapSource>> coverCache=new Dictionary<string,Task<BitmapSource>>();
    TextBlock primary, secondary;
    SolidColorBrush primaryWaiting,secondaryWaiting,primaryAccent,secondaryAccent;
    LinearGradientBrush primaryProgress,secondaryProgress;
    double primaryMaskWidth,secondaryMaskWidth;
    bool secondaryMasking;
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
        {"rotateCover",false},
        {"hoverProgress",true},
        {"smartContrast",true},
        {"trackTransitions",true},
        {"transitionDuration",280},
        {"noticeEnabled",true},
        {"noticeOnlyTaskbarSwitch",true},
        {"noticeDuration",2},
        {"noticeFadeDuration",180},
        {"noticeHoldOnHover",true},
        {"noticeBackgroundStyle","glass"},
        {"noticeBackgroundColor","#ffffff"},
        {"noticeTransparency",100},
        {"noticeWidth",180},
        {"noticeRadius",15},
        {"noticeGap",8},
        {"noticeShowArtist",true},
        {"noticeTitleFontSize",15},
        {"noticeArtistFontSize",12},
        {"noticeTextSource","auto"},
        {"noticeTextAlignment","center"},
        {"noticeTitleColor","#F1F7F6"},
        {"noticeArtistColor","#BFC5CA"},
        {"noticeScrollText",true}
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
            primaryWaiting=(SolidColorBrush)window.Resources["PrimaryWaiting"];secondaryWaiting=(SolidColorBrush)window.Resources["SecondaryWaiting"];
            primaryAccent=(SolidColorBrush)window.Resources["PrimaryAccent"];secondaryAccent=(SolidColorBrush)window.Resources["SecondaryAccent"];
            primaryProgress=(LinearGradientBrush)window.Resources["PrimaryProgress"];secondaryProgress=(LinearGradientBrush)window.Resources["SecondaryProgress"];
            coverHost=(Grid)window.FindName("CoverHost");coverVisual=(Grid)window.FindName("CoverVisual");
            lyricsHost=(Grid)window.FindName("LyricsHost");lyricsText=(StackPanel)window.FindName("LyricsText");
            songInfo=(StackPanel)window.FindName("SongInfo");songTitleViewport=(Grid)window.FindName("SongTitleViewport");songArtistViewport=(Grid)window.FindName("SongArtistViewport");
            songTitle=(TextBlock)window.FindName("SongTitle");songArtist=(TextBlock)window.FindName("SongArtist");
            songTitleShift=(TranslateTransform)window.FindName("SongTitleShift");songArtistShift=(TranslateTransform)window.FindName("SongArtistShift");
            trackNotice=(Popup)window.FindName("TrackNotice");trackNoticeSurface=(Border)window.FindName("TrackNoticeSurface");
            noticeBackground=(SolidColorBrush)window.Resources["NoticeBackground"];noticeTitleInk=(SolidColorBrush)window.Resources["NoticeTitleInk"];noticeArtistInk=(SolidColorBrush)window.Resources["NoticeArtistInk"];
            noticeTitleViewport=(Grid)window.FindName("NoticeTitleViewport");noticeArtistViewport=(Grid)window.FindName("NoticeArtistViewport");
            noticeTitle=(TextBlock)window.FindName("NoticeTitle");noticeArtist=(TextBlock)window.FindName("NoticeArtist");
            noticeTitleShift=(TranslateTransform)window.FindName("NoticeTitleShift");noticeArtistShift=(TranslateTransform)window.FindName("NoticeArtistShift");
            trackNotice.CustomPopupPlacementCallback=PlaceTrackNotice;
            trackNotice.Opened+=(sender,e)=>{
                noticeHandle=((HwndSource)PresentationSource.FromVisual(trackNoticeSurface)).Handle;
                SetWindowLong(noticeHandle,-20,GetWindowLong(noticeHandle,-20)|0x80|0x8000000);
                HwndSource.FromHwnd(noticeHandle).AddHook(NoticeHook);
                noticeCompositionKey="";ApplyNoticeComposition();
            };
            trackNotice.Closed+=(sender,e)=>{noticeHovered=false;noticeHandle=IntPtr.Zero;noticeCompositionKey="";noticeGlassApplied=false;};
            controlsAnchor=(Grid)window.FindName("ControlsAnchor");
            coverImage=(Image)window.FindName("CoverImage");coverFallback=(Border)window.FindName("CoverFallback");
            coverPrevious=(Image)window.FindName("CoverPrevious");
            coverLayers=(Grid)window.FindName("CoverLayers");
            progressHost=(Grid)window.FindName("ProgressHost");progressTrack=(Grid)window.FindName("ProgressTrack");
            progressFill=(Border)window.FindName("ProgressFill");progressThumb=(System.Windows.Shapes.Ellipse)window.FindName("ProgressThumb");
            progressBed=(Border)window.FindName("ProgressBed");progressTime=(TextBlock)window.FindName("ProgressTime");
            progressTimeViewport=(Viewbox)window.FindName("ProgressTimeViewport");
            progressTrack.MouseEnter+=(sender,e)=>SetProgressHover(true);
            progressTrack.MouseLeave+=(sender,e)=>{if(!scrubbing)SetProgressHover(false);};
            progressTrack.MouseLeftButtonDown+=(sender,e)=>{BeginScrub(e.GetPosition(progressTrack).X/progressTrack.ActualWidth);if(scrubbing)progressTrack.CaptureMouse();e.Handled=true;};
            progressTrack.MouseMove+=(sender,e)=>{if(scrubbing){scrubRatio=ClampRatio(e.GetPosition(progressTrack).X/progressTrack.ActualWidth);RenderScroll(null,EventArgs.Empty);e.Handled=true;}};
            progressTrack.MouseLeftButtonUp+=(sender,e)=>{if(scrubbing){scrubRatio=ClampRatio(e.GetPosition(progressTrack).X/progressTrack.ActualWidth);FinishScrub();e.Handled=true;}};
            progressTrack.LostMouseCapture+=(sender,e)=>{if(scrubbing)CancelScrub();};
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
        closed=true;CancelScrub();CloseTrackNotice();pointerTimer.Stop();instances.Remove(this);
        CompositionTarget.Rendering-=RenderScroll;
        UnhookWinEvent(foregroundHook);UnhookWinEvent(reorderHook);window.Close();
    }
    object Report()
    {
                        Rect actual; GetWindowRect(handle,out actual);uint selfBand,barBand;GetWindowBand(handle,out selfBand);GetWindowBand(display.Taskbar,out barBand);
                        return new {
                            text=primary.Text, secondary=secondary.Text, secondaryVisible=secondary.Visibility==Visibility.Visible,
                            fontSize=primary.FontSize, visualScale=VisualTreeHelper.GetDpi(primary).DpiScaleX, windowDpi=GetDpiForWindow(handle), screenScale=display.Scale, visible=IsWindowVisible(handle),
                            taskbarDpi=GetDpiForWindow(display.Taskbar),layoutWidth=window.ActualWidth,layoutHeight=window.ActualHeight,
                            primaryOffset=primaryShift.X, secondaryOffset=secondaryShift.X,
                            textWidth=double.IsNaN(primary.Width) ? 0 : primary.Width,
                            viewportWidth=double.IsNaN(primaryViewport.Width) ? 0 : primaryViewport.Width,
                            secondaryFontSize=secondary.FontSize, fontFamily=primary.FontFamily.Source,
                            primaryColor=primaryWaiting.ToString(), secondaryColor=secondaryWaiting.ToString(),
                            primaryPlayedColor=primaryAccent.ToString(),secondaryPlayedColor=secondaryAccent.ToString(),controlsColor=previousButton.Foreground.ToString(),favoriteColor=favoriteButton.Foreground.ToString(),
                            controlsBackground=controlsSurface.Background.ToString(),
                            progressEnabled=progressEnabled,progressVisible=progressHost.Visibility==Visibility.Visible,progressWidth=progressFill.Width,progressTrackWidth=progressTrack.ActualWidth,progressElapsed=progressElapsed,progressDuration=progressDuration,scrubbing=scrubbing,durationMs=durationMs,
                            progressHovered=progressHovered,progressTimeVisible=progressTimeViewport.Visibility==Visibility.Visible,progressTime=progressTime.Text,progressThickness=progressFill.Height,
                            coverHovered=coverHovered,songInfoVisible=songInfo.Visibility==Visibility.Visible,songTitle=songTitle.Text,songArtist=songArtist.Text,songTitleOffset=songTitleShift.X,songArtistOffset=songArtistShift.X,
                            trackNoticeVisible=trackNotice.IsOpen,trackNoticeTitle=noticeTitle.Text,trackNoticeArtist=noticeArtist.Text,trackNoticeOpacity=trackNoticeSurface.Opacity,trackNoticeHovered=noticeHovered,trackNoticeTitleOffset=noticeTitleShift.X,
                            trackNoticeNoActivate=(GetWindowLong(noticeHandle,-20)&0x8000000)!=0,
                            trackNoticeBackground=noticeBackground.ToString(),trackNoticeGlass=noticeGlassApplied,trackNoticeWidth=trackNoticeSurface.Width,trackNoticeRadius=trackNoticeSurface.CornerRadius.TopLeft,trackNoticeTitleSize=noticeTitle.FontSize,trackNoticeArtistSize=noticeArtist.FontSize,trackNoticeTitleColor=noticeTitleInk.ToString(),trackNoticeArtistColor=noticeArtistInk.ToString(),trackNoticeArtistVisible=noticeArtistViewport.Visibility==Visibility.Visible,
                            transitionsEnabled=transitionsEnabled,transitionDuration=transitionDuration,coverOpacity=coverImage.Opacity,previousCoverVisible=coverPrevious.Visibility==Visibility.Visible,previousCoverOpacity=coverPrevious.Opacity,smartContrast=smartColors,taskbarBackground=taskbarBackground.ToString(),
                            buttonHoverBackground=((SolidColorBrush)window.Resources["ButtonHoverBackground"]).Color.ToString(),buttonPressedBackground=((SolidColorBrush)window.Resources["ButtonPressedBackground"]).Color.ToString(),
                            tooltipsEnabled=ToolTipService.GetIsEnabled(previousButton)||ToolTipService.GetIsEnabled(playButton)||ToolTipService.GetIsEnabled(nextButton)||ToolTipService.GetIsEnabled(favoriteButton),
                            hasButtonTooltips=previousButton.ToolTip!=null || playButton.ToolTip!=null || nextButton.ToolTip!=null || favoriteButton.ToolTip!=null,
                            maskVisible=masking, primaryMaskWidth=primaryMaskWidth,secondaryMaskWidth=secondaryMaskWidth,
                            singleText=true,primaryColorBoundary=primaryProgress.GradientStops[1].Offset,secondaryColorBoundary=secondaryProgress.GradientStops[1].Offset,
                            shellRaises=shellRaises, aboveTaskbar=AboveTaskbar(), shellCoversLyrics=ShellCoversLyrics(),
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
        foreach(var item in screens) result.Add(new {id=item.Info.Device,label="屏幕 "+(++number)+" · "+item.Info.Monitor.Width+" × "+item.Info.Monitor.Height+((item.Info.Flags&1)!=0?"（主屏幕）":""),primary=(item.Info.Flags&1)!=0,bounds=new[]{item.Info.Monitor.Left,item.Info.Monitor.Top,item.Info.Monitor.Width,item.Info.Monitor.Height},scale=item.Scale,taskbarLight=TaskbarLight()});
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
            // 按钮与进度条接收点击，其余区域仍穿透到任务栏。
            var point=new Point(unchecked((short)(l.ToInt64()&0xffff)),unchecked((short)((l.ToInt64()>>16)&0xffff)));
            handled=true;return new IntPtr(scrubbing || (hovering && (InControls(point) || InProgress(point)))?1:-1);
        }
        return IntPtr.Zero;
    }

    IntPtr NoticeHook(IntPtr hwnd,int message,IntPtr w,IntPtr l,ref bool handled)
    {
        if(message==0x21){handled=true;return new IntPtr(3);}
        return IntPtr.Zero;
    }
    CustomPopupPlacement[] PlaceTrackNotice(Size popupSize,Size targetSize,Point offset)
    {
        double left=NoticeCenter()-popupSize.Width/2;
        // 优先出现在任务栏上方，顶部任务栏则使用下方候选位置。
        return new[]{new CustomPopupPlacement(new Point(left,-popupSize.Height-noticeGap-verticalShift.Y),PopupPrimaryAxis.Horizontal),
            new CustomPopupPlacement(new Point(left,targetSize.Height+noticeGap-verticalShift.Y),PopupPrimaryAxis.Horizontal)};
    }
    double NoticeCenter()
    {
        if(controlsViewport.Visibility!=Visibility.Collapsed && controlsSurface.ActualWidth>0)
            return controlsSurface.TransformToAncestor(lyricsHost).Transform(new Point(controlsSurface.ActualWidth/2,0)).X;
        double scale=Math.Min(1,Math.Min(controlsViewport.MaxWidth/144,controlsViewport.MaxHeight/34));
        return controlsAnchor.Margin.Left+(controlsAlignment=="left"?controlsViewport.Margin.Left+72*scale:controlsAnchor.Width/2);
    }
    static void ObserveTrack(string track,string title,bool taskbarSwitch)
    {
        if(track==""){noticePending=false;noticeClock.Reset();return;}
        if(track!=observedTrackId){
            bool changed=observedTrackId!="" && (!noticeOnlyTaskbarSwitch || taskbarSwitch);observedTrackId=track;noticeTrackId=changed?track:"";
            noticePending=changed && noticeFeatureEnabled;noticeClock.Reset();noticeHoldUntil=noticeDisplayMs;noticeExpired=false;
            foreach(var surface in instances)surface.SetNoticeHover(false);
        }
        // 新歌元数据先于封面到达；空元数据不会消耗展示时间。
        if(noticePending && noticeFeatureEnabled && !String.IsNullOrWhiteSpace(title)){noticePending=false;noticeClock.Restart();}
    }
    void SetNoticeHover(bool value)
    {
        noticeHovered=value && trackNotice.IsOpen && allowDisplay;
        if(noticeHovered && noticeHoverHold)noticeHoldUntil=Math.Max(noticeHoldUntil,noticeClock.Elapsed.TotalMilliseconds+noticeDisplayMs);
    }
    void CloseTrackNotice(){noticeHovered=false;trackNotice.IsOpen=false;}
    bool InTrackNotice(Point screen)
    {
        Rect bounds;
        return trackNotice.IsOpen && noticeHandle!=IntPtr.Zero && GetWindowRect(noticeHandle,out bounds) &&
            screen.X>=bounds.Left && screen.X<bounds.Right && screen.Y>=bounds.Top && screen.Y<bounds.Bottom &&
            WindowFromPoint(new NativePoint{X=(int)screen.X,Y=(int)screen.Y})==noticeHandle;
    }
    void RenderTrackNotice()
    {
        if(!noticeEnabled || noticeExpired || !allowDisplay || !IsWindowVisible(handle) || !noticeClock.IsRunning || controlTrackId!=noticeTrackId || String.IsNullOrWhiteSpace(noticeTitle.Text)) {CloseTrackNotice();return;}
        double elapsed=noticeClock.Elapsed.TotalMilliseconds;
        if(noticeHoverHold)foreach(var surface in instances)if(surface.noticeHovered)noticeHoldUntil=Math.Max(noticeHoldUntil,elapsed+noticeDisplayMs);
        double opacity=noticeFadeMs>0?1-ClampRatio((elapsed-noticeHoldUntil)/noticeFadeMs):elapsed<noticeHoldUntil?1:0;
        if(opacity<=0){noticeExpired=true;CloseTrackNotice();return;}
        trackNoticeSurface.Opacity=opacity;trackNotice.IsOpen=true;
        ApplyNoticeComposition();
        Rect anchor;GetWindowRect(handle,out anchor);double center=NoticeCenter();
        if(anchor.Left!=noticeAnchorBounds.Left || anchor.Top!=noticeAnchorBounds.Top || anchor.Width!=noticeAnchorBounds.Width || anchor.Height!=noticeAnchorBounds.Height || center!=noticeAnchorCenter || verticalShift.Y!=noticeTopShift){
            noticeAnchorBounds=anchor;noticeAnchorCenter=center;noticeTopShift=verticalShift.Y;
            // 弹出窗口不会自动跟随宿主移动，轻调偏移触发重新定位。
            trackNotice.HorizontalOffset=.01;trackNotice.HorizontalOffset=0;
        }
        noticeTitleShift.X=noticeScrollText?InfoOffset(noticeTitle.Width,noticeTitleViewport.Width,elapsed/1000):0;
        noticeArtistShift.X=noticeScrollText?InfoOffset(noticeArtist.Width,noticeArtistViewport.Width,elapsed/1000):0;
    }
    void ConfigureNotice(Dictionary<string,object> s)
    {
        noticeEnabled=noticeFeatureEnabled=Convert.ToBoolean(s["noticeEnabled"]);
        noticeOnlyTaskbarSwitch=Convert.ToBoolean(s["noticeOnlyTaskbarSwitch"]);
        double duration=Convert.ToDouble(s["noticeDuration"])*1000;
        noticeHoldUntil+=duration-noticeDisplayMs;noticeDisplayMs=duration;
        noticeFadeMs=Convert.ToDouble(s["noticeFadeDuration"]);noticeHoverHold=Convert.ToBoolean(s["noticeHoldOnHover"]);
        if(!noticeEnabled){noticePending=false;noticeClock.Reset();CloseTrackNotice();}
    }
    void StyleNotice(Dictionary<string,object> s,Dictionary<string,object> frame)
    {
        noticeGap=Convert.ToDouble(s["noticeGap"]);noticeScrollText=Convert.ToBoolean(s["noticeScrollText"]);
        noticeBackgroundStyle=Convert.ToString(s["noticeBackgroundStyle"]);
        trackNoticeSurface.Width=Convert.ToDouble(s["noticeWidth"]);trackNoticeSurface.CornerRadius=new CornerRadius(Convert.ToDouble(s["noticeRadius"]));
        noticeTitle.FontSize=Convert.ToDouble(s["noticeTitleFontSize"]);noticeArtist.FontSize=Convert.ToDouble(s["noticeArtistFontSize"]);
        Color background=(Color)ColorConverter.ConvertFromString(Convert.ToString(s["noticeBackgroundColor"]));
        if(frame.ContainsKey("noticePalette"))background=(Color)ColorConverter.ConvertFromString(Convert.ToString(((Dictionary<string,object>)frame["noticePalette"])["background"]));
        double alpha=1-Convert.ToDouble(s["noticeTransparency"])/100;
        Color baseColor=MixColor(taskbarBackground,background,alpha);
        background.A=(byte)Math.Round(alpha*255);noticeBackground.Color=background;
        Color title=(Color)ColorConverter.ConvertFromString(Convert.ToString(s["noticeTitleColor"])),artist=(Color)ColorConverter.ConvertFromString(Convert.ToString(s["noticeArtistColor"]));
        if(Convert.ToString(s["noticeTextSource"])=="auto"){
            Color white=(Color)ColorConverter.ConvertFromString("#F1F7F6"),dark=(Color)ColorConverter.ConvertFromString("#202D35");
            bool useWhite=Contrast(white,baseColor)>=Contrast(dark,baseColor);
            title=ReadableNoticeColor(useWhite?white:dark,baseColor);
            artist=ReadableNoticeColor((Color)ColorConverter.ConvertFromString(useWhite?"#BFC5CA":"#4B5561"),baseColor);
        }
        noticeTitleInk.Color=title;noticeArtistInk.Color=artist;
        noticeTitleViewport.Width=noticeArtistViewport.Width=trackNoticeSurface.Width-26;
        noticeArtistViewport.Visibility=Convert.ToBoolean(s["noticeShowArtist"]) && !String.IsNullOrWhiteSpace(noticeArtist.Text)?Visibility.Visible:Visibility.Collapsed;
        FitLine(noticeTitle,noticeTitleViewport.Width,noticeScrollText);FitLine(noticeArtist,noticeArtistViewport.Width,noticeScrollText);
        bool centered=Convert.ToString(s["noticeTextAlignment"])=="center";
        AlignNoticeLine(noticeTitle,noticeTitleViewport.Width,centered,noticeScrollText);
        AlignNoticeLine(noticeArtist,noticeArtistViewport.Width,centered,noticeScrollText);
        // 调整间距或形状后，仅重新定位浮层，不移动歌词窗口。
        string geometry=noticeGap+":"+trackNoticeSurface.Width+":"+noticeTitle.FontSize+":"+noticeArtist.FontSize+":"+noticeArtistViewport.Visibility;
        if(geometry!=noticeGeometryKey){noticeGeometryKey=geometry;noticeAnchorCenter=double.NaN;}
    }
    static void AlignNoticeLine(TextBlock text,double viewportWidth,bool centered,bool scrolling)
    {
        text.TextAlignment=centered?TextAlignment.Center:TextAlignment.Left;
        // 短文字居中；溢出的滚动文字从开头展示，避免先裁掉前半句。
        text.HorizontalAlignment=centered && scrolling && text.Width<=viewportWidth?HorizontalAlignment.Center:HorizontalAlignment.Left;
    }
    static Color ReadableNoticeColor(Color color,Color background)
    {
        if(Contrast(color,background)>=4.5)return color;
        Color best=color;double score=Contrast(color,background);
        for(int step=1;step<=100;step++)foreach(Color endpoint in new[]{Colors.White,Colors.Black}){
            Color candidate=MixColor(color,endpoint,step/100.0);double ratio=Contrast(candidate,background);
            if(ratio>=4.5)return candidate;if(ratio>score){best=candidate;score=ratio;}
        }
        return best;
    }
    void ApplyNoticeComposition()
    {
        if(noticeHandle==IntPtr.Zero)return;
        Rect bounds;if(!GetWindowRect(noticeHandle,out bounds) || bounds.Width<=0 || bounds.Height<=0)return;
        string key=noticeBackgroundStyle+":"+bounds.Width+":"+bounds.Height+":"+trackNoticeSurface.CornerRadius.TopLeft;
        if(key==noticeCompositionKey)return;noticeCompositionKey=key;
        var policy=new AccentPolicy{State=noticeBackgroundStyle=="glass"?3:0};
        IntPtr memory=Marshal.AllocHGlobal(Marshal.SizeOf(policy));
        try{
            Marshal.StructureToPtr(policy,memory,false);
            var data=new CompositionAttribute{Attribute=19,Data=memory,Size=Marshal.SizeOf(policy)};
            noticeGlassApplied=SetWindowCompositionAttribute(noticeHandle,ref data)!=0 && policy.State==3;
        }catch(EntryPointNotFoundException){noticeGlassApplied=false;}
        finally{Marshal.FreeHGlobal(memory);}
        // 原生模糊覆盖窗口矩形，用同一圆角区域裁掉玻璃边角。
        double dpi=Math.Max(.5,GetDpiForWindow(noticeHandle)/96.0);
        int radius=(int)Math.Round(trackNoticeSurface.CornerRadius.TopLeft*dpi*2);
        IntPtr region=CreateRoundRectRgn(0,0,bounds.Width+1,bounds.Height+1,radius,radius);
        if(SetWindowRgn(noticeHandle,region,true)==0)DeleteObject(region);
    }

    void SetHover(bool value)
    {
        hovering=value;
        if(!value)SetCoverHover(false);
        progressHost.Visibility=value && progressEnabled && !coverHovered?Visibility.Visible:Visibility.Collapsed;
        SetProgressHover(value && progressHovered);
        int style=GetWindowLong(handle,-20);
        SetWindowLong(handle,-20,value?style&~0x20:style|0x20);
    }
    void SetProgressHover(bool value)
    {
        progressHovered=hovering && !coverHovered && progressEnabled && (value || scrubbing);
        // 隐藏按钮仍保留测量尺寸，时间和进度条不会跳位。
        controlsViewport.Visibility=hovering && !coverHovered && (hoverEnabled || progressEnabled)?(hoverEnabled && !progressHovered?Visibility.Visible:Visibility.Hidden):Visibility.Collapsed;
        progressTimeViewport.Visibility=progressHovered?Visibility.Visible:Visibility.Collapsed;
        lyricsText.Visibility=coverHovered || (hovering && (hoverEnabled || progressHovered))?Visibility.Hidden:Visibility.Visible;
        progressBed.Height=progressFill.Height=progressHovered?6:3;
        progressThumb.Visibility=progressHovered?Visibility.Visible:Visibility.Collapsed;
    }
    void SetCoverHover(bool value)
    {
        bool next=value && coverEnabled && allowDisplay && !scrubbing;
        if(next!=coverHovered){if(next)infoClock.Restart();else infoClock.Reset();songTitleShift.X=songArtistShift.X=0;}
        coverHovered=next;songInfo.Visibility=next?Visibility.Visible:Visibility.Collapsed;
    }
    bool InCover(Point screen)
    {
        if(!coverEnabled || coverHost.Visibility!=Visibility.Visible)return false;
        Point first=coverHost.PointToScreen(new Point(0,0)),last=coverHost.PointToScreen(new Point(coverHost.ActualWidth,coverHost.ActualHeight));
        return new System.Windows.Rect(first,last).Contains(screen);
    }
    void UpdateHover()
    {
        NativePoint point=new NativePoint();Rect bounds;
        bool cursor=GetCursorPos(out point);
        SetNoticeHover(cursor && InTrackNotice(new Point(point.X,point.Y)));
        if(scrubbing){SetHover(true);SetProgressHover(true);return;}
        bool inside=allowDisplay && (hoverEnabled || progressEnabled || coverEnabled) && IsWindowVisible(handle) && cursor;
        if(inside) {
            GetWindowRect(handle,out bounds);
            IntPtr hit=WindowFromPoint(point),bar=display.Taskbar;
            var name=new StringBuilder(256);if(bar==IntPtr.Zero)GetClassName(hit,name,name.Capacity);
            inside=point.X>=bounds.Left && point.X<bounds.Right && point.Y>=bounds.Top && point.Y<bounds.Bottom &&
                (hit==handle || hit==bar || (bar!=IntPtr.Zero && (IsChild(bar,hit) || GetAncestor(hit,2)==bar)) || (bar==IntPtr.Zero && (name.ToString()=="Progman" || name.ToString()=="WorkerW")));
        }
        SetCoverHover(inside && InCover(new Point(point.X,point.Y)));
        SetHover(inside || noticeHovered);
        SetProgressHover(inside && InProgress(new Point(point.X,point.Y)));
    }
    bool InControls(Point screen)
    {
        if(controlsViewport.Visibility!=Visibility.Visible || controlsSurface.ActualWidth<=0 || controlsSurface.ActualHeight<=0)return false;
        Point first=controlsSurface.PointToScreen(new Point(0,0)),last=controlsSurface.PointToScreen(new Point(controlsSurface.ActualWidth,controlsSurface.ActualHeight));
        return new System.Windows.Rect(first,last).Contains(screen);
    }
    bool InProgress(Point screen)
    {
        if(progressHost.Visibility!=Visibility.Visible || progressTrack.ActualWidth<=0)return false;
        Point first=progressTrack.PointToScreen(new Point(0,0)),last=progressTrack.PointToScreen(new Point(progressTrack.ActualWidth,progressTrack.ActualHeight));
        return new System.Windows.Rect(first,last).Contains(screen);
    }
    static double ClampRatio(double value){return double.IsNaN(value)?0:Math.Max(0,Math.Min(1,value));}
    void BeginScrub(double ratio)
    {
        if(!progressEnabled || !allowDisplay || !hasControlTrack || frameBusy || actionPending || scrubOwner!=null)return;
        scrubOwner=this;scrubbing=true;scrubTrackId=controlTrackId;scrubRatio=ClampRatio(ratio);
        SetCoverHover(false);
        SetHover(true);SetProgressHover(true);foreach(var surface in instances)surface.RefreshControls();RenderScroll(null,EventArgs.Empty);
    }
    void CancelScrub()
    {
        if(!scrubbing)return;
        scrubbing=false;if(scrubOwner==this)scrubOwner=null;
        progressTrack.ReleaseMouseCapture();foreach(var surface in instances)surface.RefreshControls();
        SetProgressHover(progressTrack.IsMouseOver);
    }
    void FinishScrub()
    {
        if(!scrubbing)return;
        bool valid=progressEnabled && scrubTrackId==controlTrackId;
        double seconds=scrubRatio*durationMs/1000;
        CancelScrub();if(valid)SendAction("seek",seconds);
    }
    void RefreshControls()
    {
        bool enabled=hasControlTrack && !frameBusy && !actionPending && scrubOwner==null;
        previousButton.IsEnabled=playButton.IsEnabled=nextButton.IsEnabled=favoriteButton.IsEnabled=enabled;
        playGlyph.Visibility=clockPlaying?Visibility.Collapsed:Visibility.Visible;
        pauseGlyph.Visibility=clockPlaying?Visibility.Visible:Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetName(playButton,clockPlaying?"暂停":"播放");
        System.Windows.Automation.AutomationProperties.SetName(favoriteButton,favorite?"取消收藏":"收藏");
        favoriteButton.Foreground=controlsColor;
        favoriteGlyph.Fill=favorite?favoriteButton.Foreground:Brushes.Transparent;
    }
    async void SendAction(string action,double value=0)
    {
        if(actionPending || scrubOwner!=null || !hasControlTrack || !(action=="seek"?progressEnabled:hoverEnabled) || !allowDisplay)return;
        // 所有屏幕共享操作锁，等待播放器确认后统一更新图标。
        actionPending=true;foreach(var surface in instances)surface.RefreshControls();
        if(action=="seek")foreach(var surface in instances)surface.pendingSeekMs=value*1000;
        try {
            var url=new UriBuilder(stateEndpoint);url.Path="/command";
            // .NET Framework 的 Query setter 会加问号，拼完后只赋值一次。
            string query=stateEndpoint.Query.TrimStart('?')+"&action="+Uri.EscapeDataString(action)+"&trackId="+Uri.EscapeDataString(controlTrackId);
            if(action=="seek")query+="&value="+value.ToString("R",CultureInfo.InvariantCulture);
            url.Query=query;
            using(var response=await actions.PostAsync(url.Uri,new ByteArrayContent(new byte[0]))) {
                string body=await response.Content.ReadAsStringAsync();
                // 认证失败等空响应也保留 HTTP 状态，避免报成空引用异常。
                if(String.IsNullOrWhiteSpace(body)){Log("歌曲操作失败：HTTP "+(int)response.StatusCode+"，播放器返回空响应");return;}
                var result=json.Deserialize<Dictionary<string,object>>(body);
                if(response.IsSuccessStatusCode && result.ContainsKey("frame")) {
                    foreach(var surface in new List<TaskbarLyrics>(instances))surface.Apply((Dictionary<string,object>)result["frame"]);
                } else {
                    string error=result.ContainsKey("error")?Convert.ToString(result["error"]):"歌曲操作失败，请重试";
                    Log(error);
                }
            }
        } catch(Exception error) {
            Log("歌曲操作失败："+error.Message);
        } finally {actionPending=false;foreach(var surface in instances){surface.pendingSeekMs=-1;surface.RefreshControls();}}
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
        coverKey=key;
        if(source==""){SetCoverImage(null);return;}
        Task<BitmapSource> pending;
        if(!coverCache.TryGetValue(key,out pending)) {
            // 同一封面只读取一次，所有屏幕共享冻结后的图片。
            if(coverCache.Count>=8)coverCache.Remove(new List<string>(coverCache.Keys)[0]);
            pending=LoadCover(source,fallback);coverCache[key]=pending;
        }
        BitmapSource image=await pending;
        if(closed || coverKey!=key)return;
        SetCoverImage(image);
    }
    void SetCoverImage(BitmapSource image)
    {
        // 新封面解码前保留旧图，解码后再启动过渡；快速切歌忽略旧请求。
        var previous=coverImage.Opacity>=.5?coverImage.Source:coverPrevious.Source;
        if(transitionsEnabled && coverFadeClock.IsRunning && coverLayers.ActualWidth>0 && coverLayers.ActualHeight>0){
            // 连续切歌从正在显示的混合图继续，避免退回某一张旧封面。
            var dpi=VisualTreeHelper.GetDpi(coverLayers);
            var mixed=new RenderTargetBitmap((int)Math.Ceiling(coverLayers.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(coverLayers.ActualHeight*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);
            mixed.Render(coverLayers);mixed.Freeze();previous=mixed;
        }
        bool animate=transitionsEnabled && previous!=null && previous!=image;
        coverPrevious.Source=animate?previous:null;coverPrevious.Visibility=animate?Visibility.Visible:Visibility.Collapsed;coverPrevious.Opacity=1;
        coverImage.Source=image;coverImage.Visibility=image!=null?Visibility.Visible:Visibility.Collapsed;
        coverImage.Opacity=animate?0:1;coverFallback.Visibility=image!=null?Visibility.Collapsed:Visibility.Visible;
        if(animate)coverFadeClock.Restart();else coverFadeClock.Reset();
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
        // 辅助程序先更新、播放器尚未重载时，也兼容旧版配置快照。
        var s=new Dictionary<string,object>(defaults);
        foreach(var setting in (Dictionary<string,object>)frame["settings"])s[setting.Key]=setting.Value;
        ConfigureNotice(s);
        string nextTrack=frame.ContainsKey("trackId")?Convert.ToString(frame["trackId"]):"";
        string title=frame.ContainsKey("title")?Convert.ToString(frame["title"]):"",artist=frame.ContainsKey("artist")?Convert.ToString(frame["artist"]):"";
        bool taskbarSwitch=nextTrack!="" && frame.ContainsKey("noticeTaskbarTrackId") && Convert.ToString(frame["noticeTaskbarTrackId"])==nextTrack;
        ObserveTrack(Convert.ToBoolean(frame["hasTrack"])?nextTrack:"",title,taskbarSwitch);
        IntPtr taskbar=display.Taskbar;
        Rect bar=display.Info.Monitor;
        // 任务栏可能报告其他 DPI，排版与定位统一使用歌词窗口的实际缩放。
        double scale=Math.Max(.5,GetDpiForWindow(handle)/96.0);
        if(taskbar!=IntPtr.Zero) {
            if(!GetWindowRect(taskbar,out bar) || bar.Height<4 || bar.Width<4 || !IsWindowVisible(taskbar)){allowDisplay=false;CancelScrub();CloseTrackNotice();SetHover(false);ShowWindow(handle,0);return;}
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
        if (hide) { CancelScrub();CloseTrackNotice();SetHover(false);ShowWindow(handle,0); return; }
        OwnTaskbar(taskbar);
        Rect area=Placement(bar,scale,s);
        // WPF 使用逻辑像素，偏移自动适配每块屏幕的 DPI。
        verticalShift.Y=s.ContainsKey("verticalOffset")?Math.Max(-20,Math.Min(20,Convert.ToDouble(s["verticalOffset"]))):0;
        if(nextTrack!=controlTrackId){CancelScrub();pendingSeekMs=-1;}
        controlTrackId=nextTrack;hasControlTrack=controlTrackId!="";
        durationMs=frame.ContainsKey("durationMs")?Convert.ToDouble(frame["durationMs"]):0;
        if(double.IsNaN(durationMs) || double.IsInfinity(durationMs) || durationMs<0)durationMs=0;
        progressEnabled=s.ContainsKey("hoverProgress") && Convert.ToBoolean(s["hoverProgress"]) && durationMs>0 && hasControlTrack;
        hoverEnabled=s.ContainsKey("hoverControls") && Convert.ToBoolean(s["hoverControls"]);
        if(!progressEnabled)CancelScrub();
        SetHover(hovering && (hoverEnabled || progressEnabled || coverEnabled));
        primary.FontFamily=secondary.FontFamily=new FontFamily(Convert.ToString(frame["fontFamily"]));
        Style(s,area.Height/scale-(hovering && progressEnabled && !coverHovered?14:0),frame);
        ButtonBackground("ButtonHoverBackground",s,"buttonHoverBackground","buttonHoverColor");
        ButtonBackground("ButtonPressedBackground",s,"buttonPressedBackground","buttonPressedColor");
        primary.Text=Convert.ToString(frame["text"]);
        secondary.Text=Convert.ToString(frame["secondary"]);
        favorite=frame.ContainsKey("isFavorite") && Convert.ToBoolean(frame["isFavorite"]);
        frameBusy=frame.ContainsKey("commandBusy") && Convert.ToBoolean(frame["commandBusy"]);
        coverEnabled=s.ContainsKey("showCover") && Convert.ToBoolean(s["showCover"]);
        if(!coverEnabled)SetCoverHover(false);
        if(title!=songTitle.Text || artist!=songArtist.Text){songTitle.Text=title;songArtist.Text=artist;if(coverHovered)infoClock.Restart();}
        noticeTitle.Text=title;noticeArtist.Text=artist;
        noticeTitle.FontFamily=noticeArtist.FontFamily=primary.FontFamily;
        StyleNotice(s,frame);
        songTitle.FontFamily=songArtist.FontFamily=primary.FontFamily;
        songTitle.FontSize=Math.Min(16,Math.Max(10,(area.Height/scale-6)*.38));songArtist.FontSize=Math.Min(12,Math.Max(8,(area.Height/scale-6)*.28));
        songTitle.Foreground=primaryWaiting;songArtist.Foreground=secondaryWaiting;
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
        songTitleViewport.Width=songArtistViewport.Width=contentWidth;
        FitLine(songTitle,contentWidth,true);FitLine(songArtist,contentWidth,true);
        controlsViewport.MaxWidth=progressTimeViewport.MaxWidth=contentWidth;controlsViewport.MaxHeight=progressTimeViewport.MaxHeight=Math.Max(1,area.Height/scale-(hovering && progressEnabled && !coverHovered?14:4));
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
        controlsViewport.HorizontalAlignment=progressTimeViewport.HorizontalAlignment=alignment=="left"?HorizontalAlignment.Left:HorizontalAlignment.Center;
        masking=Convert.ToBoolean(s["progressMask"]);
        secondaryMasking=masking && Convert.ToString(s["layout"])=="double" && Convert.ToString(s["secondary"])=="translation";
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
        // 抵消整体透明度，避免点击底层被舍入成完全透明的像素。
        var hitBackground=(SolidColorBrush)window.Resources["ButtonHitBackground"];
        byte hitAlpha=(byte)Math.Min(255,Math.Ceiling(1/Math.Max(1.0/255,window.Opacity)));
        if(hitBackground.Color.A!=hitAlpha)hitBackground.Color=Color.FromArgb(hitAlpha,0,0,0);
        string theme=Convert.ToString(s["theme"]);
        bool light=theme=="light" || (theme=="auto" && TaskbarLight());
        primaryWaiting.Color=(Color)ColorConverter.ConvertFromString(theme=="custom" ? Convert.ToString(s["primaryColor"]) : light ? "#202D35" : "#F1F7F6");
        secondaryWaiting.Color=(Color)ColorConverter.ConvertFromString(theme=="custom" ? Convert.ToString(s["secondaryColor"]) : light ? "#62737B" : "#B4C3C1");
        var palette=frame.ContainsKey("playedPalette")?(Dictionary<string,object>)frame["playedPalette"]:new Dictionary<string,object>{{"primary",s["primaryPlayedColor"]},{"secondary",s["secondaryPlayedColor"]},{"controls",""}};
        Color firstPlayed=(Color)ColorConverter.ConvertFromString(Convert.ToString(palette["primary"]));
        Color secondPlayed=(Color)ColorConverter.ConvertFromString(Convert.ToString(palette["secondary"]));
        if(theme=="custom") {
            Color color=primaryWaiting.Color;light=(color.R+color.G+color.B)/3<110;
        }
        // 背景 alpha 单独调节，图标保持清晰，不跟随歌词遮罩配色。
        Color backgroundColor,iconColor;
        if(frame.ContainsKey("controlsPalette")) {
            var controls=(Dictionary<string,object>)frame["controlsPalette"];
            Color background=(Color)ColorConverter.ConvertFromString(Convert.ToString(controls["background"]));
            background.A=(byte)Math.Round(Convert.ToDouble(controls["opacity"])*255/100);
            backgroundColor=background;
            iconColor=(Color)ColorConverter.ConvertFromString(Convert.ToString(controls["foreground"]));
        }else {
            backgroundColor=(Color)ColorConverter.ConvertFromString(light?"#EDE8EDEF":"#ED202831");
            iconColor=primaryWaiting.Color;
        }
        smartColors=s.ContainsKey("smartContrast") && Convert.ToBoolean(s["smartContrast"]);
        autoPlayed=Convert.ToString(s["playedColorSource"])=="cover";autoIcons=Convert.ToString(s["controlsIconColorSource"])=="cover";
        transitionsEnabled=s.ContainsKey("trackTransitions") && Convert.ToBoolean(s["trackTransitions"]);
        transitionDuration=s.ContainsKey("transitionDuration")?Math.Max(100,Math.Min(1000,Convert.ToDouble(s["transitionDuration"]))):280;
        taskbarBackground=(Color)ColorConverter.ConvertFromString(TaskbarLight()?"#E8EDEF":"#202831");
        progressTime.Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString(TaskbarLight()?"#202D35":"#F1F7F6"));
        SetPalette(new[]{firstPlayed,secondPlayed,backgroundColor,iconColor});
        if(!transitionsEnabled){coverFadeClock.Reset();coverPrevious.Visibility=Visibility.Collapsed;coverPrevious.Source=null;coverImage.Opacity=1;}
    }

    static bool TaskbarLight()
    {
        return Convert.ToInt32(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","SystemUsesLightTheme",0))!=0;
    }
    static Color MixColor(Color from,Color to,double fraction)
    {
        return Color.FromArgb((byte)Math.Round(from.A+(to.A-from.A)*fraction,MidpointRounding.AwayFromZero),(byte)Math.Round(from.R+(to.R-from.R)*fraction,MidpointRounding.AwayFromZero),(byte)Math.Round(from.G+(to.G-from.G)*fraction,MidpointRounding.AwayFromZero),(byte)Math.Round(from.B+(to.B-from.B)*fraction,MidpointRounding.AwayFromZero));
    }
    static double Luminance(Color color)
    {
        Func<byte,double> channel=value=>{double v=value/255.0;return v<=.04045?v/12.92:Math.Pow((v+.055)/1.055,2.4);};
        return channel(color.R)*.2126+channel(color.G)*.7152+channel(color.B)*.0722;
    }
    static double Contrast(Color first,Color second)
    {
        double a=Luminance(first),b=Luminance(second);return (Math.Max(a,b)+.05)/(Math.Min(a,b)+.05);
    }
    static Color ReadableColor(Color color,List<Color> backgrounds)
    {
        Func<Color,double> score=value=>{double result=double.MaxValue;foreach(var bg in backgrounds)result=Math.Min(result,Contrast(value,bg));return result;};
        Color best=color;double bestScore=score(color);if(bestScore>=3)return color;
        // 与预览相同：只对自动配色做最小亮度调整。
        for(int step=1;step<=100;step++)foreach(Color endpoint in new[]{Colors.White,Colors.Black}){
            Color candidate=MixColor(color,endpoint,step/100.0);double result=score(candidate);
            if(result>=3)return candidate;if(result>bestScore){best=candidate;bestScore=result;}
        }
        return best;
    }
    void SetPalette(Color[] target)
    {
        bool changed=paletteTo==null;
        if(!changed)for(int i=0;i<target.Length;i++)if(paletteTo[i]!=target[i])changed=true;
        if(paletteTo==null || !transitionsEnabled){paletteFrom=(Color[])target.Clone();paletteCurrent=(Color[])target.Clone();paletteTo=target;paletteClock.Reset();}
        else if(changed){paletteFrom=(Color[])paletteCurrent.Clone();paletteTo=target;paletteClock.Restart();}
        RenderPalette();
    }
    void RenderPalette()
    {
        if(paletteTo==null)return;
        double fraction=paletteClock.IsRunning?ClampRatio(paletteClock.Elapsed.TotalMilliseconds/transitionDuration):1;
        // 重复快照不重启动画；连续切歌从当前显示颜色继续。
        for(int i=0;i<paletteTo.Length;i++)paletteCurrent[i]=MixColor(paletteFrom[i],paletteTo[i],fraction);
        Color first=paletteCurrent[0],second=paletteCurrent[1],background=paletteCurrent[2],icon=paletteCurrent[3];
        Color hover=((SolidColorBrush)window.Resources["ButtonHoverBackground"]).Color,pressed=((SolidColorBrush)window.Resources["ButtonPressedBackground"]).Color;
        string key=first.ToString()+second.ToString()+background.ToString()+icon.ToString()+smartColors+autoPlayed+autoIcons+taskbarBackground.ToString()+hover.ToString()+pressed.ToString();
        if(fraction>=1)paletteClock.Reset();
        // 静止颜色无需逐帧重建画刷或重新计算对比度。
        if(key==paletteRenderKey)return;paletteRenderKey=key;
        if(smartColors){
            if(autoPlayed){first=ReadableColor(first,new List<Color>{taskbarBackground});second=ReadableColor(second,new List<Color>{taskbarBackground});}
            if(autoIcons){
                var backgrounds=new List<Color>{MixColor(taskbarBackground,Color.FromRgb(background.R,background.G,background.B),background.A/255.0)};
                if(hover.A>0)backgrounds.Add(hover);if(pressed.A>0)backgrounds.Add(pressed);
                icon=ReadableColor(icon,backgrounds);
            }
        }
        primaryAccent.Color=first;secondaryAccent.Color=second;
        controlsSurface.Background=new SolidColorBrush(background);controlsColor=new SolidColorBrush(icon);
        previousButton.Foreground=playButton.Foreground=nextButton.Foreground=favoriteButton.Foreground=controlsColor;
        favoriteGlyph.Fill=favorite?controlsColor:Brushes.Transparent;
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
        RenderTrackNotice();
        RenderPalette();
        if(coverFadeClock.IsRunning){
            double fraction=ClampRatio(coverFadeClock.Elapsed.TotalMilliseconds/transitionDuration);
            // 底图保持不透明，顶图淡入，避免中途露出任务栏背景。
            coverImage.Opacity=fraction;coverPrevious.Opacity=coverImage.Visibility==Visibility.Visible?1:1-fraction;
            if(fraction>=1){coverFadeClock.Reset();coverPrevious.Source=null;coverPrevious.Visibility=Visibility.Collapsed;}
        }
        // Shell 并非每次点击都会发出层级事件，渲染前检查可避免等待定时置顶。
        if (allowDisplay && IsWindowVisible(handle) && (!AboveTaskbar() || ShellCoversLyrics())) RestoreLayer();
        // 渲染帧推算本句进度，暂停冻结；每次宿主快照都会校准跳转与倍速。
        double current=timelineMs+(clockAdvancing ? lyricClock.Elapsed.TotalMilliseconds*playbackRate : 0);
        coverRotation.Angle=coverSpinning?(coverTimeMs+(clockAdvancing?lyricClock.Elapsed.TotalMilliseconds*playbackRate:0))*.018%360:0;
        double media=Math.Max(0,Math.Min(durationMs,coverTimeMs+(clockAdvancing?lyricClock.Elapsed.TotalMilliseconds*playbackRate:0)));
        double shown=scrubbing?scrubRatio*durationMs:pendingSeekMs>=0?pendingSeekMs:media;
        double ratio=durationMs>0?ClampRatio(shown/durationMs):0;
        // 使用按钮实际渲染边界，兼容居中、窄宽度和不同屏幕缩放。
        if(progressHost.Visibility==Visibility.Visible && controlsSurface.ActualWidth>0){
            var transform=controlsSurface.TransformToAncestor(lyricsHost);
            Point first=transform.Transform(new Point(0,0)),last=transform.Transform(new Point(controlsSurface.ActualWidth,0));
            progressHost.Width=Math.Max(0,last.X-first.X);progressHost.Margin=new Thickness(first.X,0,0,2);
        }
        progressFill.Width=progressTrack.ActualWidth*ratio;
        progressThumb.Margin=new Thickness(Math.Max(0,Math.Min(Math.Max(0,progressTrack.ActualWidth-8),progressFill.Width-4)),0,0,0);
        progressElapsed=FormatTime(shown);progressDuration=FormatTime(durationMs);progressTime.Text=progressElapsed+" / "+progressDuration;
        controlsViewport.MaxHeight=progressTimeViewport.MaxHeight=Math.Max(1,window.ActualHeight-(hovering && progressEnabled && !coverHovered?14:4));
        songTitleShift.X=InfoOffset(songTitle.Width,songTitleViewport.Width,infoClock.Elapsed.TotalSeconds);
        songArtistShift.X=InfoOffset(songArtist.Width,songArtistViewport.Width,infoClock.Elapsed.TotalSeconds);
        double progress=lineEndMs>lineStartMs ? Math.Max(0,Math.Min(1,(current-lineStartMs)/(lineEndMs-lineStartMs))) : 0;
        primaryShift.X=scrolling ? -Math.Max(0,primary.Width-primaryViewport.Width)*progress : 0;
        secondaryShift.X=scrollingTranslation ? -Math.Max(0,secondary.Width-secondaryViewport.Width)*progress : 0;
        primaryMaskWidth=masking?MaskWidth(primaryStops,current,progress,primaryTextWidth):0;
        secondaryMaskWidth=secondaryMasking?MaskWidth(secondaryStops,current,progress,secondaryTextWidth):0;
        ColorProgress(primary,primaryProgress,primaryWaiting,masking,primaryMaskWidth,primaryTextWidth);
        ColorProgress(secondary,secondaryProgress,secondaryWaiting,secondaryMasking,secondaryMaskWidth,secondaryTextWidth);
    }
    static void ColorProgress(TextBlock text,LinearGradientBrush brush,SolidColorBrush waiting,bool enabled,double playedWidth,double textWidth)
    {
        // 双色分界与文字使用同一坐标，滚动、缩放时仍只绘制一份字形。
        brush.EndPoint=new Point(Math.Max(1,textWidth),0);
        brush.GradientStops[1].Offset=brush.GradientStops[2].Offset=ClampRatio(playedWidth/Math.Max(1,textWidth));
        text.Foreground=enabled?(Brush)brush:waiting;
    }
    static double InfoOffset(double width,double viewport,double seconds)
    {
        double distance=Math.Max(0,width-viewport);if(distance<=0)return 0;
        // 两端稍作停留，往返滚动不受歌曲暂停或播放进度影响。
        double travel=distance/26,phase=seconds%(travel*2+1.6);
        if(phase<.8)return 0;if(phase<.8+travel)return -(phase-.8)*26;
        if(phase<1.6+travel)return -distance;return -distance+(phase-1.6-travel)*26;
    }
    static string FormatTime(double milliseconds)
    {
        long seconds=(long)Math.Floor(Math.Max(0,milliseconds)/1000);
        return seconds>=3600?(seconds/3600)+":"+((seconds/60)%60).ToString("00")+":"+(seconds%60).ToString("00"):(seconds/60).ToString("00")+":"+(seconds%60).ToString("00");
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
            ColorProgress(primary,primaryProgress,primaryWaiting,mask,TextWidth(primary.Text,primary)*.55,TextWidth(primary.Text,primary));
            ColorProgress(secondary,secondaryProgress,secondaryWaiting,mask && layout=="double",TextWidth(secondary.Text,secondary)*.55,TextWidth(secondary.Text,secondary));
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
