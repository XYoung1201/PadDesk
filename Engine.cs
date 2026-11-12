using System;
using System.Collections.Generic;
using System.Linq;

namespace PadDesk {
    public sealed class InputEngine {
        HashSet<string> held=new HashSet<string>();
        bool armed; string device=""; double lastTime,residualX,residualY,nextX,nextY; int oldX,oldY;
        static int Priority(string token) {if(!token.StartsWith("K:"))return 2;return KeyNames.Modifier(int.Parse(token.Substring(2)))?0:1;}
        void Change(HashSet<string> desired,FrameResult result) {
            foreach(string token in held.Except(desired).OrderByDescending(Priority).ThenBy(t=>t)) result.Events.Add(new OutputEvent {Token=token,Down=false});
            foreach(string token in desired.Except(held).OrderBy(Priority).ThenBy(t=>t)) result.Events.Add(new OutputEvent {Token=token,Down=true});
            held=desired;
        }
        void Disarm() {armed=false;oldX=oldY=0;nextX=nextY=0;residualX=residualY=0;}
        int Step(int direction,ref int previous,ref double next,double now,Profile p) {
            if(direction==0){previous=0;next=0;return 0;}
            if(previous!=direction){previous=direction;next=now+p.RepeatDelayMs/1000.0;return direction;}
            if(p.RepeatEnabled && now>=next){next=now+p.RepeatIntervalMs/1000.0;return direction;} return 0;
        }
        static double Axis(Snapshot s,int index,double center,int sign) {
            if(index<0 || index>=s.Axes.Length)return 0;
            double delta=s.Axes[index]-center;
            double span=delta>=0?32767-center:32768+center;
            return Math.Max(-1,Math.Min(1,delta/Math.Max(1,span)))*sign;
        }
        public static double[] Stick(Snapshot s,Profile p) {
            double x=Axis(s,p.StickX,p.CenterX,p.SignX),y=Axis(s,p.StickY,p.CenterY,p.SignY);
            double mag=Math.Sqrt(x*x+y*y); if(mag<=p.Deadzone)return new[]{0.0,0.0};
            double amount=Math.Pow((Math.Min(1,mag)-p.Deadzone)/(1-p.Deadzone),p.Curve);
            return new[]{x/mag*amount,y/mag*amount};
        }
        public FrameResult Tick(Snapshot s,double now,Profile p,bool enabled) {
            var result=new FrameResult(); var desired=new HashSet<string>();
            double dt=Math.Max(0,Math.Min(.05,now-lastTime));lastTime=now;
            if(device!=s.DeviceId){Disarm();device=s.DeviceId;Change(desired,result);}
            if(!enabled || !s.Connected || p==null){Disarm();Change(desired,result);return result;}
            var stick=Stick(s,p);result.StickX=stick[0];result.StickY=stick[1];
            if(!armed) {
                if(!s.Buttons.Any(b=>b) && s.Hats.All(h=>h==0) && p.Mappings.Where(m=>m.Target!="None").All(m=>!m.Input.Active(s)) && (!p.StickEnabled || !p.NeutralConfirmed || stick[0]==0 && stick[1]==0))armed=true;
                return result;
            }
            int x=0,y=0;
            foreach(var m in p.Mappings) {
                if(!m.Input.Active(s))continue;
                if(m.Locked){if(m.Target=="Right")x++;if(m.Target=="Left")x--;if(m.Target=="Down")y++;if(m.Target=="Up")y--;continue;}
                if(p.StickEnabled && m.Input.Kind=="Axis" && (m.Input.Index==p.StickX || m.Input.Index==p.StickY))continue;
                if(m.Target=="MouseLeft")desired.Add("M:L");
                if(m.Target=="MouseRight")desired.Add("M:R");
                if(m.Target=="Keyboard")foreach(int key in m.Keys)desired.Add("K:"+KeyNames.Canonical(key));
            }
            result.Dx=Step(Math.Sign(x),ref oldX,ref nextX,now,p)*p.HorizontalStep;
            result.Dy=Step(Math.Sign(y),ref oldY,ref nextY,now,p)*p.VerticalStep;
            if(p.StickEnabled && p.NeutralConfirmed) {
                if(stick[0]==0 && stick[1]==0)residualX=residualY=0;
                else {
                    residualX+=stick[0]*p.Speed*dt;residualY+=stick[1]*p.Speed*dt;
                    int sx=(int)residualX,sy=(int)residualY;residualX-=sx;residualY-=sy;result.Dx+=sx;result.Dy+=sy;
                }
            }
            Change(desired,result);return result;
        }
    }
    public sealed class CaptureDetector {
        readonly Snapshot baseline;
        bool ready;
        readonly int ignoreX,ignoreY;
        public CaptureDetector(Snapshot initial,int stickX,int stickY) {baseline=initial.Clone();ignoreX=stickX;ignoreY=stickY;}
        public Source Poll(Snapshot s) {
            if(!s.Connected)return null;
            if(!ready){if(!s.Buttons.Any(b=>b) && s.Hats.All(h=>h==0) && s.Axes.Select((a,i)=>i==ignoreX || i==ignoreY || i>=baseline.Axes.Length || Math.Abs(a-baseline.Axes[i])<6000).All(v=>v))ready=true;return null;}
            for(int i=0;i<s.Buttons.Length;i++)if(s.Buttons[i])return new Source {Index=i};
            for(int i=0;i<s.Hats.Length;i++)foreach(int mask in new[]{1,2,4,8})if((s.Hats[i]&mask)!=0)return new Source {Kind="Hat",Index=i,Mask=mask};
            double best=14000;int index=-1;
            for(int i=0;i<Math.Min(s.Axes.Length,baseline.Axes.Length);i++) {
                if(i==ignoreX || i==ignoreY)continue;
                double change=Math.Abs(s.Axes[i]-baseline.Axes[i]);if(change>best){best=change;index=i;}
            }
            return index<0?null:new Source {Kind="Axis",Index=index,Direction=s.Axes[index]>baseline.Axes[index]?1:-1,Rest=baseline.Axes[index],Threshold=Math.Max(7000,best*.5)};
        }
    }
}
