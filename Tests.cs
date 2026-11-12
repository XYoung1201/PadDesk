using System;
using System.Linq;
using PadDesk;
class Tests {
    static int count;
    static void Check(bool ok,string name) {if(!ok){Console.WriteLine("FAIL: "+name);Environment.Exit(1);}count++;}
    static Snapshot S(bool b0=false,bool b1=false,short x=0,short y=0) {return new Snapshot {Connected=true,DeviceId="test",Buttons=new[]{b0,b1,false,false},Axes=new[]{x,y},Hats=new byte[0]};}
    static Profile P() {return new Profile {DeviceId="test",Speed=1000,Curve=1,Deadzone=.1,NeutralConfirmed=true,Mappings={new Mapping {Input=new Source {Index=0},Target="MouseLeft"}}};}
    static void Main() {
        var p=P();var e=new InputEngine();e.Tick(S(),0,p,true);
        var r=e.Tick(S(true),.016,p,true);Check(r.Events.Count==1 && r.Events[0].Token=="M:L" && r.Events[0].Down,"press sends mouse down");
        Check(e.Tick(S(true),.032,p,true).Events.Count==0,"hold does not click or release");
        r=e.Tick(S(),.048,p,true);Check(r.Events.Count==1 && !r.Events[0].Down,"release sends mouse up");
        e.Tick(S(true),.064,p,true);r=e.Tick(S(true),.08,p,false);Check(r.Events.Count==1 && !r.Events[0].Down,"pause releases held output");
        Check(e.Tick(S(true),.096,p,true).Events.Count==0,"resume held input suppressed");
        e.Tick(S(),.112,p,true);Check(e.Tick(S(true),.128,p,true).Events.Any(t=>t.Down),"neutral rearms");
        r=e.Tick(new Snapshot(),.144,p,true);Check(r.Events.Any(t=>!t.Down),"disconnect releases held output");
        p=P();p.Mappings[0].Target="Keyboard";p.Mappings[0].Keys=new[]{17,65};p.Mappings.Add(new Mapping {Input=new Source {Index=1},Target="Keyboard",Keys=new[]{17,65}});
        e=new InputEngine();e.Tick(S(),0,p,true);r=e.Tick(S(true),.016,p,true);
        Check(r.Events.Count==2 && r.Events[0].Token=="K:162" && r.Events[1].Token=="K:65","modifiers press before letter");
        Check(e.Tick(S(true,true),.032,p,true).Events.Count==0,"shared bindings press once");
        Check(e.Tick(S(false,true),.048,p,true).Events.Count==0,"shared key not released early");
        r=e.Tick(S(),.064,p,true);Check(r.Events.Count==2 && r.Events[0].Token=="K:65" && r.Events[1].Token=="K:162" && r.Events.All(t=>!t.Down),"release letter before modifier");
        p=P();p.Mappings.Clear();e=new InputEngine();e.Tick(S(),0,p,true);r=e.Tick(S(x:2000,y:2000),.016,p,true);Check(r.Dx==0 && r.Dy==0,"radial deadzone prevents drift");
        r=e.Tick(S(x:32767),.032,p,true);Check(r.Dx>0 && r.Dy==0,"stick moves horizontally");
        var ex=new InputEngine();ex.Tick(S(),0,p,true);int dx=ex.Tick(S(x:32767),.05,p,true).Dx;
        var ey=new InputEngine();ey.Tick(S(),0,p,true);int dy=ey.Tick(S(y:32767),.05,p,true).Dy;
        Check(dx==dy && dx==50,"equal horizontal vertical physical speed");
        var ed=new InputEngine();ed.Tick(S(),0,p,true);r=ed.Tick(S(x:32767,y:32767),.05,p,true);Check(Math.Sqrt(r.Dx*r.Dx+r.Dy*r.Dy)<=51,"diagonal speed normalized");
        Check(ed.Tick(S(),.1,p,true).Dx==0,"stick stops immediately at center");
        var stall=new InputEngine();stall.Tick(S(),0,p,true);r=stall.Tick(S(x:32767),8,p,true);Check(r.Dx<=50,"UI stall cannot jump cursor");
        p=P();p.Mappings.Clear();p.Mappings.Add(new Mapping {Input=new Source {Index=0},Locked=true,Target="Right"});p.Mappings.Add(new Mapping {Input=new Source {Index=1},Locked=true,Target="Down"});
        e=new InputEngine();e.Tick(S(),0,p,true);r=e.Tick(S(true,true),.01,p,true);Check(r.Dx==30 && r.Dy==20,"D-pad retains independent fixed steps");
        Check(e.Tick(S(true,true),.1,p,true).Dx==0,"D-pad repeat delay retained");
        r=e.Tick(S(true,true),.36,p,true);Check(r.Dx==30 && r.Dy==20,"D-pad repeat retained");
        var src=new Source {Kind="Axis",Index=0,Rest=-32768,Direction=1,Threshold=20000};Check(!src.Active(S(x:-32768)) && src.Active(S(x:0)),"trigger nonzero neutral handled");
        var detector=new CaptureDetector(S(true),0,1);Check(detector.Poll(S(true))==null,"calibration rejects already held button");detector.Poll(S());
        var captured=detector.Poll(S(false,true));Check(captured!=null && captured.Kind=="Button" && captured.Index==1,"calibration captures actual raw button index");
        detector=new CaptureDetector(S(),0,1);detector.Poll(S());Check(detector.Poll(S(x:32767))==null,"button calibration ignores mouse stick axes");
        var hn=new Snapshot {Connected=true,Buttons=new bool[0],Axes=new short[0],Hats=new byte[]{0}};detector=new CaptureDetector(hn,0,1);detector.Poll(hn);hn.Hats[0]=2;captured=detector.Poll(hn);Check(captured!=null && captured.Mask==2,"hat direction calibration");
        var an=new Snapshot {Connected=true,Buttons=new bool[0],Axes=new short[]{0,0,-32768}};detector=new CaptureDetector(an,0,1);detector.Poll(an);var ap=an.Clone();ap.Axes[2]=0;captured=detector.Poll(ap);Check(captured!=null && captured.Index==2 && captured.Rest== -32768 && captured.Direction==1,"trigger calibration preserves neutral and polarity");
        detector=new CaptureDetector(an,0,1);Check(detector.Poll(ap)==null,"next calibration waits for trigger release");Check(detector.Poll(ap)==null,"held trigger cannot satisfy next step");detector.Poll(an);Check(detector.Poll(ap)!=null,"released trigger can be captured again");
        p=P();p.Mappings[0].Target="Keyboard";p.Mappings[0].Keys=new[]{17};p.Mappings.Add(new Mapping {Input=new Source {Index=1},Target="Keyboard",Keys=new[]{162}});e=new InputEngine();e.Tick(S(),0,p,true);e.Tick(S(true),.016,p,true);Check(e.Tick(S(true,true),.032,p,true).Events.Count==0,"modifier aliases share output ownership");Check(e.Tick(S(false,true),.048,p,true).Events.Count==0,"modifier alias release cannot release another holder");
        p=P();p.NeutralConfirmed=false;p.Mappings.Clear();e=new InputEngine();e.Tick(S(x:20000),0,p,true);Check(e.Tick(S(x:32767),.05,p,true).Dx==0,"stick waits for explicit neutral calibration");
        p=P();p.Mappings.Add(new Mapping {Input=new Source {Kind="Axis",Index=0,Direction=-1,Rest=20000},Target="None"});e=new InputEngine();e.Tick(S(),0,p,true);Check(e.Tick(S(true),.016,p,true).Events.Any(t=>t.Down),"unassigned axis cannot prevent button arming");
        var canonical=new Source {Kind="Dpad",Index=2};var ds=S();ds.Dpad[2]=true;Check(canonical.Active(ds),"canonical D-pad directions stay distinct");
        Console.WriteLine("PASS: "+count+" behavior checks");
    }
}
