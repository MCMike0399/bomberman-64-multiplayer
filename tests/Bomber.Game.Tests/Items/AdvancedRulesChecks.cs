using System.Text.Json;
using Bomber.Game.Matches;
using Bomber.Game.Online;
using Bomber.Game.Simulation.Rules;
using Bomber.Game.Simulation.Stages;
using Bomber.Game.Simulation.State;
using Bomber.Game.Tutorial;
internal static class AdvancedRulesChecks
{
    public static int Run()
    {
        int checks=0;
        void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
        var defaults=new AdvancedRules();Check(defaults.DropPercent==40&&defaults.MaxFire==5&&defaults.MaxBombs==5,"Unexpected defaults.");
        Check(AdvancedRules.ItemTypes.Where(PowerUpRules.IsBombType).Order().SequenceEqual(new[]{"bounce","red","remote"}),"Only Bounce, Red and Remote should occupy the bomb slot.");
        Check(!AdvancedRules.ItemTypes.Contains("fast")&&!PowerUpRules.CanCollect(new BlastPlayer(),"fast"),"Removed Fast bomb still exists as a pickup.");
        foreach(string stage in new[]{BlastStages.SunkenTemple,BlastStages.RockGarden,BlastStages.UpAndDown,BlastStages.TopRules,BlastStages.CircuitGarden})
            Check(BlastStages.Get(stage).InitialItems.Concat(BlastStages.Get(stage).TrainingItems).All(item=>AdvancedRules.ItemTypes.Contains(item.Type)),"Stage contains a removed pickup: "+stage);
        Check(!TutorialCourse.Contains("fast"),"Tutorial still teaches removed Fast bombs.");
        foreach(string type in AdvancedRules.ItemTypes)
        {
            var only=defaults with{DisabledItems=string.Join(',',AdvancedRules.ItemTypes.Where(t=>t!=type))};
            for(int i=0;i<100;i++)Check(PowerUpRules.ChooseDrop(new Random(i),only)==type,"Disabled item leaked through the pool.");
        }
        Check(PowerUpRules.ChooseDrop(new Random(1),defaults with{DisabledItems=string.Join(',',AdvancedRules.ItemTypes)}) is null,"Empty pool creates items.");
        var rules=defaults with{DropPercent=0,BlockPercent=45,ItemSeconds=5,MaxBombs=2,MaxFire=4,EffectSeconds=5,DisabledItems="red,big"};
        var wire=new MatchOptions{Advanced=rules}.ToSettings();
        Check(AdvancedRules.FromSettings(wire)==rules,"Rules did not round trip.");
        using(var edit=JsonDocument.Parse(JsonSerializer.Serialize(wire)))
            Check(RoomRules.TryUpdate(new Dictionary<string,string>(),edit.RootElement,out var accepted)&&AdvancedRules.FromSettings(accepted)==rules,"Room rejected valid advanced edit.");
        foreach(var json in new[]{"{\"drop-percent\":\"101\"}","{\"item-seconds\":\"0\"}","{\"max-fire\":\"6\"}","{\"disabled-items\":\"unknown\"}","{\"max-bombs\":\"99999999999999999\"}"})
        {using var edit=JsonDocument.Parse(json);Check(!RoomRules.TryUpdate(wire,edit.RootElement,out var result)&&ReferenceEquals(wire,result),"Invalid edit changed room rules.");}
        foreach(string effect in new[]{"tiny","big","haste","slow"})
        {
            var match=Match(effect,rules with{DisabledItems=""});Step(match,64);
            var p=Player(match);Check(p.GetProperty("statusEffect").GetString()==effect,"Real pickup did not apply status.");
            float before=p.GetProperty("x").GetSingle();match.SetInput(0,new PlayerInput(1,0));Step(match,20);match.SetInput(0,default);
            float travel=Player(match).GetProperty("x").GetSingle()-before;
            Check(Math.Abs(travel-(effect is "tiny" or "haste"?4.5f:1.95f))<.05f,"Status did not change real movement speed.");
            Step(match,140);p=Player(match);Check(p.GetProperty("statusEffect").GetString()=="none"&&p.GetProperty("statusScale").GetSingle()==1,"Status failed to expire.");
            Check(p.GetProperty("bombs").GetInt32()==2,"Starting inventory exceeded custom cap.");
        }
        var player=new BlastPlayer();PowerUpRules.Reset(player,rules);
        for(int i=0;i<12;i++){PowerUpRules.Apply(player,"fire");PowerUpRules.Apply(player,"bomb");}
        Check(player.FireLevel==4&&player.BombCapacity==2,"Custom stack caps ignored.");
        PowerUpRules.Apply(player,"tiny");PowerUpRules.Apply(player,"big");Check(player.StatusEffect=="big"&&player.StatusRemaining==5,"Effect replacement failed.");PowerUpRules.Reset(player,rules);Check(player.StatusEffect=="none","Reset kept effect.");
        Console.WriteLine($"PASS {checks} advanced rules, item filtering, atomic edits, real status pickups/movement/expiry and custom caps checks");return checks;
    }
    static LocalMatch Match(string effect,AdvancedRules rules)
    {
        var cells=new CellDef[40,20];for(int x=0;x<40;x++)for(int z=0;z<20;z++)cells[x,z]=CellDef.Floor0;
        return new LocalMatch(new MatchOptions{HumanPlayers=2,BotPlayers=0,SuddenDeath=false,Advanced=rules},new StageLayout{Id="effects",Width=40,Height=20,Cells=cells,Spawns=[(4,4),(35,15)],SoftBlockDensity=0,InitialItems=[(4,4,effect)]});
    }
    static void Step(LocalMatch match,int ticks){for(int i=0;i<ticks;i++)match.Tick();}
    static JsonElement Player(LocalMatch match){using var d=JsonDocument.Parse(match.StateJson!);return d.RootElement.GetProperty("d").GetProperty("players")[0].Clone();}
}
