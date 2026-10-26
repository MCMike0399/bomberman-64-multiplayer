using Godot;
using Bomber.Client.Sessions;
using Bomber.Game.Simulation.Rules;
using BlastCircuit.Arena.Effects;
using BlastCircuit.InputDevices;
using BlastCircuit.UI;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task ReticleVisibility()=>await WithControllerFocusRetries(ReticleVisibilityAttempt,"reticle");
    private async Task ReticleVisibilityAttempt()
    {
        var hub=ControllerHub.Current;
        try
        {
            CtlReleaseAll();hub.OverrideDevicesForPlaytest([]);
            _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:0),2);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"normal two-human temple match starts",180);
            _root.InjectInput(0,null);_root.InjectInput(1,null);
            await ControllerFocusReady();_controllerFocusTrial=true;
            CtlKey(Key.K,true);await ControllerFrames(2);
            CtlKey(Key.J,true);await ControllerFrames(2);CtlKey(Key.J,false);await ControllerFrames(4);
            Check(Player.GetProperty("carrying").GetString()=="bomb","normal match K plus J carries a bomb overhead");
            var preview=_root.ThrowPreviews.Single();Event("normal-reticle",preview);
            await Capture("normal-reticle-overhead",0);
            Check(preview.EndpointMarkerVisible,"normal carrier has a visible endpoint reticle even near stock cover");
            ReticlePixels(preview);
            foreach(var key in new[]{Key.D,Key.W,Key.A,Key.S})
            {
                CtlKey(key,true);await ControllerFrames(3);CtlKey(key,false);await ControllerFrames(3);
                preview=_root.ThrowPreviews.Single();Event("normal-reticle-direction",new{key=key.ToString(),preview});
                await Capture("normal-reticle-"+key,0);
                Check(preview.EndpointMarkerVisible,"normal carrier endpoint remains marked facing "+key+" even when cover truncates the arc");
                ReticlePixels(preview);
            }
            // Facing south, move the cursor north toward the nearby cover. Stop
            // on accepted range rather than relying on the old polar-aim clamp.
            CtlKey(Key.L,true);await ControllerFrames(3);CtlKey(Key.W,true);
            for(int frame=0;frame<40&&AimNumber(Player,"aimRange")>2.1f;frame++)await ControllerFrames(1);
            CtlKey(Key.W,false);await ControllerFrames(3);
            preview=_root.ThrowPreviews.Single();Event("normal-reticle-short",preview);await Capture("normal-reticle-short",0);
            Check(preview.EndpointMarkerVisible,"short-range ordinary keyboard aim retains a visible endpoint marker");
            Check(preview.End=="Obstructed"&&!preview.LandingMarkerVisible,"stock cover produces a warning cross without suggesting a safe landing");
            ReticlePixels(preview);
            CtlKey(Key.M,true);await ControllerFrames(2);CtlKey(Key.N,true);await ControllerFrames(2);CtlKey(Key.N,false);await ControllerFrames(3);
            Check(_root.ThrowPreviews.Count==2&&_root.ThrowPreviews.Select(p=>p.PlayerId).Distinct().Count()==2,
                "both local keyboard humans receive their own carrier markers");
            await Capture("normal-reticle-two-carriers",0);
            foreach(var owned in _root.ThrowPreviews)ReticlePixels(owned);
            CtlKey(Key.M,false);await ControllerFrames(4);
            Check(_root.ThrowPreviews.Count==1&&_root.ThrowPreviews.Single().PlayerId==preview.PlayerId,
                "P2 release removes only P2's marker and preserves P1's held aim");
            // Isolated renderer contract, separately from the ordinary normal-match witness above.
            // No authority actors, snapshots or input values are mutated for these outcome cases.
            var probe=new BombThrowPreview();_root.AddChild(probe);
            try
            {
                var camera=GetViewport().GetCamera3D();
                foreach(var end in Enum.GetValues<ThrowProjectionEnd>())
                {
                    var endpoint=new System.Numerics.Vector3(preview.X+2,preview.Y,preview.Z);
                    probe.Update(new ThrowProjection([endpoint],end),0,6);probe.UpdateScreen(camera);
                    await Capture("reticle-render-contract-"+end,0);
                    Check(probe.EndpointMarkerVisible&&probe.LandingMarkerVisible==(end==ThrowProjectionEnd.Landing),"renderer draws a visible semantic endpoint for "+end);
                    ReticlePixels(new("probe",0,endpoint.X,endpoint.Y,endpoint.Z,end.ToString(),1,probe.LandingMarkerVisible,probe.EndpointLabel,
                        EndpointMarkerVisible:probe.EndpointMarkerVisible,ScreenX:probe.EndpointScreenPosition.X,ScreenY:probe.EndpointScreenPosition.Y));
                }
                probe.Update(new ThrowProjection([new(1000,0,1000)],ThrowProjectionEnd.Void),0,6);probe.UpdateScreen(camera);
                await Capture("reticle-offscreen-arrow",0);
                Check(probe.EndpointEdgeClamped&&GetViewport().GetVisibleRect().HasPoint(probe.EndpointScreenPosition),"offscreen outcome has a visible edge arrow inside the camera frame");
                ReticlePixels(new("probe",0,1000,0,1000,"Void",1,false,probe.EndpointLabel,ScreenX:probe.EndpointScreenPosition.X,ScreenY:probe.EndpointScreenPosition.Y));
            }
            finally {probe.QueueFree();}
            CtlKey(Key.L,false);CtlKey(Key.K,false);await ControllerFrames(4);
            Check(_root.ThrowPreviews.Count==0,"releasing the final carrier removes every endpoint overlay");
            await ReticleResultsLifecycle();
        }
        finally
        {
            _controllerFocusTrial=false;CtlReleaseAll();if(_root.ActiveSession!=null)_root.Back();hub.RestorePhysicalDevicesForPlaytest();await Frames(3);
        }
    }
    private async Task ReticleResultsLifecycle()
    {
        CtlReleaseAll();_root.Back();await Frames(3);
        _root.StartLocal(new SessionSettings(Stage:"sunken-temple",Bots:0,Wins:1,Ghosts:false),2);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"reticle lifecycle normal duel reaches fight",150);
        Input(0,new Bomber.Game.Matches.PlayerInput(0,0,A:true,B:true));await Frames(2);
        Input(0,new Bomber.Game.Matches.PlayerInput(0,0,B:true));await Frames(3);
        Check(Player.GetProperty("carrying").GetString()=="bomb"&&_root.ThrowPreviews.Count==1,
            "normal survivor holds an actual bomb with an owned marker before match end");
        var held=_root.FindChildren("*","",true,false).OfType<BombThrowPreview>().Single();
        await Capture("reticle-lifecycle-carried-before-results",0);
        // The second human accepts their ordinary bomb's blast. Authority determines
        // the winner and Results transition; no snapshots or screen state are forged.
        Input(1,new Bomber.Game.Matches.PlayerInput(0,0,A:true));await Frames(2);Input(1,default);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==3,"ordinary opponent bomb ends the duel while survivor still carries",150);
        Check(_root.ScreenName=="Results"&&Player.GetProperty("carrying").GetString()=="bomb",
            "actual match-end navigation retains the survivor's carried-bomb snapshot");
        Check(_root.ThrowPreviews.Count==0&&(!GodotObject.IsInstanceValid(held)||!held.EndpointMarkerVisible),
            "leaving Match hides every carrier overlay immediately before deferred node removal");
        await Frames(4);
        Check(!_root.FindChildren("*","",true,false).OfType<ThrowEndpointReticle>().Any(marker=>marker.IsVisibleInTree()),
            "Results keeps the arena background without any frozen screen-space endpoint marker");
        await Capture("reticle-lifecycle-results-without-overlay",0);
    }
    private void ReticlePixels(ThrowPreviewState preview)
    {
        // Read only the ring/cross/arrow region. EndpointLabel is semantic metadata,
        // never a claim that a player/range/status label exists in the image.
        using var image=GetViewport().GetTexture().GetImage();
        var viewport=GetViewport().GetVisibleRect().Size;
        var center=new Vector2(preview.ScreenX*image.GetWidth()/viewport.X,preview.ScreenY*image.GetHeight()/viewport.Y);
        var color=preview.End=="Landing"?ArcadeTheme.Aqua:ArcadeTheme.Gold;
        int colored=0,dark=0;
        for(int y=Math.Max(0,(int)center.Y-24);y<Math.Min(image.GetHeight(),center.Y+24);y++)
        for(int x=Math.Max(0,(int)center.X-24);x<Math.Min(image.GetWidth(),center.X+24);x++)
        {
            var pixel=image.GetPixel(x,y);
            if(Math.Abs(pixel.R-color.R)+Math.Abs(pixel.G-color.G)+Math.Abs(pixel.B-color.B)<.20f)colored++;
            if(pixel.R<.18f&&pixel.G<.25f&&pixel.B<.45f)dark++;
        }
        Event("reticle-pixels",new{preview.End,center,colored,dark});
        Check(colored>=45&&dark>=45,"actual endpoint pixels include the colored marker and contrasting outline for "+preview.End);
    }
}
