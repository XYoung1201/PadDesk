using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

namespace PadDesk {
    public static class Program {
        [STAThread] public static int Main(string[] args) {
            NativeOutput.EnableDpi();
            bool first;
            using(var mutex=new Mutex(true,"Local\\PadDesk.v2",out first)) {
                if(!first){try{using(var e=EventWaitHandle.OpenExisting("Local\\PadDesk.Show.v2"))e.Set();}catch{}return 0;}
                using(var signal=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\PadDesk.Show.v2")) {
                    try {var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};using(var dashboard=new Dashboard(app,signal))app.Run();}
                    catch(Exception ex){File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"error.log"),ex+Environment.NewLine);MessageBox.Show(ex.Message,"PadDesk 启动失败");return 1;}
                }
            }
            return 0;
        }
    }
    public sealed class Dashboard : IDisposable {
        readonly Application app;readonly Window window;readonly EventWaitHandle signal;
        readonly Device device=new Device();readonly NativeOutput output=new NativeOutput();
        InputEngine engine=new InputEngine();readonly Stopwatch clock=Stopwatch.StartNew();
        readonly DispatcherTimer timer=new DispatcherTimer(DispatcherPriority.Input),saveTimer=new DispatcherTimer();
        readonly System.Drawing.Icon appIcon;readonly Forms.NotifyIcon tray;readonly Forms.ToolStripMenuItem trayPause;
        HwndSource source;Profile profile;Snapshot current=new Snapshot();bool paused,busy,quitting,loading,disposed;
        int[] selectedKeys=new int[0];Action<Snapshot> capture;double nextStatus;
        readonly ControllerView controllerView;
        T Get<T>(string name) where T:class {return window.FindName(name) as T;}
        public Dashboard(Application app,EventWaitHandle signal) {
            this.app=app;this.signal=signal;
            using(var f=File.OpenRead(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Main.xaml")))window=(Window)XamlReader.Load(f);
            app.MainWindow=window;
            using(var iconStream=typeof(Dashboard).Assembly.GetManifestResourceStream("PadDesk.ico"))appIcon=new System.Drawing.Icon(iconStream);
            window.Icon=Imaging.CreateBitmapSourceFromHIcon(appIcon.Handle,Int32Rect.Empty,System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            controllerView=new ControllerView();Get<Border>("ControllerHost").Child=controllerView;
            controllerView.ControlClicked+=SelectModelControl;
            controllerView.InputFocused+=SelectModelControl;
            var menu=new Forms.ContextMenuStrip();menu.Items.Add("打开控制台",null,delegate{Show();});trayPause=new Forms.ToolStripMenuItem("暂停控制",null,delegate{Toggle();});menu.Items.Add(trayPause);menu.Items.Add("退出",null,delegate{Quit();});
            tray=new Forms.NotifyIcon {Icon=appIcon,Text="PadDesk · 手柄控制台",ContextMenuStrip=menu,Visible=true};tray.DoubleClick+=delegate{Show();};
            Get<Button>("DpadApplyButton").Click+=delegate{ApplyDpad();};
            Get<ComboBox>("ModelCombo").SelectionChanged+=delegate{if(loading)return;string layout=Get<ComboBox>("ModelCombo").SelectedIndex==1?"Photo":"Xbox";controllerView.SetLayout(layout);if(profile!=null){profile.VisualLayout=layout;Save();}};
            Get<Button>("PauseButton").Click+=delegate{Toggle();};Get<Button>("TrayButton").Click+=delegate{window.Hide();};
            Get<Button>("WizardButton").Click+=delegate{OpenCalibration(false);};Get<Button>("LocateButton").Click+=delegate{OpenCalibration(true);};
            Get<Button>("StickCalibrateButton").Click+=delegate{OpenStickCalibration();};
            Get<DataGrid>("MappingGrid").SelectionChanged+=delegate{Selection();};
            Get<ComboBox>("TargetCombo").SelectionChanged+=delegate{if(!loading){bool keyboard=Get<ComboBox>("TargetCombo").SelectedIndex==3;Get<Button>("KeyButton").IsEnabled=keyboard;Get<Button>("KeyButton").Visibility=keyboard?Visibility.Visible:Visibility.Collapsed;}};
            Get<Button>("KeyButton").Click+=delegate{OpenKeyPicker();};
            Get<Button>("ApplyButton").Click+=delegate{ApplyMapping();};Get<Button>("RenameButton").Click+=delegate{Rename();};
            Get<Slider>("SpeedSlider").ValueChanged+=delegate{TuneStick();};Get<Slider>("DeadzoneSlider").ValueChanged+=delegate{TuneStick();};Get<ComboBox>("CurveCombo").SelectionChanged+=delegate{TuneStick();};
            Get<CheckBox>("StickEnabled").Checked+=delegate{TuneStick();};Get<CheckBox>("StickEnabled").Unchecked+=delegate{TuneStick();};
            window.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!quitting){e.Cancel=true;window.Hide();tray.ShowBalloonTip(2000,"PadDesk 正在后台运行","双击托盘图标打开；右键选择退出。",Forms.ToolTipIcon.Info);}};
            window.SourceInitialized+=delegate{
                source=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);source.AddHook(Hotkey);
                if(!RegisterHotKey(source.Handle,1,0x4003,0x7B))Footer("暂停快捷键已被其他程序占用，请用控制台或托盘菜单暂停。");
            };
            app.SessionEnding+=delegate{output.ReleaseAll();};
            SystemEvents.SessionSwitch+=SessionSwitch;
            saveTimer.Interval=TimeSpan.FromMilliseconds(650);saveTimer.Tick+=delegate{saveTimer.Stop();Save();};
            timer.Interval=TimeSpan.FromMilliseconds(16);timer.Tick+=Tick;timer.Start();
            window.Show();
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h,int id,uint mods,uint vk);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h,int id);
        IntPtr Hotkey(IntPtr h,int msg,IntPtr w,IntPtr l,ref bool handled){if(msg==0x312){Toggle();handled=true;}return IntPtr.Zero;}
        void SessionSwitch(object sender,SessionSwitchEventArgs e){if(e.Reason==SessionSwitchReason.SessionLock)window.Dispatcher.BeginInvoke(new Action(delegate{paused=true;Suspend();UpdateState();}));}
        void Show(){window.Show();window.WindowState=WindowState.Normal;window.Activate();}
        void Footer(string text){Get<TextBlock>("FooterText").Text=text;}
        void Suspend(){output.ReleaseAll();engine=new InputEngine();}
        void Toggle(){paused=!paused;Suspend();UpdateState();}
        void UpdateState(){string state=!current.Connected?"等待手柄":busy?"校准 / 编辑中":paused?"已暂停":"正在控制";Get<TextBlock>("StateText").Text=state;Get<Button>("PauseButton").Content=paused?"恢复控制":"暂停控制";trayPause.Text=paused?"恢复控制":"暂停控制";trayPause.Checked=paused;tray.Text="PadDesk · "+state;}
        void Tick(object sender,EventArgs args) {
            try {
                double now=clock.Elapsed.TotalSeconds;current=device.Read(now);
                if(current.Connected && (profile==null || profile.DeviceId!=current.DeviceId))LoadProfile();
                bool editing=window.IsActive && (Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is ComboBox);
                bool enabled=!paused && !busy && !editing && current.Connected;
                if(!enabled)output.ReleaseAll();
                var result=engine.Tick(current,now,profile,enabled);output.Apply(result);
                if(profile!=null)foreach(var m in profile.Mappings)m.Active=m.Input.Active(current);
                if(capture!=null)capture(current);
                UpdateLive();UpdateState();
                if(signal.WaitOne(0))Show();
                if(now>=nextStatus){nextStatus=now+3;try{File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"status.txt"),DateTimeOffset.Now.ToString("o")+"\n"+Get<TextBlock>("StateText").Text+"\n"+current.Name+"\n"+current.DeviceId+"\nMappings="+(profile==null?0:profile.Mappings.Count));}catch{}}
            }catch(Exception ex){paused=true;Suspend();Footer("已暂停："+ex.Message);UpdateState();}
        }
        void LoadProfile() {
            if(saveTimer.IsEnabled){saveTimer.Stop();Save();}
            Suspend();loading=true;
            try{profile=Profile.Load(current.DeviceId);}catch(Exception ex){profile=null;paused=true;Footer("旧配置未载入："+ex.Message);}
            if(profile==null){profile=device.CreateProfile(current);MigrateDpad();}
            Get<DataGrid>("MappingGrid").ItemsSource=profile.Mappings;
            Get<Slider>("SpeedSlider").Value=profile.Speed;Get<Slider>("DeadzoneSlider").Value=profile.Deadzone*100;
            Get<ComboBox>("CurveCombo").SelectedIndex=profile.Curve<=1?0:profile.Curve>=2?2:1;Get<CheckBox>("StickEnabled").IsChecked=profile.StickEnabled;
            Get<TextBox>("HorizontalStepBox").Text=profile.HorizontalStep.ToString();Get<TextBox>("VerticalStepBox").Text=profile.VerticalStep.ToString();
            Get<TextBlock>("DpadError").Text="";UpdateDpadLabel();
            Get<ComboBox>("ModelCombo").SelectedIndex=profile.VisualLayout=="Photo"?1:0;controllerView.SetLayout(profile.VisualLayout);
            Get<TextBlock>("DeviceText").Text=current.Name+"  ·  "+current.Buttons.Length+" 按钮 / "+current.Axes.Length+" 轴";
            Get<DataGrid>("MappingGrid").SelectedIndex=0;loading=false;TuneLabels();Selection();controllerView.FocusControl(null);
        }
        [System.Runtime.Serialization.DataContract] class Legacy {
            [System.Runtime.Serialization.DataMember]public int HorizontalStep=30,VerticalStep=20,RepeatDelayMs=350,RepeatIntervalMs=90;
            [System.Runtime.Serialization.DataMember]public bool RepeatEnabled=true;
        }
        void MigrateDpad(){try{
            string path=System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","GamepadMouse","settings.json"));
            if(File.Exists(path))using(var f=File.OpenRead(path)){var old=(Legacy)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Legacy)).ReadObject(f);if(old.HorizontalStep>0 && old.VerticalStep>0 && old.RepeatDelayMs>=100 && old.RepeatIntervalMs>=30){profile.HorizontalStep=old.HorizontalStep;profile.VerticalStep=old.VerticalStep;profile.RepeatDelayMs=old.RepeatDelayMs;profile.RepeatIntervalMs=old.RepeatIntervalMs;profile.RepeatEnabled=old.RepeatEnabled;}}
        }catch{}}
        void Save(){if(profile==null)return;try{profile.Save();Footer("已保存到本机 · Ctrl+Alt+F12 暂停 / 恢复");}catch(Exception ex){paused=true;Suspend();Footer("保存失败，已暂停："+ex.Message);}}
        void UpdateDpadLabel(){Get<TextBlock>("DpadText").Text="左右 "+profile.HorizontalStep+" px · 上下 "+profile.VerticalStep+" px"+(profile.RepeatEnabled?"\n长按 "+profile.RepeatDelayMs+" ms 后，每 "+profile.RepeatIntervalMs+" ms 一步":"\n长按重复已关闭");}
        void ApplyDpad(){
            if(profile==null || !current.Connected){Get<TextBlock>("DpadError").Text="请先连接手柄。";return;}
            if(!profile.SetDpadSteps(Get<TextBox>("HorizontalStepBox").Text,Get<TextBox>("VerticalStepBox").Text)){Get<TextBlock>("DpadError").Text="请输入 1–5000 之间的整数（px）。";return;}
            Suspend();Get<TextBlock>("DpadError").Text="";UpdateDpadLabel();Save();
        }
        void TuneLabels(){if(profile==null)return;Get<TextBlock>("SpeedText").Text=((int)profile.Speed)+" px/s";Get<TextBlock>("DeadzoneText").Text=((int)(profile.Deadzone*100))+"%";}
        void TuneStick(){if(loading || profile==null)return;profile.Speed=Get<Slider>("SpeedSlider").Value;profile.Deadzone=Get<Slider>("DeadzoneSlider").Value/100;profile.Curve=new[]{1.0,1.6,2.2}[Math.Max(0,Get<ComboBox>("CurveCombo").SelectedIndex)];profile.StickEnabled=Get<CheckBox>("StickEnabled").IsChecked==true;TuneLabels();saveTimer.Stop();saveTimer.Start();}
        Mapping Selected {get{return Get<DataGrid>("MappingGrid").SelectedItem as Mapping;}}
        void SelectModelControl(string key){
            Get<TabControl>("InspectorTabs").SelectedIndex=0;
            if(profile==null)return;
            var row=profile.Mappings.FirstOrDefault(m=>ControllerLayout.KeyFor(m)==key);
            if(row!=null){Get<DataGrid>("MappingGrid").SelectedItem=row;Get<DataGrid>("MappingGrid").ScrollIntoView(row);}
            else Get<TextBlock>("GuideText").Text=ControllerLayout.Caption(key)+" 尚未对准，请用“全按键校准”确认实体键。";
        }
        void Selection(){var m=Selected;bool wasLoading=loading;loading=true;
            controllerView.FocusControl(ControllerLayout.KeyFor(m));
            Get<TextBlock>("SelectedText").Text=m==null?"选择一个按键":m.Physical+"  ·  "+m.Raw;
            bool editable=m!=null && !m.Locked && !m.Reserved;Get<ComboBox>("TargetCombo").IsEnabled=editable;Get<Button>("ApplyButton").IsEnabled=editable;Get<Button>("RenameButton").IsEnabled=editable;Get<TextBox>("PhysicalNameBox").IsEnabled=editable;
            Get<TextBlock>("LockHint").Text=m!=null && (m.Locked || m.Reserved)?"方向键输出锁定":"";
            Get<TextBox>("PhysicalNameBox").Text=m==null?"":m.Name;
            Get<ComboBox>("TargetCombo").SelectedIndex=m==null?0:m.Target=="MouseLeft"?1:m.Target=="MouseRight"?2:m.Target=="Keyboard"?3:0;
            selectedKeys=m==null?new int[0]:(int[])m.Keys.Clone();Get<Button>("KeyButton").Content=selectedKeys.Length==0?"选择键盘键 / 组合键":KeyNames.Chord(selectedKeys);Get<Button>("KeyButton").IsEnabled=editable && m.Target=="Keyboard";Get<Button>("KeyButton").Visibility=editable && m.Target=="Keyboard"?Visibility.Visible:Visibility.Collapsed;loading=wasLoading;
        }
        void ApplyMapping(){var m=Selected;if(m==null || m.Locked || m.Reserved)return;int index=Get<ComboBox>("TargetCombo").SelectedIndex;
            if(index==3 && selectedKeys.Length==0){OpenKeyPicker();if(selectedKeys.Length==0)return;}
            Suspend();m.Target=new[]{"None","MouseLeft","MouseRight","Keyboard"}[Math.Max(0,index)];m.Keys=index==3?(int[])selectedKeys.Clone():new int[0];m.Notify();Save();Get<TextBlock>("GuideText").Text=m.Physical+" → "+m.OutputLabel+"。按住保持按下，松开立即弹起。";
        }
        void Rename(){var m=Selected;if(m==null || m.Locked || m.Reserved)return;string name=Get<TextBox>("PhysicalNameBox").Text.Trim();if(name.Length==0){Footer("请输入实体按键名称，例如 A、L1 或自定义名称。");return;}if(profile.Mappings.Any(r=>r!=m && r.Name==name && r.Confirmed)){Footer("此名称已被另一个按键使用，请使用全按键校准重新对准。");return;}m.Name=name;m.Confirmed=true;m.Notify();Save();Selection();}
        void UpdateLive(){
            controllerView.FollowInput=!busy && !(Keyboard.FocusedElement is TextBox) && !Get<ComboBox>("TargetCombo").IsDropDownOpen;
            controllerView.UpdateInput(current,profile);
            if(profile==null)return;
            string[] names=profile.Mappings.Where(m=>m.Active).Select(m=>(m.Confirmed?m.Physical:m.Raw)).ToArray();
            Get<TextBlock>("RawText").Text=names.Length==0?"松开 · 等待输入":string.Join("  /  ",names);
            Get<TextBlock>("LiveHint").Text=names.Length==0?"LIVE":"● 输入中";
            Get<TextBlock>("MapCountText").Text=profile.Mappings.Count+" 个输入  ·  "+profile.Mappings.Count(m=>m.Confirmed && !m.Locked)+" 个已对准";
        }
        void OpenKeyPicker(){if(Selected==null || Selected.Locked || Selected.Reserved)return;busy=true;Suspend();var dialog=new KeyDialog(window,selectedKeys);try{if(dialog.ShowDialog()==true){selectedKeys=dialog.Keys;Get<Button>("KeyButton").Content=KeyNames.Chord(selectedKeys);}}finally{busy=false;Suspend();}}
        void RecordNeutral(Snapshot neutral){
            if(profile.StickX<neutral.Axes.Length)profile.CenterX=neutral.Axes[profile.StickX];if(profile.StickY<neutral.Axes.Length)profile.CenterY=neutral.Axes[profile.StickY];profile.NeutralConfirmed=true;
            foreach(var m in profile.Mappings.Where(m=>m.Input.Kind=="Axis" && m.Input.Index<neutral.Axes.Length))m.Input.Rest=neutral.Axes[m.Input.Index];
            for(int i=0;i<neutral.Axes.Length;i++){if(i==profile.StickX || i==profile.StickY)continue;foreach(int sign in new[]{1,-1})if((sign>0?32767-neutral.Axes[i]:neutral.Axes[i]+32768)>14000){var input=new Source {Kind="Axis",Index=i,Direction=sign,Rest=neutral.Axes[i]};if(!profile.Mappings.Any(m=>m.Input.Id==input.Id))profile.Mappings.Add(new Mapping {Input=input});}}
        }
        void OpenCalibration(bool locate){if(profile==null || !current.Connected){Footer("请先连接手柄。");return;}busy=true;Suspend();var dialog=new CalibrationDialog(window,profile,current,locate,delegate(Source input,string name){return AssignPhysical(input,name,locate);},RecordNeutral);capture=dialog.Poll;try{dialog.ShowDialog();}finally{capture=null;busy=false;Suspend();Get<DataGrid>("MappingGrid").Items.Refresh();Save();Selection();}}
        string AssignPhysical(Source input,string label,bool locate){
            var row=profile.Mappings.FirstOrDefault(m=>m.Input.Id==input.Id);
            if(row!=null && (row.Locked || row.Reserved)){if(locate){Get<DataGrid>("MappingGrid").SelectedItem=row;Get<DataGrid>("MappingGrid").ScrollIntoView(row);return null;}return "这是固定方向键，请按提示中的其他按键。";}
            if(row==null){row=new Mapping {Input=input};profile.Mappings.Add(row);}else if(!locate)row.Input=input;
            if(!locate){var old=profile.Mappings.FirstOrDefault(m=>m!=row && m.Name==label && !m.Locked && !m.Reserved);
                if(old!=null){string oldName=row.Name;bool confirmed=row.Confirmed;string target=row.Target;int[] keys=row.Keys;row.Target=old.Target;row.Keys=old.Keys;old.Name=oldName;old.Confirmed=confirmed;old.Target=target;old.Keys=keys;old.Notify();}
                row.Name=label;row.Confirmed=true;
                if(label=="A" && row.Target=="None")row.Target="MouseLeft";row.Notify();
            }
            Get<DataGrid>("MappingGrid").SelectedItem=row;Get<DataGrid>("MappingGrid").ScrollIntoView(row);return null;
        }
        void OpenStickCalibration(){if(profile==null || !current.Connected)return;busy=true;Suspend();var dialog=new StickDialog(window,profile,current);capture=dialog.Poll;try{if(dialog.ShowDialog()==true){profile.StickX=dialog.X;profile.StickY=dialog.Y;profile.CenterX=dialog.CenterX;profile.CenterY=dialog.CenterY;profile.SignX=dialog.SignX;profile.SignY=dialog.SignY;profile.StickCalibrated=true;profile.NeutralConfirmed=true;
                    foreach(var m in profile.Mappings.Where(m=>!m.Locked && m.Input.Kind=="Axis" && (m.Input.Index==profile.StickX || m.Input.Index==profile.StickY))){m.Target="None";m.Keys=new int[0];m.Notify();}Save();}}
            finally{capture=null;busy=false;Suspend();}}
        void Quit(){if(quitting)return;quitting=true;bool pending=saveTimer.IsEnabled;saveTimer.Stop();if(pending)Save();output.ReleaseAll();window.Close();app.Shutdown();}
        public void Dispose(){if(disposed)return;disposed=true;timer.Stop();saveTimer.Stop();SystemEvents.SessionSwitch-=SessionSwitch;if(source!=null){UnregisterHotKey(source.Handle,1);source.RemoveHook(Hotkey);}output.Dispose();device.Dispose();tray.Visible=false;tray.Dispose();appIcon.Dispose();}
    }
}
