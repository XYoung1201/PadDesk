using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace PadDesk {
    public class Snapshot {
        public bool Connected; public string DeviceId="",Name="";
        public double RightStickX,RightStickY;
        public bool[] Buttons=new bool[0]; public short[] Axes=new short[0]; public byte[] Hats=new byte[0]; public bool[] Dpad=new bool[4];
        public Snapshot Clone() { return new Snapshot {Connected=Connected,DeviceId=DeviceId,Name=Name,RightStickX=RightStickX,RightStickY=RightStickY,Buttons=(bool[])Buttons.Clone(),Axes=(short[])Axes.Clone(),Hats=(byte[])Hats.Clone(),Dpad=(bool[])Dpad.Clone()}; }
    }
    [DataContract] public class Source {
        [DataMember] public string Kind="Button";
        [DataMember] public int Index,Direction=1,Mask;
        [DataMember] public double Rest,Threshold=14000;
        public string Id {get {return Kind+":"+Index+":"+(Kind=="Hat"?Mask:Direction);}}
        public string Caption {get {return Kind=="Dpad"?"方向键 "+new[]{"↑","↓","←","→"}[Index]:Kind=="Button"?"按钮 "+(Index+1).ToString("00"):Kind=="Hat"?"方向帽 "+(Index+1)+" / "+Mask:"轴 "+(Index+1)+(Direction>0?" +":" −");}}
        public bool Active(Snapshot s) {
            if(!s.Connected) return false;
            if(Kind=="Dpad")return Index>=0 && Index<s.Dpad.Length && s.Dpad[Index];
            if(Kind=="Button") return Index>=0 && Index<s.Buttons.Length && s.Buttons[Index];
            if(Kind=="Hat") return Index>=0 && Index<s.Hats.Length && (s.Hats[Index]&Mask)!=0;
            return Index>=0 && Index<s.Axes.Length && (s.Axes[Index]-Rest)*Direction>=Threshold;
        }
    }
    [DataContract] public class Mapping : INotifyPropertyChanged {
        [DataMember] public Source Input=new Source();
        [DataMember] public string Name="";
        [DataMember] public bool Confirmed;
        [DataMember] public string Target="None";
        [DataMember] public int[] Keys=new int[0];
        [DataMember] public bool Locked;
        [DataMember] public bool Reserved;
        bool active;
        public string Physical {get {return Name.Length==0?Input.Caption:Name;}}
        public string Raw {get {return Input.Caption;}}
        public string Verification {get {return Locked || Reserved?"固定":Confirmed?"已对准":"待确认";}}
        public string OutputLabel {get {
            if(Reserved)return "方向轴 · 保留";
            if(Locked) return Target=="Up"?"鼠标上移 · 固定":Target=="Down"?"鼠标下移 · 固定":Target=="Left"?"鼠标左移 · 固定":"鼠标右移 · 固定";
            return Target=="MouseLeft"?"鼠标左键":Target=="MouseRight"?"鼠标右键":Target=="Keyboard"?KeyNames.Chord(Keys):"未分配";
        }}
        public bool Active {get {return active;} set {if(active==value)return;active=value;Notify();}}
        public event PropertyChangedEventHandler PropertyChanged;
        public void Notify() {if(PropertyChanged!=null) PropertyChanged(this,new PropertyChangedEventArgs(""));}
    }
    public static class KeyNames {
        public static string Name(int k) {
            if(k>=65 && k<=90 || k>=48 && k<=57) return ((char)k).ToString();
            if(k>=112 && k<=135) return "F"+(k-111);
            switch(k) {case 16:case 160:case 161:return "Shift";case 17:case 162:case 163:return "Ctrl";case 18:case 164:case 165:return "Alt";case 91:return "Win";case 13:return "Enter";case 32:return "Space";case 27:return "Esc";case 9:return "Tab";case 8:return "Backspace";case 46:return "Delete";case 37:return "←";case 38:return "↑";case 39:return "→";case 40:return "↓";case 36:return "Home";case 35:return "End";case 33:return "PageUp";case 34:return "PageDown";}
            return "VK "+k;
        }
        public static string Chord(int[] keys) {return keys==null || keys.Length==0?"键盘（未选择）":string.Join(" + ",keys.Select(Name));}
        public static bool Modifier(int k) {return k==16 || k==17 || k==18 || k==91 || k>=160 && k<=165;}
        public static int Canonical(int k) {return k==16?160:k==17?162:k==18?164:k;}
    }
    [DataContract] public class Profile {
        [DataMember] public int Version=2;
        [DataMember] public string VisualLayout="Xbox";
        public bool SetDpadSteps(string horizontal,string vertical) {
            int x,y;if(!int.TryParse(horizontal,out x)||!int.TryParse(vertical,out y)||x<1||x>5000||y<1||y>5000)return false;
            HorizontalStep=x;VerticalStep=y;return true;
        }
        [DataMember] public string DeviceId="",DeviceName="";
        [DataMember] public int HorizontalStep=30,VerticalStep=20,RepeatDelayMs=350,RepeatIntervalMs=90;
        [DataMember] public bool RepeatEnabled=true;
        [DataMember] public int StickX=0,StickY=1,SignX=1,SignY=1;
        [DataMember] public double CenterX,CenterY,Deadzone=0.16,Speed=800,Curve=1.6;
        [DataMember] public bool StickEnabled=true,StickCalibrated,NeutralConfirmed;
        [DataMember] public List<Mapping> Mappings=new List<Mapping>();
        public void Validate() {
            if(Version!=2 || string.IsNullOrEmpty(DeviceId) || Mappings==null || HorizontalStep<1 || HorizontalStep>5000 || VerticalStep<1 || VerticalStep>5000 || RepeatDelayMs<100 || RepeatDelayMs>5000 || RepeatIntervalMs<30 || RepeatIntervalMs>5000 || Deadzone<0.05 || Deadzone>0.5 || Speed<50 || Speed>3000 || Curve<1 || Curve>3 || StickX<0 || StickY<0 || StickX==StickY || Math.Abs(SignX)!=1 || Math.Abs(SignY)!=1 || CenterX< -32768 || CenterX>32767 || CenterY< -32768 || CenterY>32767)
                throw new InvalidDataException("配置参数无效。");
            var ids=new HashSet<string>();
            foreach(var m in Mappings) {
                if(m==null || m.Input==null || m.Input.Index<0 || m.Name==null || m.Keys==null || !ids.Add(m.Input.Id) || !new[]{"Button","Hat","Axis","Dpad"}.Contains(m.Input.Kind) || m.Input.Kind=="Dpad" && m.Input.Index>3 || !new[]{"None","MouseLeft","MouseRight","Keyboard","Up","Down","Left","Right"}.Contains(m.Target) || m.Keys.Any(k=>k<1 || k>254) || m.Target=="Keyboard" && m.Keys.Length==0 || m.Locked!=new[]{"Up","Down","Left","Right"}.Contains(m.Target) || m.Reserved && m.Target!="None" || m.Input.Kind=="Axis" && (m.Input.Threshold<4000 || m.Input.Threshold>65000 || Math.Abs(m.Input.Direction)!=1)) throw new InvalidDataException("按键映射无效。");
            }
        }
        public static string DirectoryPath {get {return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"profiles");}}
        public string FilePath {get {return Path.Combine(DirectoryPath,SafeId(DeviceId)+".json");}}
        static string SafeId(string s) {return new string(s.Where(c=>char.IsLetterOrDigit(c)||c=='-').ToArray());}
        public void Save() {
            Validate(); Directory.CreateDirectory(DirectoryPath); string tmp=FilePath+".tmp";
            using(var f=File.Create(tmp)) new DataContractJsonSerializer(typeof(Profile)).WriteObject(f,this);
            if(File.Exists(FilePath)) File.Replace(tmp,FilePath,FilePath+".bak"); else File.Move(tmp,FilePath);
        }
        public static Profile Load(string id) {
            string path=Path.Combine(DirectoryPath,SafeId(id)+".json"); if(!File.Exists(path)) return null;
            using(var f=File.OpenRead(path)) {var p=(Profile)new DataContractJsonSerializer(typeof(Profile)).ReadObject(f);p.Validate();if(p.DeviceId!=id)throw new InvalidDataException("手柄配置身份不匹配。");return p;}
        }
    }
    public class OutputEvent {public string Token;public bool Down;public override string ToString(){return Token+(Down?" down":" up");}}
    public class FrameResult {public int Dx,Dy; public List<OutputEvent> Events=new List<OutputEvent>();public double StickX,StickY;}
}
