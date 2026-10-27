using System.Numerics;
using System.Text.Json;
using Bomber.Game.Matches;
using Bomber.Game.Simulation;
using Bomber.Game.Simulation.Rules;
using Bomber.Game.Simulation.Stages;
using Bomber.Game.Simulation.State;

internal static class AimedThrowChecks
{
    public static int Run()
    {
        int count=0;
        void Check(string name,Action action){action();count++;Console.WriteLine("PASS "+name);}
        Check("deliberate lob range is precise and independent of charge",()=>{
            foreach(float range in new[]{BombThrowRules.MinAimRange,4.5f,BombThrowRules.MaxAimRange}){
                var arc=BombThrowRules.Project(new(0,BombThrowRules.LaunchHeight,0),BombThrowRules.AimedVelocity(0,1,range),(x,z)=>0,_=>false);
                Assert(arc.End==ThrowProjectionEnd.Landing&&Math.Abs(arc.Last.Z-range)<.001f,"Deliberate range drifted from discrete landing.");
            }
        });
        Check("aim cursor follows arena directions regardless of facing",()=>{
            foreach(var heading in new[]{new Vector2(1,0),new Vector2(0,1),new Vector2(-1,0),new Vector2(0,-1)})
            foreach(var control in new[]{Vector2.UnitX,-Vector2.UnitX,Vector2.UnitY,-Vector2.UnitY}){
                var expected=heading*4.5f+control*BombThrowRules.AimCursorSpeed*BombThrowRules.SimulationStep;
                var aimed=BombThrowRules.AdjustAim(new(heading.X,heading.Y,4.5f),control.X,control.Y,BombThrowRules.SimulationStep);
                Assert(Vector2.Distance(expected,new Vector2(aimed.X,aimed.Z)*aimed.Range)<.0001f,"Cursor moved in a different direction from the input.");
            }
        });
        Check("cursor crosses the carrier smoothly and clamps at maximum range",()=>{
            var aim=new ThrowAim(0,1,.2f);
            var center=BombThrowRules.AdjustAim(aim,0,-1,BombThrowRules.SimulationStep);
            Assert(center.Range<.00001f,"Cursor cannot reach the carrier's center.");
            var behind=BombThrowRules.AdjustAim(center,0,-1,BombThrowRules.SimulationStep);
            Assert(behind.Z<-.99f&&Math.Abs(behind.Range-.2f)<.0001f,"Cursor got stuck trying to aim behind the carrier.");
            Assert(BombThrowRules.AdjustAim(behind,0,0,BombThrowRules.SimulationStep)==behind,"Neutral reset deliberate intent.");
            for(int i=0;i<300;i++)behind=BombThrowRules.AdjustAim(behind,0,-1,BombThrowRules.SimulationStep);
            Assert(behind.Range==BombThrowRules.MaxAimRange,"Cursor escaped the shared radius cap.");
        });
        Check("diagonal aim cannot adjust faster than normalized controller intent",()=>{
            var start=new ThrowAim(0,1,4.5f);
            var keyboard=BombThrowRules.AdjustAim(start,1,-1,BombThrowRules.SimulationStep);
            var stick=BombThrowRules.AdjustAim(start,1/MathF.Sqrt(2),-1/MathF.Sqrt(2),BombThrowRules.SimulationStep);
            Assert(Vector2.Distance(new(keyboard.X,keyboard.Z),new(stick.X,stick.Z))<.0001f&&Math.Abs(keyboard.Range-stick.Range)<.0001f,"Keyboard diagonals gain aiming speed.");
        });
        Check("aim math rejects nonfinite control and range values",()=>{
            var aim=BombThrowRules.AdjustAim(new(0,1,4.5f),float.NaN,float.PositiveInfinity,BombThrowRules.SimulationStep);
            var velocity=BombThrowRules.AimedVelocity(float.NaN,0,float.NaN);
            Assert(aim==new ThrowAim(0,1,4.5f)&&float.IsFinite(velocity.X)&&float.IsFinite(velocity.Y)&&float.IsFinite(velocity.Z),"Malformed aim propagated into physics.");
        });
        Check("single tick aim nudges start fine and sustained input reaches shared speed",()=>{
            foreach(bool turn in new[]{true,false}){
                var m=Match();Lift(m);var before=Player(m);
                var input=new PlayerInput(turn?1:0,turn?0:1,B:true,Aim:true);
                m.SetInput(0,input);Step(m,1);m.SetInput(0,new PlayerInput(0,0,B:true,Aim:true));Step(m,1);var nudged=Player(m);
                float Value(JsonElement p)=>N(p,turn?"aimX":"aimZ")*N(p,"aimRange");
                var nudge=Value(nudged)-Value(before);
                var expected=BombThrowRules.AimCursorSpeed*BombThrowRules.SimulationStep*BombThrowRules.AimFineScale;
                Assert(Math.Abs(nudge-expected)<.0001f,"First-tick aim did not start at fine speed.");
                m.SetInput(0,input);Step(m,14);before=Player(m);Step(m,2);var steady=Value(Player(m))-Value(before);
                Assert(steady>nudge*7.9f&&steady<nudge*8.1f,"Sustained input failed to reach bounded full speed.");
            }
        });
        Check("neutral and reversal reset each aim axis to fine speed",()=>{
            foreach(bool turn in new[]{true,false}){
                var m=Match();Lift(m);
                PlayerInput Intent(float axis)=>new(turn?axis:0,turn?0:axis,B:true,Aim:true);
                float Value(JsonElement p)=>N(p,turn?"aimX":"aimZ")*N(p,"aimRange");
                m.SetInput(0,Intent(1));Step(m,14);var before=Value(Player(m));
                m.SetInput(0,Intent(-1));Step(m,1);m.SetInput(0,Intent(0));Step(m,1);var reversed=Value(Player(m));
                var expected=BombThrowRules.AimCursorSpeed*BombThrowRules.SimulationStep*BombThrowRules.AimFineScale;
                Assert(Math.Abs(before-reversed-expected)<.0001f,"Direction reversal retained accelerated aim speed.");
                m.SetInput(0,Intent(1));Step(m,1);m.SetInput(0,Intent(0));Step(m,1);
                Assert(Math.Abs(Value(Player(m))-reversed-expected)<.0001f,"Neutral failed to rearm a fine nudge.");
            }
        });
        Check("ramped keyboard diagonals and equivalent stick intent remain equal",()=>{
            var keyboard=Match();var controller=Match();Lift(keyboard);Lift(controller);
            keyboard.SetInput(0,new PlayerInput(1,-1,B:true,Aim:true));controller.SetInput(0,new PlayerInput(1/MathF.Sqrt(2),-1/MathF.Sqrt(2),B:true,Aim:true));
            Step(keyboard,18);Step(controller,18);
            foreach(var property in new[]{"aimX","aimZ","aimRange"})Assert(Math.Abs(N(Player(keyboard),property)-N(Player(controller),property))<.0001f,"Ramped keyboard intent gained speed over controller.");
            var full=Match();var partial=Match();Lift(full);Lift(partial);
            full.SetInput(0,new PlayerInput(1,0,B:true,Aim:true));partial.SetInput(0,new PlayerInput(.5f,0,B:true,Aim:true));Step(full,14);Step(partial,14);
            var fullAngle=N(Player(full),"aimX")*N(Player(full),"aimRange");var partialAngle=N(Player(partial),"aimX")*N(Player(partial),"aimRange");
            Assert(Math.Abs(fullAngle-partialAngle*2)<.0001f,"Partial stick intent lost proportional fine control.");
        });
        Check("ignored aim modifier preserves normal walking without a carried bomb",()=>{
            foreach(float magnitude in new[]{.01f,.05f,.1f,.25f}){
                var normal=Match();var ignored=Match();
                normal.SetInput(0,new PlayerInput(1,0));ignored.SetInput(0,new PlayerInput(magnitude,0,Aim:true));Step(normal,4);Step(ignored,4);
                Assert(Math.Abs(N(Player(normal),"x")-N(Player(ignored),"x"))<.001f&&!Player(ignored).GetProperty("aiming").GetBoolean(),$"Ignored modifier slowed ordinary walking at magnitude {magnitude}.");
            }
            var diagonal=Match();var smallDiagonal=Match();
            diagonal.SetInput(0,new PlayerInput(1,1));smallDiagonal.SetInput(0,new PlayerInput(.01f,.01f,Aim:true));Step(diagonal,4);Step(smallDiagonal,4);
            Assert(Math.Abs(N(Player(diagonal),"x")-N(Player(smallDiagonal),"x"))<.001f&&Math.Abs(N(Player(diagonal),"z")-N(Player(smallDiagonal),"z"))<.001f,"Ignored modifier changed a small diagonal movement direction or speed.");
            var neutral=Match();var before=Player(neutral);neutral.SetInput(0,new PlayerInput(0,0,Aim:true));Step(neutral,4);
            Assert(N(before,"x")==N(Player(neutral),"x")&&N(before,"z")==N(Player(neutral),"z"),"Ignored neutral modifier generated movement.");
        });
        Check("aim stance plants the carrier and resumes ordinary movement on release",()=>{
            var m=Match();Lift(m);var before=Player(m);
            m.SetInput(0,new PlayerInput(1,-1,B:true,Aim:true));Step(m,20);var aimed=Player(m);
            Assert(N(before,"x")==N(aimed,"x")&&N(before,"z")==N(aimed,"z"),"Aiming moved the carrier.");
            Assert(aimed.GetProperty("aiming").GetBoolean()&&aimed.GetProperty("aimSet").GetBoolean(),"Authority did not accept the stance.");
            m.SetInput(0,new PlayerInput(1,0,B:true));Step(m,4);
            Assert(N(Player(m),"x")>N(before,"x")&&!Player(m).GetProperty("aiming").GetBoolean(),"Releasing aim did not restore movement.");
        });
        Check("release commits retained aim and charge never changes deliberate landing",()=>{
            foreach(int pumps in new[]{0,3}){
                var m=Match();Lift(m);
                m.SetInput(0,new PlayerInput(0,-1,B:true,Aim:true));Step(m,18);
                for(int i=0;i<pumps;i++){
                    m.SetInput(0,new PlayerInput(0,0,A:true,B:true,Aim:true));Step(m,2);
                    m.SetInput(0,new PlayerInput(0,0,B:true,Aim:true));Step(m,14);
                }
                var p=Player(m);var origin=new Vector3(N(p,"x"),N(p,"y")+BombThrowRules.LaunchHeight,N(p,"z"));
                var projection=BombThrowRules.Project(origin,BombThrowRules.AimedVelocity(N(p,"aimX"),N(p,"aimZ"),N(p,"aimRange"),pumps:pumps),(x,z)=>0,_=>false,gravityScale:BombThrowRules.Weight(pumps));
                var fuse=N(Bomb(m),"fuse");m.SetInput(0,default);Step(m,64);
                var actual=Bomb(m);
                Assert(Math.Abs(N(actual,"x")-projection.Last.X)<.011f&&Math.Abs(N(actual,"z")-projection.Last.Z)<.011f,"Authority did not honor retained landing intent.");
                Assert(N(actual,"fuse")<fuse&&!Player(m).GetProperty("aimSet").GetBoolean(),"Throw did not resume fuse or clear old intent.");
            }
        });
        Check("close aimed lobs retain real self-hit risk",()=>{
            var m=Match();Lift(m);m.SetInput(0,new PlayerInput(0,-1,B:true,Aim:true));
            for(int tick=0;tick<40&&N(Player(m),"aimRange")>.15f;tick++)m.Tick();
            Assert(N(Player(m),"aimRange")<.15f,"Fixture did not aim at the carrier's feet.");
            m.SetInput(0,default);bool hit=false;
            for(int i=0;i<65;i++){m.Tick();hit|=Player(m).GetProperty("stunned").GetBoolean();}
            Assert(hit,"An aimed self-lob granted contact immunity.");
        });
        Check("a deliberate high lob reaches raised tier four and matches advisory",()=>{
            var m=Match(raised:true);Lift(m);m.SetInput(0,new PlayerInput(0,0,B:true,Aim:true));Step(m,2);
            var p=Player(m);float Surface(float x,float z)=>z>=13?4:0;
            var projection=BombThrowRules.Project(new(N(p,"x"),BombThrowRules.LaunchHeight,N(p,"z")),BombThrowRules.AimedVelocity(0,1,N(p,"aimRange")),Surface,_=>false);
            Assert(projection.End==ThrowProjectionEnd.Landing&&projection.Last.Y==4,"High lob failed to reach elevated terrain.");
            m.SetInput(0,default);Step(m,64);var bomb=Bomb(m);
            Assert(N(bomb,"y")==4&&Math.Abs(N(bomb,"z")-projection.Last.Z)<.011f,"Elevated authoritative landing diverged from advisory.");
        });
        Check("staggered diagonal key release keeps the diagonal heading and throw",()=>{
            foreach(bool carrying in new[]{false,true}){
                var m=Match();if(carrying)Lift(m);
                m.SetInput(0,new PlayerInput(1,1,B:carrying));Step(m,6);
                // One key is released a tick before the other: the classic keyboard diagonal miss.
                m.SetInput(0,new PlayerInput(1,0,B:carrying));Step(m,1);
                m.SetInput(0,new PlayerInput(0,0,B:carrying));Step(m,1);
                var p=Player(m);
                Assert(Math.Abs(N(p,"fx")-N(p,"fz"))<.001f&&N(p,"fx")>.7f,$"Staggered release snapped facing to a cardinal (carrying {carrying}).");
                if(!carrying)continue;
                float x=N(p,"x"),z=N(p,"z");
                m.SetInput(0,default);Step(m,8);var bomb=Bomb(m);
                float dx=N(bomb,"x")-x,dz=N(bomb,"z")-z;
                Assert(dx>.5f&&Math.Abs(dx-dz)<.05f,$"Ordinary throw after a staggered release left the diagonal ({dx},{dz}).");
            }
        });
        Check("pointer range on level ground is the distance and clamps at maximum",()=>{
            foreach(int pumps in new[]{0,3})
            foreach(float distance in new[]{0f,2.5f,6f}){
                Assert(Math.Abs(BombThrowRules.RangeToTarget(distance,0,pumps)-distance)<.0001f,"Level pointer target changed its distance.");
            }
            Assert(BombThrowRules.RangeToTarget(40,0)==BombThrowRules.MaxAimRange&&BombThrowRules.RangeToTarget(float.NaN,0)==BombThrowRules.DefaultAimRange,"Pointer range escaped its bounds.");
            Assert(BombThrowRules.RangeToTarget(4,2)>4&&BombThrowRules.RangeToTarget(4,-1)<4,"Raised and lowered targets need longer and shorter level ranges.");
        });
        Check("pointer target steers a walking carrier and lands where it points",()=>{
            var m=Match();Lift(m);var start=Player(m);
            var target=new Vector2(N(start,"x")+3,N(start,"z")+2);
            m.SetInput(0,new PlayerInput(-1,0,B:true,AimTarget:target));Step(m,6);
            var walking=Player(m);
            Assert(N(walking,"x")<N(start,"x")-.5f&&!walking.GetProperty("aiming").GetBoolean()&&walking.GetProperty("aimSet").GetBoolean(),"Pointer aim planted the carrier or was not accepted.");
            var expected=new Vector2(N(walking,"x"),N(walking,"z"))+new Vector2(N(walking,"aimX"),N(walking,"aimZ"))*N(walking,"aimRange");
            Assert(Vector2.Distance(expected,target)<.01f,"Authority aim does not name the pointed point after the carrier moved.");
            m.SetInput(0,new PlayerInput(0,0,AimTarget:target));Step(m,64);var bomb=Bomb(m);
            Assert(bomb.GetProperty("state").GetInt32()==0&&Vector2.Distance(new(N(bomb,"x"),N(bomb,"z")),target)<.05f,"Pointer throw missed its target.");
        });
        Check("relative stick target follows the carrier's post-move position",()=>{
            var m=Match();Lift(m);var offset=new Vector2(-2,3);
            m.SetInput(0,new PlayerInput(1,0,B:true,AimTarget:offset,AimRelative:true));Step(m,5);var p=Player(m);
            Assert(!p.GetProperty("aiming").GetBoolean()&&Vector2.Distance(new Vector2(N(p,"aimX"),N(p,"aimZ"))*N(p,"aimRange"),offset)<.01f,"Relative stick offset drifted while walking.");
        });
        Check("pointer target on raised terrain lands on that tier",()=>{
            var m=Match(raised:true);Lift(m);var p=Player(m);
            var target=new Vector2(N(p,"x")+1,15.5f);
            m.SetInput(0,new PlayerInput(0,0,B:true,AimTarget:target));Step(m,2);
            m.SetInput(0,new PlayerInput(0,0,AimTarget:target));Step(m,64);var bomb=Bomb(m);
            Assert(N(bomb,"y")==4&&Vector2.Distance(new(N(bomb,"x"),N(bomb,"z")),target)<.15f,$"Raised pointer target missed: {bomb}.");
        });
        Check("pointer target without a carried bomb leaves heading and walking alone",()=>{
            var m=Match();var before=Player(m);
            m.SetInput(0,new PlayerInput(1,0,AimTarget:new(0,0)));Step(m,4);var after=Player(m);
            Assert(N(after,"fx")>.999f&&N(after,"x")>N(before,"x")+.4f&&!after.GetProperty("aimSet").GetBoolean(),"An empty-handed pointer changed facing or movement.");
        });
        Check("deliberate turn from a diagonal to a cardinal still turns promptly",()=>{
            var m=Match();m.SetInput(0,new PlayerInput(1,1));Step(m,6);
            m.SetInput(0,new PlayerInput(1,0));Step(m,5);var p=Player(m);
            Assert(N(p,"fx")>.999f&&Math.Abs(N(p,"fz"))<.001f,"Held cardinal input never replaced the old diagonal heading.");
        });
        Check("new carry never inherits previous throw intent",()=>{
            var m=Match();Lift(m);m.SetInput(0,new PlayerInput(1,1,B:true,Aim:true));Step(m,20);
            m.SetInput(0,default);Step(m,2);
            m.SetInput(0,new PlayerInput(0,0,A:true,B:true));Step(m,2);
            Assert(Player(m).GetProperty("carrying").GetString()=="bomb"&&!Player(m).GetProperty("aimSet").GetBoolean(),"A second carried bomb inherited old intent.");
        });
        Check("authoritative wall stop lands exactly where shared static guide predicts",()=>{
            var m=Match(raised:true);Lift(m);var p=Player(m);
            var projection=BombThrowRules.Project(new(N(p,"x"),BombThrowRules.LaunchHeight,N(p,"z")),BombThrowRules.Velocity(0,1,0,0),
                (x,z)=>z>=13?4:0,_=>false,cellKind:(x,z)=>CellKind.Floor);
            m.SetInput(0,default);Step(m,50);var actual=Bomb(m);
            Assert(projection.End==ThrowProjectionEnd.Obstructed&&Math.Abs(N(actual,"z")-projection.Last.Z)<.011f&&N(actual,"y")==0,"Wall impact diverged from shared advisory.");
        });
        Check("aimed bombs rest on shared cover and pillar tops without sinking",()=>{
            foreach(var kind in new[]{CellKind.Floor,CellKind.Pillar}){
                var m=Match(support:kind);Lift(m);m.SetInput(0,new PlayerInput(0,1,B:true,Aim:true));Step(m,6);
                m.SetInput(0,new PlayerInput(0,0,B:true,Aim:true));Step(m,2);
                m.SetInput(0,default);Step(m,64);var bomb=Bomb(m);
                var expected=kind==CellKind.Pillar?BombThrowRules.PillarHeight:BombThrowRules.CoverHeight;
                Assert(bomb.GetProperty("state").GetInt32()==0&&Math.Abs(N(bomb,"y")-expected)<.001f,"Descending bomb sank into a solid top.");
                Assert(bomb.GetProperty("remote").GetBoolean(),"Fixture lost remote command semantics.");
            }
        });
        Check("destroying a perched bomb support starts gravity and preserves remote fuse",()=>{
            var m=Match(support:CellKind.Floor);Lift(m);m.SetInput(0,new PlayerInput(0,1,B:true,Aim:true));Step(m,6);
            m.SetInput(0,new PlayerInput(0,0,B:true,Aim:true));Step(m,2);m.SetInput(0,default);Step(m,64);
            var fuse=N(Bomb(m),"fuse");
            // Isolate support invalidation from the explosion's separate chain-reaction rule.
            // Throws and landings above still use ordinary authoritative input messages.
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var session=(BlastSession)typeof(LocalMatch).GetField("_session",flags)!.GetValue(m)!;
            var grid=(SoftBlockGrid)typeof(BlastSession).GetField("_softBlocks",flags)!.GetValue(session)!;
            grid.Set(10,15,false);
            bool fell=false,landed=false;
            for(int i=0;i<100;i++){
                m.Tick();var bombs=State(m).GetProperty("bombs").EnumerateArray();
                var perched=bombs.FirstOrDefault(b=>b.GetProperty("ownerId").GetString()=="local-0");
                Assert(perched.ValueKind!=JsonValueKind.Undefined,"Support explosion unexpectedly chained the perched remote.");
                fell|=perched.GetProperty("state").GetInt32()==3&&N(perched,"y")>0&&N(perched,"y")<BombThrowRules.CoverHeight;
                landed|=fell&&perched.GetProperty("state").GetInt32()==0&&N(perched,"y")==0;
                Assert(N(perched,"fuse")==fuse,"Support fall changed the remote fuse.");
            }
            Assert(fell&&landed,"Removed cover left a floating bomb or teleported it to ground.");
        });
        Check("outgoing shore footprint does not catch the land behind a descending throw",()=>{
            float Surface(float x,float z)=>z<22?0:-1;
            CellKind Kind(int x,int z)=>z<22?CellKind.Floor:CellKind.Water;
            var step=BombThrowRules.AdvanceStatic(new(6.5f,-.01f,22.05f),new(0,-2,4.5f),BombThrowRules.SimulationStep,Surface,Kind,(x,z)=>false);
            Assert(step.Position.Z>22.19f&&step.Velocity.Z==4.5f,"Trailing shoreline stopped an outgoing falling bomb.");
            var arc=BombThrowRules.Project(new(6.5f,BombThrowRules.LaunchHeight,18.01f),BombThrowRules.Velocity(0,1,0,0),Surface,_=>false,
                landingKind:(x,z)=>Kind((int)MathF.Floor(x),(int)MathF.Floor(z)),cellKind:Kind);
            Assert(arc.End==ThrowProjectionEnd.Water&&arc.Last.Y==-1&&arc.Last.Z>22.35f,"Shoreline advisory snagged on the floor behind the bomb.");
        });
        Check("ordinary authoritative throw clears shore and agrees with shared water projection",()=>{
            var m=Match(waterEdge:true);
            m.SetInput(0,new PlayerInput(0,-.3f));Step(m,11);m.SetInput(0,new PlayerInput(0,.2f));Step(m,1);
            Lift(m);var p=Player(m);
            var arc=BombThrowRules.Project(new(N(p,"x"),BombThrowRules.LaunchHeight,N(p,"z")),BombThrowRules.Velocity(0,1,0,0),
                (x,z)=>z<14?0:-1,_=>false,landingKind:(x,z)=>z<14?CellKind.Floor:CellKind.Water,cellKind:(x,z)=>z<14?CellKind.Floor:CellKind.Water);
            Assert(arc.End==ThrowProjectionEnd.Water&&arc.Last.Z>14.35f,$"Fixture failed to exercise the outgoing shoreline: origin={p}, end={arc.End}, last={arc.Last}.");
            m.SetInput(0,default);bool below=false,removed=false;
            for(int i=0;i<65;i++){
                m.Tick();var bombs=State(m).GetProperty("bombs");
                if(bombs.GetArrayLength()==0){removed=true;break;}
                var b=bombs[0];
                if(N(b,"y")<0){
                    below=true;Assert(N(b,"z")>14,"Authority snagged at outgoing land.");
                    var point=new Vector3(N(b,"x"),N(b,"y"),N(b,"z"));
                    Assert(arc.Points.Any(expected=>Vector3.Distance(expected,point)<.025f),"Actual shoreline flight diverged from shared projection.");
                }
            }
            Assert(below&&removed,"Thrown bomb failed to descend to water and disappear before its fuse.");
            Assert(!State(m).GetProperty("events").EnumerateArray().Any(e=>e.GetProperty("kind").GetString()=="blast"),"Shoreline bomb reached its fuse instead of water.");
        });
        Check("accepted flood plane ends the guide over submerged floor with fuse precedence",()=>{
            var origin=new Vector3(6.5f,BombThrowRules.LaunchHeight,6.5f);var velocity=BombThrowRules.Velocity(0,1,0,0);
            var flooded=BombThrowRules.Project(origin,velocity,(x,z)=>0,_=>false,waterLevel:.5f);
            var dry=BombThrowRules.Project(origin,velocity,(x,z)=>0,_=>false);
            var fuse=BombThrowRules.Project(origin,velocity,(x,z)=>0,_=>false,.01f,waterLevel:.5f);
            Assert(flooded.End==ThrowProjectionEnd.Water&&flooded.Last.Y==.5f&&flooded.Last.Z<dry.Last.Z,"Current flood plane promised a dry floor landing.");
            Assert(fuse.End==ThrowProjectionEnd.Fuse,"Flood projection extended the remaining fuse.");
        });
        Check("height aware sweep clears low cover and stops a tall terrace face",()=>{
            float Surface(float x,float z)=>z>=13?4:0;
            CellKind Kind(int x,int z)=>CellKind.Floor;
            var origin=new Vector3(10.5f,BombThrowRules.LaunchHeight,10.5f);
            var velocity=BombThrowRules.Velocity(0,1,0,0);
            var wall=BombThrowRules.Project(origin,velocity,Surface,_=>false,cellKind:Kind);
            Assert(wall.End==ThrowProjectionEnd.Obstructed&&wall.Last.Z<13-BlastBomb.Radius+.11f&&wall.Last.Y==0,"Low throw tunneled into the tall terrace.");
            var lob=BombThrowRules.Project(origin,BombThrowRules.AimedVelocity(0,1,4.5f),(x,z)=>0,_=>false,cellKind:Kind,hasCover:(x,z)=>z==12);
            Assert(lob.End==ThrowProjectionEnd.Landing&&Math.Abs(lob.Last.Z-15)<.001f,"High lob incorrectly hit cover below it.");
            var shortFuse=BombThrowRules.Project(origin,velocity,Surface,_=>false,.6f,cellKind:Kind);
            Assert(shortFuse.End==ThrowProjectionEnd.Fuse,"Static impact extended remaining fuse.");
        });
        return count;
    }
    static LocalMatch Match(bool raised=false,CellKind? support=null,bool waterEdge=false)
    {
        var cells=new CellDef[31,31];for(int x=0;x<31;x++)for(int z=0;z<31;z++)cells[x,z]=new CellDef{Kind=CellKind.Floor,Tier=raised&&z>=13?4:0};
        if(waterEdge)for(int x=0;x<31;x++)for(int z=14;z<31;z++)cells[x,z]=CellDef.Water;
        if(support==CellKind.Pillar)cells[10,15]=CellDef.Pillar;
        var m=new LocalMatch(new MatchOptions{HumanPlayers=2,BotPlayers=0,Practice=true},new StageLayout{
            Id="aim-fixture",Width=31,Height=31,Cells=cells,Spawns=[(10,10),support is null?(waterEdge?(28,10):(28,28)):(11,15)],SoftBlockDensity=0,
            TrainingItems=support is null?[]:[(10,10,"remote")],FixedSoftBlocks=support==CellKind.Floor?new HashSet<(int,int)>{(10,15)}:null});
        Step(m,64);return m;
    }
    static void Lift(LocalMatch m){m.SetInput(0,new PlayerInput(0,0,A:true,B:true));Step(m,2);m.SetInput(0,new PlayerInput(0,0,B:true));Step(m,2);Assert(Player(m).GetProperty("carrying").GetString()=="bomb","Fixture did not lift.");}
    static void Step(LocalMatch m,int ticks){for(int i=0;i<ticks;i++)m.Tick();}
    static JsonElement State(LocalMatch m){using var doc=JsonDocument.Parse(m.StateJson!);return doc.RootElement.GetProperty("d").Clone();}
    static JsonElement Player(LocalMatch m)=>State(m).GetProperty("players")[0];
    static JsonElement Bomb(LocalMatch m)=>State(m).GetProperty("bombs")[0];
    static float N(JsonElement element,string property)=>element.GetProperty(property).GetSingle();
    static void Assert(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
}
