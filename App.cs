using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        // Remotes and Bluetooth/IR HID devices: a separate shared profile, engine and output so they work with or without a gamepad.
        readonly RemoteInput remote=new RemoteInput();readonly NativeOutput remoteOutput=new NativeOutput();InputEngine remoteEngine=new InputEngine();
        Profile remoteProfile;bool remoteView;int[][] selectedSteps=new int[0][];string selectedSequence="",selectedMode="Hog";
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
            controllerView.InputFocused+=delegate(string key){if(!remoteView)SelectModelControl(key);};
            try{remoteProfile=Profile.Load(RemoteProfileId);}catch(Exception ex){remoteProfile=null;Footer("遥控配置未载入："+ex.Message);}
            if(remoteProfile==null)remoteProfile=new Profile {DeviceId=RemoteProfileId,DeviceName="遥控 / 蓝牙外设"};
            foreach(int vk in KeyNames.Media)Get<ComboBox>("MediaCombo").Items.Add(new ComboBoxItem {Content=KeyNames.Name(vk),Tag=vk});
            Get<Selector>("SourceTabs").SelectionChanged+=delegate{if(loading)return;remoteView=Get<Selector>("SourceTabs").SelectedIndex==1;ShowList();};
            Get<Button>("LearnButton").Click+=delegate{LearnRemote();};Get<Button>("RemoveButton").Click+=delegate{RemoveRemote();};
            Get<Button>("SequenceButton").Click+=delegate{if(OpenSequenceEditor())Commit(5);};
            var menu=new Forms.ContextMenuStrip();menu.Items.Add("打开控制台",null,delegate{Show();});trayPause=new Forms.ToolStripMenuItem("暂停控制",null,delegate{Toggle();});menu.Items.Add(trayPause);menu.Items.Add("退出",null,delegate{Quit();});
            tray=new Forms.NotifyIcon {Icon=appIcon,Text="PadDesk · 手柄控制台",ContextMenuStrip=menu,Visible=true};tray.DoubleClick+=delegate{Show();};
            Get<Button>("DpadApplyButton").Click+=delegate{ApplyDpad();};
            foreach(var box in new[]{"HorizontalStepBox","VerticalStepBox","RepeatDelayBox","RepeatIntervalBox"})Get<TextBox>(box).KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Enter){ApplyDpad();e.Handled=true;}};
            Get<CheckBox>("RepeatEnabled").Checked+=delegate{ToggleRepeat();};Get<CheckBox>("RepeatEnabled").Unchecked+=delegate{ToggleRepeat();};
            Get<Selector>("ModelCombo").SelectionChanged+=delegate{if(loading)return;string layout=Get<Selector>("ModelCombo").SelectedIndex==1?"Photo":"Xbox";controllerView.SetLayout(layout);if(profile!=null){profile.VisualLayout=layout;Save();}};
            Get<Button>("PauseButton").Click+=delegate{Toggle();};
            Get<Button>("MinButton").Click+=delegate{window.WindowState=WindowState.Minimized;};
            Get<Button>("MaxButton").Click+=delegate{window.WindowState=window.WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;};
            Get<Button>("CloseButton").Click+=delegate{window.Close();};
            window.StateChanged+=delegate{bool max=window.WindowState==WindowState.Maximized;var edge=SystemParameters.WindowResizeBorderThickness;Get<Grid>("RootGrid").Margin=max?new Thickness(edge.Left+4,edge.Top+4,edge.Right+4,edge.Bottom+4):new Thickness(0);Get<Button>("MaxButton").Content=max?"\uE923":"\uE922";Get<Button>("MaxButton").ToolTip=max?"还原":"最大化";};
            Get<Button>("WizardButton").Click+=delegate{OpenCalibration(false);};Get<Button>("LocateButton").Click+=delegate{OpenCalibration(true);};
            Get<Button>("StickCalibrateButton").Click+=delegate{OpenStickCalibration();};
            Get<DataGrid>("MappingGrid").SelectionChanged+=delegate{Selection();};
            // Output choices take effect as soon as they are complete; there is no separate apply step.
            Get<Selector>("TargetCombo").SelectionChanged+=delegate{if(!loading)TargetChosen(Get<Selector>("TargetCombo").SelectedIndex);};
            Get<ComboBox>("MediaCombo").SelectionChanged+=delegate{if(!loading && Get<Selector>("TargetCombo").SelectedIndex==4 && Get<ComboBox>("MediaCombo").SelectedItem!=null)Commit(4);};
            Get<Button>("KeyButton").Click+=delegate{if(OpenKeyPicker())Commit(3);};
            Get<Button>("RenameToggle").Click+=delegate{ShowRename(Get<Grid>("RenamePanel").Visibility!=Visibility.Visible);};
            Get<Button>("RenameButton").Click+=delegate{Rename();};
            Get<TextBox>("PhysicalNameBox").KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Enter){Rename();e.Handled=true;}else if(e.Key==Key.Escape){ShowRename(false);e.Handled=true;}};
            Get<Slider>("SpeedSlider").ValueChanged+=delegate{TuneStick();};Get<Slider>("DeadzoneSlider").ValueChanged+=delegate{TuneStick();};Get<Selector>("CurveCombo").SelectionChanged+=delegate{TuneStick();};
            Get<CheckBox>("StickEnabled").Checked+=delegate{TuneStick();};Get<CheckBox>("StickEnabled").Unchecked+=delegate{TuneStick();};
            window.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!quitting){e.Cancel=true;window.Hide();tray.ShowBalloonTip(2000,"PadDesk 正在后台运行","双击托盘图标打开；右键选择退出。",Forms.ToolTipIcon.Info);}};
            window.SourceInitialized+=delegate{
                source=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);source.AddHook(Hotkey);Theme.Apply(window);
                if(!RegisterHotKey(source.Handle,1,0x4003,0x7B))Footer("暂停快捷键已被其他程序占用，请用控制台或托盘菜单暂停。");
                if(!remote.Register(source.Handle))Footer("无法监听遥控 / 蓝牙外设按键，其余功能不受影响。");
            };
            app.SessionEnding+=delegate{output.ReleaseAll();remoteOutput.ReleaseAll();};
            SystemEvents.SessionSwitch+=SessionSwitch;
            saveTimer.Interval=TimeSpan.FromMilliseconds(650);saveTimer.Tick+=delegate{saveTimer.Stop();Save();};
            timer.Interval=TimeSpan.FromMilliseconds(16);timer.Tick+=Tick;timer.Start();
            DrawCurve(.16,1.6);window.Show();
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h,int id,uint mods,uint vk);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h,int id);
        IntPtr Hotkey(IntPtr h,int msg,IntPtr w,IntPtr l,ref bool handled){if(msg==0x312){Toggle();handled=true;}else remote.Handle(msg,w,l);return IntPtr.Zero;}
        const string RemoteProfileId="remotes";
        Profile Editing {get{return remoteView?remoteProfile:profile;}}
        void SessionSwitch(object sender,SessionSwitchEventArgs e){if(e.Reason==SessionSwitchReason.SessionLock)window.Dispatcher.BeginInvoke(new Action(delegate{paused=true;Suspend();UpdateState();}));}
        void Show(){window.Show();window.WindowState=WindowState.Normal;window.Activate();}
        void Footer(string text){Get<TextBlock>("FooterText").Text=text;}
        void Suspend(){output.ReleaseAll();engine=new InputEngine();remoteOutput.ReleaseAll();remoteEngine=new InputEngine();}
        void Toggle(){paused=!paused;Suspend();UpdateState();}
        string shownState;
        bool RemoteReady {get{return remoteProfile!=null && remoteProfile.Mappings.Any(m=>m.Target!="None");}}
        void UpdateState(){string state=busy?"校准 / 编辑中":paused?"已暂停":!current.Connected?(RemoteReady?"遥控待命":"等待手柄"):"正在控制";Get<TextBlock>("StateText").Text=state;
            if(shownState!=state){shownState=state;bool live=state=="正在控制" || state=="遥控待命";string fg=live?"#86C5B3":state=="已暂停"?"#E5664A":state=="等待手柄"?"#9C968C":"#D9A55A";string bg=live?"#1A2A27":state=="已暂停"?"#2E1C17":state=="等待手柄"?"#1E2025":"#2B2418";
                Get<TextBlock>("StateText").Foreground=Theme.Brush(fg);Get<Ellipse>("StateDot").Fill=Theme.Brush(fg);Get<Border>("StateBadge").Background=Theme.Brush(bg);}
            Get<Button>("PauseButton").Content=paused?"恢复控制":"暂停控制";trayPause.Text=paused?"恢复控制":"暂停控制";trayPause.Checked=paused;tray.Text="PadDesk · "+state;}
        void Tick(object sender,EventArgs args) {
            try {
                double now=clock.Elapsed.TotalSeconds;current=device.Read(now);
                if(current.Connected && (profile==null || profile.DeviceId!=current.DeviceId))LoadProfile();
                bool editing=window.IsActive && (Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is ComboBox);
                bool enabled=!paused && !busy && !editing && current.Connected;
                if(!enabled)output.ReleaseAll();
                // A tap shorter than one poll is replayed as its own press and release before the regular frame.
                if(enabled && device.Partial!=null)output.Apply(engine.Tick(device.Partial,now,profile,true,true));
                var result=engine.Tick(current,now,profile,enabled);output.Apply(result);
                if(profile!=null)foreach(var m in profile.Mappings)m.Active=m.Input.Active(current);
                HashSet<string> remoteTaps;var remoteKeys=remote.Read(out remoteTaps);
                var remoteState=new Snapshot {Connected=true,DeviceId=RemoteProfileId,Name=remoteProfile.DeviceName,Remote=remoteKeys};
                bool remoteEnabled=!paused && !busy && !editing;if(!remoteEnabled)remoteOutput.ReleaseAll();
                if(remoteEnabled && remoteTaps!=null){var tap=remoteState.Clone();tap.Remote=remoteTaps;remoteOutput.Apply(remoteEngine.Tick(tap,now,remoteProfile,true,true));}
                remoteOutput.Apply(remoteEngine.Tick(remoteState,now,remoteProfile,remoteEnabled));
                foreach(var m in remoteProfile.Mappings)m.Active=m.Input.Active(remoteState);
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
            if(!remoteView)Get<DataGrid>("MappingGrid").ItemsSource=profile.Mappings;
            Get<Slider>("SpeedSlider").Value=profile.Speed;Get<Slider>("DeadzoneSlider").Value=profile.Deadzone*100;
            Get<Selector>("CurveCombo").SelectedIndex=profile.Curve<=1?0:profile.Curve>=2?2:1;Get<CheckBox>("StickEnabled").IsChecked=profile.StickEnabled;
            Get<TextBox>("HorizontalStepBox").Text=profile.HorizontalStep.ToString();Get<TextBox>("VerticalStepBox").Text=profile.VerticalStep.ToString();
            Get<TextBox>("RepeatDelayBox").Text=profile.RepeatDelayMs.ToString();Get<TextBox>("RepeatIntervalBox").Text=profile.RepeatIntervalMs.ToString();
            Get<CheckBox>("RepeatEnabled").IsChecked=profile.RepeatEnabled;Get<Grid>("RepeatFields").IsEnabled=profile.RepeatEnabled;
            Get<TextBlock>("DpadError").Text="";UpdateDpadLabel();
            Get<Selector>("ModelCombo").SelectedIndex=profile.VisualLayout=="Photo"?1:0;controllerView.SetLayout(profile.VisualLayout);
            Get<TextBlock>("DeviceText").Text=current.Name+"   ·   "+current.Buttons.Length+" 键  "+current.Axes.Length+" 轴";
            if(!remoteView)Get<DataGrid>("MappingGrid").SelectedIndex=0;loading=false;TuneLabels();Selection();controllerView.FocusControl(null);
        }
        [System.Runtime.Serialization.DataContract] class Legacy {
            [System.Runtime.Serialization.DataMember]public int HorizontalStep=30,VerticalStep=20,RepeatDelayMs=350,RepeatIntervalMs=90;
            [System.Runtime.Serialization.DataMember]public bool RepeatEnabled=true;
        }
        void MigrateDpad(){try{
            string path=System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","GamepadMouse","settings.json"));
            if(File.Exists(path))using(var f=File.OpenRead(path)){var old=(Legacy)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Legacy)).ReadObject(f);if(old.HorizontalStep>0 && old.VerticalStep>0 && old.RepeatDelayMs>=100 && old.RepeatIntervalMs>=30){profile.HorizontalStep=old.HorizontalStep;profile.VerticalStep=old.VerticalStep;profile.RepeatDelayMs=old.RepeatDelayMs;profile.RepeatIntervalMs=old.RepeatIntervalMs;profile.RepeatEnabled=old.RepeatEnabled;}}
        }catch{}}
        void Save(){try{if(remoteProfile.Mappings.Count>0 || File.Exists(remoteProfile.FilePath))remoteProfile.Save();if(profile==null)return;profile.Save();Footer("已保存到本机  ·  "+DateTime.Now.ToString("HH:mm:ss"));}catch(Exception ex){paused=true;Suspend();Footer("保存失败，已暂停："+ex.Message);}}
        void UpdateDpadLabel(){Get<TextBlock>("DpadText").Text="当前 左右 "+profile.HorizontalStep+" · 上下 "+profile.VerticalStep+" px"+(profile.RepeatEnabled?"\n长按 "+profile.RepeatDelayMs+" ms 后每 "+profile.RepeatIntervalMs+" ms 一步":"\n长按重复已关闭");}
        void ApplyDpad(){
            if(profile==null || !current.Connected){Get<TextBlock>("DpadError").Text="请先连接手柄。";return;}
            // Validate both groups before changing anything, so a bad repeat value cannot leave new steps half-applied.
            if(!new Profile().SetDpadSteps(Get<TextBox>("HorizontalStepBox").Text,Get<TextBox>("VerticalStepBox").Text)){Get<TextBlock>("DpadError").Text="步长请输入 1–5000 之间的整数（px）。";return;}
            if(!new Profile().SetRepeat(Get<TextBox>("RepeatDelayBox").Text,Get<TextBox>("RepeatIntervalBox").Text)){Get<TextBlock>("DpadError").Text="首次延迟请输入 100–5000，重复间隔请输入 30–5000 之间的整数（ms）。";return;}
            profile.SetDpadSteps(Get<TextBox>("HorizontalStepBox").Text,Get<TextBox>("VerticalStepBox").Text);profile.SetRepeat(Get<TextBox>("RepeatDelayBox").Text,Get<TextBox>("RepeatIntervalBox").Text);
            Suspend();Get<TextBlock>("DpadError").Text="";UpdateDpadLabel();Save();
        }
        void ToggleRepeat(){bool on=Get<CheckBox>("RepeatEnabled").IsChecked==true;Get<Grid>("RepeatFields").IsEnabled=on;if(loading || profile==null)return;profile.RepeatEnabled=on;Suspend();UpdateDpadLabel();Save();}
        void TuneLabels(){if(profile==null)return;Get<TextBlock>("SpeedText").Text=((int)profile.Speed).ToString();Get<TextBlock>("DeadzoneText").Text=((int)Math.Round(profile.Deadzone*100)).ToString();DrawCurve(profile.Deadzone,profile.Curve);}
        void DrawCurve(double dz,double exp){
            // Response preview: dead zone flat, then the profile curve, in a 72 x 72 box.
            var fig=new PathFigure {StartPoint=new Point(0,72)};var seg=new PolyLineSegment();
            for(int i=1;i<=48;i++){double x=i/48.0,y=x<=dz?0:Math.Pow((x-dz)/(1-dz),exp);seg.Points.Add(new Point(x*72,72-y*72));}
            fig.Segments.Add(seg);var geo=new PathGeometry();geo.Figures.Add(fig);geo.Freeze();Get<System.Windows.Shapes.Path>("CurvePath").Data=geo;
        }
        void TuneStick(){if(loading || profile==null)return;profile.Speed=Get<Slider>("SpeedSlider").Value;profile.Deadzone=Get<Slider>("DeadzoneSlider").Value/100;profile.Curve=new[]{1.0,1.6,2.2}[Math.Max(0,Get<Selector>("CurveCombo").SelectedIndex)];profile.StickEnabled=Get<CheckBox>("StickEnabled").IsChecked==true;TuneLabels();saveTimer.Stop();saveTimer.Start();}
        Mapping Selected {get{return Get<DataGrid>("MappingGrid").SelectedItem as Mapping;}}
        void SelectModelControl(string key){
            Get<TabControl>("InspectorTabs").SelectedIndex=0;
            if(profile==null)return;
            if(remoteView)Get<Selector>("SourceTabs").SelectedIndex=0;
            var row=profile.Mappings.FirstOrDefault(m=>ControllerLayout.KeyFor(m)==key);
            if(row!=null){Get<DataGrid>("MappingGrid").SelectedItem=row;Get<DataGrid>("MappingGrid").ScrollIntoView(row);}
            else Get<TextBlock>("GuideText").Text=ControllerLayout.Caption(key)+" 尚未对准，请用“全按键校准”确认实体键。";
        }
        void Selection(){var m=Selected;bool wasLoading=loading;loading=true;
            controllerView.FocusControl(ControllerLayout.KeyFor(m));
            Get<TextBlock>("SelectedText").Text=m==null?"选择一个按键":m.Physical;
            Get<TextBlock>("SelectedRaw").Text=m==null?"点选模型、列表，或直接按下实体键":m.Raw+"  ·  "+m.Verification;
            Get<TextBlock>("SelectedGlyph").Text=Glyph(m);
            bool editable=m!=null && !m.Locked && !m.Reserved;Get<Selector>("TargetCombo").IsEnabled=editable;Get<Button>("RenameToggle").IsEnabled=editable;Get<Button>("RenameButton").IsEnabled=editable;Get<TextBox>("PhysicalNameBox").IsEnabled=editable;ShowRename(false);
            Get<TextBlock>("LockHint").Text=m!=null && (m.Locked || m.Reserved)?"固定输出":"";
            Get<TextBox>("PhysicalNameBox").Text=m==null?"":m.Name;
            int index=m==null?0:Math.Max(0,Array.IndexOf(Targets,m.Target));Get<Selector>("TargetCombo").SelectedIndex=index;
            selectedKeys=m==null || m.Target!="Keyboard"?new int[0]:(int[])m.Keys.Clone();Get<Button>("KeyButton").Content=selectedKeys.Length==0?"录入键盘键 / 组合键":KeyNames.Chord(selectedKeys);
            Get<ComboBox>("MediaCombo").SelectedItem=m==null || m.Target!="Media" || m.Keys.Length!=1?null:Get<ComboBox>("MediaCombo").Items.Cast<ComboBoxItem>().FirstOrDefault(c=>(int)c.Tag==m.Keys[0]);
            selectedSteps=m==null || m.Target!="Sequence"?new int[0][]:m.Steps.Select(st=>(int[])st.Clone()).ToArray();selectedSequence=m==null?"":m.SequenceText;selectedMode=m==null?"Hog":m.SequenceMode;SequenceLabel();
            TargetExtras(index,editable);Get<Button>("RemoveButton").IsEnabled=remoteView && m!=null;loading=wasLoading;
        }
        static readonly string[] Targets={"None","MouseLeft","MouseRight","Keyboard","Media","Sequence"};
        void TargetExtras(int index,bool editable){
            Get<Button>("KeyButton").IsEnabled=editable && index==3;Get<Button>("KeyButton").Visibility=editable && index==3?Visibility.Visible:Visibility.Collapsed;
            Get<ComboBox>("MediaCombo").IsEnabled=editable && index==4;Get<ComboBox>("MediaCombo").Visibility=editable && index==4?Visibility.Visible:Visibility.Collapsed;
            Get<Button>("SequenceButton").IsEnabled=editable && index==5;Get<Button>("SequenceButton").Visibility=editable && index==5?Visibility.Visible:Visibility.Collapsed;
        }
        void SequenceLabel(){Get<Button>("SequenceButton").Content=selectedSteps.Length==0?"设置按键序列 / HOG 热键":(selectedMode=="Hog"?"HOG 热键  ":"序列  ")+(selectedSequence.Length>0?selectedSequence:Sequences.Describe(selectedSteps));}
        bool OpenSequenceEditor(){
            var m=Selected;if(m==null || m.Locked || m.Reserved)return false;busy=true;Suspend();
            var dialog=new SequenceDialog(window,selectedMode,selectedSequence);
            try{if(dialog.ShowDialog()==true && dialog.Steps.Length>0){selectedSteps=dialog.Steps;selectedSequence=dialog.Text;selectedMode=dialog.Mode;SequenceLabel();return true;}return false;}finally{busy=false;Suspend();}
        }
        void ShowList(){
            var grid=Get<DataGrid>("MappingGrid");var source=Editing;grid.ItemsSource=null;grid.ItemsSource=source==null?null:source.Mappings;if(source!=null && source.Mappings.Count>0)grid.SelectedIndex=0;
            Get<Button>("LearnButton").Visibility=remoteView?Visibility.Visible:Visibility.Collapsed;Get<Button>("RemoveButton").Visibility=remoteView?Visibility.Visible:Visibility.Collapsed;
            if(remoteView && remoteProfile.Mappings.Count==0)Get<TextBlock>("GuideText").Text="点击“学习按键”，再按遥控器、蓝牙遥控器或翻页笔上的键，把它加入列表后设置输出。";
            Selection();
        }
        void LearnRemote(){
            busy=true;Suspend();var dialog=new RemoteLearnDialog(window,remote);Source learned=null;
            try{if(dialog.ShowDialog()==true)learned=dialog.Learned;}finally{busy=false;Suspend();}
            if(learned==null)return;
            var row=remoteProfile.Mappings.FirstOrDefault(r=>r.Input.Id==learned.Id);
            if(row==null){row=new Mapping {Input=learned,Name=RemoteKeys.Caption(learned.Mask,learned.Index),Confirmed=true};remoteProfile.Mappings.Add(row);}
            remoteView=true;loading=true;Get<Selector>("SourceTabs").SelectedIndex=1;loading=false;ShowList();
            Get<DataGrid>("MappingGrid").SelectedItem=row;Get<DataGrid>("MappingGrid").ScrollIntoView(row);Save();
            Get<TextBlock>("GuideText").Text="已加入 "+row.Input.Caption+"。点选输出即可生效。外设自身的按键功能仍会同时生效。";
        }
        void RemoveRemote(){
            var m=Selected;if(!remoteView || m==null)return;Suspend();remoteProfile.Mappings.Remove(m);ShowList();Save();
        }
        static string Glyph(Mapping m){
            if(m==null)return "—";string key=ControllerLayout.KeyFor(m);
            switch(key){case "UP":return "↑";case "DOWN":return "↓";case "LEFT":return "←";case "RIGHT":return "→";case "View":return "⧉";case "Menu":return "≡";case "Home":return "⌂";case "Share":return "◎";case null:break;default:return key;}
            if(m.Input.Kind=="Dpad")return new[]{"↑","↓","←","→"}[Math.Max(0,Math.Min(3,m.Input.Index))];
            string name=m.Physical;return name.Length<=3?name:"·";
        }
        // Picking an output: simple ones apply at once; keyboard and sequence open their editor first (cancel restores the old output);
        // media waits for a choice in the dropdown.
        void TargetChosen(int index){
            var m=Selected;if(m==null || m.Locked || m.Reserved || index<0)return;
            TargetExtras(index,true);
            if(index<=2){Commit(index);return;}
            if(index==4){if(Get<ComboBox>("MediaCombo").SelectedItem!=null)Commit(4);else{Get<TextBlock>("GuideText").Text="在下拉框中选择一个媒体键，选好即生效。";window.Dispatcher.BeginInvoke(new Action(delegate{Get<ComboBox>("MediaCombo").IsDropDownOpen=true;}),DispatcherPriority.Input);}return;}
            bool ready=index==3?selectedKeys.Length>0:selectedSteps.Length>0;
            if(ready){Commit(index);return;}
            // Open the editor after the click finishes so the dialog does not fight the list's mouse capture.
            window.Dispatcher.BeginInvoke(new Action(delegate{
                if(Selected!=m)return;
                bool ok=index==3?OpenKeyPicker():OpenSequenceEditor();
                if(ok)Commit(index);else Selection();
            }),DispatcherPriority.Input);
        }
        void Commit(int index){var m=Selected;if(m==null || m.Locked || m.Reserved)return;
            var media=Get<ComboBox>("MediaCombo").SelectedItem as ComboBoxItem;
            if(index==3 && selectedKeys.Length==0 || index==4 && media==null || index==5 && selectedSteps.Length==0)return;
            Suspend();m.Target=Targets[index];m.Keys=index==3?(int[])selectedKeys.Clone():index==4?new[]{(int)media.Tag}:new int[0];
            m.Steps=index==5?selectedSteps.Select(st=>(int[])st.Clone()).ToArray():new int[0][];m.SequenceText=index==5?selectedSequence:"";m.SequenceMode=index==5?selectedMode:"Hog";
            m.Notify();Save();
            Get<TextBlock>("GuideText").Text=m.Physical+" → "+m.OutputLabel+"。"+(index==5?"按下时依次点按，每次按下触发一遍。":index==4 && KeyNames.Media.Take(2).Contains(m.Keys[0])?"按住时连续调节音量，松开停止。":"按住保持按下，松开立即弹起。");
        }
        void ShowRename(bool show){var panel=Get<Grid>("RenamePanel");panel.Visibility=show?Visibility.Visible:Visibility.Collapsed;
            var box=Get<TextBox>("PhysicalNameBox");
            if(show){var m=Selected;box.Text=m==null?"":m.Name;box.Focus();box.SelectAll();}
            else if(box.IsKeyboardFocusWithin)Get<DataGrid>("MappingGrid").Focus(); // a focused text box pauses output, so never leave focus in the hidden one
        }
        void Rename(){var m=Selected;if(m==null || m.Locked || m.Reserved || Editing==null)return;string name=Get<TextBox>("PhysicalNameBox").Text.Trim();if(name.Length==0){Footer("请输入实体按键名称，例如 A、L1 或自定义名称。");return;}if(Editing.Mappings.Any(r=>r!=m && r.Name==name && r.Confirmed)){Footer("此名称已被另一个按键使用，请使用全按键校准重新对准。");return;}m.Name=name;m.Confirmed=true;m.Notify();Save();ShowRename(false);Selection();}
        string liveShown;
        void UpdateLive(){
            controllerView.FollowInput=!busy && !(Keyboard.FocusedElement is TextBox);
            controllerView.UpdateInput(current,profile);
            string[] names=(profile==null?new Mapping[0]:profile.Mappings.Where(m=>m.Active)).Concat(remoteProfile.Mappings.Where(m=>m.Active)).Select(m=>(m.Confirmed?m.Physical:m.Raw)).ToArray();
            string joined=string.Join("\n",names);
            if(joined!=liveShown){liveShown=joined;Get<ItemsControl>("LiveChips").ItemsSource=names;
                Get<TextBlock>("RawText").Text=names.Length==0?"松开 · 等待输入":"";
                Get<TextBlock>("LiveHint").Text=names.Length==0?"实时输入":"输入中";
                Get<Ellipse>("LiveDot").Fill=Theme.Brush(names.Length==0?"#3A3C42":"#86C5B3");}
            var list=Editing;Get<TextBlock>("MapCountText").Text=list==null?"":remoteView?list.Mappings.Count(m=>m.Target!="None")+" / "+list.Mappings.Count+" 已设置":list.Mappings.Count(m=>m.Confirmed && !m.Locked)+" / "+list.Mappings.Count+" 已对准";
        }
        bool OpenKeyPicker(){if(Selected==null || Selected.Locked || Selected.Reserved)return false;busy=true;Suspend();var dialog=new KeyDialog(window,selectedKeys);try{if(dialog.ShowDialog()==true && dialog.Keys.Length>0){selectedKeys=dialog.Keys;Get<Button>("KeyButton").Content=KeyNames.Chord(selectedKeys);return true;}return false;}finally{busy=false;Suspend();}}
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
        void Quit(){if(quitting)return;quitting=true;bool pending=saveTimer.IsEnabled;saveTimer.Stop();if(pending)Save();output.ReleaseAll();remoteOutput.ReleaseAll();window.Close();app.Shutdown();}
        public void Dispose(){if(disposed)return;disposed=true;timer.Stop();saveTimer.Stop();SystemEvents.SessionSwitch-=SessionSwitch;if(source!=null){UnregisterHotKey(source.Handle,1);source.RemoveHook(Hotkey);}output.Dispose();remoteOutput.Dispose();device.Dispose();tray.Visible=false;tray.Dispose();appIcon.Dispose();}
    }
}
