using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace PadDesk {
    public sealed class NativeOutput : IDisposable {
        [StructLayout(LayoutKind.Sequential)] public struct Point {public int X,Y;}
        [StructLayout(LayoutKind.Sequential)] struct Mouse {public int x,y;public uint data,flags,time;public UIntPtr extra;}
        [StructLayout(LayoutKind.Sequential)] struct Keyboard {public ushort vk,scan;public uint flags,time;public UIntPtr extra;}
        [StructLayout(LayoutKind.Explicit)] struct Union {[FieldOffset(0)] public Mouse mouse;[FieldOffset(0)]public Keyboard key;}
        [StructLayout(LayoutKind.Sequential)] struct Input {public uint type;public Union data;}
        [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint n,Input[] data,int size);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point p);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code,uint type);
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr p);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        readonly HashSet<string> owned=new HashSet<string>();
        public static void EnableDpi(){try{SetProcessDpiAwarenessContext(new IntPtr(-4));}catch(EntryPointNotFoundException){SetProcessDPIAware();}}
        static Input Make(string token,bool down) {
            if(token.StartsWith("M:"))return new Input {data=new Union {mouse=new Mouse {flags=token=="M:L"?(down?2u:4u):(down?8u:16u)}}};
            int vk=int.Parse(token.Substring(2));uint scan=MapVirtualKey((uint)vk,4);
            bool useScan=scan!=0 && (scan>>8)!=0xE1;
            return new Input {type=1,data=new Union {key=new Keyboard {vk=(ushort)(useScan?0:vk),scan=(ushort)(scan&255),flags=(down?0u:2u)|(useScan?8u:0u)|((scan>>8)==0xE0?1u:0u)}}};
        }
        void Emit(string token,bool down) {
            if(SendInput(1,new[]{Make(token,down)},Marshal.SizeOf(typeof(Input)))!=1)throw new Exception("系统阻止了输入。请检查目标窗口是否以管理员身份运行。");
            if(down)owned.Add(token);else owned.Remove(token);
        }
        public void Apply(FrameResult r) {
            try {
                if(r.Dx!=0 || r.Dy!=0){Point p;if(!GetCursorPos(out p)||!SetCursorPos(p.X+r.Dx,p.Y+r.Dy))throw new Exception("无法移动鼠标。");}
                foreach(var e in r.Events)Emit(e.Token,e.Down);
            }catch {ReleaseAll();throw;}
        }
        public void ReleaseAll() {
            foreach(string token in owned.OrderBy(t=>t.StartsWith("K:") && KeyNames.Modifier(int.Parse(t.Substring(2)))?1:0).ToArray())try{Emit(token,false);}catch{}
        }
        public void Dispose(){ReleaseAll();}
    }
}
