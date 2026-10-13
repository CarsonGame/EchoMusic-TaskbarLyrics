using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

// 用自己的隐藏父窗口模拟任务栏更换、销毁，不操作 Explorer。
class HostLifecycleHarness {
    static Type type;static BindingFlags flags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static object Field(object target,string name){return (target==null?type:target.GetType()).GetField(name,flags).GetValue(target);}
    static object Call(object target,string name,params object[] args){return type.GetMethod(name,flags).Invoke(target,args);}
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static HwndSource Parent(){return new HwndSource(new HwndSourceParameters("EchoMusic 生命周期测试父窗口") {WindowStyle=unchecked((int)0x86000000),ExtendedWindowStyle=0x8000080,PositionX=-3000,PositionY=-3000,Width=540,Height=72,UsesPerPixelTransparency=true});}
    static object Display(IntPtr parent){var displays=(IEnumerable)Call(null,"Screens");foreach(object display in displays){display.GetType().GetField("Taskbar",flags).SetValue(display,parent);return display;}throw new Exception("没有屏幕");}
    static object Surface(object display){var surface=Activator.CreateInstance(type,flags,null,new[]{display},null);Call(surface,"Open");((DispatcherTimer)Field(surface,"pointerTimer")).Stop();return surface;}
    [STAThread] static int Main(string[] args){
        string report=args[1];
        try{
            AppContext.SetSwitch("Switch.System.Windows.DoNotScaleForDpiChanges",false);
            type=Assembly.LoadFrom(args[0]).GetType("TaskbarLyrics");
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};
            timer.Tick+=async(sender,e)=>{
                timer.Stop();int code=0;
                try{
                    int checks=0;
                    for(int i=0;i<5;i++){
                        using(var first=Parent())using(var replacement=Parent()){
                            var original=Display(first.Handle);var surface=Surface(original);
                            Check((bool)Field(surface,"embedded"),"未创建子窗口");checks++;
                            Check(!(bool)Call(surface,"NeedsRebuild",original),"相同任务栏错误重建");checks++;
                            var changed=Display(replacement.Handle);Check((bool)Call(surface,"NeedsRebuild",changed),"父任务栏变更未触发重建");checks++;
                            first.Dispose();await Task.Delay(50);
                            Check((bool)Call(surface,"NeedsRebuild",original),"父窗口销毁未触发重建");checks++;
                            Call(surface,"RenderScroll",null,EventArgs.Empty);Call(surface,"UpdateHover");Call(surface,"Close");Call(surface,"Close");checks++;
                            var rebuilt=Surface(changed);Check(!(bool)Call(rebuilt,"NeedsRebuild",changed),"替换任务栏无法重建");Call(rebuilt,"Close");checks++;
                        }
                    }
                    var missing=Display(IntPtr.Zero);var floating=Surface(missing);
                    Check(!(bool)Field(floating,"embedded"),"任务栏不存在时未回退浮窗");checks++;
                    using(var appeared=Parent()){Check((bool)Call(floating,"NeedsRebuild",Display(appeared.Handle)),"任务栏恢复后未重试子窗口");checks++;}
                    Call(floating,"Close");
                    var instances=Field(null,"instances");Check((int)instances.GetType().GetProperty("Count").GetValue(instances,null)==0,"关闭后保留窗口实例");checks++;
                    File.WriteAllText(report,new JavaScriptSerializer().Serialize(new{checks=checks,passed=true}));
                }catch(Exception error){File.WriteAllText(report+".error",error.ToString());code=1;}
                app.Shutdown(code);
            };timer.Start();return app.Run();
        }catch(Exception error){File.WriteAllText(report+".error",error.ToString());return 1;}
    }
}
