using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace PadDesk {
    public static class Sdl {
        const string D="SDL2.dll";
        [StructLayout(LayoutKind.Sequential)] public struct Bind {public int Type,Index,Mask;}
        [StructLayout(LayoutKind.Sequential)] public struct Guid16 {public ulong A,B;}
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)] public static extern int SDL_SetHint(string n,string v);
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)] public static extern int SDL_Init(uint f);
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)] public static extern void SDL_Quit();
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)] public static extern int SDL_NumJoysticks();
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)] public static extern int SDL_IsGameController(int i);
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr SDL_GameControllerOpen(int i);
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)] public static extern void SDL_GameControllerClose(IntPtr p);
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr SDL_GameControllerGetJoystick(IntPtr p);
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)] public static extern Bind SDL_GameControllerGetBindForButton(IntPtr p,int i);
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)] public static extern Bind SDL_GameControllerGetBindForAxis(IntPtr p,int i);
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)] public static extern byte SDL_GameControllerGetButton(IntPtr p,int i);
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr SDL_JoystickOpen(int i);
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)] public static extern void SDL_JoystickClose(IntPtr p);
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)] public static extern int SDL_JoystickGetAttached(IntPtr p);
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)] public static extern IntPtr SDL_JoystickName(IntPtr p);
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)] public static extern Guid16 SDL_JoystickGetGUID(IntPtr p);
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)] public static extern void SDL_JoystickGetGUIDString(Guid16 id,StringBuilder text,int size);
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)] public static extern int SDL_JoystickNumAxes(IntPtr p);
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)] public static extern int SDL_JoystickNumButtons(IntPtr p);
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)] public static extern int SDL_JoystickNumHats(IntPtr p);
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)] public static extern short SDL_JoystickGetAxis(IntPtr p,int i);
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)] public static extern byte SDL_JoystickGetButton(IntPtr p,int i);
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)] public static extern byte SDL_JoystickGetHat(IntPtr p,int i);
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)] public static extern void SDL_JoystickUpdate();
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)] public static extern void SDL_PumpEvents();
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)] public static extern void SDL_FlushEvents(uint a,uint b);
        public static string Utf8(IntPtr p) {if(p==IntPtr.Zero)return "";int n=0;while(Marshal.ReadByte(p,n)!=0)n++;byte[] b=new byte[n];Marshal.Copy(p,b,0,n);return Encoding.UTF8.GetString(b);}
    }
    public sealed class Device : IDisposable {
        IntPtr pad,joy;string name="",id="";double nextScan;
        public Device() {
            Sdl.SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS","1");Sdl.SDL_SetHint("SDL_GAMECONTROLLER_USE_BUTTON_LABELS","1");
            if(Sdl.SDL_Init(0x2200)!=0)throw new Exception("无法初始化 SDL 手柄库。");
        }
        void Close() {if(pad!=IntPtr.Zero)Sdl.SDL_GameControllerClose(pad);else if(joy!=IntPtr.Zero)Sdl.SDL_JoystickClose(joy);pad=joy=IntPtr.Zero;name=id="";}
        public Snapshot Read(double now) {
            Sdl.SDL_PumpEvents();Sdl.SDL_JoystickUpdate();Sdl.SDL_FlushEvents(0,65535);
            if(joy!=IntPtr.Zero && Sdl.SDL_JoystickGetAttached(joy)==0){Close();nextScan=0;}
            if(joy==IntPtr.Zero && now>=nextScan) {
                nextScan=now+1;
                for(int i=0;i<Sdl.SDL_NumJoysticks();i++) {
                    if(Sdl.SDL_IsGameController(i)!=0){pad=Sdl.SDL_GameControllerOpen(i);if(pad!=IntPtr.Zero)joy=Sdl.SDL_GameControllerGetJoystick(pad);}else joy=Sdl.SDL_JoystickOpen(i);
                    if(joy==IntPtr.Zero)continue;
                    if(Sdl.SDL_JoystickNumButtons(joy)==0 && Sdl.SDL_JoystickNumAxes(joy)<2){Close();continue;}
                    name=Sdl.Utf8(Sdl.SDL_JoystickName(joy));var text=new StringBuilder(33);Sdl.SDL_JoystickGetGUIDString(Sdl.SDL_JoystickGetGUID(joy),text,33);id=text.ToString();break;
                }
            }
            var s=new Snapshot {Connected=joy!=IntPtr.Zero,DeviceId=id,Name=name};if(!s.Connected)return s;
            Sdl.SDL_JoystickUpdate();
            s.Buttons=Enumerable.Range(0,Sdl.SDL_JoystickNumButtons(joy)).Select(i=>Sdl.SDL_JoystickGetButton(joy,i)!=0).ToArray();
            s.Axes=Enumerable.Range(0,Sdl.SDL_JoystickNumAxes(joy)).Select(i=>Sdl.SDL_JoystickGetAxis(joy,i)).ToArray();
            s.Hats=Enumerable.Range(0,Sdl.SDL_JoystickNumHats(joy)).Select(i=>Sdl.SDL_JoystickGetHat(joy,i)).ToArray();
            if(pad!=IntPtr.Zero)s.Dpad=Enumerable.Range(11,4).Select(i=>Sdl.SDL_GameControllerGetButton(pad,i)!=0).ToArray();return s;
        }
        Source Convert(Sdl.Bind b,Snapshot s) {
            if(b.Type==1)return new Source {Index=b.Index};
            if(b.Type==3)return new Source {Kind="Hat",Index=b.Index,Mask=b.Mask};
            if(b.Type==2 && b.Index<s.Axes.Length)return new Source {Kind="Axis",Index=b.Index,Rest=s.Axes[b.Index]};
            return null;
        }
        public Profile CreateProfile(Snapshot s) {
            var p=new Profile {DeviceId=s.DeviceId,DeviceName=s.Name};
            if(pad!=IntPtr.Zero) {
                var x=Sdl.SDL_GameControllerGetBindForAxis(pad,0);var y=Sdl.SDL_GameControllerGetBindForAxis(pad,1);
                if(x.Type==2)p.StickX=x.Index;if(y.Type==2)p.StickY=y.Index;
            }
            if(p.StickX<s.Axes.Length)p.CenterX=s.Axes[p.StickX];if(p.StickY<s.Axes.Length)p.CenterY=s.Axes[p.StickY];
            for(int i=0;i<s.Buttons.Length;i++)p.Mappings.Add(new Mapping {Input=new Source {Index=i}});
            for(int i=0;i<s.Hats.Length;i++)foreach(int mask in new[]{1,2,4,8})p.Mappings.Add(new Mapping {Input=new Source {Kind="Hat",Index=i,Mask=mask}});
            for(int i=0;i<s.Axes.Length;i++) {
                if(i==p.StickX || i==p.StickY)continue;
                foreach(int sign in new[]{1,-1})if((sign>0?32767-s.Axes[i]:s.Axes[i]+32768)>14000)p.Mappings.Add(new Mapping {Input=new Source {Kind="Axis",Index=i,Direction=sign,Rest=s.Axes[i]}});
            }
            if(pad!=IntPtr.Zero) {
                string[] labels={"A","B","X","Y","Home","返回 / Select","开始 / Start","左摇杆按下","右摇杆按下","L1 / LB","R1 / RB","方向上","方向下","方向左","方向右","额外键","Capture"};
                for(int i=0;i<labels.Length;i++) {
                    var bind=Sdl.SDL_GameControllerGetBindForButton(pad,i);
                    Source source=Convert(bind,s);if(source==null)continue;
                    if(i>=11 && i<=14 && bind.Type==2) {
                        foreach(var reserved in p.Mappings.Where(m=>m.Input.Kind=="Axis" && m.Input.Index==bind.Index)){reserved.Reserved=true;reserved.Target="None";}
                        source=new Source {Kind="Dpad",Index=i-11};
                    }
                    var row=p.Mappings.FirstOrDefault(m=>m.Input.Id==source.Id);if(row==null){row=new Mapping {Input=source};p.Mappings.Add(row);}
                    row.Name=labels[i];
                    if(i>=11 && i<=14){row.Locked=true;row.Confirmed=true;row.Target=new[]{"Up","Down","Left","Right"}[i-11];}
                }
            } else if(s.Hats.Length>0) {
                foreach(var m in p.Mappings.Where(m=>m.Input.Kind=="Hat" && m.Input.Index==0)) {m.Locked=true;m.Confirmed=true;m.Name="方向";m.Target=m.Input.Mask==1?"Up":m.Input.Mask==2?"Right":m.Input.Mask==4?"Down":"Left";}
            }
            return p;
        }
        public void Dispose(){Close();Sdl.SDL_Quit();}
    }
}
