using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PadDesk {
    public class DialogBase : Window {
        protected readonly StackPanel Panel=new StackPanel();
        protected readonly TextBlock Head,Hint,Status;
        protected readonly StackPanel Buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,22,0,0)};
        public DialogBase(Window owner,string title,string hint) {
            Owner=owner;Title=title;Width=560;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;
            FontFamily=new FontFamily("Microsoft YaHei UI");FontSize=14;Background=new SolidColorBrush(Color.FromRgb(246,248,252));Foreground=new SolidColorBrush(Color.FromRgb(23,36,61));Resources=owner.Resources;
            Panel.Margin=new Thickness(28);Content=Panel;
            Head=new TextBlock {Text=title,FontSize=23,FontWeight=FontWeights.Bold,TextWrapping=TextWrapping.Wrap};Panel.Children.Add(Head);
            Hint=new TextBlock {Text=hint,Margin=new Thickness(0,13,0,22),TextWrapping=TextWrapping.Wrap,Foreground=Brushes.SlateGray};Panel.Children.Add(Hint);
            Status=new TextBlock {TextWrapping=TextWrapping.Wrap,FontSize=15,Margin=new Thickness(0,12,0,0)};
        }
        protected Button Button(string text,Action action,bool primary=false) {var b=new Button {Content=text,Margin=new Thickness(8,0,0,0),Padding=new Thickness(16,9,16,9)};if(primary){b.Background=new SolidColorBrush(Color.FromRgb(83,101,233));b.Foreground=Brushes.White;}b.Click+=delegate{action();};Buttons.Children.Add(b);return b;}
        protected void FinishLayout(){Panel.Children.Add(Status);Panel.Children.Add(Buttons);}
    }
    public sealed class KeyDialog : DialogBase {
        public int[] Keys {get;private set;}
        readonly TextBlock preview;readonly ComboBox manual;
        public KeyDialog(Window owner,int[] existing):base(owner,"选择键盘输出","在这里按下一个键或组合键。输出会与手柄同步：按下保持，松开弹起。") {
            Keys=(int[])existing.Clone();preview=new TextBlock {Text=KeyNames.Chord(Keys),FontSize=27,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,6,0,20)};Panel.Children.Add(preview);
            Panel.Children.Add(new TextBlock {Text="也可直接选择键盘键（包含 Esc、Win 等）",Foreground=Brushes.SlateGray,Margin=new Thickness(0,0,0,8)});
            manual=new ComboBox {MaxDropDownHeight=240};
            foreach(int vk in Enumerable.Range(8,247).Where(k=>k<=254))manual.Items.Add(new ComboBoxItem {Content=KeyNames.Name(vk),Tag=vk});
            manual.SelectionChanged+=delegate{if(manual.SelectedItem!=null){Keys=new[]{(int)((ComboBoxItem)manual.SelectedItem).Tag};preview.Text=KeyNames.Chord(Keys);}};Panel.Children.Add(manual);
            Button("取消",delegate{DialogResult=false;});Button("使用此按键",delegate{if(Keys.Length>0)DialogResult=true;},true);FinishLayout();
            PreviewKeyDown+=OnKey;
        }
        void OnKey(object sender,KeyEventArgs e) {
            if(Keyboard.FocusedElement is ComboBox)return;
            Key key=e.Key==Key.System?e.SystemKey:e.Key==Key.ImeProcessed?e.ImeProcessedKey:e.Key;int vk=KeyInterop.VirtualKeyFromKey(key);if(vk<1 || vk>254)return;
            var keys=new List<int>();var mods=Keyboard.Modifiers;
            if((mods&ModifierKeys.Control)!=0)keys.Add(17);if((mods&ModifierKeys.Alt)!=0)keys.Add(18);if((mods&ModifierKeys.Shift)!=0)keys.Add(16);if((mods&ModifierKeys.Windows)!=0)keys.Add(91);
            if(!KeyNames.Modifier(vk))keys.Add(vk);else if(keys.Count==0)keys.Add(vk);
            Keys=keys.Distinct().ToArray();preview.Text=KeyNames.Chord(Keys);Status.Text="已录入。点击“使用此按键”，再在主窗口点击“应用”。";e.Handled=true;
        }
    }
    public sealed class CalibrationDialog : DialogBase {
        readonly Profile profile;readonly bool locate;readonly Func<Source,string,string> assign;
        Snapshot neutral,current;CaptureDetector detector;readonly string[] labels;int step,done;bool finished,started;
        readonly TextBlock prompt;readonly ProgressBar progress;readonly Button skip,neutralButton;readonly Action<Snapshot> recordNeutral;
        readonly ControllerView controllerView;readonly DispatcherTimer advance=new DispatcherTimer();bool advancing;
        public CalibrationDialog(Window owner,Profile p,Snapshot initial,bool locate,Func<Source,string,string> assign,Action<Snapshot> recordNeutral):base(owner,locate?"按实体键定位":"逐个对准实体按键",locate?"先松开全部按键、扳机和摇杆并记录中立状态，再按要设置的实体键。此时不会发送鼠标或键盘事件。":"先松开全部按键、扳机和摇杆并记录中立状态，再按提示校准。没有的键可跳过；方向键保持原配置。") {
            profile=p;this.locate=locate;this.assign=assign;this.recordNeutral=recordNeutral;current=initial;neutral=initial.Clone();
            Width=650;
            controllerView=new ControllerView {Height=275,GuidanceMode=true,Margin=new Thickness(0,0,0,8)};Panel.Children.Add(controllerView);controllerView.SetLayout(profile.VisualLayout);controllerView.FocusControl(null);
            advance.Interval=TimeSpan.FromMilliseconds(380);advance.Tick+=delegate{advance.Stop();advancing=false;Next();};Closed+=delegate{advance.Stop();};
            foreach(var m in p.Mappings.Where(m=>m.Input.Kind=="Axis" && m.Input.Index<neutral.Axes.Length))neutral.Axes[m.Input.Index]=(short)m.Input.Rest;
            detector=new CaptureDetector(neutral,p.StickX,p.StickY);
            var list=new List<string>{"A","B","X","Y","L1 / LB","R1 / RB","L2 / LT","R2 / RT","返回 / Select","开始 / Start","左摇杆按下","右摇杆按下","Home","Capture"};
            int extra=Math.Max(0,p.Mappings.Count(m=>!m.Locked && m.Input.Kind=="Button")-14);for(int i=0;i<extra;i++)list.Add("额外按钮 "+(i+1));labels=list.ToArray();
            prompt=new TextBlock {FontSize=24,FontWeight=FontWeights.Bold,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,8,0,15)};Panel.Children.Add(prompt);
            progress=new ProgressBar {Minimum=0,Maximum=labels.Length,Height=6,Foreground=new SolidColorBrush(Color.FromRgb(83,101,233))};if(!locate)Panel.Children.Add(progress);
            Button("结束",delegate{Close();});skip=Button("跳过此键",Next);skip.IsEnabled=false;if(locate)skip.Visibility=Visibility.Collapsed;
            neutralButton=Button("记录松开状态",delegate{if(!current.Connected || current.DeviceId!=profile.DeviceId)return;if(current.Buttons.Any(b=>b) || current.Hats.Any(h=>h!=0)){Status.Text="请先松开所有手柄按键和方向键。";return;}neutral=current.Clone();recordNeutral(neutral);detector=new CaptureDetector(neutral,profile.StickX,profile.StickY);started=true;neutralButton.Visibility=Visibility.Collapsed;skip.IsEnabled=true;UpdatePrompt();},true);
            FinishLayout();prompt.Text="松开全部输入后，记录中立状态";Status.Text="请让两个摇杆自然回中，并完全松开两个扳机。";
        }
        void UpdatePrompt(){string key=locate?null:ControllerLayout.Key(labels[step]);controllerView.FocusControl(key);prompt.Text=locate?"按下要定位的实体键":"请按  "+(key==null?labels[step]:ControllerLayout.Caption(key,profile.VisualLayout));if(!locate)Status.Text="第 "+(step+1)+" / "+labels.Length+" 步 · "+(key==null?"扩展键因型号而异，请按实体标记确认；没有可跳过。":"请先松开其他按键，再按模型中发光的实体键。");}
        void Next(){if(finished)return;step++;progress.Value=step;if(step>=labels.Length){finished=true;controllerView.FocusControl(null);prompt.Text="对准完成";Status.Text="已确认 "+done+" 个实体键。其余输入可用“按键定位”选择并命名。";skip.IsEnabled=false;return;}skip.IsEnabled=true;detector=new CaptureDetector(neutral,profile.StickX,profile.StickY);UpdatePrompt();}
        public void Poll(Snapshot s){current=s;controllerView.UpdateInput(s,profile);if(!s.Connected){Status.Text="手柄已断开，请重新连接或结束校准。";return;}if(s.DeviceId!=profile.DeviceId){Status.Text="当前设备已改变，请结束后重新校准。";return;}if(finished || !started || advancing)return;Source input=detector.Poll(s);if(input==null)return;
            string error=assign(input,locate?"":labels[step]);if(error!=null){Status.Text=error;detector=new CaptureDetector(neutral,profile.StickX,profile.StickY);return;}
            if(locate){DialogResult=true;return;}done++;advancing=true;skip.IsEnabled=false;prompt.Text="✓ 已识别 · "+ControllerLayout.Caption(ControllerLayout.Key(labels[step]),profile.VisualLayout);Status.Text="请松开按键，准备下一步。";controllerView.UpdateInput(s,profile);advance.Start();
        }
    }
    public sealed class StickDialog : DialogBase {
        public int X,Y,SignX=1,SignY=1;public double CenterX,CenterY;
        Snapshot current,neutral;int stage;readonly TextBlock prompt;readonly Button center;
        public StickDialog(Window owner,Profile p,Snapshot initial):base(owner,"校准左摇杆","依次记录回中位置、向右和向下方向。校准期间鼠标与键盘输出均暂停。") {
            current=initial;X=p.StickX;Y=p.StickY;
            prompt=new TextBlock {Text="① 松开摇杆，让它自然回中",FontSize=23,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,20)};Panel.Children.Add(prompt);
            Button("取消",delegate{DialogResult=false;});center=Button("记录中心",delegate{if(stage==4){DialogResult=true;return;}if(!current.Connected || current.Axes.Length<2){Status.Text="没有检测到两个摇杆轴。";return;}neutral=current.Clone();stage=1;center.IsEnabled=false;prompt.Text="② 将左摇杆向右推到边缘";},true);FinishLayout();
        }
        public void Poll(Snapshot s){current=s;if(!s.Connected){Status.Text="手柄已断开，请取消后重新连接。";return;}if(stage==0 || neutral==null)return;
            if(s.DeviceId!=neutral.DeviceId){Status.Text="设备已改变，请取消并重新开始。";return;}
            if(stage==2){if(s.Axes.Select((a,i)=>Math.Abs(a-neutral.Axes[i])).All(a=>a<6000)){stage=3;prompt.Text="③ 将左摇杆向下推到边缘";}return;}
            double strongest=18000;int axis=-1;for(int i=0;i<Math.Min(s.Axes.Length,neutral.Axes.Length);i++){if(stage==3 && i==X)continue;double delta=Math.Abs(s.Axes[i]-neutral.Axes[i]);if(delta>strongest){strongest=delta;axis=i;}}
            if(axis<0)return;
            if(stage==1){X=axis;CenterX=neutral.Axes[X];SignX=s.Axes[X]>CenterX?1:-1;stage=2;prompt.Text="已识别水平轴，请松开摇杆回中";}
            else if(stage==3){Y=axis;CenterY=neutral.Axes[Y];SignY=s.Axes[Y]>CenterY?1:-1;stage=4;prompt.Text="左摇杆校准完成";Status.Text="松开摇杆后点击“完成”。两轴将使用同一速度。";center.Content="完成";center.IsEnabled=true;}
        }
    }
}
