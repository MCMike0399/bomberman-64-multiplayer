namespace Bomber.Game.Simulation;

/// <summary>Internal inspection and fixture hooks for the deterministic AI suite (Bomber.Ai.Tests).</summary>
public sealed partial class BlastSession
{
    internal int AiTestThinkCount {get;private set;}
    internal RoundPhase AiTestPhase => _phase;
    internal IReadOnlyList<BlastBomb> AiTestBombs => _bombs;
    internal IReadOnlyList<BlastHazard> AiTestHazards => _hazards;
    internal IReadOnlyCollection<BlastPlayer> AiTestPlayers => _players.Values;
    internal void AiTestAddBot(string id, int slot) => _players[id] = new BlastPlayer { Id=id,Slot=slot,IsBot=true,DisplayName="CPU "+slot };
    internal void AiTestAddHuman(string id,int slot) => _players[id]=new BlastPlayer {Id=id,Slot=slot,DisplayName="Stationary opponent"};
    internal void AiTestAddItem(int x,int z,string type) => _items.Add(new BlastItem {Id="fixture-item",CellX=x,CellZ=z,Tier=_stage.At(x,z).Tier,Type=type});
    internal void AiTestAddBomb(BlastBomb bomb) {_bombs.Add(bomb);_aiFieldTick=-1;}
    internal void AiTestPosition(string id,float x,float z) {var p=_players[id];p.X=x;p.Z=z;p.SurfaceY=_stage.SurfaceAt(x,z);p.Tier=(int)MathF.Round(p.SurfaceY);_aiFieldTick=-1;}
    internal void AiTestFightTime(float seconds) {_roundTimer=seconds;_fightTicks=(int)(seconds*30);_phase=RoundPhase.Hurry;_aiFieldTick=-1;}
    internal bool AiTestRemoteOpportunity(string id) {BotRefreshField();return BotRemoteOpportunity(_players[id]);}
    internal void AiTestThink(string id,float dt=.2f) { var bot=_players[id];bot.BotThinkTimer=0;BotThink(bot,dt); }
    internal void AiTestSetSoft(int x,int z,bool alive) => _softBlocks.Set(x,z,alive);
}
