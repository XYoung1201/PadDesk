using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PadDesk {
    // Shared palette helpers and native window chrome tweaks.
    public static class Theme {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attr,ref int value,int size);
        public static SolidColorBrush Brush(string hex){var b=new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));b.Freeze();return b;}
        public static readonly FontFamily Display=new FontFamily("Georgia, Noto Serif SC, Source Han Serif SC, STZhongsong, SimSun");
        public static readonly FontFamily SerifCn=new FontFamily("Noto Serif SC, Source Han Serif SC, STZhongsong, SimSun");
        // Dark caption that matches the ink ground (Windows 11 colours it exactly; Windows 10 falls back to dark mode).
        public static void Apply(Window window){
            try{
                IntPtr h=new System.Windows.Interop.WindowInteropHelper(window).Handle;if(h==IntPtr.Zero)return;
                int on=1,round=2,caption=0x00151211,text=0x00DBE6EC;
                DwmSetWindowAttribute(h,20,ref on,4);DwmSetWindowAttribute(h,33,ref round,4);
                DwmSetWindowAttribute(h,35,ref caption,4);DwmSetWindowAttribute(h,36,ref text,4);
            }catch{}
        }
    }
    public class DialogBase : Window {
        protected readonly StackPanel Panel=new StackPanel();
        protected readonly TextBlock Head,Hint,Status;
        protected readonly StackPanel Buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,24,0,0)};
        public DialogBase(Window owner,string title,string hint) {
            Owner=owner;Title=title;Width=580;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
            FontFamily=owner.FontFamily;FontSize=14;Background=Theme.Brush("#16171B");Foreground=Theme.Brush("#ECE6DB");Resources=owner.Resources;
            UseLayoutRounding=true;TextOptions.SetTextFormattingMode(this,TextFormattingMode.Display);
            SourceInitialized+=delegate{Theme.Apply(this);};
            PreviewKeyDown+=delegate(object s,KeyEventArgs e){if(e.Key==Key.Escape && !(this is KeyDialog)){Close();e.Handled=true;}};
            Panel.Margin=new Thickness(32,26,32,28);Content=Panel;
            var seal=new Border {Width=22,Height=3,Background=Theme.Brush("#D9502F"),HorizontalAlignment=HorizontalAlignment.Left,CornerRadius=new CornerRadius(1.5),Margin=new Thickness(0,0,0,16)};Panel.Children.Add(seal);
            Head=new TextBlock {Text=title,FontFamily=Theme.SerifCn,FontSize=24,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};Panel.Children.Add(Head);
            Hint=new TextBlock {Text=hint,Margin=new Thickness(0,10,0,22),TextWrapping=TextWrapping.Wrap,FontSize=13,LineHeight=21,Foreground=Theme.Brush("#9C968C")};Panel.Children.Add(Hint);
            Status=new TextBlock {TextWrapping=TextWrapping.Wrap,FontSize=13,Foreground=Theme.Brush("#9C968C"),Margin=new Thickness(0,14,0,0)};
        }
        protected Button Button(string text,Action action,bool primary=false) {var b=new Button {Content=text,Margin=new Thickness(10,0,0,0),Padding=new Thickness(18,0,18,0)};if(primary)b.Style=(Style)Resources["Primary"];b.Click+=delegate{action();};Buttons.Children.Add(b);return b;}
        protected void FinishLayout(){Panel.Children.Add(Status);Panel.Children.Add(Buttons);}
    }
    public sealed class KeyDialog : DialogBase {
        public int[] Keys {get;private set;}
        readonly TextBlock preview;readonly ComboBox manual;readonly CheckBox[] mods;bool syncing;
        static readonly int[] modKeys={17,18,16,91};static readonly string[] modNames={"Ctrl","Alt","Shift","Win"};
        public KeyDialog(Window owner,int[] existing):base(owner,"选择键盘输出","在这里按下一个键或组合键。输出会与手柄同步：按下保持，松开弹起。\n想“按住一个键、反复点另一个键”（如 Alt+Tab、Win+Tab、Ctrl+Win+←/→ 切换桌面），请把两个实体键分别映射：例如 LB → Alt，A → Tab。") {
            Keys=(int[])existing.Clone();preview=new TextBlock {Text=KeyNames.Chord(Keys),FontFamily=new FontFamily("Cascadia Mono, Consolas"),FontSize=26,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center};
            Panel.Children.Add(new Border {Background=Theme.Brush("#0D0E10"),BorderBrush=Theme.Brush("#34363D"),BorderThickness=new Thickness(1,1,1,3),CornerRadius=new CornerRadius(12),Padding=new Thickness(20,26,20,26),Margin=new Thickness(0,0,0,22),Child=preview});
            Panel.Children.Add(new TextBlock {Text="系统会拦截的组合（Alt+Tab、Win 键等）可用下方勾选修饰键 + 选择按键组成；只勾修饰键表示单独按住它。",FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=Theme.Brush("#66625C"),Margin=new Thickness(0,0,0,8)});
            var row=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,10)};mods=new CheckBox[modKeys.Length];
            for(int i=0;i<modKeys.Length;i++){mods[i]=new CheckBox {Content=modNames[i],Margin=new Thickness(0,0,18,0),Foreground=Theme.Brush("#ECE6DB"),VerticalAlignment=VerticalAlignment.Center};mods[i].Checked+=delegate{Rebuild();};mods[i].Unchecked+=delegate{Rebuild();};row.Children.Add(mods[i]);}
            Panel.Children.Add(row);
            manual=new ComboBox {MaxDropDownHeight=240};
            manual.Items.Add(new ComboBoxItem {Content="（不加其他键）",Tag=0});
            foreach(int vk in Enumerable.Range(8,247).Where(k=>k<=254 && !KeyNames.Modifier(k) && !KeyNames.Name(k).StartsWith("VK ")))manual.Items.Add(new ComboBoxItem {Content=KeyNames.Name(vk),Tag=vk});
            manual.SelectionChanged+=delegate{Rebuild();};Panel.Children.Add(manual);
            Sync();
            Button("取消",delegate{DialogResult=false;});Button("使用此按键",delegate{if(Keys.Length>0)DialogResult=true;},true);FinishLayout();
            PreviewKeyDown+=OnKey;
        }
        void Sync(){syncing=true;for(int i=0;i<modKeys.Length;i++)mods[i].IsChecked=Keys.Any(k=>KeyNames.Canonical(k)==KeyNames.Canonical(modKeys[i]) || modKeys[i]==91 && k==92);int main=Keys.FirstOrDefault(k=>!KeyNames.Modifier(k));manual.SelectedItem=manual.Items.Cast<ComboBoxItem>().FirstOrDefault(c=>(int)c.Tag==main);syncing=false;}
        void Rebuild(){
            if(syncing)return;var keys=new List<int>();
            for(int i=0;i<modKeys.Length;i++)if(mods[i].IsChecked==true)keys.Add(modKeys[i]);
            var item=manual.SelectedItem as ComboBoxItem;int main=item==null?0:(int)item.Tag;if(main!=0)keys.Add(main);
            Keys=keys.ToArray();preview.Text=KeyNames.Chord(Keys);
        }
        void OnKey(object sender,KeyEventArgs e) {
            if(Keyboard.FocusedElement is ComboBox)return;
            Key key=e.Key==Key.System?e.SystemKey:e.Key==Key.ImeProcessed?e.ImeProcessedKey:e.Key;int vk=KeyInterop.VirtualKeyFromKey(key);if(vk<1 || vk>254)return;
            var keys=new List<int>();var mods=Keyboard.Modifiers;
            if((mods&ModifierKeys.Control)!=0)keys.Add(17);if((mods&ModifierKeys.Alt)!=0)keys.Add(18);if((mods&ModifierKeys.Shift)!=0)keys.Add(16);if((mods&ModifierKeys.Windows)!=0)keys.Add(91);
            if(!KeyNames.Modifier(vk))keys.Add(vk);else if(keys.Count==0)keys.Add(vk);
            Keys=keys.Distinct().ToArray();preview.Text=KeyNames.Chord(Keys);Sync();Status.Text="已录入。点击“使用此按键”，再在主窗口点击“应用”。";e.Handled=true;
        }
    }
    // Edits a button's key sequence: either a HOG hotkey (letters, * and +) or general chords such as "Win+Ctrl+Right".
    public sealed class SequenceDialog : DialogBase {
        public int[][] Steps {get;private set;}
        public string Text {get;private set;}
        public string Mode {get;private set;}
        readonly RadioButton hog,keys;readonly TextBox box;readonly TextBlock preview;
        public SequenceDialog(Window owner,string mode,string text):base(owner,"按键序列","按下手柄键时，依次点按序列里的每一步（每步约 30 ms）。松开手柄键不会中断已开始的序列。") {
            Mode=mode=="Keys"?"Keys":"Hog";Text=text??"";Steps=new int[0][];
            var modes=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,12)};
            hog=new RadioButton {Content="HOG 热键",GroupName="seqmode",Foreground=Theme.Brush("#ECE6DB"),Margin=new Thickness(0,0,24,0),IsChecked=Mode=="Hog"};
            keys=new RadioButton {Content="通用按键序列",GroupName="seqmode",Foreground=Theme.Brush("#ECE6DB"),IsChecked=Mode=="Keys"};
            hog.Checked+=delegate{Mode="Hog";Update();};keys.Checked+=delegate{Mode="Keys";Update();};
            modes.Children.Add(hog);modes.Children.Add(keys);Panel.Children.Add(modes);
            string hogPath;var entries=HogConfig.Load(out hogPath);
            if(entries.Count>0) {
                Panel.Children.Add(new TextBlock {Text="从 HOG 配置选择（"+hogPath+"）",FontSize=12,Foreground=Theme.Brush("#66625C"),Margin=new Thickness(0,0,0,6),TextTrimming=TextTrimming.CharacterEllipsis});
                var pick=new ComboBox {MaxDropDownHeight=280,Margin=new Thickness(0,0,0,12)};foreach(var entry in entries)pick.Items.Add(entry);
                pick.SelectionChanged+=delegate{if(pick.SelectedItem==null)return;hog.IsChecked=true;Mode="Hog";box.Text=((HogConfig.Entry)pick.SelectedItem).Hotkey;};
                Panel.Children.Add(pick);
            }
            box=new TextBox {Text=Text,FontFamily=new FontFamily("Cascadia Mono, Consolas"),FontSize=18,Padding=new Thickness(10,6,10,6)};box.TextChanged+=delegate{Update();};Panel.Children.Add(box);
            preview=new TextBlock {FontSize=13,TextWrapping=TextWrapping.Wrap,Foreground=Theme.Brush("#9FD6C6"),Margin=new Thickness(0,10,0,0)};Panel.Children.Add(preview);
            Panel.Children.Add(new TextBlock {TextWrapping=TextWrapping.Wrap,FontSize=12,LineHeight=19,Foreground=Theme.Brush("#66625C"),Margin=new Thickness(0,12,0,0),
                Text="HOG 热键：直接写 HOG 配置里的字母序列，如 JFQR；* 表示先点一下左 Ctrl，+ 表示右 Ctrl（如 *JFYX、****）。\n通用序列：每步用空格分隔，同时按下的键用 + 连接，如 Win+Ctrl+Right、Ctrl+C Ctrl+V、VolUp VolUp。\n可用键名：Ctrl Alt Shift Win Tab Enter Esc Space Left Right Up Down Home End PgUp PgDn Delete F1–F24 A–Z 0–9 VolUp VolDown Mute PlayPause Next Prev。"});
            Button("取消",delegate{DialogResult=false;});Button("使用此序列",delegate{if(Update())DialogResult=true;},true);FinishLayout();
            Loaded+=delegate{box.Focus();box.SelectAll();};Update();
        }
        bool Update() {
            if(box==null || preview==null)return false;
            string error;var steps=Sequences.Parse(Mode,box.Text,out error);
            if(steps==null){preview.Text="";Status.Text=box.Text.Trim().Length==0?"":error;Steps=new int[0][];return false;}
            Steps=steps;Text=box.Text.Trim();if(Mode=="Hog")Text=Text.ToUpperInvariant();Status.Text="";
            preview.Text="共 "+steps.Length+" 步： "+Sequences.Describe(steps);return true;
        }
    }
    // Waits for a key on a remote or Bluetooth HID device and returns it as an input source.
    public sealed class RemoteLearnDialog : DialogBase {
        public Source Learned {get;private set;}
        readonly RemoteInput remote;readonly TextBlock shown;readonly Button add;
        public RemoteLearnDialog(Window owner,RemoteInput remote):base(owner,"学习遥控 / 蓝牙按键","按下遥控器、蓝牙遥控器、翻页笔或其他外设上的按键。会显示来自哪个设备，确认无误后点击“添加此按键”。这里不会发送鼠标或键盘输出。") {
            this.remote=remote;
            shown=new TextBlock {Text="等待按键…",FontFamily=Theme.SerifCn,FontSize=22,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};
            Panel.Children.Add(new Border {Background=Theme.Brush("#0D0E10"),BorderBrush=Theme.Brush("#34363D"),BorderThickness=new Thickness(1,1,1,3),CornerRadius=new CornerRadius(12),Padding=new Thickness(20,26,20,26),Child=shown});
            Status.Text=remote.Registered?"提示：电脑自带键盘的按键也会显示，请看清设备名。":"无法注册原始输入，外设按键可能无法识别。";
            Button("取消",delegate{DialogResult=false;});add=Button("添加此按键",delegate{if(Learned!=null)DialogResult=true;},true);add.IsEnabled=false;FinishLayout();
            remote.Pressed+=OnPressed;Closed+=delegate{remote.Pressed-=OnPressed;};
        }
        void OnPressed(Source source){Learned=source;shown.Text=RemoteKeys.Caption(source.Mask,source.Index)+"\n"+source.DeviceName;add.IsEnabled=true;}
    }
    public sealed class CalibrationDialog : DialogBase {
        readonly Profile profile;readonly bool locate;readonly Func<Source,string,string> assign;
        Snapshot neutral,current;CaptureDetector detector;readonly string[] labels;int step,done;bool finished,started;
        readonly TextBlock prompt;readonly ProgressBar progress;readonly Button skip,neutralButton;readonly Action<Snapshot> recordNeutral;
        readonly ControllerView controllerView;readonly DispatcherTimer advance=new DispatcherTimer();bool advancing;
        public CalibrationDialog(Window owner,Profile p,Snapshot initial,bool locate,Func<Source,string,string> assign,Action<Snapshot> recordNeutral):base(owner,locate?"按实体键定位":"逐个对准实体按键",locate?"先松开全部按键、扳机和摇杆并记录中立状态，再按要设置的实体键。此时不会发送鼠标或键盘事件。":"先松开全部按键、扳机和摇杆并记录中立状态，再按提示校准。没有的键可跳过；方向键保持原配置。") {
            profile=p;this.locate=locate;this.assign=assign;this.recordNeutral=recordNeutral;current=initial;neutral=initial.Clone();
            Width=650;
            controllerView=new ControllerView {Height=300,GuidanceMode=true,Margin=new Thickness(-32,0,-32,8)};Panel.Children.Add(controllerView);controllerView.SetLayout(profile.VisualLayout);controllerView.FocusControl(null);
            advance.Interval=TimeSpan.FromMilliseconds(380);advance.Tick+=delegate{advance.Stop();advancing=false;Next();};Closed+=delegate{advance.Stop();};
            foreach(var m in p.Mappings.Where(m=>m.Input.Kind=="Axis" && m.Input.Index<neutral.Axes.Length))neutral.Axes[m.Input.Index]=(short)m.Input.Rest;
            detector=new CaptureDetector(neutral,p.StickX,p.StickY);
            var list=new List<string>{"A","B","X","Y","L1 / LB","R1 / RB","L2 / LT","R2 / RT","返回 / Select","开始 / Start","左摇杆按下","右摇杆按下","Home","Capture"};
            int extra=Math.Max(0,p.Mappings.Count(m=>!m.Locked && m.Input.Kind=="Button")-14);for(int i=0;i<extra;i++)list.Add("额外按钮 "+(i+1));labels=list.ToArray();
            prompt=new TextBlock {FontFamily=Theme.SerifCn,FontSize=24,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,10,0,16)};Panel.Children.Add(prompt);
            progress=new ProgressBar {Minimum=0,Maximum=labels.Length};if(!locate)Panel.Children.Add(progress);
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
            prompt=new TextBlock {Text="① 松开摇杆，让它自然回中",FontFamily=Theme.SerifCn,FontSize=22,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,20)};Panel.Children.Add(prompt);
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
