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
        // Remote / Bluetooth HID keys currently held, by Source.Id ("Remote:<device>:<page>:<code>").
        public HashSet<string> Remote=new HashSet<string>();
        public Snapshot Clone() { return new Snapshot {Connected=Connected,DeviceId=DeviceId,Name=Name,RightStickX=RightStickX,RightStickY=RightStickY,Buttons=(bool[])Buttons.Clone(),Axes=(short[])Axes.Clone(),Hats=(byte[])Hats.Clone(),Dpad=(bool[])Dpad.Clone(),Remote=new HashSet<string>(Remote)}; }
    }
    [DataContract] public class Source {
        [DataMember] public string Kind="Button";
        [DataMember] public int Index,Direction=1,Mask;
        [DataMember] public double Rest,Threshold=14000;
        // Remote sources: Device is a stable HID identity (VID/PID/collection), Mask the HID usage page
        // (RemoteKeys.KeyboardPage for keyboard-style remotes, 0x0C consumer control, ...), Index the key/usage code.
        [DataMember] public string Device="",DeviceName="";
        [OnDeserialized] void Defaults(StreamingContext c){if(Device==null)Device="";if(DeviceName==null)DeviceName="";if(Kind==null)Kind="Button";}
        public string Id {get {return Kind=="Remote"?"Remote:"+Device+":"+Mask+":"+Index:Kind+":"+Index+":"+(Kind=="Hat"?Mask:Direction);}}
        public string Caption {get {return Kind=="Remote"?RemoteKeys.Caption(Mask,Index)+" · "+(DeviceName.Length>0?DeviceName:"外设"):Kind=="Dpad"?"方向键 "+new[]{"↑","↓","←","→"}[Index]:Kind=="Button"?"按钮 "+(Index+1).ToString("00"):Kind=="Hat"?"方向帽 "+(Index+1)+" / "+Mask:"轴 "+(Index+1)+(Direction>0?" +":" −");}}
        public bool Active(Snapshot s) {
            if(!s.Connected) return false;
            if(Kind=="Remote")return s.Remote!=null && s.Remote.Contains(Id);
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
        // Target "Sequence": each step is a chord tapped in order (down, then up) on the rising edge.
        [DataMember] public int[][] Steps=new int[0][];
        [DataMember] public string SequenceText="",SequenceMode="Hog";
        [OnDeserialized] void Defaults(StreamingContext c){if(Steps==null)Steps=new int[0][];if(SequenceText==null)SequenceText="";if(SequenceMode==null)SequenceMode="Hog";if(Keys==null)Keys=new int[0];if(Name==null)Name="";if(Target==null)Target="None";}
        bool active;
        public string Physical {get {return Name.Length==0?Input.Caption:Name;}}
        public string Raw {get {return Input.Caption;}}
        public string Verification {get {return Locked || Reserved?"固定":Confirmed?"已对准":"待确认";}}
        public string OutputLabel {get {
            if(Reserved)return "方向轴 · 保留";
            if(Locked) return Target=="Up"?"鼠标上移 · 固定":Target=="Down"?"鼠标下移 · 固定":Target=="Left"?"鼠标左移 · 固定":"鼠标右移 · 固定";
            if(Target=="Media")return "媒体 · "+(Keys.Length==1?KeyNames.Name(Keys[0]):"未选择");
            if(Target=="Sequence")return (SequenceMode=="Hog"?"HOG · ":"序列 · ")+(SequenceText.Length>0?SequenceText:Sequences.Describe(Steps));
            return Target=="MouseLeft"?"鼠标左键":Target=="MouseRight"?"鼠标右键":Target=="Keyboard"?KeyNames.Chord(Keys):"未分配";
        }}
        public bool Active {get {return active;} set {if(active==value)return;active=value;Notify();}}
        public event PropertyChangedEventHandler PropertyChanged;
        public void Notify() {if(PropertyChanged!=null) PropertyChanged(this,new PropertyChangedEventArgs(""));}
    }
    public static class KeyNames {
        static readonly Dictionary<int,string> names=new Dictionary<int,string> {
            {8,"Backspace"},{9,"Tab"},{13,"Enter"},{16,"Shift"},{17,"Ctrl"},{18,"Alt"},{19,"Pause"},{20,"CapsLock"},{27,"Esc"},{32,"Space"},
            {33,"PageUp"},{34,"PageDown"},{35,"End"},{36,"Home"},{37,"←"},{38,"↑"},{39,"→"},{40,"↓"},{44,"PrintScreen"},{45,"Insert"},{46,"Delete"},
            {91,"Win"},{92,"右Win"},{93,"Menu"},{95,"Sleep"},{144,"NumLock"},{145,"ScrollLock"},
            {160,"Shift"},{161,"右Shift"},{162,"Ctrl"},{163,"右Ctrl"},{164,"Alt"},{165,"右Alt"},
            {166,"浏览器后退"},{167,"浏览器前进"},{168,"浏览器刷新"},{170,"浏览器搜索"},{172,"浏览器主页"},
            {173,"静音"},{174,"音量−"},{175,"音量+"},{176,"下一曲"},{177,"上一曲"},{178,"停止播放"},{179,"播放 / 暂停"},
            {180,"邮件"},{181,"选择媒体"},{182,"启动应用 1"},{183,"启动应用 2"},
            {186,";"},{187,"="},{188,","},{189,"-"},{190,"."},{191,"/"},{192,"`"},{219,"["},{220,"\\"},{221,"]"},{222,"'"}
        };
        // Media / system keys offered by the "媒体键" output, in menu order.
        public static readonly int[] Media={175,174,173,179,176,177,178,166,167,168,172};
        public static bool IsMedia(int k){return k>=166 && k<=183;}
        public static string Name(int k) {
            if(k>=65 && k<=90 || k>=48 && k<=57) return ((char)k).ToString();
            if(k>=112 && k<=135) return "F"+(k-111);
            if(k>=96 && k<=105) return "Num"+(k-96);
            string n;if(names.TryGetValue(k,out n))return n;
            return "VK "+k;
        }
        public static string Chord(int[] keys) {return keys==null || keys.Length==0?"键盘（未选择）":string.Join(" + ",keys.Select(Name));}
        public static bool Modifier(int k) {return k==16 || k==17 || k==18 || k==91 || k==92 || k>=160 && k<=165;}
        public static int Canonical(int k) {return k==16?160:k==17?162:k==18?164:k;}
        // Parses one key name as typed in a sequence ("Ctrl", "Tab", "VolUp", "F5", "←", "VK 173" ...).
        public static int Parse(string text) {
            string t=(text??"").Trim();if(t.Length==0)return 0;string u=t.ToUpperInvariant();
            if(u.Length==1 && (u[0]>='A' && u[0]<='Z' || u[0]>='0' && u[0]<='9'))return u[0];
            if(u.Length>=2 && u[0]=='F'){int f;if(int.TryParse(u.Substring(1),out f) && f>=1 && f<=24)return 111+f;}
            if(u.StartsWith("VK")){int v;if(int.TryParse(u.Substring(2).Trim(),out v) && v>=1 && v<=254)return v;}
            switch(u) {
                case "CTRL":case "CONTROL":case "LCTRL":return 162;case "RCTRL":return 163;
                case "ALT":case "LALT":case "OPTION":return 164;case "RALT":case "ALTGR":return 165;
                case "SHIFT":case "LSHIFT":return 160;case "RSHIFT":return 161;
                case "WIN":case "LWIN":case "META":case "SUPER":case "CMD":case "COMMAND":return 91;case "RWIN":return 92;
                case "TAB":return 9;case "ENTER":case "RETURN":return 13;case "ESC":case "ESCAPE":return 27;case "SPACE":return 32;
                case "BACKSPACE":case "BKSP":return 8;case "DELETE":case "DEL":return 46;case "INSERT":case "INS":return 45;
                case "HOME":return 36;case "END":return 35;case "PAGEUP":case "PGUP":return 33;case "PAGEDOWN":case "PGDN":return 34;
                case "LEFT":case "←":return 37;case "UP":case "↑":return 38;case "RIGHT":case "→":return 39;case "DOWN":case "↓":return 40;
                case "VOLUP":case "VOLUMEUP":case "音量+":return 175;case "VOLDOWN":case "VOLUMEDOWN":case "音量-":case "音量−":return 174;
                case "MUTE":case "静音":return 173;case "PLAY":case "PAUSE":case "PLAYPAUSE":case "播放":return 179;
                case "NEXT":case "下一曲":return 176;case "PREV":case "PREVIOUS":case "上一曲":return 177;case "STOP":return 178;
                case "PRINTSCREEN":case "PRTSC":return 44;case "MENU":case "APPS":return 93;case "CAPSLOCK":return 20;
            }
            foreach(var pair in names)if(string.Equals(pair.Value,t,StringComparison.OrdinalIgnoreCase))return pair.Key;
            return 0;
        }
    }
    public static class Sequences {
        public const int MaxSteps=64,MaxChord=6;
        // HOG hotkeys: letters A–Z tapped in order; '*' taps left Ctrl and '+' taps right Ctrl (HOG's modifier prefixes on Windows).
        public static int[][] ParseHog(string text,out string error) {
            error=null;var steps=new List<int[]>();
            foreach(char ch in (text??"").Trim()) {
                char c=char.ToUpperInvariant(ch);
                if(c>='A' && c<='Z')steps.Add(new[]{(int)c});
                else if(c=='*')steps.Add(new[]{162});
                else if(c=='+')steps.Add(new[]{163});
                else if(char.IsWhiteSpace(c))continue;
                else {error="HOG 热键只能包含字母 A–Z、* 和 +。";return null;}
            }
            if(steps.Count==0){error="请输入 HOG 热键，例如 JFQR 或 *JFYX。";return null;}
            if(steps.Count>MaxSteps){error="序列最多 "+MaxSteps+" 步。";return null;}
            return steps.ToArray();
        }
        // Key sequences: steps separated by spaces or commas, keys inside a step joined with '+', e.g. "Win+Ctrl+Right Enter".
        public static int[][] ParseKeys(string text,out string error) {
            error=null;var steps=new List<int[]>();
            foreach(string part in (text??"").Split(new[]{' ',',','，','\t'},StringSplitOptions.RemoveEmptyEntries)) {
                var chord=new List<int>();
                foreach(string name in part.Split(new[]{'+'},StringSplitOptions.RemoveEmptyEntries)) {
                    int vk=KeyNames.Parse(name);if(vk==0){error="无法识别按键“"+name+"”。";return null;}
                    vk=KeyNames.Canonical(vk);if(!chord.Contains(vk))chord.Add(vk);
                }
                if(chord.Count==0)continue;if(chord.Count>MaxChord){error="每一步最多同时按 "+MaxChord+" 个键。";return null;}
                steps.Add(chord.ToArray());
            }
            if(steps.Count==0){error="请输入按键序列，例如 Win+Ctrl+Right 或 Ctrl+C Ctrl+V。";return null;}
            if(steps.Count>MaxSteps){error="序列最多 "+MaxSteps+" 步。";return null;}
            return steps.ToArray();
        }
        public static int[][] Parse(string mode,string text,out string error){return mode=="Hog"?ParseHog(text,out error):ParseKeys(text,out error);}
        public static string Describe(int[][] steps){return steps==null || steps.Length==0?"未设置":string.Join("  ",steps.Select(KeyNames.Chord));}
        public static bool Valid(int[][] steps){return steps!=null && steps.Length>=1 && steps.Length<=MaxSteps && steps.All(st=>st!=null && st.Length>=1 && st.Length<=MaxChord && st.All(k=>k>=1 && k<=254));}
    }
    // Reads hotkeys from the HOG configuration (github.com/XYoung1201/HOG) so a controller button can trigger them.
    public static class HogConfig {
        public struct Entry {public string Hotkey,Action;public override string ToString(){return Hotkey+"   ·   "+Action;}}
        public static List<Entry> Parse(IEnumerable<string> lines) {
            var list=new List<Entry>();
            foreach(string raw in lines) {
                string line=(raw??"").Trim();if(line.Length==0 || line.StartsWith("//"))continue;
                int space=line.IndexOf(' ');if(space<=0)continue;
                string hotkey=line.Substring(0,space),action=line.Substring(space+1).Trim();int comment=action.IndexOf(" //");if(comment>0)action=action.Substring(0,comment).Trim();
                string error;if(Sequences.ParseHog(hotkey,out error)==null || action.Length==0)continue;
                list.Add(new Entry {Hotkey=hotkey.ToUpperInvariant(),Action=action});
            }
            return list;
        }
        // Windows HOG keeps the active configuration path in C:\ProgramData\HOG\config, defaulting to HOG.conf beside it.
        public static string FindPath() {
            string dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"HOG");
            try{string pointer=Path.Combine(dir,"config");if(File.Exists(pointer)){string path=File.ReadAllText(pointer,System.Text.Encoding.Default).Trim();if(path.Length>0 && File.Exists(path))return path;}}catch{}
            string fallback=Path.Combine(dir,"HOG.conf");return File.Exists(fallback)?fallback:null;
        }
        public static List<Entry> Load(out string path) {
            path=FindPath();if(path==null)return new List<Entry>();
            try{return Parse(File.ReadAllLines(path,System.Text.Encoding.UTF8));}catch{return new List<Entry>();}
        }
    }
    // Names for keys coming from remotes and Bluetooth HID devices.
    public static class RemoteKeys {
        public const int KeyboardPage=0x07,ConsumerPage=0x0C,McePage=0xFFBC;
        public static string Caption(int page,int code) {
            if(page==KeyboardPage)return "按键 "+KeyNames.Name(code);
            if(page==ConsumerPage)switch(code){
                case 0xE9:return "音量+";case 0xEA:return "音量−";case 0xE2:return "静音";case 0xCD:return "播放 / 暂停";case 0xB0:return "播放";case 0xB1:return "暂停";
                case 0xB5:return "下一曲";case 0xB6:return "上一曲";case 0xB7:return "停止";case 0xB3:return "快进";case 0xB4:return "快退";case 0x30:return "电源";
                case 0x40:return "菜单";case 0x41:return "菜单确认";case 0x42:return "菜单上";case 0x43:return "菜单下";case 0x44:return "菜单左";case 0x45:return "菜单右";case 0x46:return "菜单返回";
                case 0x9C:return "频道+";case 0x9D:return "频道−";case 0x223:return "主页";case 0x224:return "返回";case 0x225:return "前进";case 0x221:return "搜索";
            }
            if(page==McePage)return "MCE 键 "+code;
            return "HID "+page.ToString("X2")+":"+code.ToString("X2");
        }
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
                if(m==null || m.Input==null || m.Input.Index<0 || m.Name==null || m.Keys==null || !ids.Add(m.Input.Id) || !new[]{"Button","Hat","Axis","Dpad","Remote"}.Contains(m.Input.Kind) || m.Input.Kind=="Dpad" && m.Input.Index>3 || m.Input.Kind=="Remote" && string.IsNullOrEmpty(m.Input.Device) || !new[]{"None","MouseLeft","MouseRight","Keyboard","Media","Sequence","Up","Down","Left","Right"}.Contains(m.Target) || m.Keys.Any(k=>k<1 || k>254) || m.Target=="Keyboard" && m.Keys.Length==0 || m.Target=="Media" && (m.Keys.Length!=1 || !KeyNames.IsMedia(m.Keys[0])) || m.Target=="Sequence" && !Sequences.Valid(m.Steps) || m.Locked!=new[]{"Up","Down","Left","Right"}.Contains(m.Target) || m.Reserved && m.Target!="None" || m.Input.Kind=="Axis" && (m.Input.Threshold<4000 || m.Input.Threshold>65000 || Math.Abs(m.Input.Direction)!=1)) throw new InvalidDataException("按键映射无效。");
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
