using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PadDesk;
class IntegrationTests {
    class InputTestForm : Form { protected override bool IsInputKey(Keys keyData) { return true; } }
    static int count;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);count++;Console.WriteLine("PASS: "+name);}
    static bool Pump(Func<bool> condition){var end=DateTime.UtcNow.AddSeconds(2);while(DateTime.UtcNow<end){Application.DoEvents();if(condition())return true;System.Threading.Thread.Sleep(8);}return condition();}
    static FrameResult Event(string token,bool down){var f=new FrameResult();f.Events.Add(new OutputEvent {Token=token,Down=down});return f;}
    [STAThread] static int Main(){NativeOutput.EnableDpi();Application.EnableVisualStyles();NativeOutput.Point original;NativeOutput.GetCursorPos(out original);
        try {
            var p=new Profile {DeviceId="integration-fixture",DeviceName="Test",NeutralConfirmed=true,HorizontalStep=100,VerticalStep=80,Mappings={new Mapping {Input=new Source {Kind="Axis",Index=2,Rest=-32768,Threshold=12000},Name="LT",Confirmed=true,Target="Keyboard",Keys=new[]{17,65}}}};
            p.Save();p.Speed=1200;p.Save();var read=Profile.Load(p.DeviceId);Check(read.Speed==1200 && read.HorizontalStep==100 && read.VerticalStep==80,"per-device config persists and updates");Check(read.Mappings[0].Input.Rest==-32768 && read.Mappings[0].Input.Threshold==12000 && read.Mappings[0].Keys.Length==2,"axis calibration and chord survive reload");
            using(var device=new Device()){var snap=device.Read(0);Check(snap.Connected,"current physical controller opens");var actual=device.CreateProfile(snap);actual.Validate();Check(actual.Mappings.Count(m=>m.Locked)==4,"all four D-pad outputs locked");Console.WriteLine(snap.Name+" | "+snap.DeviceId+" | "+snap.Buttons.Length+" buttons / "+snap.Axes.Length+" axes");}
            using(var form=new InputTestForm {Text="PadDesk · 输入验证",TopMost=true,StartPosition=FormStartPosition.CenterScreen,Width=440,Height=240,KeyPreview=true})
            using(var output=new NativeOutput()){
                int leftDown=0,leftUp=0,rightDown=0,rightUp=0,keyDown=0,keyUp=0,arrowDown=0,arrowUp=0;
                form.MouseDown+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left)leftDown++;if(e.Button==MouseButtons.Right)rightDown++;};form.MouseUp+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left)leftUp++;if(e.Button==MouseButtons.Right)rightUp++;};
                form.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.A)keyDown++;if(e.KeyCode==Keys.Right)arrowDown++;e.Handled=true;};form.KeyUp+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.A)keyUp++;if(e.KeyCode==Keys.Right)arrowUp++;e.Handled=true;};
                form.Show();form.Activate();Application.DoEvents();var point=form.PointToScreen(new System.Drawing.Point(60,70));NativeOutput.SetCursorPos(point.X,point.Y);
                output.Apply(new FrameResult {Dx=37,Dy=13});NativeOutput.Point position;NativeOutput.GetCursorPos(out position);Check(position.X==point.X+37 && position.Y==point.Y+13,"real cursor deltas are exact physical pixels");
                output.Apply(Event("M:L",true));Check(Pump(()=>leftDown==1) && NativeOutput.GetAsyncKeyState(1)<0,"mouse left down delivered and held");output.Apply(new FrameResult());Application.DoEvents();Check(leftUp==0,"empty frame does not release held mouse");output.Apply(Event("M:L",false));Check(Pump(()=>leftUp==1) && NativeOutput.GetAsyncKeyState(1)>=0,"mouse left up delivered");
                output.Apply(Event("M:R",true));Check(Pump(()=>rightDown==1),"mouse right down delivered");output.Apply(Event("M:R",false));Check(Pump(()=>rightUp==1),"mouse right up delivered");
                output.Apply(Event("K:65",true));Check(Pump(()=>keyDown==1) && NativeOutput.GetAsyncKeyState(65)<0,"keyboard down delivered and held");output.Apply(Event("K:65",false));Check(Pump(()=>keyUp==1) && NativeOutput.GetAsyncKeyState(65)>=0,"keyboard up delivered");
                output.Apply(Event("K:39",true));Check(Pump(()=>arrowDown==1),"extended arrow down delivered");output.Apply(Event("K:39",false));Check(Pump(()=>arrowUp==1),"extended arrow up delivered");
                output.Apply(Event("K:162",true));output.Apply(Event("M:L",true));Check(Pump(()=>NativeOutput.GetAsyncKeyState(17)<0 && NativeOutput.GetAsyncKeyState(1)<0),"modifier and mouse can be held together");output.ReleaseAll();Check(Pump(()=>NativeOutput.GetAsyncKeyState(17)>=0 && NativeOutput.GetAsyncKeyState(1)>=0),"cleanup releases every owned key and mouse button");form.Close();
            }
            Console.WriteLine("PASS: "+count+" integration checks");return 0;
        }catch(Exception ex){Console.WriteLine(ex.Message);return 1;}finally{NativeOutput.SetCursorPos(original.X,original.Y);}
    }
}
