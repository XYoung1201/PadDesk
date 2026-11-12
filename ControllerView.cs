using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace PadDesk {
    // Native meshes: no browser, model download or third-party renderer required.
    public sealed class ControllerView : Grid {
        sealed class Part {
            public string Key;public Point3D Center;public double Radius;
            public Model3DGroup Model=new Model3DGroup();
            public TranslateTransform3D Press=new TranslateTransform3D();
            public AxisAngleRotation3D Tilt=new AxisAngleRotation3D(new Vector3D(1,0,0),0);
            public SolidColorBrush Glow=new SolidColorBrush(Color.FromArgb(0,85,224,255));
            public double Amount;public bool Active;
        }
        readonly Viewport3D viewport=new Viewport3D();
        readonly Model3DGroup scene=new Model3DGroup();
        readonly Dictionary<string,Part> parts=new Dictionary<string,Part>();
        readonly Dictionary<Model3D,string> hitKeys=new Dictionary<Model3D,string>();
        readonly PerspectiveCamera camera=new PerspectiveCamera {FieldOfView=40,NearPlaneDistance=.1,FarPlaneDistance=60};
        readonly TextBlock title,subtitle; TextBlock modelBadge; string layout="Xbox";
        readonly Stopwatch clock=Stopwatch.StartNew();
        static readonly Point3D OverviewEye=new Point3D(2.3,3.7,10.8),OverviewLook=new Point3D(0,-.12,0);
        Point3D eye=OverviewEye,look=OverviewLook,targetEye,targetLook;
        double lastTime,stickX,stickY,rightX,rightY;bool rendering,dragging,moved;
        Point dragOrigin;double yaw,pitch;
        public event Action<string> ControlClicked;
        public event Action<string> InputFocused;
        public bool FollowInput=true;
        public string FocusedControl {get;private set;}
        public bool GuidanceMode {get;set;}
        public IEnumerable<string> ActiveControls {get{return parts.Values.Where(p=>p.Active).Select(p=>p.Key);}}

        public ControllerView() {
            MinHeight=240;ClipToBounds=true;
            Background=new RadialGradientBrush(Color.FromRgb(38,60,91),Color.FromRgb(10,18,33)) {Center=new Point(.42,.38),GradientOrigin=new Point(.4,.3),RadiusX=.8,RadiusY=.85};
            // Restrained floor halo gives the model depth without a bitmap backdrop.
            var halo=new System.Windows.Shapes.Ellipse {Height=65,Margin=new Thickness(40,0,40,40),VerticalAlignment=VerticalAlignment.Bottom,IsHitTestVisible=false,
                Fill=new RadialGradientBrush(Color.FromArgb(85,45,178,209),Colors.Transparent)};
            Children.Add(halo);Children.Add(viewport);viewport.Camera=camera;
            viewport.Children.Add(new ModelVisual3D {Content=scene});
            scene.Children.Add(new AmbientLight(Color.FromRgb(92,104,127)));
            scene.Children.Add(new DirectionalLight(Color.FromRgb(255,245,229),new Vector3D(-.5,-1,-2)));
            scene.Children.Add(new DirectionalLight(Color.FromRgb(107,195,242),new Vector3D(2,.3,-1)));
            scene.Children.Add(new DirectionalLight(Color.FromRgb(149,127,235),new Vector3D(-1,2,.5)));
            BuildController();
            var badge=new Border {Background=new SolidColorBrush(Color.FromArgb(170,10,18,33)),CornerRadius=new CornerRadius(8),Padding=new Thickness(11,6,11,6),Margin=new Thickness(14),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,IsHitTestVisible=false};
            badge.Child=modelBadge=new TextBlock {Text="XBOX  /  3D",Foreground=Brush("#87DCEC"),FontSize=10,FontWeight=FontWeights.SemiBold};Children.Add(badge);
            var caption=new StackPanel {Margin=new Thickness(16,0,16,12),VerticalAlignment=VerticalAlignment.Bottom,IsHitTestVisible=false};
            title=new TextBlock {Text="Xbox 标准布局",Foreground=Brushes.White,FontSize=15,FontWeight=FontWeights.SemiBold};
            subtitle=new TextBlock {Text="点选按键聚焦 · 拖动旋转",Foreground=Brush("#8FA4BF"),FontSize=11,Margin=new Thickness(0,4,0,0),TextWrapping=TextWrapping.Wrap};
            caption.Children.Add(title);caption.Children.Add(subtitle);Children.Add(caption);
            var reset=new Button {Content="全貌 ↺",FontSize=11,Padding=new Thickness(9,5,9,5),Margin=new Thickness(12),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Background=Brush("#23334B"),Foreground=Brush("#D0DEEF"),BorderBrush=Brush("#3C506A"),ToolTip="回到手柄全貌"};
            reset.Click+=delegate{if(GuidanceMode){yaw=.21;pitch=.03;targetLook=OverviewLook;targetEye=OverviewEye;}else FocusControl(null);};Children.Add(reset);
            viewport.MouseLeftButtonDown+=MouseDownOnModel;viewport.MouseMove+=MouseMoveOnModel;viewport.MouseLeftButtonUp+=MouseUpOnModel;
            viewport.LostMouseCapture+=delegate{dragging=false;};
            Loaded+=delegate{Subscribe();};Unloaded+=delegate{Unsubscribe();};
            IsVisibleChanged+=delegate{if(IsVisible && IsLoaded)Subscribe();else Unsubscribe();};
            FocusControl(null);ApplyCamera();
        }
        public void SetLayout(string value) {
            string next=value=="Photo"?"Photo":"Xbox"; if(next==layout)return;
            layout=next;while(scene.Children.Count>4)scene.Children.RemoveAt(4);
            parts.Clear();hitKeys.Clear();BuildController();
            modelBadge.Text=layout=="Photo"?"SWITCH PRO  /  3D":"XBOX  /  3D";
            FocusControl(null);
        }
        static SolidColorBrush Brush(string hex) {var b=new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));b.Freeze();return b;}
        static Material Surface(string hex,double shine) {
            var group=new MaterialGroup();group.Children.Add(new DiffuseMaterial(Brush(hex)));
            group.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(110,201,220,240)),shine));group.Freeze();return group;
        }
        static GeometryModel3D Geometry(MeshGeometry3D mesh,Material material) {return new GeometryModel3D(mesh,material);}
        void AddStatic(MeshGeometry3D mesh,Material material) {scene.Children.Add(Geometry(mesh,material));}

        void BuildController() {
            bool photo=layout=="Photo"; var shell=Surface(photo?"#42474E":"#D5DEE7",42);var dark=Surface("#171E28",28);var rubber=Surface("#252A32",9);
            var outline=new List<Point>();
            // Smooth Xbox-style shoulder lobes, tapered grips and recessed lower bridge.
            Bezier(outline,new Point(0,1.35),new Point(.9,1.35),new Point(1.6,1.72),new Point(2.24,1.39));
            Bezier(outline,new Point(2.24,1.39),new Point(2.75,1.18),new Point(2.86,.61),new Point(3.04,-.18));
            Bezier(outline,new Point(3.04,-.18),new Point(3.2,-.9),new Point(3.46,-1.8),new Point(2.98,-2.08));
            Bezier(outline,new Point(2.98,-2.08),new Point(2.54,-2.38),new Point(2.1,-1.49),new Point(1.62,-1.12));
            Bezier(outline,new Point(1.62,-1.12),new Point(1.2,-.83),new Point(.55,-.94),new Point(0,-.94));
            if(photo) {
                outline.Clear();
                Bezier(outline,new Point(0,1.24),new Point(.8,1.24),new Point(1.8,1.44),new Point(2.35,1.19));
                Bezier(outline,new Point(2.35,1.19),new Point(2.72,1.04),new Point(2.89,.49),new Point(2.99,-.15));
                Bezier(outline,new Point(2.99,-.15),new Point(3.15,-.83),new Point(3.22,-1.67),new Point(2.83,-1.91));
                Bezier(outline,new Point(2.83,-1.91),new Point(2.37,-2.16),new Point(1.99,-1.34),new Point(1.53,-1.04));
                Bezier(outline,new Point(1.53,-1.04),new Point(1.16,-.83),new Point(.48,-.88),new Point(0,-.88));
            }
            int half=outline.Count;for(int i=half-1;i>=0;i--)outline.Add(new Point(-outline[i].X,outline[i].Y));
            AddStatic(Shell(outline,new[]{.01,.25,.45,.65,.78,.86,.92,.96,.985,1.0},new[]{.63,.63,.62,.60,.56,.50,.41,.31,.19,.05}),shell);
            AddStatic(Shell(outline,new[]{1.0,.995,.96,.85,.03},new[]{.035,-.05,-.28,-.47,-.48}),dark);
            // Fine perimeter seam and sculpted dark grip inserts.
            AddStatic(Shell(outline,new[]{.998,1.002},new[]{.052,.015}),Surface("#65738A",35));
            foreach(int side in new[]{-1,1}) {
                AddStatic(Grip(side),rubber);
                // Fine rubber stippling follows the curved grip face.
                var texture=new MeshGeometry3D();
                for(int row=0;row<32;row++)for(int col=0;col<12;col++) {
                    double t=.1+row*.025,y=.50-2.30*t;
                    double left=2.25-.15*Math.Sin(t*Math.PI)+.34*t;
                    double right=2.60+.46*Math.Sin(t*Math.PI*.85);
                    double gripHalf=(right-left)/2*Math.Sqrt(Math.Sin(Math.PI*(.015+.97*t)));
                    double x=(left+right)/2+gripHalf*(col/11.0*1.7-.85);
                    double z=GripDepth(x,y)+.004;
                    int n=texture.Positions.Count;
                    texture.Positions.Add(new Point3D(side*(x-.007),y,z));
                    texture.Positions.Add(new Point3D(side*x,y-.009,z));
                    texture.Positions.Add(new Point3D(side*(x+.007),y,z));
                    if(side>0)Triangle(texture,n,n+1,n+2);else Triangle(texture,n+2,n+1,n);
                }
                Finish(texture);AddStatic(texture,Surface("#50555E",6));
            }
            Stick("LS",-1.72,.55);Stick("RS",.97,-.65);
            if(!photo){AddStatic(Cylinder(new Point3D(-1.02,-.69,.57),.59,.055),Surface("#6D7B8F",45));
            AddStatic(Cylinder(new Point3D(-1.02,-.69,.61),.51,.07),dark);}
            AddButton("UP",-1.02,-.36,.71,.20,"↑","#3E4D60",true);
            AddButton("DOWN",-1.02,-1.02,.71,.20,"↓","#3E4D60",true);
            AddButton("LEFT",-1.35,-.69,.71,.20,"←","#3E4D60",true);
            AddButton("RIGHT",-.69,-.69,.71,.20,"→","#3E4D60",true);
            AddStatic(photo?FlatBox(new Point3D(-1.02,-.69,.71),.34,.34,.16):Cylinder(new Point3D(-1.02,-.69,.69),.24,.08),Surface(photo?"#242A31":"#3E4D60",25));
            AddButton(photo?"B":"A",1.8,.16,.64,.25,photo?"B":"A",photo?"#B7BDC3":"#69C65A",false);
            AddButton(photo?"A":"B",2.25,.61,.61,.25,photo?"A":"B",photo?"#B7BDC3":"#EC696E",false);
            AddButton(photo?"Y":"X",1.35,.61,.64,.25,photo?"Y":"X",photo?"#B7BDC3":"#54ABEF",false);
            AddButton(photo?"X":"Y",1.8,1.06,.59,.25,photo?"X":"Y",photo?"#B7BDC3":"#F4CE55",false);
            AddButton("View",photo?-.72:-.46,photo?1.02:.54,.64,.14,photo?"−":"▣","#A1ADB9",false);
            AddButton("Menu",photo?.72:.46,photo?1.02:.54,.64,.14,photo?"+":"≡","#A1ADB9",false);
            AddButton("Home",photo?.42:0,photo?.40:1.04,.63,photo?.16:.23,photo?"⌂":"X","#BACEDD",false);
            AddButton("Share",photo?-.42:0,photo?.40:-.02,.64,.14,photo?"□":"↑","#A1ADB9",photo);
            Shoulder("LB",-1.76,photo?1.25:1.48,.32,false);Shoulder("RB",1.76,photo?1.25:1.48,.32,false);
            Shoulder("LT",-1.8,photo?1.36:1.57,-.25,true);Shoulder("RT",1.8,photo?1.36:1.57,-.25,true);
            if(photo) {
                AddStatic(RoundedBox(new Point3D(0,1.23,-.06),.34,.12,.09),dark);
                for(int i=0;i<4;i++)AddStatic(RoundedBox(new Point3D(-.15+i*.1,-.85,.43),.045,.025,.015),Surface(i==0?"#8CCB80":"#20262E",10));
            }
        }
        static double GripDepth(double x,double y) {
            return .67-.21*Math.Pow((x-2.40)/.65,2)-.11*Math.Pow((y+.7)/1.2,2);
        }
        static MeshGeometry3D Grip(int side) {
            var mesh=new MeshGeometry3D();const int rows=32,cols=18;
            for(int j=0;j<=rows;j++) {
                double t=j/(double)rows,y=.50-2.30*t;
                double left=2.25-.15*Math.Sin(t*Math.PI)+.34*t;
                double right=2.60+.46*Math.Sin(t*Math.PI*.85);
                double taper=Math.Sin(Math.PI*(.015+.97*t));
                double middle=(left+right)/2,half=(right-left)/2*Math.Sqrt(taper);
                for(int i=0;i<=cols;i++){double x=middle+half*(2.0*i/cols-1);mesh.Positions.Add(new Point3D(side*x,y,GripDepth(x,y)));}
            }
            for(int j=0;j<rows;j++)for(int i=0;i<cols;i++) {
                int a=j*(cols+1)+i,b=a+cols+1;
                if(side>0){Triangle(mesh,a,b,a+1);Triangle(mesh,a+1,b,b+1);}
                else {Triangle(mesh,a,a+1,b);Triangle(mesh,a+1,b+1,b);}
            }
            Finish(mesh);return mesh;
        }
        Part NewPart(string key,Point3D center,double radius) {
            var p=new Part {Key=key,Center=center,Radius=radius};
            var transforms=new Transform3DGroup();
            transforms.Children.Add(new RotateTransform3D(p.Tilt,center));transforms.Children.Add(p.Press);p.Model.Transform=transforms;
            parts.Add(key,p);scene.Children.Add(p.Model);return p;
        }
        void PartMesh(Part part,MeshGeometry3D mesh,Material material) {var g=Geometry(mesh,material);part.Model.Children.Add(g);hitKeys.Add(g,part.Key);}
        void Ring(Part p,Point3D at,double radius) {PartMesh(p,Torus(at,radius,.035),new EmissiveMaterial(p.Glow));}
        void AddButton(string key,double x,double y,double z,double radius,string text,string color,bool dpad) {
            var center=new Point3D(x,y,z);var p=NewPart(key,center,radius);
            if(!(dpad && layout=="Photo"))AddStatic(Cylinder(new Point3D(x,y,z-.04),radius+.035,.06),Surface("#152033",20));
            PartMesh(p,dpad?RoundedBox(center,radius*1.7,radius*1.7,.16):Cylinder(center,radius,.14),Surface(dpad?(layout=="Photo"?"#242A31":color):"#202631",32));
            PartMesh(p,Disc(new Point3D(x,y,z+.165),radius*.7),Label(text,dpad?"#91A2B4":color));
            Ring(p,new Point3D(x,y,z+.04),radius+.055);
        }
        void Stick(string key,double x,double y) {
            var center=new Point3D(x,y,.65);var p=NewPart(key,center,.4);
            AddStatic(Cylinder(new Point3D(x,y,.53),.53,.065),Surface("#728095",48));
            AddStatic(Ellipsoid(new Point3D(x,y,.58),.46,.46,.14),Surface("#182232",20));
            PartMesh(p,Cylinder(new Point3D(x,y,.6),.19,.31),Surface("#3C4959",50));
            PartMesh(p,Cylinder(new Point3D(x,y,.87),.37,.12),Surface("#273341",14));
            PartMesh(p,Torus(new Point3D(x,y,.995),.335,.048),Surface("#475362",12));
            for(int i=0;i<4;i++)PartMesh(p,Torus(new Point3D(x,y,.994),.12+i*.043,.005),Surface("#343E49",7));
            if(layout!="Photo")PartMesh(p,Disc(new Point3D(x,y,.998),.29),Label(key,"#92A3B6"));
            for(int i=0;i<28;i++){double a=i*Math.PI*2/28;PartMesh(p,Ellipsoid(new Point3D(x+.356*Math.Cos(a),y+.356*Math.Sin(a),.953),.015,.015,.035),Surface("#788696",8));}
            Ring(p,new Point3D(x,y,.65),.49);
        }
        void Shoulder(string key,double x,double y,double z,bool trigger) {
            var transform=new Transform3DGroup();transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1,0,0),-68)));
            transform.Children.Add(new TranslateTransform3D(x,y-.12,z));transform.Freeze();
            var p=NewPart(key,transform.Transform(new Point3D(0,0,.20)),.43);
            var block=Geometry(RoundedBox(new Point3D(0,0,0),trigger?.84:1.17,trigger?.82:.35,.19),Surface(trigger?"#27364A":"#48586D",40));
            block.Transform=transform;p.Model.Children.Add(block);hitKeys.Add(block,key);
            var label=Geometry(Disc(new Point3D(0,0,.202),trigger?.24:.18),Label(key,trigger?"#A7E6F4":"#E2EDF8"));
            label.Transform=transform;p.Model.Children.Add(label);hitKeys.Add(label,key);
            // Rim follows the button surface, above the mesh so it cannot be occluded.
            var ringMesh=Torus(new Point3D(0,0,.205),1,.055).Clone();
            for(int i=0;i<ringMesh.Positions.Count;i++){var v=ringMesh.Positions[i];ringMesh.Positions[i]=new Point3D(v.X*(trigger?.37:.51),v.Y*(trigger?.35:.135),v.Z);}ringMesh.Freeze();
            var ring=Geometry(ringMesh,new EmissiveMaterial(p.Glow));ring.Transform=transform;p.Model.Children.Add(ring);hitKeys.Add(ring,key);
        }
        static Material Label(string text,string color) {
            var drawing=new DrawingGroup();
            using(var dc=drawing.Open()) {
                dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,128,128));
                // Keep the .NET 4.x-compatible overload; the texture is resolution independent.
