using System;
using System.Linq;
using PadDesk;
class Tests {
    static int count;
    static void Check(bool ok,string name) {if(!ok){Console.WriteLine("FAIL: "+name);Environment.Exit(1);}count++;}
    static Snapshot S(bool b0=false,bool b1=false,short x=0,short y=0) {return new Snapshot {Connected=true,DeviceId="test",Buttons=new[]{b0,b1,false,false},Axes=new[]{x,y},Hats=new byte[0]};}
    static Profile P() {return new Profile {DeviceId="test",Speed=1000,Curve=1,Deadzone=.1,NeutralConfirmed=true,Mappings={new Mapping {Input=new Source {Index=0},Target="MouseLeft"}}};}
    static void Main() {
        var steps=P();
        Check(!steps.SetDpadSteps("0","20") && steps.HorizontalStep==30,"invalid zero step preserves existing settings");
        Check(!steps.SetDpadSteps("45","5001") && steps.HorizontalStep==30 && steps.VerticalStep==20,"invalid second axis cannot partially change first axis");
        Check(!steps.SetDpadSteps("1.5","20") && !steps.SetDpadSteps("","20"),"step editor rejects decimals and empty values");
        Check(steps.SetDpadSteps("1","5000") && steps.HorizontalStep==1 && steps.VerticalStep==5000,"step editor accepts inclusive bounds");
        Check(!steps.SetRepeat("99","90") && !steps.SetRepeat("350","29") && steps.RepeatDelayMs==350 && steps.RepeatIntervalMs==90,"repeat editor rejects out-of-range values without partial changes");
        Check(!steps.SetRepeat("","90") && !steps.SetRepeat("350","1.5"),"repeat editor rejects empty and decimal values");
        Check(steps.SetRepeat("100","5000") && steps.RepeatDelayMs==100 && steps.RepeatIntervalMs==5000,"repeat editor accepts inclusive bounds");
        steps.Mappings.Add(new Mapping {Input=new Source {Kind="Dpad",Index=3},Target="Right",Locked=true});var stepEngine=new InputEngine();var stepSnapshot=S();stepEngine.Tick(stepSnapshot,0,steps,true);stepSnapshot.Dpad[3]=true;
        Check(stepEngine.Tick(stepSnapshot,.016,steps,true).Dx==1,"edited horizontal step reaches input engine");
        steps.VisualLayout="Photo";
        using(var stream=new System.IO.MemoryStream()){
            var serializer=new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Profile));serializer.WriteObject(stream,steps);stream.Position=0;
            var restored=(Profile)serializer.ReadObject(stream);restored.Validate();
            Check(restored.VisualLayout=="Photo" && restored.HorizontalStep==1 && restored.VerticalStep==5000,"layout and edited steps survive profile serialization");
        }
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
        var visual=new Mapping {Name="A",Confirmed=false};Check(ControllerLayout.KeyFor(visual)==null,"unconfirmed hint cannot light a physical face button");
        visual.Confirmed=true;Check(ControllerLayout.KeyFor(visual)=="A","confirmed face button drives its physical model");
        visual.Name="L2 / LT";Check(ControllerLayout.KeyFor(visual)=="LT","saved trigger alias resolves to Xbox LT");
        visual.Name="Custom key";Check(ControllerLayout.KeyFor(visual)==null,"custom name cannot falsely target a known button");
        visual.Locked=true;visual.Target="Left";Check(ControllerLayout.KeyFor(visual)=="LEFT","locked dpad uses direction instead of editable name");
        visual.Reserved=true;Check(ControllerLayout.KeyFor(visual)==null,"reserved direction axes never double-light a control");
        // Hold one button, tap another: LB holds Alt while A taps Tab (Alt+Tab switching).
        p=P();p.Mappings[0].Target="Keyboard";p.Mappings[0].Keys=new[]{18};p.Mappings.Add(new Mapping {Input=new Source {Index=1},Target="Keyboard",Keys=new[]{9}});
        e=new InputEngine();e.Tick(S(),0,p,true);r=e.Tick(S(true),.016,p,true);Check(r.Events.Count==1 && r.Events[0].ToString()=="K:164 down","held modifier pressed alone");
        r=e.Tick(S(true,true),.032,p,true);Check(r.Events.Count==1 && r.Events[0].ToString()=="K:9 down","tap key pressed while modifier held");
        r=e.Tick(S(true),.048,p,true);Check(r.Events.Count==1 && r.Events[0].ToString()=="K:9 up","tap key released, modifier stays held");
        r=e.Tick(S(true,true),.064,p,true);Check(r.Events.Count==1 && r.Events[0].ToString()=="K:9 down","second tap repeats under same modifier");
        r=e.Tick(S(),.08,p,true);Check(r.Events.Count==2 && r.Events[0].ToString()=="K:9 up" && r.Events[1].ToString()=="K:164 up","releasing both lifts tap key before modifier");
        // Sub-frame taps are replayed as a full press and release.
        var before=S(true);var after=S(true);
        var mid=Device.Between(after,new[]{1},new int[0],new System.Collections.Generic.Dictionary<int,byte>(),new int[0],new int[0]);
        Check(mid!=null && mid.Buttons[1] && !after.Buttons[1],"press and release inside one poll yields an in-between snapshot");
        Check(Device.Between(after,new[]{0},new int[0],new System.Collections.Generic.Dictionary<int,byte>(),new int[0],new int[0])==null,"stale down event for a held button adds nothing");
        var re=Device.Between(S(true),new int[0],new[]{0},new System.Collections.Generic.Dictionary<int,byte>(),new int[0],new int[0]);Check(re!=null && !re.Buttons[0],"release and re-press inside one poll yields a released in-between snapshot");
        e=new InputEngine();e.Tick(S(),0,p,true);e.Tick(before,.016,p,true);
        var pr=e.Tick(mid,.032,p,true,true);Check(pr.Events.Count==1 && pr.Events[0].ToString()=="K:9 down","partial frame presses the quick tap");
        r=e.Tick(after,.032,p,true);Check(r.Events.Count==1 && r.Events[0].ToString()=="K:9 up","following frame releases the quick tap");
        // Media keys.
        p=P();p.Mappings[0].Target="Media";p.Mappings[0].Keys=new[]{179};p.Validate();
        e=new InputEngine();e.Tick(S(),0,p,true);r=e.Tick(S(true),.016,p,true);Check(r.Events.Count==1 && r.Events[0].ToString()=="K:179 down","play/pause pressed with button");
        Check(e.Tick(S(true),1,p,true).Events.Count==0,"play/pause does not repeat");
        Check(e.Tick(S(),1.02,p,true).Events.Single().ToString()=="K:179 up","play/pause released with button");
        p.Mappings[0].Keys=new[]{175};e=new InputEngine();e.Tick(S(),0,p,true);
        Check(e.Tick(S(true),.01,p,true).Events.Single().ToString()=="K:175 down","volume up pressed");
        Check(e.Tick(S(true),.2,p,true).Events.Count==0,"volume waits for repeat delay");
        Check(e.Tick(S(true),.37,p,true).Events.Single().ToString()=="K:175 up","volume repeat releases");
        Check(e.Tick(S(true),.39,p,true).Events.Single().ToString()=="K:175 down","volume repeat presses again");
        Check(e.Tick(S(),.4,p,true).Events.Single().ToString()=="K:175 up","volume stops on release");
        p.Mappings[0].Keys=new[]{65};bool rejected=false;try{p.Validate();}catch(System.IO.InvalidDataException){rejected=true;}Check(rejected,"media target rejects ordinary keys");
        // Sequences for HOG hotkeys.
        string err;var hog=Sequences.ParseHog("*jfyx",out err);Check(hog!=null && hog.Length==5 && hog[0][0]==162 && hog[1][0]=='J' && hog[4][0]=='X',"HOG hotkey parses prefix and letters");
        Check(Sequences.ParseHog("JF Q1",out err)==null && err!=null,"HOG hotkey rejects digits");
        var keys=Sequences.ParseKeys("Win+Ctrl+Right  ctrl+c,VolUp",out err);Check(keys!=null && keys.Length==3 && keys[0].SequenceEqual(new[]{91,162,39}) && keys[1].SequenceEqual(new[]{162,67}) && keys[2][0]==175,"general sequence parses chords");
        Check(Sequences.ParseKeys("Ctrl+Nope",out err)==null && err.Contains("Nope"),"unknown key name reported");
        p=P();p.Mappings[0].Target="Sequence";p.Mappings[0].Steps=Sequences.ParseHog("JFQR",out err);p.Mappings[0].SequenceText="JFQR";p.Validate();
        e=new InputEngine();e.Tick(S(),0,p,true);var played=new System.Collections.Generic.List<string>();double tt=0;
        for(int i=0;i<12;i++){tt+=.016;played.AddRange(e.Tick(S(i<2),tt,p,true).Events.Select(ev=>ev.ToString()));}
        Check(string.Join(",",played)=="K:74 down,K:74 up,K:70 down,K:70 up,K:81 down,K:81 up,K:82 down,K:82 up","sequence taps each key in order once per press");
        p.Mappings[0].Steps=Sequences.ParseHog("****",out err);e=new InputEngine();e.Tick(S(),0,p,true);played.Clear();tt=0;
        for(int i=0;i<10;i++){tt+=.016;played.AddRange(e.Tick(S(i==0),tt,p,true).Events.Select(ev=>ev.ToString()));}
        Check(played.Count==8 && played.Where((x,i)=>i%2==0).All(x=>x=="K:162 down"),"repeated modifier taps stay separate");
        e=new InputEngine();e.Tick(S(),0,p,true);e.Tick(S(true),.016,p,true);r=e.Tick(S(true),.032,p,false);Check(!e.SequencePending && r.Events.All(ev=>!ev.Down),"pause cancels a running sequence");
        p.Mappings[0].Steps=new int[0][];rejected=false;try{p.Validate();}catch(System.IO.InvalidDataException){rejected=true;}Check(rejected,"empty sequence rejected");
        var conf=HogConfig.Parse(new[]{"//comment","JFQR CMD:QRCODE","*JFYX TEXT:a@b.c","JFRLD CMD:RELOAD                     // 重载","bad1 CMD:X","",""});
        Check(conf.Count==3 && conf[0].Hotkey=="JFQR" && conf[1].Action=="TEXT:a@b.c" && conf[2].Action=="CMD:RELOAD","HOG config lists valid hotkeys");
        // Remote / Bluetooth keys.
        var remote=new Source {Kind="Remote",Device="HID#VID_1234&PID_0001",Mask=RemoteKeys.ConsumerPage,Index=0xE9};
        var rs=new Snapshot {Connected=true,DeviceId="remotes"};Check(!remote.Active(rs),"remote key idle");rs.Remote.Add(remote.Id);Check(remote.Active(rs),"remote key active by device and usage");
        var other=new Source {Kind="Remote",Device="HID#VID_9999&PID_0001",Mask=RemoteKeys.ConsumerPage,Index=0xE9};Check(!other.Active(rs),"same key on another device stays separate");
        var rp=new Profile {DeviceId="remotes",Mappings={new Mapping {Input=remote,Target="Sequence",Steps=Sequences.ParseHog("JFQR",out err),SequenceText="JFQR"}}};rp.Validate();
        e=new InputEngine();e.Tick(new Snapshot {Connected=true,DeviceId="remotes"},0,rp,true);Check(e.Tick(rs,.016,rp,true).Events.Single().ToString()=="K:74 down","remote key drives the engine without a gamepad");
        Check(RemoteInput.StableId(@"\\?\HID#VID_046D&PID_C52B&MI_01&Col02#7&2a8b&0&0001#{4d1e55b2-f16f-11cf-88cb-001111000030}")=="HID#VID_046D&PID_C52B&MI_01&COL02","device identity drops the instance part");
        using(var stream=new System.IO.MemoryStream()){
            var serializer=new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Profile));serializer.WriteObject(stream,rp);stream.Position=0;
            var restored=(Profile)serializer.ReadObject(stream);restored.Validate();
            Check(restored.Mappings[0].Input.Id==remote.Id && restored.Mappings[0].Steps.Length==4 && restored.Mappings[0].SequenceText=="JFQR","remote sequence mapping survives serialization");
        }
        string legacy="{\"DeviceId\":\"old\",\"Version\":2,\"Mappings\":[{\"Input\":{\"Kind\":\"Button\",\"Index\":0,\"Direction\":1,\"Mask\":0,\"Rest\":0,\"Threshold\":14000},\"Name\":\"A\",\"Confirmed\":true,\"Target\":\"MouseLeft\",\"Keys\":[],\"Locked\":false,\"Reserved\":false}],\"HorizontalStep\":30,\"VerticalStep\":20,\"RepeatDelayMs\":350,\"RepeatIntervalMs\":90,\"RepeatEnabled\":true,\"StickX\":0,\"StickY\":1,\"SignX\":1,\"SignY\":1,\"CenterX\":0,\"CenterY\":0,\"Deadzone\":0.16,\"Speed\":800,\"Curve\":1.6,\"StickEnabled\":true,\"StickCalibrated\":false,\"NeutralConfirmed\":false,\"VisualLayout\":\"Xbox\"}";
        using(var stream=new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(legacy))){
            var old=(Profile)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Profile)).ReadObject(stream);old.Validate();
            Check(old.Mappings[0].Steps!=null && old.Mappings[0].Input.Device=="" && old.Mappings[0].Input.Id=="Button:0:1","profiles from the previous version still load");
        }
        Console.WriteLine("PASS: "+count+" behavior checks");
    }
}
