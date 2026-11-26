using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace PadDesk {
    // Keys from remotes and Bluetooth/USB HID devices that Windows exposes as keyboards or consumer controls
    // (IR receivers, media remotes, presentation clickers, selfie shutters, page turners ...), read with Raw Input.
    // Each device is told apart by its HID identity, so a remote's Enter can be mapped without touching the real keyboard.
    // Raw Input only observes: the device's own key still reaches Windows as usual.
    public sealed class RemoteInput {
        const int WM_INPUT=0xFF,WM_INPUT_DEVICE_CHANGE=0xFE;
        const uint RID_INPUT=0x10000003,RIDI_DEVICENAME=0x20000007,RIDI_PREPARSEDDATA=0x20000005,RIDEV_INPUTSINK=0x100,RIDEV_DEVNOTIFY=0x2000;
        const int HIDP_STATUS_SUCCESS=0x00110000;
        [StructLayout(LayoutKind.Sequential)] struct RawDevice {public ushort Page,Usage;public uint Flags;public IntPtr Target;}
        [StructLayout(LayoutKind.Sequential)] struct RawHeader {public uint Type,Size;public IntPtr Device,WParam;}
        [StructLayout(LayoutKind.Sequential)] struct HidCaps {
            public ushort Usage,UsagePage,InputReportByteLength,OutputReportByteLength,FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray,SizeConst=17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes,NumberInputButtonCaps,NumberInputValueCaps,NumberInputDataIndices,NumberOutputButtonCaps,NumberOutputValueCaps,NumberOutputDataIndices,NumberFeatureButtonCaps,NumberFeatureValueCaps,NumberFeatureDataIndices;
        }
        [DllImport("user32.dll",SetLastError=true)] static extern bool RegisterRawInputDevices(RawDevice[] devices,uint count,uint size);
        [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr input,uint command,IntPtr data,ref uint size,uint headerSize);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern uint GetRawInputDeviceInfo(IntPtr device,uint command,IntPtr data,ref uint size);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr preparsed,ref HidCaps caps);
        [DllImport("hid.dll")] static extern int HidP_GetUsages(int reportType,ushort page,ushort link,[Out] ushort[] usages,ref uint length,IntPtr preparsed,IntPtr report,uint reportLength);
        [DllImport("hid.dll")] static extern int HidP_GetUsageValue(int reportType,ushort page,ushort link,ushort usage,out uint value,IntPtr preparsed,IntPtr report,uint reportLength);
        [DllImport("hid.dll")] static extern bool HidD_GetProductString(IntPtr file,byte[] buffer,uint length);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateFile(string name,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

        sealed class Info {public string Id,Name;public IntPtr Preparsed;public ushort Page,Usage;}
        readonly Dictionary<IntPtr,Info> devices=new Dictionary<IntPtr,Info>();
        readonly HashSet<string> down=new HashSet<string>(),pressedSinceRead=new HashSet<string>();
        public bool Registered {get;private set;}
        // Raised for every new press, used by the learning dialog.
        public event Action<Source> Pressed;

        public bool Register(IntPtr hwnd) {
            var list=new[]{
                new RawDevice {Page=0x01,Usage=0x06,Flags=RIDEV_INPUTSINK|RIDEV_DEVNOTIFY,Target=hwnd},   // keyboards and keyboard-style remotes
                new RawDevice {Page=0x0C,Usage=0x01,Flags=RIDEV_INPUTSINK|RIDEV_DEVNOTIFY,Target=hwnd},   // consumer control: media and volume keys
                new RawDevice {Page=0xFFBC,Usage=0x88,Flags=RIDEV_INPUTSINK|RIDEV_DEVNOTIFY,Target=hwnd}, // Windows Media Center IR remotes
            };
            Registered=RegisterRawInputDevices(list,(uint)list.Length,(uint)Marshal.SizeOf(typeof(RawDevice)));
            return Registered;
        }

        // Call from the window procedure; returns true when the message was raw input.
        public bool Handle(int msg,IntPtr wParam,IntPtr lParam) {
            if(msg==WM_INPUT_DEVICE_CHANGE){if(wParam.ToInt64()==2)Forget(lParam);return true;}
            if(msg!=WM_INPUT)return false;
            uint headerSize=(uint)Marshal.SizeOf(typeof(RawHeader)),size=0;
            GetRawInputData(lParam,RID_INPUT,IntPtr.Zero,ref size,headerSize);if(size==0 || size>65536)return true;
            IntPtr buffer=Marshal.AllocHGlobal((int)size);
            try {
                if(GetRawInputData(lParam,RID_INPUT,buffer,ref size,headerSize)!=size)return true;
                var header=(RawHeader)Marshal.PtrToStructure(buffer,typeof(RawHeader));
                if(header.Device==IntPtr.Zero)return true; // synthesized input (including PadDesk's own) has no device
                var info=Describe(header.Device);if(info==null)return true;
                IntPtr data=new IntPtr(buffer.ToInt64()+headerSize);
                if(header.Type==1)Keyboard(info,data);
                else if(header.Type==2)Hid(info,data,size-headerSize);
            } catch {} finally {Marshal.FreeHGlobal(buffer);}
            return true;
        }

        void Keyboard(Info info,IntPtr data) {
            int make=(ushort)Marshal.ReadInt16(data,0),flags=(ushort)Marshal.ReadInt16(data,2),vk=(ushort)Marshal.ReadInt16(data,6);
            if(vk==0 || vk>=255)return;
            bool e0=(flags&2)!=0,up=(flags&1)!=0;
            if(vk==16)vk=make==0x36?161:160;else if(vk==17)vk=e0?163:162;else if(vk==18)vk=e0?165:164;
            Set(info,RemoteKeys.KeyboardPage,vk,!up);
        }

        void Hid(Info info,IntPtr data,uint length) {
            if(info.Preparsed==IntPtr.Zero || length<8)return;
            uint reportSize=(uint)Marshal.ReadInt32(data,0),count=(uint)Marshal.ReadInt32(data,4);
            if(reportSize==0 || 8+(long)reportSize*count>length)return;
            for(uint r=0;r<count;r++) {
                var usages=new ushort[64];uint n=(uint)usages.Length;
                IntPtr report=new IntPtr(data.ToInt64()+8+reportSize*r);
                HashSet<int> now;uint value;
                if(HidP_GetUsages(0,info.Page,0,usages,ref n,info.Preparsed,report,reportSize)==HIDP_STATUS_SUCCESS)now=new HashSet<int>(usages.Take((int)n).Select(u=>(int)u).Where(u=>u!=0));
                // Vendor pages such as the Media Center remote report the button code as a value instead of button usages.
                else if(HidP_GetUsageValue(0,info.Page,0,info.Usage,out value,info.Preparsed,report,reportSize)==HIDP_STATUS_SUCCESS){now=new HashSet<int>();if(value!=0 && value<65536)now.Add((int)value);}
                else continue;
                string prefix="Remote:"+info.Id+":"+info.Page+":";
                foreach(string id in down.Where(d=>d.StartsWith(prefix)).ToArray()){int code;if(int.TryParse(id.Substring(prefix.Length),out code) && !now.Contains(code))Set(info,info.Page,code,false);}
                foreach(int code in now)Set(info,info.Page,code,true);
            }
        }

        void Set(Info info,int page,int code,bool pressed) {
            var source=new Source {Kind="Remote",Device=info.Id,DeviceName=info.Name,Mask=page,Index=code};
            string id=source.Id;
            if(pressed){if(down.Add(id)){pressedSinceRead.Add(id);var handler=Pressed;if(handler!=null)handler(source);}}
            else down.Remove(id);
        }

        void Forget(IntPtr handle) {
            Info info;if(!devices.TryGetValue(handle,out info))return;
            string prefix="Remote:"+info.Id+":";down.RemoveWhere(d=>d.StartsWith(prefix));
            if(info.Preparsed!=IntPtr.Zero)Marshal.FreeHGlobal(info.Preparsed);devices.Remove(handle);
        }

        // Current keys, plus taps that went down and up since the previous call (null when there were none).
        public HashSet<string> Read(out HashSet<string> partial) {
            var active=new HashSet<string>(down);
            var taps=pressedSinceRead.Where(t=>!down.Contains(t)).ToArray();pressedSinceRead.Clear();
            partial=null;if(taps.Length>0){partial=new HashSet<string>(active);partial.UnionWith(taps);}
            return active;
        }

        public void Clear(){down.Clear();pressedSinceRead.Clear();}

        Info Describe(IntPtr handle) {
            Info info;if(devices.TryGetValue(handle,out info))return info;
            string path=DeviceName(handle);if(path.Length==0)return null;
            info=new Info {Id=StableId(path),Name=Product(path)};
            uint size=0;GetRawInputDeviceInfo(handle,RIDI_PREPARSEDDATA,IntPtr.Zero,ref size);
            if(size>0 && size<1<<20) {
                info.Preparsed=Marshal.AllocHGlobal((int)size);
                var caps=new HidCaps {Reserved=new ushort[17]};
                if(GetRawInputDeviceInfo(handle,RIDI_PREPARSEDDATA,info.Preparsed,ref size)==uint.MaxValue || HidP_GetCaps(info.Preparsed,ref caps)!=HIDP_STATUS_SUCCESS){Marshal.FreeHGlobal(info.Preparsed);info.Preparsed=IntPtr.Zero;}
                else {info.Page=caps.UsagePage;info.Usage=caps.Usage;}
            }
            if(info.Name.Length==0)info.Name=FallbackName(info.Id);
            devices[handle]=info;return info;
        }

        static string DeviceName(IntPtr handle) {
            uint chars=0;GetRawInputDeviceInfo(handle,RIDI_DEVICENAME,IntPtr.Zero,ref chars);if(chars==0 || chars>4096)return "";
            IntPtr buffer=Marshal.AllocHGlobal((int)chars*2+2);
            try{if(GetRawInputDeviceInfo(handle,RIDI_DEVICENAME,buffer,ref chars)==uint.MaxValue)return "";return Marshal.PtrToStringUni(buffer)??"";}
            finally{Marshal.FreeHGlobal(buffer);}
        }

        // "\\?\HID#VID_046D&PID_C52B&MI_01&Col02#7&2a&0&0001#{...}" -> "HID#VID_046D&PID_C52B&MI_01&COL02".
        // The instance part (which changes with the USB port or Bluetooth pairing) is dropped so mappings survive reconnects.
        public static string StableId(string path) {
            string p=path.StartsWith(@"\\?\")?path.Substring(4):path;string[] parts=p.Split('#');
            string id=parts.Length>=2?parts[0]+"#"+parts[1]:p;
            id=new string(id.ToUpperInvariant().Where(c=>c>32 && c!=':').ToArray());
            return id.Length>120?id.Substring(0,120):id;
        }

        static string FallbackName(string id) {
            string u=id.ToUpperInvariant();int v=u.IndexOf("VID"),pid=u.IndexOf("PID");
            if(v>=0 && pid>v)return (u.Contains("00001124-0000-1000-8000-00805F9B34FB") || u.StartsWith("BTH")?"蓝牙设备 ":"HID 设备 ")+u.Substring(v,Math.Min(u.Length-v,8)).Replace("&","_")+" "+u.Substring(pid,Math.Min(u.Length-pid,8)).Replace("&","_");
            return "HID 设备";
        }

        static string Product(string path) {
            IntPtr file=CreateFile(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero);if(file==new IntPtr(-1))return "";
            try{var buffer=new byte[254];if(!HidD_GetProductString(file,buffer,(uint)buffer.Length))return "";return Encoding.Unicode.GetString(buffer).TrimEnd('\0').Trim();}
            catch{return "";}finally{CloseHandle(file);}
        }
    }
}