#pragma warning disable 618
                var ft=new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(new FontFamily("Segoe UI"),FontStyles.Normal,FontWeights.Bold,FontStretches.Normal),text.Length>1?40:70,Brush(color));
#pragma warning restore 618
                dc.DrawText(ft,new Point((128-ft.Width)/2,(128-ft.Height)/2));
            }
            var brush=new DrawingBrush(drawing);brush.Freeze();var mat=new EmissiveMaterial(brush);mat.Freeze();return mat;
        }
        public void FocusControl(string name) {
            FocusedControl=ControllerLayout.Key(name);yaw=0;pitch=0;
            Part p;
            if(FocusedControl!=null && parts.TryGetValue(FocusedControl,out p)) {
                targetLook=p.Center;
                bool shoulder=FocusedControl=="LT" || FocusedControl=="RT" || FocusedControl=="LB" || FocusedControl=="RB";
                targetEye=p.Center+(shoulder?new Vector3D(p.Center.X*.15,4.2,3.6):new Vector3D(p.Center.X*.12,1.45,5.5));
            } else {FocusedControl=null;yaw=.21;pitch=.03;targetLook=OverviewLook;targetEye=OverviewEye;}
            title.Text=FocusedControl==null?(layout=="Photo"?"Switch Pro · ABXY":"Xbox 标准布局"):ControllerLayout.Caption(FocusedControl,layout);
            subtitle.Text=GuidanceMode?(FocusedControl==null?"按对话框提示操作实体手柄":"请按实体手柄上发光的按键"):(FocusedControl==null?"按下实体键或点选模型聚焦 · 拖动旋转":"跟随新按键切换 · 松开保留焦点 · 全貌返回");
        }
        public void UpdateInput(Snapshot snapshot,Profile profile) {
            var held=new HashSet<string>(ActiveControls);
            foreach(var p in parts.Values)p.Active=false;
            stickX=stickY=rightX=rightY=0;
            if(snapshot==null || !snapshot.Connected || profile==null || snapshot.DeviceId!=profile.DeviceId)return;
            foreach(var m in profile.Mappings) {
                string key=ControllerLayout.KeyFor(m);Part p;
                if(key!=null && parts.TryGetValue(key,out p) && m.Input.Active(snapshot))p.Active=true;
            }
            if(!GuidanceMode && FollowInput) {
                // Only rising edges may move focus: a held key must not steal it back.
                var next=profile.Mappings.Select(ControllerLayout.KeyFor)
                    .FirstOrDefault(key=>key!=null && parts.ContainsKey(key) && parts[key].Active && !held.Contains(key));
                if(next!=null) {FocusControl(next);if(InputFocused!=null)InputFocused(next);}
            }
            var xy=InputEngine.Stick(snapshot,profile);stickX=xy[0];stickY=xy[1];
            rightX=snapshot.RightStickX;rightY=snapshot.RightStickY;
        }
        void Subscribe(){if(rendering)return;rendering=true;lastTime=clock.Elapsed.TotalSeconds;CompositionTarget.Rendering+=Render;}
        void Unsubscribe(){if(!rendering)return;rendering=false;CompositionTarget.Rendering-=Render;}
        void Render(object sender,EventArgs e) {
            double now=clock.Elapsed.TotalSeconds,dt=Math.Min(.06,Math.Max(0,now-lastTime));lastTime=now;
            double ease=1-Math.Exp(-dt*9);
            eye=Lerp(eye,targetEye,ease);look=Lerp(look,targetLook,ease);ApplyCamera();
            foreach(var p in parts.Values) {
                p.Amount+=( (p.Active?1:0)-p.Amount)*(1-Math.Exp(-dt*22));
                bool shoulder=p.Key=="LT" || p.Key=="RT" || p.Key=="LB" || p.Key=="RB";
                p.Press.OffsetZ=shoulder?0:-p.Amount*.075;p.Press.OffsetY=shoulder?-p.Amount*.085:0;
                bool focused=p.Key==FocusedControl;
                byte alpha=p.Active?(byte)255:focused?(byte)(180+55*Math.Sin(now*4)): (byte)0;
                p.Glow.Color=p.Active?Color.FromArgb(alpha,100,255,174):Color.FromArgb(alpha,78,211,255);
            }
            TiltStick(parts["LS"],stickX,stickY,ease);TiltStick(parts["RS"],rightX,rightY,ease);
        }
        static void TiltStick(Part p,double x,double y,double ease) {double length=Math.Sqrt(x*x+y*y);if(length>.001)p.Tilt.Axis=new Vector3D(y,x,0);p.Tilt.Angle+=(length*18-p.Tilt.Angle)*ease;}
        static Point3D Lerp(Point3D a,Point3D b,double t){return a+(b-a)*t;}
        void ApplyCamera(){camera.Position=eye;camera.LookDirection=look-eye;camera.UpDirection=new Vector3D(0,1,0);}
        void MouseDownOnModel(object sender,MouseButtonEventArgs e){dragOrigin=e.GetPosition(viewport);dragging=true;moved=false;viewport.CaptureMouse();e.Handled=true;}
        void MouseMoveOnModel(object sender,MouseEventArgs e) {
            if(!dragging)return;var point=e.GetPosition(viewport);var delta=point-dragOrigin;
            if(!moved && delta.Length<4)return;moved=true;dragOrigin=point;
            yaw+=delta.X*.008;pitch=Math.Max(-.35,Math.Min(.9,pitch+delta.Y*.006));
            double distance=FocusedControl==null?11.3:6.0,angle=.3+pitch;
            targetEye=targetLook+new Vector3D(Math.Sin(yaw)*Math.Cos(angle)*distance,Math.Sin(angle)*distance,Math.Cos(yaw)*Math.Cos(angle)*distance);
        }
        void MouseUpOnModel(object sender,MouseButtonEventArgs e) {
            if(!dragging)return;dragging=false;viewport.ReleaseMouseCapture();if(moved)return;
            SelectAt(e.GetPosition(viewport));
        }
        void SelectAt(Point point) {
            var hit=VisualTreeHelper.HitTest(viewport,point) as RayMeshGeometry3DHitTestResult;
            string key;if(hit!=null && hitKeys.TryGetValue(hit.ModelHit,out key)) {
                // During calibration only real device input may advance/focus the next step.
                if(GuidanceMode)return;
                FocusControl(key);if(ControlClicked!=null)ControlClicked(key);
            }
        }
        static void Bezier(List<Point> pts,Point a,Point b,Point c,Point d) {
            for(int i=0;i<28;i++){double t=i/28.0,u=1-t;pts.Add(new Point(u*u*u*a.X+3*u*u*t*b.X+3*u*t*t*c.X+t*t*t*d.X,u*u*u*a.Y+3*u*u*t*b.Y+3*u*t*t*c.Y+t*t*t*d.Y));}
        }
        static MeshGeometry3D Shell(List<Point> outline,double[] scales,double[] depths) {
            var mesh=new MeshGeometry3D();int n=outline.Count;
            for(int j=0;j<scales.Length;j++)foreach(var p in outline)mesh.Positions.Add(new Point3D(p.X*scales[j],(p.Y-.1)*scales[j]+.1,depths[j]));
            for(int j=0;j<scales.Length-1;j++)for(int i=0;i<n;i++){int a=j*n+i,b=j*n+(i+1)%n,c=(j+1)*n+i,d=(j+1)*n+(i+1)%n;Triangle(mesh,a,b,c);Triangle(mesh,b,d,c);}
            for(int i=1;i<n-1;i++)Triangle(mesh,0,i+1,i);
            Finish(mesh);return mesh;
        }
        static MeshGeometry3D Ellipsoid(Point3D c,double rx,double ry,double rz) {
            var m=new MeshGeometry3D();const int rows=16,cols=40;
            for(int j=0;j<=rows;j++){double phi=Math.PI*j/rows;for(int i=0;i<=cols;i++){double a=2*Math.PI*i/cols;m.Positions.Add(new Point3D(c.X+rx*Math.Sin(phi)*Math.Cos(a),c.Y+ry*Math.Sin(phi)*Math.Sin(a),c.Z+rz*Math.Cos(phi)));}}
            for(int j=0;j<rows;j++)for(int i=0;i<cols;i++){int a=j*(cols+1)+i,b=a+cols+1;Triangle(m,a,b,a+1);Triangle(m,a+1,b,b+1);}Finish(m);return m;
        }
        static MeshGeometry3D Cylinder(Point3D c,double radius,double height) {
            var pts=new List<Point>();for(int i=0;i<48;i++){double a=-2*Math.PI*i/48;pts.Add(new Point(Math.Cos(a)*radius,Math.Sin(a)*radius+.1));}
            var m=Shell(pts,new[]{.01,.85,1.0,1.0,.94},new[]{height,height,height-.025,.02,0});
            var copy=m.Clone();for(int i=0;i<copy.Positions.Count;i++){var p=copy.Positions[i];copy.Positions[i]=new Point3D(p.X+c.X,p.Y-.1+c.Y,p.Z+c.Z);}copy.Freeze();return copy;
        }
        static MeshGeometry3D FlatBox(Point3D c,double width,double height,double depth) {
            var m=new MeshGeometry3D();double x=width/2,y=height/2;
            var v=new[]{new Point3D(c.X-x,c.Y-y,c.Z),new Point3D(c.X+x,c.Y-y,c.Z),new Point3D(c.X+x,c.Y+y,c.Z),new Point3D(c.X-x,c.Y+y,c.Z),new Point3D(c.X-x,c.Y-y,c.Z+depth),new Point3D(c.X+x,c.Y-y,c.Z+depth),new Point3D(c.X+x,c.Y+y,c.Z+depth),new Point3D(c.X-x,c.Y+y,c.Z+depth)};
            foreach(var face in new[]{new[]{4,5,6,7},new[]{1,0,3,2},new[]{0,1,5,4},new[]{2,3,7,6},new[]{3,0,4,7},new[]{1,2,6,5}}){int start=m.Positions.Count;foreach(int i in face)m.Positions.Add(v[i]);Triangle(m,start,start+1,start+2);Triangle(m,start,start+2,start+3);}
            Finish(m);return m;
        }
        static MeshGeometry3D RoundedBox(Point3D c,double width,double height,double depth) {
            var pts=new List<Point>();for(int i=0;i<48;i++){double a=-2*Math.PI*i/48;double x=Math.Cos(a),y=Math.Sin(a);pts.Add(new Point(Math.Sign(x)*Math.Pow(Math.Abs(x),.4)*width/2,Math.Sign(y)*Math.Pow(Math.Abs(y),.4)*height/2+.1));}
            var m=Shell(pts,new[]{.01,.8,1.0,1.0},new[]{depth,depth,depth-.035,0}).Clone();
            for(int i=0;i<m.Positions.Count;i++){var p=m.Positions[i];m.Positions[i]=new Point3D(p.X+c.X,p.Y-.1+c.Y,p.Z+c.Z);}m.Freeze();return m;
        }
        static MeshGeometry3D Disc(Point3D c,double radius) {
            var m=new MeshGeometry3D();m.Positions.Add(c);m.TextureCoordinates.Add(new Point(.5,.5));
            for(int i=0;i<=48;i++){double a=2*Math.PI*i/48;m.Positions.Add(new Point3D(c.X+radius*Math.Cos(a),c.Y+radius*Math.Sin(a),c.Z));m.TextureCoordinates.Add(new Point(.5+.5*Math.Cos(a),.5-.5*Math.Sin(a)));}
            for(int i=1;i<=48;i++)Triangle(m,0,i,i+1);Finish(m);return m;
        }
        static MeshGeometry3D Torus(Point3D c,double radius,double tube) {
            var m=new MeshGeometry3D();const int n=48,k=8;
            for(int i=0;i<=n;i++)for(int j=0;j<=k;j++){double a=2*Math.PI*i/n,b=2*Math.PI*j/k,r=radius+tube*Math.Cos(b);m.Positions.Add(new Point3D(c.X+r*Math.Cos(a),c.Y+r*Math.Sin(a),c.Z+tube*Math.Sin(b)));}
            for(int i=0;i<n;i++)for(int j=0;j<k;j++){int a=i*(k+1)+j,b=a+k+1;Triangle(m,a,b,a+1);Triangle(m,a+1,b,b+1);}Finish(m);return m;
        }
        static void Triangle(MeshGeometry3D m,int a,int b,int c){m.TriangleIndices.Add(a);m.TriangleIndices.Add(b);m.TriangleIndices.Add(c);}
        static void Finish(MeshGeometry3D m) {
            var normals=new Vector3D[m.Positions.Count];
            for(int i=0;i<m.TriangleIndices.Count;i+=3){int a=m.TriangleIndices[i],b=m.TriangleIndices[i+1],c=m.TriangleIndices[i+2];var n=Vector3D.CrossProduct(m.Positions[b]-m.Positions[a],m.Positions[c]-m.Positions[a]);normals[a]+=n;normals[b]+=n;normals[c]+=n;}
            foreach(var n0 in normals){var n=n0;if(n.LengthSquared>1e-12)n.Normalize();else n=new Vector3D(0,0,1);m.Normals.Add(n);}m.Freeze();
        }
    }
}
