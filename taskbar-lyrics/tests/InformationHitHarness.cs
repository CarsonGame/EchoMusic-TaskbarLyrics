using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Threading.Tasks;

// 只在测试自己的宿主窗口，不移动用户鼠标，不操作真实任务栏。
class InformationHitHarness {
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr handle,int command);
    static Type type;static BindingFlags flags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static object Field(object target,string name){return (target==null?type:target.GetType()).GetField(name,flags).GetValue(target);}
    static void Set(object target,string name,object value){type.GetField(name,flags).SetValue(target,value);}
    static object Call(object target,string name,params object[] args){return type.GetMethod(name,flags).Invoke(target,args);}
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static Point RangePoint(Run run,int offset,int length){
        var first=run.ContentStart.GetPositionAtOffset(offset).GetCharacterRect(LogicalDirection.Forward);
        var last=run.ContentStart.GetPositionAtOffset(offset+length).GetCharacterRect(LogicalDirection.Backward);
        return new Point((first.Left+last.Right)/2,(first.Top+last.Bottom)/2);
    }
    [STAThread] static int Main(string[] args){
        string report=args[1];
        try{
            type=Assembly.LoadFrom(args[0]).GetType("TaskbarLyrics");
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(30)};
            timer.Tick+=async(sender,e)=>{
                timer.Stop();int code=0;
                try{
                    int checks=0;var observations=new List<object>();
                    using(var parent=new HwndSource(new HwndSourceParameters("歌曲信息命中测试") {WindowStyle=unchecked((int)0x86000000),ExtendedWindowStyle=0x8000080,PositionX=-3000,PositionY=-3000,Width=600,Height=72,UsesPerPixelTransparency=true})){
                        object display=null;foreach(object candidate in (IEnumerable)Call(null,"Screens")){display=candidate;break;}
                        display.GetType().GetField("Taskbar",flags).SetValue(display,parent.Handle);
                        var surface=Activator.CreateInstance(type,flags,null,new[]{display},null);Call(surface,"Open");
                        ((DispatcherTimer)Field(surface,"pointerTimer")).Stop();
                        ShowWindow(parent.Handle,4);ShowWindow((IntPtr)Field(surface,"handle"),4);
                        Set(surface,"allowDisplay",true);Set(surface,"hoverInfoEnabled",true);Set(surface,"hovering",true);Set(surface,"hoverAmount",1.0);Set(surface,"noticeVisible",true);
                        // 补齐实际帧提供的布局尺寸，避免零高度导致命中测试被裁切。
                        Set(surface,"lyricAreaHeight",48.0);Set(surface,"hoverEnabled",true);
                        ((Grid)Field(surface,"controlsAnchor")).Width=308;
                        ((Viewbox)Field(surface,"controlsViewport")).MaxWidth=308;
                        ((Grid)Field(surface,"coverHost")).Visibility=Visibility.Visible;
                        var view=(UserControl)Field(surface,"window");var viewport=(Viewbox)Field(surface,"noticeViewport");
                        var text=(TextBlock)Field(surface,"noticeTitle");var titleRun=(Run)Field(surface,"noticeSongRun");var artistRun=(Run)Field(surface,"noticeArtistRun");
                        var settings=new Dictionary<string,object>((Dictionary<string,object>)Field(null,"defaults"));settings["noticeTextSource"]="custom";settings["noticeTextAlignment"]="left";
                        foreach(string alignment in new[]{"left","center","right"})foreach(double size in new[]{10.0,19.0,24.0})foreach(string[] artists in new[]{new[]{"甲","乙"},new[]{"AC/DC","Earth, Wind & Fire","陈奕迅"},new[]{"歌手😀","最后一位歌手"}}){
                            Call(surface,"LayoutOrder",alignment);
                            Call(surface,"LayoutControls",308.0);
                            settings["noticeTitleFontSize"]=size;Set(surface,"noticeSong","很长的歌曲名称与滚动验证 ABC 2026");Set(surface,"noticePerformer",String.Join(" / ",artists));
                            var names=(List<string>)Field(surface,"noticeArtists");names.Clear();names.AddRange(artists);
                            Call(surface,"StyleNotice",settings,new Dictionary<string,object>());
                            viewport.Visibility=Visibility.Visible;
                            view.Width=360;view.Height=48;view.Measure(new Size(360,48));view.Arrange(new Rect(0,0,360,48));view.UpdateLayout();
                            Call(surface,"LayoutInteraction");view.UpdateLayout();
                            Check((int)Call(surface,"NoticeTarget",RangePoint(titleRun,0,titleRun.Text.Length))==-1,"歌名未正确命中");checks++;
                            int offset=0;
                            for(int i=0;i<artists.Length;i++){
                                Point local=RangePoint(artistRun,offset,artists[i].Length);
                                Check((int)Call(surface,"NoticeTarget",local)==i,"合作歌手命中错误："+artists[i]);checks++;
                                ((TranslateTransform)Field(surface,"noticeTitleShift")).X=Math.Min(0,((Grid)Field(surface,"noticeTitleViewport")).ActualWidth/2-local.X);
                                Point screen=text.PointToScreen(local),rootPoint=view.PointFromScreen(screen);
                                Check(view.InputHitTest(rootPoint)!=null,"文字区域没有 WPF 输入命中");checks++;
                                if((bool)Call(surface,"InNotice",screen)){
                                    long packed=(long)((uint)(ushort)(short)screen.X | ((uint)(ushort)(short)screen.Y<<16));
                                    Check((IntPtr)Call(surface,"Hook",(IntPtr)Field(surface,"handle"),0x84,IntPtr.Zero,new IntPtr(packed),false)==new IntPtr(1),"歌曲信息原生命中穿透到任务栏");checks++;
                                }
                                // 真实坐标先经过 Viewbox、缩放及滚动，再还原到文字坐标。
                                ((TranslateTransform)Field(surface,"noticeTitleShift")).X=-37;
                                Point transformed=text.PointFromScreen(text.PointToScreen(local));
                                Check((int)Call(surface,"NoticeTarget",transformed)==i,"滚动/缩放后的命中错误");checks++;
                                ((TranslateTransform)Field(surface,"noticeTitleShift")).X=0;
                                if(i<artists.Length-1){Check((int)Call(surface,"NoticeTarget",RangePoint(artistRun,offset+artists[i].Length,3))==-2,"分隔符误触发歌手跳转");checks++;}
                                offset+=artists[i].Length+3;
                            }
                            Check((int)Call(surface,"NoticeTarget",new Point(-100,-100))==-2,"文字外错误命中");checks++;
                            observations.Add(new{size=size,artists=artists,width=text.Width,height=viewport.ActualHeight});
                        }
                        if(args.Length>2){
                            Set(null,"stateEndpoint",new Uri(args[2]));Set(surface,"controlTrackId","歌曲 & id");
                            for(int index=-1;index<2;index++){
                                Call(surface,"NavigateInformation",index,"歌曲 & id",index<0?"":index==0?"歌手😀":"最后一位歌手");
                                for(int retry=0;retry<200 && (bool)Field(null,"mainWindowPending");retry++)await Task.Delay(5);
                                Check(!(bool)Field(null,"mainWindowPending"),"导航请求未结束");checks++;
                            }
                        }
                        Set(surface,"hoverInfoEnabled",false);Check((int)Call(surface,"NoticeTarget",RangePoint(titleRun,0,titleRun.Text.Length))==-2,"关闭信息后仍可点击");checks++;
                        Call(surface,"Close");
                    }
                    File.WriteAllText(report,new JavaScriptSerializer().Serialize(new{passed=true,checks=checks,observations=observations}));
                }catch(Exception error){File.WriteAllText(report+".error",error.ToString());code=1;}
                app.Shutdown(code);
            };timer.Start();return app.Run();
        }catch(Exception error){File.WriteAllText(report+".error",error.ToString());return 1;}
    }
}
