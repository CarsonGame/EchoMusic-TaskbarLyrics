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
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd,int attribute,out int value,int size);
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
    UserControl window;
    HwndSource nativeHost;
    bool embedded,hostLost;
    IntPtr boundTaskbar;
    [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr hwnd,IntPtr parent);
    [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr hwnd,ref NativePoint point);
    Grid coverHost,coverVisual,coverLayers,lyricsHost,controlsAnchor,interactionHost;
    RowDefinition infoRow,controlsRow,progressRow;
    bool hoverInfoEnabled=true;
    Button coverButton;
    static bool mainWindowPending;
    StackPanel lyricsText;
    bool coverHovered;
    readonly Stopwatch infoClock=new Stopwatch();
    readonly Stopwatch hoverClock=new Stopwatch();
    double hoverAmount,hoverFrom,hoverTarget;
    const double HoverDuration=160;
    Viewbox noticeViewport;
    StackPanel noticeLine;
    System.Windows.Documents.Run noticeSongRun,noticeSeparatorRun,noticeArtistRun;
    string noticeSong="",noticePerformer="";
    bool noticeArtistVisible;
    bool noticeVisible;
    Border trackNoticeSurface;
    Grid noticeTitleViewport;
    TextBlock noticeTitle;
    TranslateTransform noticeTitleShift;
    bool noticeScrollText=true,noticeMeasurePending;
    SolidColorBrush noticeTitleInk,noticeArtistInk;
    readonly Stopwatch noticeColorClock=new Stopwatch();
    Color[] noticeColorFrom,noticeColorTo,noticeColorCurrent;
    double uiAmount;
    Image coverImage,coverPrevious;
    Border coverFallback,controlsSurface;
    Viewbox controlsViewport,progressTimeViewport;
    RotateTransform coverRotation;
    RectangleGeometry coverRectangle;
    EllipseGeometry coverCircle;
    Button previousButton,playButton,nextButton,favoriteButton;
    System.Windows.Shapes.Path playGlyph,pauseGlyph,favoriteGlyph,loadingGlyph;
    RotateTransform loadingSpin;
    bool frameLoading;
    static readonly Stopwatch loadingAnimationClock=Stopwatch.StartNew();
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
    double requestedPrimarySize=16,requestedSecondarySize=12,lyricAreaHeight=48;
    bool dualLyrics;
    object primaryCharacters=new object[0],secondaryCharacters=new object[0];
    TranslateTransform lyricTransitionShift;
    ScaleTransform lyricTransitionScale;
    bool lyricTransitions,lyricTransitionSuppressed;
    double lyricTransitionDuration=180,lyricTransitionTravel,lyricTransitionHorizontalTravel,lyricTransitionTopTravel;
    string lyricTransitionStyle="up";
    LinearGradientBrush lyricTransitionReveal;
    string appliedLyricTransitionKey="";
    static string observedLyricKey="",observedLyricTrack="";
    static double observedLyricTimeline;
    static readonly Stopwatch lyricTransitionClock=new Stopwatch();
    IntPtr foregroundHook, reorderHook;
    readonly WinEvent shellEvent;
    int shellRaises,positionUpdates,styleUpdates;
    bool restoreQueued;
    IntPtr unresolvedShellCover;
    sealed class MaskStop { public double Start,End,Left,Right; }
    List<MaskStop> primaryStops=new List<MaskStop>(), secondaryStops=new List<MaskStop>();
    string primaryProfileKey="",secondaryProfileKey="";
    IntPtr handle,ownerBar;
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
        {"hoverSongInfo",true},
        {"smartContrast",true},
        {"trackTransitions",true},
        {"transitionDuration",280},
        {"lyricTransitions",true},
        {"lyricTransitionDuration",180},
        {"lyricTransitionStyle","up"},
        {"noticeBackgroundStyle","glass"},
        {"noticeBackgroundColor","#ffffff"},
        {"noticeTransparency",100},
        {"noticeRadius",15},
        {"noticeGap",8},
        {"noticeShowArtist",true},
        {"noticeTitleFontSize",19},
        {"noticeArtistFontSize",19},
        {"noticeTextSource","cover-primary"},
        {"noticeTextAlignment","center"},
        {"noticeTitleColor","#ffffff"},
        {"noticeArtistColor","#ffffff"},
        {"noticeScrollText",true}
    };


    TaskbarLyrics(Display target) : this(target,false) { }
    TaskbarLyrics(Display target,bool floating)
    {
        display=target;shellEvent=OnShellEvent;
            // 嵌入 XAML，发布时无需额外 UI 文件或运行时安装。
            using (Stream stream = typeof(TaskbarLyrics).Assembly.GetManifestResourceStream("LyricsWindow.xaml"))
                window = (UserControl)System.Windows.Markup.XamlReader.Load(stream);
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
            coverButton=(Button)window.FindName("CoverButton");
            coverButton.Click+=(sender,e)=>ShowMainWindow();
            lyricsHost=(Grid)window.FindName("LyricsHost");lyricsText=(StackPanel)window.FindName("LyricsText");
            lyricTransitionShift=(TranslateTransform)window.FindName("LyricTransitionShift");
            lyricTransitionScale=(ScaleTransform)window.FindName("LyricTransitionScale");
            lyricTransitionReveal=(LinearGradientBrush)window.FindName("LyricTransitionReveal");
            noticeViewport=(Viewbox)window.FindName("NoticeViewport");noticeLine=(StackPanel)window.FindName("NoticeLine");noticeSongRun=(System.Windows.Documents.Run)window.FindName("NoticeSongRun");noticeSeparatorRun=(System.Windows.Documents.Run)window.FindName("NoticeSeparatorRun");noticeArtistRun=(System.Windows.Documents.Run)window.FindName("NoticeArtistRun");trackNoticeSurface=(Border)window.FindName("TrackNoticeSurface");
            noticeTitleInk=(SolidColorBrush)window.Resources["NoticeTitleInk"];noticeArtistInk=(SolidColorBrush)window.Resources["NoticeArtistInk"];
            noticeTitleViewport=(Grid)window.FindName("NoticeTitleViewport");
            noticeTitle=(TextBlock)window.FindName("NoticeTitle");
            noticeTitleShift=(TranslateTransform)window.FindName("NoticeTitleShift");
            controlsAnchor=(Grid)window.FindName("ControlsAnchor");
            interactionHost=(Grid)window.FindName("InteractionHost");infoRow=(RowDefinition)window.FindName("InfoRow");controlsRow=(RowDefinition)window.FindName("ControlsRow");progressRow=(RowDefinition)window.FindName("ProgressRow");
            coverImage=(Image)window.FindName("CoverImage");coverFallback=(Border)window.FindName("CoverFallback");
            coverPrevious=(Image)window.FindName("CoverPrevious");
            coverLayers=(Grid)window.FindName("CoverLayers");
            progressHost=(Grid)window.FindName("ProgressHost");progressTrack=(Grid)window.FindName("ProgressTrack");
            progressFill=(Border)window.FindName("ProgressFill");progressThumb=(System.Windows.Shapes.Ellipse)window.FindName("ProgressThumb");
            progressBed=(Border)window.FindName("ProgressBed");progressTime=(TextBlock)window.FindName("ProgressTime");
            progressTimeViewport=(Viewbox)window.FindName("ProgressTimeViewport");
            progressTrack.MouseEnter+=(sender,e)=>SetProgressHover(true);
            progressTrack.MouseLeave+=(sender,e)=>{if(!scrubbing)UpdateHover();};
            progressTrack.MouseLeftButtonDown+=(sender,e)=>{BeginScrub(e.GetPosition(progressTrack).X/progressTrack.ActualWidth);if(scrubbing)progressTrack.CaptureMouse();e.Handled=true;};
            progressTrack.MouseMove+=(sender,e)=>{if(scrubbing){scrubRatio=ClampRatio(e.GetPosition(progressTrack).X/progressTrack.ActualWidth);RenderScroll(null,EventArgs.Empty);e.Handled=true;}};
            progressTrack.MouseLeftButtonUp+=(sender,e)=>{if(scrubbing){scrubRatio=ClampRatio(e.GetPosition(progressTrack).X/progressTrack.ActualWidth);FinishScrub();e.Handled=true;}};
            progressTrack.LostMouseCapture+=(sender,e)=>{if(scrubbing)CancelScrub();};
            coverRotation=(RotateTransform)window.FindName("CoverRotation");
            coverRectangle=(RectangleGeometry)coverVisual.Resources["CoverRectangle"];coverCircle=(EllipseGeometry)coverVisual.Resources["CoverCircle"];
            controlsViewport=(Viewbox)window.FindName("ControlsViewport");controlsSurface=(Border)window.FindName("ControlsSurface");
            previousButton=(Button)window.FindName("PreviousButton");playButton=(Button)window.FindName("PlayButton");nextButton=(Button)window.FindName("NextButton");favoriteButton=(Button)window.FindName("FavoriteButton");
            playGlyph=(System.Windows.Shapes.Path)window.FindName("PlayGlyph");pauseGlyph=(System.Windows.Shapes.Path)window.FindName("PauseGlyph");favoriteGlyph=(System.Windows.Shapes.Path)window.FindName("FavoriteGlyph");
            loadingGlyph=(System.Windows.Shapes.Path)window.FindName("LoadingGlyph");loadingSpin=(RotateTransform)window.FindName("LoadingSpin");
            previousButton.Click+=(sender,e)=>SendAction("previousTrack");playButton.Click+=(sender,e)=>SendAction("togglePlayback");nextButton.Click+=(sender,e)=>SendAction("nextTrack");favoriteButton.Click+=(sender,e)=>SendAction("toggleFavorite");

        try { CreateHost(!floating && IsWindow(target.Taskbar)); }
        catch(Exception error) {
            if(floating || !(error is System.ComponentModel.Win32Exception || error is InvalidOperationException || error is NotSupportedException))throw;
            DestroyHost();Log("任务栏挂载失败，改用透明浮窗："+error.Message);CreateHost(false);
        }
        instances.Add(this);
    }
    void CreateHost(bool child)
    {
        embedded=child;hostLost=false;boundTaskbar=display.Taskbar;ownerBar=IntPtr.Zero;
        Rect area=display.Info.Monitor;
        if(IsWindow(boundTaskbar))GetWindowRect(boundTaskbar,out area);
        else area.Top=area.Bottom-(int)Math.Round(48*display.Scale);
        // 原生宿主承载 XAML；透明子窗口使用新的每像素透明接口。
        var parameters=new HwndSourceParameters("EchoMusic · 微光任务栏歌词") {
            ParentWindow=child?boundTaskbar:IntPtr.Zero,
            WindowStyle=child?0x46000000:unchecked((int)0x86000000),
            ExtendedWindowStyle=0x80000A0|(child?0:8),
            PositionX=child?0:area.Left,PositionY=child?0:area.Top,
            Width=(int)Math.Round(360*display.Scale),Height=area.Height,
            UsesPerPixelTransparency=true,TreatAsInputRoot=true,HwndSourceHook=Hook
        };
        nativeHost=new HwndSource(parameters);handle=nativeHost.Handle;
        nativeHost.Disposed+=(sender,e)=>hostLost=true;
        if(!IsWindow(handle) || (child && GetAncestor(handle,1)!=boundTaskbar))throw new InvalidOperationException("原生窗口挂载校验失败");
        nativeHost.RootVisual=window;
    }
    bool HostAlive { get { return !hostLost && !nativeHost.IsDisposed && IsWindow(handle); } }
    bool NeedsRebuild(Display target)
    {
        // Explorer 重建或屏幕任务栏改变时创建新宿主，不跨进程改挂旧窗口。
        return !HostAlive || boundTaskbar!=target.Taskbar || (embedded && GetAncestor(handle,1)!=boundTaskbar);
    }
    void DestroyHost()
    {
        if(handle==IntPtr.Zero)return;
        if(!nativeHost.IsDisposed) {
            nativeHost.RootVisual=null;
            uint process;
            if(IsWindow(handle) && GetWindowThreadProcessId(handle,out process)!=0 && process==(uint)Process.GetCurrentProcess().Id) {
                ShowWindow(handle,0);
                if(embedded)SetParent(handle,IntPtr.Zero);
            }
            nativeHost.Dispose();
        }
        handle=IntPtr.Zero;
    }
    void Open()
    {
        window.Opacity=0;ShowWindow(handle,0);
        CompositionTarget.Rendering+=RenderScroll;
        pointerTimer=new DispatcherTimer(DispatcherPriority.Input){Interval=TimeSpan.FromMilliseconds(30)};
        pointerTimer.Tick+=(sender,e)=>UpdateHover();pointerTimer.Start();
        if(!embedded) {
            foregroundHook=SetWinEventHook(3,3,IntPtr.Zero,shellEvent,0,0,2);
            reorderHook=SetWinEventHook(0x8002,0x8004,IntPtr.Zero,shellEvent,0,0,2);
        }
    }
    void Close()
    {
        if(closed)return;
        closed=true;CancelScrub();CloseTrackNotice();pointerTimer.Stop();instances.Remove(this);
        CompositionTarget.Rendering-=RenderScroll;
        UnhookWinEvent(foregroundHook);UnhookWinEvent(reorderHook);
        // 在自身 UI 线程解挂后销毁，兼容父任务栏已销毁的情形。
        DestroyHost();
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
                            coverHovered=coverHovered,songInfoVisible=(hovering || hoverAmount>0) && noticeVisible,songTitle=noticeSong,songArtist=noticePerformer,songInfoText=noticeSongRun.Text+noticeSeparatorRun.Text+noticeArtistRun.Text,songTitleOffset=noticeTitleShift.X,songArtistOffset=noticeTitleShift.X,hoverOpacity=hoverAmount,
                            trackNoticeVisible=noticeVisible,trackNoticeTitle=noticeSong,trackNoticeArtist=noticePerformer,trackNoticeOpacity=noticeViewport.Opacity,trackNoticeHovered=false,trackNoticeTitleOffset=noticeTitleShift.X,
                            trackNoticeNoActivate=(GetWindowLong(handle,-20)&0x8000000)!=0,
                            trackNoticeBackground="Transparent",trackNoticeGlass=false,trackNoticeWidth=trackNoticeSurface.Width,trackNoticeRadius=trackNoticeSurface.CornerRadius.TopLeft,trackNoticeTitleSize=noticeTitle.FontSize,trackNoticeArtistSize=noticeArtistRun.FontSize,trackNoticeTitleColor=noticeTitleInk.ToString(),trackNoticeArtistColor=noticeArtistInk.ToString(),trackNoticeArtistVisible=noticeArtistVisible,
                            transitionsEnabled=transitionsEnabled,transitionDuration=transitionDuration,coverOpacity=coverImage.Opacity,previousCoverVisible=coverPrevious.Visibility==Visibility.Visible,previousCoverOpacity=coverPrevious.Opacity,smartContrast=smartColors,taskbarBackground=taskbarBackground.ToString(),
                            buttonHoverBackground=((SolidColorBrush)window.Resources["ButtonHoverBackground"]).Color.ToString(),buttonPressedBackground=((SolidColorBrush)window.Resources["ButtonPressedBackground"]).Color.ToString(),
                            tooltipsEnabled=ToolTipService.GetIsEnabled(previousButton)||ToolTipService.GetIsEnabled(playButton)||ToolTipService.GetIsEnabled(nextButton)||ToolTipService.GetIsEnabled(favoriteButton),
                            hasButtonTooltips=previousButton.ToolTip!=null || playButton.ToolTip!=null || nextButton.ToolTip!=null || favoriteButton.ToolTip!=null,
                            maskVisible=masking, primaryMaskWidth=primaryMaskWidth,secondaryMaskWidth=secondaryMaskWidth,
                            singleText=true,primaryColorBoundary=primaryProgress.GradientStops[1].Offset,secondaryColorBoundary=secondaryProgress.GradientStops[1].Offset,
                            embedded=embedded && GetAncestor(handle,1)==boundTaskbar,hostMode=embedded?"taskbar-child":"floating",parent=GetAncestor(handle,1).ToInt64(),shellRaises=shellRaises,positionUpdates=positionUpdates,styleUpdates=styleUpdates, aboveTaskbar=AboveTaskbar(), shellCoversLyrics=ShellCoversLyrics(),
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
                        if(surfaces.TryGetValue(target.Info.Device,out surface) && surface.NeedsRebuild(target)){surface.Close();surfaces.Remove(target.Info.Device);}
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
        if(embedded && message==0x3d) { handled=true;return IntPtr.Zero; }
        if (message == 0x21) { handled=true; return new IntPtr(3); }
        if (message == 0x84) {
            // 封面、按钮与进度条接收点击，其余区域仍穿透到任务栏。
            var point=new Point(unchecked((short)(l.ToInt64()&0xffff)),unchecked((short)((l.ToInt64()>>16)&0xffff)));
            handled=true;return new IntPtr(scrubbing || (allowDisplay && InCover(point)) || (hovering && (InControls(point) || InProgress(point)))?1:-1);
        }
        return IntPtr.Zero;
    }

    void CloseTrackNotice(){noticeVisible=false;noticeViewport.Visibility=Visibility.Collapsed;}
    void RenderTrackNotice()
    {
        // 歌曲信息仅随悬停显隐，与按钮共用过渡进度。
        if(!hoverInfoEnabled || !allowDisplay || !IsWindowVisible(handle) || String.IsNullOrWhiteSpace(noticeSong) || hoverAmount<=0){CloseTrackNotice();return;}
        noticeVisible=true;noticeViewport.Visibility=Visibility.Visible;noticeViewport.Opacity=hoverAmount;
        if(noticeMeasurePending && noticeTitle.IsVisible){MeasureNotice();noticeMeasurePending=false;}
        noticeTitleShift.X=noticeScrollText?InfoOffset(noticeTitle.Width,noticeTitleViewport.Width,infoClock.Elapsed.TotalSeconds):0;
    }
    void StyleNotice(Dictionary<string,object> s,Dictionary<string,object> frame)
    {
        noticeScrollText=Convert.ToBoolean(s["noticeScrollText"]);
        // 歌名、分隔符和歌手共用字号，保留各自配色。
        noticeTitle.FontSize=noticeSongRun.FontSize=noticeArtistRun.FontSize=noticeSeparatorRun.FontSize=Convert.ToDouble(s["noticeTitleFontSize"]);
        Color title=(Color)ColorConverter.ConvertFromString(Convert.ToString(s["noticeTitleColor"])),artist=(Color)ColorConverter.ConvertFromString(Convert.ToString(s["noticeArtistColor"]));
        if(Convert.ToString(s["noticeTextSource"])=="auto"){
            bool light=TaskbarLight();
            title=ReadableNoticeColor((Color)ColorConverter.ConvertFromString(light?"#202D35":"#F1F7F6"),taskbarBackground);
            artist=ReadableNoticeColor((Color)ColorConverter.ConvertFromString(light?"#4B5561":"#BFC5CA"),taskbarBackground);
        }
        string informationSource=Convert.ToString(s["noticeTextSource"]);
        if((informationSource=="cover" || informationSource=="cover-primary") && frame.ContainsKey("noticePalette")){
            var colors=(Dictionary<string,object>)frame["noticePalette"];
            title=(Color)ColorConverter.ConvertFromString(Convert.ToString(colors["title"]));artist=(Color)ColorConverter.ConvertFromString(Convert.ToString(colors["artist"]));
            if(smartColors){title=ReadableNoticeColor(title,taskbarBackground);artist=ReadableNoticeColor(artist,taskbarBackground);}
        }
        SetNoticePalette(title,artist);
        noticeArtistVisible=Convert.ToBoolean(s["noticeShowArtist"]) && !String.IsNullOrWhiteSpace(noticePerformer);
        noticeSongRun.Text=noticeSong;noticeSeparatorRun.Text=noticeArtistVisible?" · ":"";noticeArtistRun.Text=noticeArtistVisible?noticePerformer:"";
        noticeLine.HorizontalAlignment=Convert.ToString(s["noticeTextAlignment"])=="center"?HorizontalAlignment.Center:HorizontalAlignment.Left;
        noticeMeasurePending=true;
        MeasureNotice();
    }
    void SetNoticePalette(Color title,Color artist)
    {
        var target=new[]{title,artist};
        bool changed=noticeColorTo==null || noticeColorTo[0]!=title || noticeColorTo[1]!=artist;
        if(noticeColorTo==null || !transitionsEnabled){noticeColorFrom=(Color[])target.Clone();noticeColorCurrent=(Color[])target.Clone();noticeColorTo=target;noticeColorClock.Reset();}
        else if(changed){
            RenderNoticePalette();
            // 从当前显示颜色继续，重复快照不重启动画，连续切歌不闪白。
            noticeColorFrom=new[]{noticeTitleInk.Color,noticeArtistInk.Color};noticeColorTo=target;noticeColorClock.Restart();
        }
        RenderNoticePalette();
    }
    void RenderNoticePalette()
    {
        if(noticeColorTo==null)return;
        double fraction=noticeColorClock.IsRunning?ClampRatio(noticeColorClock.Elapsed.TotalMilliseconds/transitionDuration):1;
        for(int i=0;i<noticeColorTo.Length;i++)noticeColorCurrent[i]=MixColor(noticeColorFrom[i],noticeColorTo[i],fraction);
        noticeTitleInk.Color=noticeColorCurrent[0];noticeArtistInk.Color=noticeColorCurrent[1];
        if(fraction>=1)noticeColorClock.Reset();
    }
    void MeasureNotice()
    {
        // 整段信息只测量和滚动一次，歌手不会被单独裁成另一块。
        noticeTitleViewport.Width=Math.Max(1,trackNoticeSurface.Width);
        FitLine(noticeTitle,noticeTitleViewport.Width,noticeScrollText);
        AlignNoticeLine(noticeTitle,noticeTitleViewport.Width,noticeLine.HorizontalAlignment==HorizontalAlignment.Center,noticeScrollText);
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
    void SetHover(bool value)
    {
        // 从当前透明度开始反向过渡，快速移入移出不会闪跳。
        bool pointerInside=value;
        value=value && (hoverEnabled || progressEnabled || hoverInfoEnabled);
        double target=value?1:0;
        if(target!=hoverTarget){
            hoverFrom=hoverAmount;hoverTarget=target;hoverClock.Restart();if(value)infoClock.Restart();
        }
        if(!allowDisplay){hoverAmount=hoverTarget=0;hoverClock.Reset();}
        hovering=value;
        if(!value)SetCoverHover(false);
        progressHost.Visibility=(value || hoverAmount>0) && progressEnabled?Visibility.Visible:Visibility.Collapsed;
        SetProgressHover(value && progressHovered);
        int style=GetWindowLong(handle,-20);
        int desired=pointerInside?style&~0x20:style|0x20;
        // 仅在穿透状态改变时写入，避免鼠标检测反复触发窗口更新。
        if(desired!=style){SetWindowLong(handle,-20,desired);styleUpdates++;}
    }
    void SetProgressHover(bool value)
    {
        progressHovered=hovering && progressEnabled && (value || scrubbing);
        UpdateHoverVisibility();
    }
    void UpdateHoverVisibility()
    {
        bool active=hovering || hoverAmount>0 || noticeVisible;
        progressHost.Visibility=active && progressEnabled?Visibility.Visible:Visibility.Collapsed;
        // 隐藏按钮仍保留测量尺寸，时间和进度条不会跳位。
        controlsViewport.Visibility=active && (hoverEnabled || progressEnabled || hoverInfoEnabled)?(hoverEnabled && !progressHovered?Visibility.Visible:Visibility.Hidden):Visibility.Collapsed;
        progressTimeViewport.Visibility=progressHovered?Visibility.Visible:Visibility.Collapsed;
        bool showLyrics=!(hovering && (hoverEnabled || progressEnabled || hoverInfoEnabled));
        // 先恢复字号与测量，再显示歌词，避免等下一次快照时闪动。
        SizeLyrics(showLyrics);
        if(!showLyrics){lyricTransitionSuppressed=true;ResetLyricTransition();}
        lyricsText.Visibility=showLyrics || uiAmount<1?Visibility.Visible:Visibility.Hidden;
        LayoutInteraction();
        progressBed.Height=progressFill.Height=progressHovered?6:3;
        progressThumb.Visibility=progressHovered?Visibility.Visible:Visibility.Collapsed;
    }
    void SetCoverHover(bool value)
    {
        // 封面只保留点击命中，悬停展示与歌词区共用同一套 UI。
        coverHovered=value && coverEnabled && allowDisplay && !scrubbing;
    }
    void RenderHoverUi()
    {
        if(hoverClock.IsRunning){
            double phase=ClampRatio(hoverClock.Elapsed.TotalMilliseconds/HoverDuration);
            hoverAmount=hoverFrom+(hoverTarget-hoverFrom)*phase*phase*(3-2*phase);
            if(phase>=1){hoverAmount=hoverTarget;hoverClock.Reset();}
        }
        RenderTrackNotice();
        uiAmount=noticeVisible?Math.Max(hoverAmount,noticeViewport.Opacity):hoverAmount;
        UpdateHoverVisibility();
        controlsViewport.Opacity=progressHost.Opacity=progressTimeViewport.Opacity=uiAmount;
        // 只改变绘制透明度，退出时先恢复正常字号，再淡入歌词。
        if(hoverEnabled || progressEnabled || hoverInfoEnabled)lyricsText.Opacity*=1-uiAmount;
    }
    void LayoutInteraction()
    {
        bool active=hovering || hoverAmount>0 || noticeVisible;
        lyricsHost.Margin=active?new Thickness(0,Math.Max(2,-verticalShift.Y),0,Math.Max(2,verticalShift.Y)):new Thickness(0);
        if(!active){controlsViewport.MaxHeight=progressTimeViewport.MaxHeight=Math.Max(1,lyricAreaHeight-4);return;}
        double height=Math.Max(1,lyricAreaHeight-lyricsHost.Margin.Top-lyricsHost.Margin.Bottom);
        // 字号改变整段信息高度，同时给按钮和进度条保留空间。
        noticeViewport.MaxHeight=Math.Min(12*noticeTitle.FontSize/15,Math.Max(1,height*.4));
        double infoHeight=noticeVisible?noticeViewport.MaxHeight:0;
        double progressHeight=progressHost.Visibility==Visibility.Visible?progressHost.Height:0;
        controlsViewport.MaxHeight=progressTimeViewport.MaxHeight=Math.Max(1,height-infoHeight-progressHeight);
        // 明确共用缩放尺寸；关闭的按钮仍可测量宽度，但该行高度为零。
        double buttonScale=Math.Min(1,Math.Min(Math.Max(1,controlsViewport.MaxWidth-6)/144,controlsViewport.MaxHeight/24));
        controlsViewport.Width=progressTimeViewport.Width=144*buttonScale;
        controlsViewport.Height=progressTimeViewport.Height=24*buttonScale;
        infoRow.Height=new GridLength(infoHeight);
        controlsRow.Height=new GridLength(active && (hoverEnabled || progressHovered)?24*buttonScale:0);
        progressRow.Height=new GridLength(progressHeight);
        interactionHost.Height=infoRow.Height.Value+controlsRow.Height.Value+progressRow.Height.Value;
        double available=Math.Max(1,Math.Min(controlsViewport.MaxWidth,Math.Max(1,lyricsHost.ActualWidth)));
        // 与按钮同一中心；窄区域会整体缩小，文字宽度不影响按钮尺寸。
        double center=controlsSurface.ActualWidth>0 && controlsViewport.Visibility!=Visibility.Collapsed?controlsSurface.TransformToAncestor(lyricsHost).Transform(new Point(controlsSurface.ActualWidth/2,0)).X:controlsAnchor.Margin.Left+controlsAnchor.ActualWidth/2;
        double buttonsWidth=controlsSurface.TransformToAncestor(lyricsHost).Transform(new Point(controlsSurface.ActualWidth,0)).X-controlsSurface.TransformToAncestor(lyricsHost).Transform(new Point(0,0)).X;
        double width=Math.Max(1,Math.Min(buttonsWidth,Math.Min(available,2*Math.Min(center,Math.Max(0,available-center)))));
        double naturalHeight=Math.Max(1,noticeLine.ActualHeight>0?noticeLine.ActualHeight:noticeTitle.FontFamily.LineSpacing*noticeTitle.FontSize);
        double scale=Math.Min(1,noticeViewport.MaxHeight/naturalHeight),layoutWidth=width/scale;
        if(Math.Abs(trackNoticeSurface.Width-layoutWidth)>.01){trackNoticeSurface.Width=layoutWidth;noticeMeasurePending=true;}
        noticeViewport.MaxWidth=width;
        noticeViewport.Margin=new Thickness(Math.Max(0,center-width/2),0,0,0);
    }
    bool InCover(Point screen)
    {
        if(!coverEnabled || coverHost.Visibility!=Visibility.Visible)return false;
        Point first=coverHost.PointToScreen(new Point(0,0)),last=coverHost.PointToScreen(new Point(coverHost.ActualWidth,coverHost.ActualHeight));
        return new System.Windows.Rect(first,last).Contains(screen);
    }
    void UpdateHover()
    {
        if(closed || !HostAlive)return;
        NativePoint point=new NativePoint();Rect bounds;
        bool cursor=GetCursorPos(out point);
        if(scrubbing){SetHover(true);SetProgressHover(true);return;}
        bool inside=allowDisplay && IsWindowVisible(handle) && cursor;
        if(inside) {
            GetWindowRect(handle,out bounds);
            IntPtr hit=WindowFromPoint(point),bar=display.Taskbar;
            var name=new StringBuilder(256);if(bar==IntPtr.Zero)GetClassName(hit,name,name.Capacity);
            inside=point.X>=bounds.Left && point.X<bounds.Right && point.Y>=bounds.Top && point.Y<bounds.Bottom &&
                (hit==handle || hit==bar || (bar!=IntPtr.Zero && (IsChild(bar,hit) || GetAncestor(hit,2)==bar)) || (bar==IntPtr.Zero && (name.ToString()=="Progman" || name.ToString()=="WorkerW")));
        }
        SetCoverHover(inside && InCover(new Point(point.X,point.Y)));
        // 信息也在宿主内；先确认窗口未被遮挡，再更新悬停。
        SetHover(inside);
        var screen=new Point(point.X,point.Y);
        // 单独显示进度条时展开时间会移动布局，保留时间区域内的悬停。
        SetProgressHover(inside && (InProgress(screen) || (!hoverEnabled && progressHovered && InProgressTime(screen))));
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
    bool InProgressTime(Point screen)
    {
        if(progressTimeViewport.Visibility!=Visibility.Visible)return false;
        Point first=progressTimeViewport.PointToScreen(new Point(0,0)),last=progressTimeViewport.PointToScreen(new Point(progressTimeViewport.ActualWidth,progressTimeViewport.ActualHeight));
        return new System.Windows.Rect(first,last).Contains(screen);
    }
    static double ClampRatio(double value){return double.IsNaN(value)?0:Math.Max(0,Math.Min(1,value));}
    async void ShowMainWindow()
    {
        if(!coverEnabled || !allowDisplay || mainWindowPending)return;
        // 唤出主窗口不占用歌曲操作锁，所有屏幕合并重复点击。
        mainWindowPending=true;
        try{
            var url=new UriBuilder(stateEndpoint);url.Path="/show-main";
            using(var response=await actions.PostAsync(url.Uri,new ByteArrayContent(new byte[0])))
                if(!response.IsSuccessStatusCode)Log("显示播放器失败：HTTP "+(int)response.StatusCode);
        }catch(Exception error){Log("显示播放器失败："+error.Message);}
        finally{mainWindowPending=false;}
    }
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
        playButton.IsEnabled=enabled && !frameLoading;
        playGlyph.Visibility=!frameLoading && !clockPlaying?Visibility.Visible:Visibility.Collapsed;
        pauseGlyph.Visibility=!frameLoading && clockPlaying?Visibility.Visible:Visibility.Collapsed;
        loadingGlyph.Visibility=frameLoading?Visibility.Visible:Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetName(playButton,frameLoading?"歌曲加载中":clockPlaying?"暂停":"播放");
        System.Windows.Automation.AutomationProperties.SetName(favoriteButton,favorite?"取消收藏":"收藏");
        favoriteButton.Foreground=controlsColor;
        favoriteGlyph.Fill=favorite?favoriteButton.Foreground:Brushes.Transparent;
    }
    async void SendAction(string action,double value=0)
    {
        if(action=="togglePlayback" && frameLoading)return;
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
        if(closed || embedded || !HostAlive)return;
        IntPtr bar=display.Taskbar;
        // 合并 Shell 事件并检查实际遮挡，避免反复重排挤占歌词绘制。
        if ((evt!=3 && (objectId!=0 || childId!=0)) || !allowDisplay || !IsShellSurface(hwnd,bar)) return;
        if(evt==0x8003 && hwnd==unresolvedShellCover)unresolvedShellCover=IntPtr.Zero;
        if(restoreQueued)return;
        restoreQueued=true;
        // 先处理已经发生的遮挡，绘制前再检查动画期间的后续层级变化。
        EnsureLayer();
        window.Dispatcher.BeginInvoke(DispatcherPriority.Render,new Action(() => {
            restoreQueued=false;
            EnsureLayer();
        }));
    }

    void OwnTaskbar(IntPtr bar)
    {
        if(embedded)return;
        if(ownerBar==bar && GetWindow(handle,4)==bar)return;
        // 回退浮窗关联任务栏所有者，保留原来的悬浮与穿透行为。
        SetWindowLongPtr(handle,-8,bar);ownerBar=GetWindow(handle,4);
    }
    void Position(Rect area)
    {
        Rect actual;GetWindowRect(handle,out actual);
        bool moved=area.Left!=actual.Left || area.Top!=actual.Top || area.Width!=actual.Width || area.Height!=actual.Height;
        bool showing=!IsWindowVisible(handle);
        if(!moved && !showing)return;
        // 子窗口使用客户区坐标；浮窗才在首次显示时进入置顶层。
        var point=new NativePoint{X=area.Left,Y=area.Top};
        if(embedded && !ScreenToClient(boundTaskbar,ref point))return;
        uint flags=0x210u | (showing?0x40u:0x4u);
        if(SetWindowPos(handle,embedded?IntPtr.Zero:new IntPtr(-1),point.X,point.Y,area.Width,area.Height,flags))positionUpdates++;
        if(showing)unresolvedShellCover=IntPtr.Zero;
        EnsureLayer();
    }
    void EnsureLayer()
    {
        if(embedded || closed || !HostAlive || !allowDisplay || !IsWindowVisible(handle))return;
        // 每帧检查实际层级，异常立即恢复；正常时不调用置顶接口。
        bool above=AboveTaskbar(),topmost=(GetWindowLong(handle,-20)&8)!=0;IntPtr cover=FindShellCover();
        if(cover==IntPtr.Zero)unresolvedShellCover=IntPtr.Zero;
        if(above && topmost && (cover==IntPtr.Zero || cover==unresolvedShellCover))return;
        if(!above)unresolvedShellCover=IntPtr.Zero;
        RestoreLayer();
    }
    void RestoreLayer()
    {
        if(embedded || !HostAlive)return;
        IntPtr cover=FindShellCover();
        if(!SetWindowPos(handle,new IntPtr(-1),0,0,0,0,0x213))return;
        shellRaises++;
        // 仅对不同系统层保留遮挡记录，普通层的连续变化必须继续恢复。
        uint coverBand,selfBand;
        bool differentBand=cover!=IntPtr.Zero && GetWindowBand(cover,out coverBand) && GetWindowBand(handle,out selfBand) && coverBand!=selfBand;
        unresolvedShellCover=differentBand && FindShellCover()==cover?cover:IntPtr.Zero;
    }

    bool AboveTaskbar()
    {
        IntPtr bar=display.Taskbar;
        if(embedded)return GetAncestor(handle,1)==boundTaskbar;
        if(bar==IntPtr.Zero || GetWindow(handle,4)==bar)return true;
        for(IntPtr item=GetWindow(handle,2);item!=IntPtr.Zero;item=GetWindow(item,2)) if(item==bar) return true;
        return false;
    }

    bool ShellCoversLyrics(){return FindShellCover()!=IntPtr.Zero;}
    IntPtr FindShellCover()
    {
        if(embedded || !HostAlive)return IntPtr.Zero;
        // 开始菜单透明宿主也可能覆盖任务栏区域，检查实际 Shell 层级。
        Rect lyrics;GetWindowRect(handle,out lyrics);
        for(IntPtr item=GetWindow(handle,3);item!=IntPtr.Zero;item=GetWindow(item,3)) {
            Rect bounds;
            if(!IsWindowVisible(item) || !GetWindowRect(item,out bounds))continue;
            int cloaked;if(DwmGetWindowAttribute(item,14,out cloaked,4)==0 && cloaked!=0)continue;
            if(bounds.Left>=lyrics.Right || bounds.Right<=lyrics.Left || bounds.Top>=lyrics.Bottom || bounds.Bottom<=lyrics.Top)continue;
            if(IsShellSurface(item,display.Taskbar))return item;
        }
        return IntPtr.Zero;
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
        if(closed || NeedsRebuild(display))return;
        // 辅助程序先更新、播放器尚未重载时，也兼容旧版配置快照。
        var s=new Dictionary<string,object>(defaults);
        foreach(var setting in (Dictionary<string,object>)frame["settings"])s[setting.Key]=setting.Value;
        string nextTrack=frame.ContainsKey("trackId")?Convert.ToString(frame["trackId"]):"";
        string title=frame.ContainsKey("title")?Convert.ToString(frame["title"]):"",artist=frame.ContainsKey("artist")?Convert.ToString(frame["artist"]):"";
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
        hoverInfoEnabled=Convert.ToBoolean(s["hoverSongInfo"]);
        if(!progressEnabled)CancelScrub();
        SetHover(hovering);
        primary.FontFamily=secondary.FontFamily=new FontFamily(Convert.ToString(frame["fontFamily"]));
        Style(s,area.Height/scale,frame);
        ButtonBackground("ButtonHoverBackground",s,"buttonHoverBackground","buttonHoverColor");
        ButtonBackground("ButtonPressedBackground",s,"buttonPressedBackground","buttonPressedColor");
        primary.Text=Convert.ToString(frame["text"]);
        secondary.Text=Convert.ToString(frame["secondary"]);
        favorite=frame.ContainsKey("isFavorite") && Convert.ToBoolean(frame["isFavorite"]);
        frameBusy=frame.ContainsKey("commandBusy") && Convert.ToBoolean(frame["commandBusy"]);
        frameLoading=frame.ContainsKey("loading") && Convert.ToBoolean(frame["loading"]);
        coverEnabled=s.ContainsKey("showCover") && Convert.ToBoolean(s["showCover"]);
        if(!coverEnabled)SetCoverHover(false);
        if((title!=noticeSong || artist!=noticePerformer) && hovering)infoClock.Restart();
        noticeSong=title;noticePerformer=artist;
        noticeTitle.FontFamily=primary.FontFamily;
        StyleNotice(s,frame);
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
        controlsViewport.MaxWidth=progressTimeViewport.MaxWidth=contentWidth;
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
        UpdateLyricTransition(frame,s,nextTrack);
        RenderScroll(null,EventArgs.Empty);
        Rect actual;GetWindowRect(handle,out actual);
        // WPF 切换 DPI 会调整窗口尺寸，按实际边界校正，避免沿用旧屏幕尺寸。
        bool moved=area.Left!=actual.Left || area.Top!=actual.Top || area.Width!=actual.Width || area.Height!=actual.Height;
        // 重复快照不重排窗口，仅在真实移动或恢复显示时定位。
        if (moved || !IsWindowVisible(handle)) {
            Position(area);
        }
        UpdateHover();
    }

    void Style(Dictionary<string,object> s,double height,Dictionary<string,object> frame)
    {
        dualLyrics=Convert.ToString(s["layout"]) == "double";
        requestedPrimarySize=Convert.ToDouble(s["primaryFontSize"]);requestedSecondarySize=Convert.ToDouble(s["secondaryFontSize"]);lyricAreaHeight=height;
        primaryCharacters=frame.ContainsKey("characters")?frame["characters"]:new object[0];
        secondaryCharacters=frame.ContainsKey("secondaryCharacters")?frame["secondaryCharacters"]:new object[0];
        SizeLyrics(lyricsText.Visibility==Visibility.Visible);
        secondary.Visibility=dualLyrics ? Visibility.Visible : Visibility.Collapsed;
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

    void SizeLyrics(bool visible)
    {
        // 仅实际显示的歌词给进度条让位，隐藏时保持正常字号。
        double height=lyricAreaHeight-(visible && hovering && progressHost.Visibility==Visibility.Visible?14:0);
        double fit=Math.Min(1,Math.Max(1,height-4)/(primary.FontFamily.LineSpacing*(requestedPrimarySize+(dualLyrics?requestedSecondarySize:0))+(dualLyrics?1:0)));
        double first=requestedPrimarySize*fit,second=requestedSecondarySize*fit;
        if(primary.FontSize==first && secondary.FontSize==second)return;
        primary.FontSize=first;secondary.FontSize=second;
        primaryTextWidth=FitLine(primary,primaryViewport.Width,scrolling);
        secondaryTextWidth=FitLine(secondary,secondaryViewport.Width,scrollingTranslation);
        UpdateStops(primary,primaryCharacters,ref primaryProfileKey,ref primaryStops);
        UpdateStops(secondary,secondaryCharacters,ref secondaryProfileKey,ref secondaryStops);
    }

    void UpdateLyricTransition(Dictionary<string,object> frame,Dictionary<string,object> s,string track)
    {
        lyricTransitions=Convert.ToBoolean(s["lyricTransitions"]);
        lyricTransitionDuration=Convert.ToDouble(s["lyricTransitionDuration"]);
        string key=frame.ContainsKey("lyricKey")?Convert.ToString(frame["lyricKey"]):track+":"+lineStartMs;
        string nextStyle=Convert.ToString(s["lyricTransitionStyle"]);
        if(lyricTransitionStyle!=nextStyle && appliedLyricTransitionKey==key)lyricTransitionSuppressed=true;
        lyricTransitionStyle=nextStyle;
        if(key!=observedLyricKey || track!=observedLyricTrack){
            // 同曲正常换句才播放；首次显示、切歌和跳转直接同步。
            bool animate=observedLyricTrack==track && observedLyricKey!="" && clockAdvancing && Math.Abs(timelineMs-observedLyricTimeline)<1500 && lineEndMs-lineStartMs>=100 && scrubOwner==null && pendingSeekMs<0;
            observedLyricKey=key;observedLyricTrack=track;
            if(animate && dualLyrics && lyricTransitions)lyricTransitionClock.Restart();else lyricTransitionClock.Reset();
        }
        observedLyricTimeline=timelineMs;
        if(appliedLyricTransitionKey!=key){
            appliedLyricTransitionKey=key;lyricTransitionSuppressed=lyricsText.Visibility!=Visibility.Visible;
            ResetLyricTransition();window.UpdateLayout();
            var root=(Grid)window.Content;
            Point bottom=lyricsText.TransformToAncestor(root).Transform(new Point(0,lyricsText.ActualHeight));
            // 空间不足时只淡入，避免移动时裁切任务栏边缘。
            lyricTransitionTravel=Math.Min(3,Math.Max(0,root.ActualHeight-bottom.Y-1));
            Point right=lyricsText.TransformToAncestor(root).Transform(new Point(lyricsText.ActualWidth,0));
            lyricTransitionHorizontalTravel=Math.Min(6,Math.Max(0,root.ActualWidth-right.X-1));
            Point top=lyricsText.TransformToAncestor(root).Transform(new Point(0,0));
            lyricTransitionTopTravel=Math.Min(2,Math.Max(0,top.Y-1));
        }
        if(!lyricTransitions || !dualLyrics)lyricTransitionSuppressed=true;
    }
    void RenderLyricTransition()
    {
        if(!clockAdvancing)lyricTransitionSuppressed=true;
        double duration=Math.Min(lyricTransitionDuration,Math.Max(40,(lineEndMs-lineStartMs)*.35));
        double phase=lyricTransitionClock.Elapsed.TotalMilliseconds/duration;
        bool active=lyricTransitions && dualLyrics && !lyricTransitionSuppressed && clockAdvancing && lyricTransitionClock.IsRunning && phase<1;
        double remaining=active?Math.Pow(1-Math.Max(0,phase),3):0;
        lyricsText.Opacity=1-.55*remaining;
        lyricTransitionShift.X=lyricTransitionStyle=="slide"?lyricTransitionHorizontalTravel*remaining:0;
        lyricTransitionShift.Y=lyricTransitionStyle=="up"?lyricTransitionTravel*remaining:0;
        // 缩放只作用于绘制，不改字号、排版或进度遮罩的坐标。
        lyricTransitionScale.ScaleX=lyricTransitionScale.ScaleY=lyricTransitionStyle=="zoom"?1-.03*remaining:1;
        double progress=active?ClampRatio(phase):1;
        // 回弹始终收在原范围内，避免任务栏边缘裁字。
        if(lyricTransitionStyle=="spring")lyricTransitionScale.ScaleX=lyricTransitionScale.ScaleY=LyricKeyframe(progress,.88,1,.965,1);
        if(lyricTransitionStyle=="flip")lyricTransitionScale.ScaleY=1-.94*remaining;
        if(lyricTransitionStyle=="jelly"){
            lyricTransitionScale.ScaleX=LyricKeyframe(progress,.78,1,.94,1);
            lyricTransitionScale.ScaleY=LyricKeyframe(progress,1,.82,1,1);
        }
        // 柔和样式以平滑起停和较小振幅保持文字可读。
        double gentleRemaining=1-progress*progress*(3-2*progress);
        if(lyricTransitionStyle=="down" || lyricTransitionStyle=="drift" || lyricTransitionStyle=="breathe" || lyricTransitionStyle=="unveil")lyricsText.Opacity=1-.3*gentleRemaining;
        if(lyricTransitionStyle=="down")lyricTransitionShift.Y=-lyricTransitionTopTravel*gentleRemaining;
        if(lyricTransitionStyle=="drift"){
            lyricTransitionShift.X=Math.Min(3,lyricTransitionHorizontalTravel)*gentleRemaining;
            lyricTransitionShift.Y=Math.Min(1.5,lyricTransitionTravel)*gentleRemaining;
        }
        if(lyricTransitionStyle=="breathe")lyricTransitionScale.ScaleY=1-.06*gentleRemaining;
        RenderLyricReveal(lyricTransitionStyle=="wipe"?1-remaining:lyricTransitionStyle=="unveil"?1-gentleRemaining:1,lyricTransitionStyle=="unveil");
    }
    // 分段平滑插值与预览使用相同关键帧，最后回到原比例。
    static double LyricKeyframe(double progress,double first,double second,double third,double last){
        double start,end,local;
        if(progress<.55){start=first;end=second;local=progress/.55;}
        else if(progress<.78){start=second;end=third;local=(progress-.55)/.23;}
        else{start=third;end=last;local=(progress-.78)/.22;}
        return start+(end-start)*local*local*(3-2*local);
    }
    void RenderLyricReveal(double progress,bool vertical=false){
        Point direction=new Point(vertical?0:1,vertical?1:0);
        if(lyricTransitionReveal.EndPoint!=direction)lyricTransitionReveal.EndPoint=direction;
        double softness=vertical?.35:.16;
        lyricTransitionReveal.GradientStops[1].Offset=progress>=1?1:ClampRatio(progress*(1+softness)-softness);
        lyricTransitionReveal.GradientStops[2].Offset=progress>=1?1:ClampRatio(progress*(1+softness));
    }
    void ResetLyricTransition(){lyricsText.Opacity=1;lyricTransitionShift.X=lyricTransitionShift.Y=0;lyricTransitionScale.ScaleX=lyricTransitionScale.ScaleY=1;RenderLyricReveal(1);}

    void RenderScroll(object sender,EventArgs e)
    {
        if(closed || !HostAlive)return;
        // 所有屏幕共用动画时钟，结束加载立即恢复普通图标。
        loadingSpin.Angle=frameLoading?(loadingAnimationClock.Elapsed.TotalMilliseconds*.4)%360:0;
        RenderPalette();RenderNoticePalette();
        if(coverFadeClock.IsRunning){
            double fraction=ClampRatio(coverFadeClock.Elapsed.TotalMilliseconds/transitionDuration);
            // 底图保持不透明，顶图淡入，避免中途露出任务栏背景。
            coverImage.Opacity=fraction;coverPrevious.Opacity=coverImage.Visibility==Visibility.Visible?1:1-fraction;
            if(fraction>=1){coverFadeClock.Reset();coverPrevious.Source=null;coverPrevious.Visibility=Visibility.Collapsed;}
        }
        // Shell 并非每次点击都会发出层级事件，渲染前检查可避免等待定时置顶。
        EnsureLayer();
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
            progressHost.Width=Math.Max(0,last.X-first.X);progressHost.Margin=new Thickness(first.X,0,0,0);
        }
        progressFill.Width=progressTrack.ActualWidth*ratio;
        progressThumb.Margin=new Thickness(Math.Max(0,Math.Min(Math.Max(0,progressTrack.ActualWidth-8),progressFill.Width-4)),0,0,0);
        progressElapsed=FormatTime(shown);progressDuration=FormatTime(durationMs);progressTime.Text=progressElapsed+" / "+progressDuration;
        double progress=lineEndMs>lineStartMs ? Math.Max(0,Math.Min(1,(current-lineStartMs)/(lineEndMs-lineStartMs))) : 0;
        primaryShift.X=scrolling ? -Math.Max(0,primary.Width-primaryViewport.Width)*progress : 0;
        secondaryShift.X=scrollingTranslation ? -Math.Max(0,secondary.Width-secondaryViewport.Width)*progress : 0;
        primaryMaskWidth=masking?MaskWidth(primaryStops,current,progress,primaryTextWidth):0;
        secondaryMaskWidth=secondaryMasking?MaskWidth(secondaryStops,current,progress,secondaryTextWidth):0;
        ColorProgress(primary,primaryProgress,primaryWaiting,masking,primaryMaskWidth,primaryTextWidth);
        ColorProgress(secondary,secondaryProgress,secondaryWaiting,secondaryMasking,secondaryMaskWidth,secondaryTextWidth);
        RenderLyricTransition();
        RenderHoverUi();
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
