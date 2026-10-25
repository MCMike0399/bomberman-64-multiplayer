using Godot;
using Bomber.Client;
using Bomber.Game;
using BlastCircuit.Arena.Models;
using BlastCircuit.UI.Menu;
namespace BlastCircuit.Testing;

public partial class PlaytestDriver
{
    private async Task AdvancedSettingsTour()
    {
        _root.Navigate("Setup");await Frames(3);await ActivateNamed("AdvancedSettings");
        Check(_root.ScreenName=="Advanced","match configuration opens the dedicated advanced page");
        var menu=_root.FindChildren("*","",true,false).OfType<ArcadeMenu>().Single();
        Check(menu.Settings.Advanced.DropPercent==40,"advanced default drop chance is 40 percent");
        await ActivateNamed("Itemdropchance");Check(menu.Settings.Advanced.DropPercent==45,"native dial changes independent drop chance");
        Check(_root.FindChild("Allow_fast",true,false)==null&&_root.FindChild("Allow_haste",true,false)!=null,"advanced menu exposes Haste without Fast bomb equipment");
        await ActivateNamed("Allow_big");Check(!menu.Settings.Advanced.Allows("big"),"native toggle disables a debuff item");
        await Capture("advanced-match-settings");
        await KeyPress(Key.Escape);Check(_root.ScreenName=="Setup","Escape returns to match setup");
        await ActivateNamed("AdvancedSettings");Check(menu.Settings.Advanced.DropPercent==45&&!menu.Settings.Advanced.Allows("big"),"advanced edits survive back navigation");
        await ActivateNamed("ResetAdvanced");Check(menu.Settings.Advanced==new AdvancedRules(),"restore defaults resets every advanced option");
        await ActivateNamed("AdvancedBack");
        foreach(var (kind,x,z,scale) in new[]{("tiny",9.5f,15.5f,.65f),("big",15.5f,15.5f,1.4f),("haste",10.5f,16.5f,1f),("slow",14.5f,16.5f,1f)})
        {
            _root.StartLocal(new SessionSettings(Bots:0){Advanced=new AdvancedRules{EffectSeconds=5,DisabledItems=string.Join(',',AdvancedRules.ItemTypes.Where(t=>t!=kind))}},1,practice:true);
            await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"status practice starts: "+kind,150);
            await MoveTo(x,12.5f,100);await MoveTo(x,z,70);
            await WaitState(_=>Player.GetProperty("statusEffect").GetString()==kind,"ordinary walking collects "+kind,15);
            await Frames(3);
            var model=LightingArena.FindChildren("*","",true,false).OfType<BomberModel>().Single(m=>m.IsVisibleInTree());
            Check(Math.Abs(model.Scale.X-1.12f*scale)<.01f,"actual character renders the accepted status size: "+kind);
            await Capture("status-"+kind);
            await WaitState(_=>Player.GetProperty("statusEffect").GetString()=="none","temporary status wears off: "+kind,180);
            await Frames(3);Check(Math.Abs(model.Scale.X-1.12f)<.01f,"normal model scale returns after "+kind);
            _root.Back();await Frames(3);
        }
        _root.StartLocal(new SessionSettings(Bots:0){Advanced=new AdvancedRules{ItemSeconds=5}},1,practice:true);
        await WaitState(s=>s.GetProperty("phase").GetInt32()==1,"expiration practice starts",150);
        Check(State.GetProperty("items").GetArrayLength()>0,"practice starts with real uncollected pickups");
        await Capture("items-before-expiration");
        await WaitState(s=>s.GetProperty("items").GetArrayLength()==0,"all uncollected sandbox items expire",180);
        await Frames(4);Check(!LightingArena.FindChildren("*","",true,false).OfType<PowerupModel>().Any(m=>m.IsVisibleInTree()),"expired items leave the actual rendered arena");
        await Capture("items-after-expiration");_root.Back();await Frames(3);
    }
}
