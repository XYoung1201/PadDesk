using System;
using System.Reflection;
using System.Windows;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Collections.Generic;
using System.Collections;
using System.Windows.Media.Media3D;
using PadDesk;

// Runs on the STA without sending any mouse/keyboard output or touching profiles.
class VisualTests {
    static int count;
    static void Check(bool value,string message) {if(!value)throw new Exception("FAIL: "+message);count++;}
    static IEnumerable<T> Children<T>(DependencyObject parent) where T:DependencyObject {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) {var child=VisualTreeHelper.GetChild(parent,i);var match=child as T;if(match!=null)yield return match;foreach(var nested in Children<T>(child))yield return nested;}
    }
    static void Pump(int ms){var frame=new DispatcherFrame();var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(ms)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
    static void Click(Window window,string caption){Children<Button>(window).First(b=>(string)b.Content==caption).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
    static void Capture(FrameworkElement element,string path){element.UpdateLayout();var bitmap=new RenderTargetBitmap((int)element.ActualWidth,(int)element.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(element);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var f=File.Create(path))encoder.Save(f);}
    static Point Project(Viewport3D viewport,Point3D position){var camera=(PerspectiveCamera)viewport.Camera;var forward=camera.LookDirection;forward.Normalize();var right=Vector3D.CrossProduct(forward,camera.UpDirection);right.Normalize();var up=Vector3D.CrossProduct(right,forward);var delta=position-camera.Position;double scale=viewport.ActualWidth/(2*Math.Tan(camera.FieldOfView*Math.PI/360)*Vector3D.DotProduct(delta,forward));return new Point(viewport.ActualWidth/2+scale*Vector3D.DotProduct(delta,right),viewport.ActualHeight/2-scale*Vector3D.DotProduct(delta,up));}
    [STAThread] static int Main(string[] args) {
        try {
            var assembly=Assembly.LoadFrom(args[0]);
            var type=assembly.GetType("PadDesk.ControllerView");
            Check(type!=null,"dashboard and calibration have a reusable 3D controller view");
            var view=(FrameworkElement)Activator.CreateInstance(type);
            Check(type.GetMethod("SetLayout")!=null,"photo-reference layout can be selected");
            var focus=type.GetMethod("FocusControl");
            focus.Invoke(view,new object[]{"L2 / LT"});
            Check((string)type.GetProperty("FocusedControl").GetValue(view,null)=="LT","legacy trigger name focuses physical LT");
            focus.Invoke(view,new object[]{"额外按钮 1"});
            Check((string)type.GetProperty("FocusedControl").GetValue(view,null)==null,"unknown control resets camera instead of pointing at an unrelated key");
            focus.Invoke(view,new object[]{"A"});
            Check((string)type.GetProperty("FocusedControl").GetValue(view,null)=="A","face button focus selects A");
            focus.Invoke(view,new object[]{null});
            Check((string)type.GetProperty("FocusedControl").GetValue(view,null)==null,"overview clears focus");
            var controller=(ControllerView)view;
            controller.SetLayout("Photo");
            var photoParts=(IDictionary)type.GetField("parts",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(controller);
            Func<string,Point3D> center=key=>(Point3D)photoParts[key].GetType().GetField("Center").GetValue(photoParts[key]);
            Check(center("X").Y>center("A").Y && center("B").Y<center("A").Y && center("Y").X<center("A").X,"photo layout places X top A right B bottom Y left");
            controller.SetLayout("Xbox");
            var profile=new Profile {DeviceId="visual-test",NeutralConfirmed=true};
            profile.Mappings.Add(new Mapping {Input=new Source {Index=0},Name="A",Confirmed=true});
            profile.Mappings.Add(new Mapping {Input=new Source {Index=1},Name="B",Confirmed=false});
            var snapshot=new Snapshot {Connected=true,DeviceId="visual-test",Buttons=new[]{true,true},Axes=new short[]{0,0}};
            controller.UpdateInput(snapshot,profile);
            Check(controller.ActiveControls.SequenceEqual(new[]{"A"}),"real confirmed input lights A, unconfirmed input does not light B");
            Check(controller.FocusedControl=="A","physical press moves focus to A");
            profile.Mappings[1].Confirmed=true;
            snapshot.Buttons[1]=false;controller.UpdateInput(snapshot,profile);
            snapshot.Buttons[1]=true;controller.UpdateInput(snapshot,profile);
            Check(controller.FocusedControl=="B","new B press takes focus while A remains held");
            controller.UpdateInput(snapshot,profile);
            Check(controller.FocusedControl=="B","held inputs do not restart or steal focus");
            snapshot.Buttons=new[]{false,false};controller.UpdateInput(snapshot,profile);
            Check(controller.FocusedControl=="B","release retains the last focus");
            controller.GuidanceMode=true;controller.FocusControl("LT");
            snapshot.Buttons[0]=true;controller.UpdateInput(snapshot,profile);
            Check(controller.FocusedControl=="LT","live input does not override calibration guidance");
            controller.GuidanceMode=false;profile.Mappings[1].Confirmed=false;
            int focusEvents=0;string inputKey=null;
            controller.InputFocused+=delegate(string key){focusEvents++;inputKey=key;};
            controller.FollowInput=false;controller.FocusControl("RT");
            snapshot.Buttons[0]=false;controller.UpdateInput(snapshot,profile);
            snapshot.Buttons[0]=true;controller.UpdateInput(snapshot,profile);
            Check(controller.FocusedControl=="RT" && focusEvents==0,"editing suppresses automatic focus and selection events");
            controller.FollowInput=true;controller.UpdateInput(snapshot,profile);
            Check(controller.FocusedControl=="RT","ending edit does not replay a held press");
            snapshot.Buttons[0]=false;controller.UpdateInput(snapshot,profile);
            snapshot.Buttons[0]=true;controller.UpdateInput(snapshot,profile);
            controller.UpdateInput(snapshot,profile);
            Check(controller.FocusedControl=="A" && focusEvents==1 && inputKey=="A","new input emits exactly one event for mapping selection");
            controller.UpdateInput(new Snapshot(),profile);Check(!controller.ActiveControls.Any(),"disconnect clears held visual controls");
            snapshot.DeviceId="other";controller.UpdateInput(snapshot,profile);Check(!controller.ActiveControls.Any(),"different device cannot drive previous profile visual");
            snapshot.DeviceId="visual-test";snapshot.Buttons=new[]{false,false};
            var folder=Path.GetDirectoryName(Path.GetFullPath(args[0]));
            Window owner;using(var f=File.OpenRead(Path.Combine(folder,"Main.xaml")))owner=(Window)XamlReader.Load(f);
            owner.Left=-12000;owner.Top=0;owner.WindowStartupLocation=WindowStartupLocation.Manual;
            ((Border)owner.FindName("ControllerHost")).Child=controller;
            var examples=new[]{"A","B","X","Y","L1 / LB","R1 / RB","L2 / LT","R2 / RT","返回 / Select","开始 / Start","左摇杆按下","右摇杆按下","Home","Capture"}.Select((name,i)=>new Mapping {Name=name,Confirmed=true,Input=new Source {Index=i}}).ToList();
            ((DataGrid)owner.FindName("MappingGrid")).ItemsSource=examples;
            ((TextBlock)owner.FindName("DeviceText")).Text="Xbox 标准布局 · 界面预览";
            ((TextBlock)owner.FindName("StateText")).Text="预览";
            owner.Show();Pump(650);Capture(owner,Path.Combine(folder,"overview.png"));Capture(controller,Path.Combine(folder,"model.png"));
            controller.SetLayout("Photo");((System.Windows.Controls.Primitives.Selector)owner.FindName("ModelCombo")).SelectedIndex=1;Pump(650);Capture(owner,Path.Combine(folder,"photo-overview.png"));
            owner.Width=900;owner.Height=600;Pump(200);Capture(owner,Path.Combine(folder,"minimum.png"));
            Check(((DataGrid)owner.FindName("MappingGrid")).ActualHeight>=150,"mapping list keeps several rows at minimum window size");
            var renamePanel=(Grid)owner.FindName("RenamePanel");renamePanel.Visibility=Visibility.Visible;((Button)owner.FindName("KeyButton")).Visibility=Visibility.Visible;Pump(150);
            var renameButton=(Button)owner.FindName("RenameButton");
            Check(renameButton.TransformToAncestor(owner).Transform(new Point(0,renameButton.ActualHeight)).Y<owner.ActualHeight-35,"expanded mapping editor stays accessible at minimum size");
            renamePanel.Visibility=Visibility.Collapsed;((Button)owner.FindName("KeyButton")).Visibility=Visibility.Collapsed;
            ((TabControl)owner.FindName("InspectorTabs")).SelectedIndex=1;Pump(200);Capture(owner,Path.Combine(folder,"movement.png"));
            Check(((TextBox)owner.FindName("HorizontalStepBox")).ActualWidth>80,"step inputs remain usable at minimum size");
            ((TabControl)owner.FindName("InspectorTabs")).SelectedIndex=0;owner.Width=1080;owner.Height=680;controller.SetLayout("Xbox");Pump(650);
            var viewport=Children<Viewport3D>(controller).Single();var camera=(PerspectiveCamera)viewport.Camera;
            var select=type.GetMethod("SelectAt",BindingFlags.NonPublic|BindingFlags.Instance);
            string clicked=null;controller.ControlClicked+=delegate(string key){clicked=key;};
            select.Invoke(controller,new object[]{Project(viewport,new Point3D(1.8,.16,.78))});
            Check(clicked=="A" && controller.FocusedControl=="A","ray picking at Xbox bottom face button selects A");
            var startEye=camera.Position;Pump(650);Check((camera.Position-startEye).Length>3,"selection actually animates camera toward the selected button");
            profile.Mappings[1].Confirmed=true;
            snapshot.Buttons=new[]{false,false};controller.UpdateInput(snapshot,profile);
            snapshot.Buttons[1]=true;var beforeSwitch=camera.Position;controller.UpdateInput(snapshot,profile);
            Check(controller.FocusedControl=="B" && (camera.Position-beforeSwitch).Length<.001,"input changes target without teleporting camera");
            Pump(120);Check((camera.Position-beforeSwitch).Length>.01,"camera moves toward newly pressed B");
            snapshot.Buttons[0]=true;var midway=camera.Position;controller.UpdateInput(snapshot,profile);
            Check(controller.FocusedControl=="A" && (camera.Position-midway).Length<.001,"new A redirects an in-flight transition continuously");
            profile.Mappings[1].Confirmed=false;
            snapshot.Buttons[1]=false;
            snapshot.Buttons[0]=true;controller.UpdateInput(snapshot,profile);Pump(250);
            var parts=(IDictionary)type.GetField("parts",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(controller);var aPart=parts["A"];
            var press=(TranslateTransform3D)aPart.GetType().GetField("Press").GetValue(aPart);
            Check(press.OffsetZ<-.06,"physical press animates the button down into the shell");
            snapshot.Buttons[0]=false;controller.UpdateInput(snapshot,profile);for(int settle=0;settle<20 && Math.Abs(press.OffsetZ)>=.005;settle++)Pump(100);Check(Math.Abs(press.OffsetZ)<.005,"physical release returns button to rest");
            controller.GuidanceMode=true;controller.FocusControl("LT");clicked=null;Pump(650);
            select.Invoke(controller,new object[]{Project(viewport,new Point3D(-1.8,1.64,-.17))});
            Check(clicked==null && controller.FocusedControl=="LT","model clicks in guidance mode cannot select or advance calibration");
            controller.GuidanceMode=false;
            controller.FocusControl("A");Pump(650);Capture(controller,Path.Combine(folder,"focus-a.png"));
            controller.FocusControl("LT");Pump(650);Capture(controller,Path.Combine(folder,"focus-lt.png"));
            int assigned=0;
            profile.VisualLayout="Photo";
            var calibration=new CalibrationDialog(owner,profile,snapshot,false,delegate(Source input,string label){assigned++;profile.Mappings[0].Input=input;profile.Mappings[0].Name=label;return null;},delegate{});
            calibration.WindowStartupLocation=WindowStartupLocation.Manual;calibration.Left=-12000;calibration.Top=0;calibration.Show();Pump(100);
            var guide=Children<ControllerView>(calibration).Single();
            var guideParts=(IDictionary)type.GetField("parts",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(guide);var guideA=guideParts["A"];
            Check(((Point3D)guideA.GetType().GetField("Center").GetValue(guideA)).X>2,"photo calibration points A at right-hand face button");
            Check(guide.FocusedControl==null,"calibration starts in neutral overview");
            Click(calibration,"记录松开状态");Check(guide.FocusedControl=="A","neutral capture focuses first physical A");
            calibration.Poll(snapshot);Check(assigned==0,"neutral polling cannot complete a calibration step");
            snapshot.Buttons[0]=true;calibration.Poll(snapshot);Check(assigned==1,"real button advances calibration exactly once");
            Pump(450);Check(guide.FocusedControl=="B","confirmation animation then focuses next B");
            calibration.Poll(snapshot);Check(assigned==1,"held A cannot satisfy next calibration step");
            var other=snapshot.Clone();other.DeviceId="different-device";other.Buttons[0]=false;calibration.Poll(other);other.Buttons[0]=true;calibration.Poll(other);
            Check(assigned==1 && guide.FocusedControl=="B","replacement device cannot advance original device calibration");
            for(int i=0;i<5;i++)Click(calibration,"跳过此键");
            Check(guide.FocusedControl=="LT","skip navigation targets the left trigger");
            Pump(650);Capture(calibration,Path.Combine(folder,"calibration-lt.png"));
            calibration.Poll(new Snapshot());Check(!guide.ActiveControls.Any(),"disconnect during calibration clears pressed highlights");
            for(int i=0;i<8;i++)Click(calibration,"跳过此键");
            Check(guide.FocusedControl==null,"calibration completion restores overview");
            calibration.Close();owner.Close();Pump(30);
            Console.WriteLine("PASS: "+count+" visual behavior checks");return 0;
        } catch(Exception ex) {Console.WriteLine(ex);return 1;}
    }
}
