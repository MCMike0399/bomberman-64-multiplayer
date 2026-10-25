using Bomber.Game;
using Bomber.Game.Simulation;
using static GameFixtures;

/// <summary>Real inputs across both stories, with shared authority/geometry and wire data.</summary>
internal static class TopRulesChecks
{
    public static int Run()
    {
        int passed = 0;
        void Check(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
        LocalMatch Match(StageLayout? layout = null) => layout is null
            ? new(new MatchOptions { Stage = BlastStages.TopRules, HumanPlayers = 1, BotPlayers = 0, Practice = true })
            : new(new MatchOptions { Stage = BlastStages.TopRules, HumanPlayers = 1, BotPlayers = 0, Practice = true }, layout);
        Check("Top Rules selects and serializes both stories", () =>
        {
            var match = Match();
            var data = Data(match.StageJson!);
            Assert(data.GetProperty("id").GetString() == "top-rules", "Stage fell back to another arena.");
            Assert(data.GetProperty("underpasses").GetArrayLength() > 40, "Network stage lost lower passages.");
            var stage = BlastStages.Get(BlastStages.TopRules);
            Assert(stage.Spawns.Distinct().Count() == 8, "Eight distinct safe starts required.");
            Assert(stage.SurfaceAt(5.5f, 8.5f) == 2 && stage.SurfaceAt(5.5f, 8.5f, .25f) == 0, "Bridge must have two supports.");
        });
        Check("Top Rules ordinary movement descends stairs and crosses beneath all three bridges", () =>
        {
            var match = Match(); Fight(match);
            Walk(match, 5.5f, 1.5f); Walk(match, 2.5f, 1.5f); Walk(match, 2.5f, 6.5f);
            Assert(Player(match).GetProperty("y").GetSingle() == 0, "Rear staircase did not reach lower floor.");
            Walk(match, 16.5f, 6.5f);
            Assert(Player(match).GetProperty("y").GetSingle() == 0, "Lower route snapped onto a bridge.");
            Walk(match, 16.5f, 1.5f);
            Assert(Player(match).GetProperty("y").GetSingle() == 2, "Opposite stairs did not reach upper perimeter.");
            Walk(match, 13.5f, 1.5f); Walk(match, 13.5f, 8.5f); Walk(match, 5.5f, 8.5f);
            Assert(Player(match).GetProperty("y").GetSingle() == 2, "Crosswalk lost upper support.");
            Walk(match, 5.5f, 15.5f); Walk(match, 2.5f, 15.5f); Walk(match, 2.5f, 10.5f);
            Assert(Player(match).GetProperty("y").GetSingle() == 0, "Front stairs did not reach lower floor.");
            Walk(match, 16.5f, 10.5f); Walk(match, 16.5f, 15.5f);
            Assert(Player(match).GetProperty("y").GetSingle() == 2, "Fourth staircase disconnected.");
        });
        Check("Top Rules lower bombs stay below bridges and preserve their fuse", () =>
        {
            var match = Match(); Fight(match);
            Walk(match, 5.5f, 1.5f); Walk(match, 2.5f, 1.5f); Walk(match, 2.5f, 6.5f); Walk(match, 5.5f, 6.5f);
            match.SetInput(0, new PlayerInput(0, 0, A: true)); Step(match, 2); match.SetInput(0, default); Step(match, 2);
            var bomb = State(match).GetProperty("bombs")[0];
            Assert(bomb.GetProperty("y").GetSingle() == 0, "Ground bomb appeared on upper deck.");
            Walk(match, 2.5f, 6.5f); Step(match, 90);
            Assert(State(match).GetProperty("bombs").GetArrayLength() == 0, "Bomb fuse stopped under deck.");
            Assert(Events(match).Any(e => Kind(e) == "blast"), "No real detonation under deck.");
        });
        Check("Top Rules bridge slab shields stories but leaves lower horizontal blasts open", () =>
        {
            var stage = BlastStages.Get(BlastStages.TopRules); var soft = new SoftBlockGrid(stage.Width, stage.Height);
            Assert(BlastGeometry.Contains(stage, soft, 4.5f, 0, 4.5f, 4, 6.5f, 0, 4.5f, true), "Bridge became a solid wall below.");
            Assert(!BlastGeometry.Contains(stage, soft, 5.5f, 0, 4.5f, 4, 5.5f, 2, 4.5f, true), "Blast passed through deck slab.");
        });
        Check("Top Rules walking off a bridge falls to court and cannot climb back through its slab", () =>
        {
            var match=Match();Fight(match);Walk(match,5.5f,4.5f);
            match.SetInput(0,new PlayerInput(1,0));bool falling=false;
            for(int tick=0;tick<20;tick++){match.Tick();falling|=Player(match).GetProperty("falling").GetBoolean();}
            match.SetInput(0,default);Step(match,30);
            Assert(falling&&Player(match).GetProperty("y").GetSingle()==0,"Bridge exit skipped real gravity or did not reach court.");
            Walk(match,5.5f,4.5f);
            Assert(Player(match).GetProperty("y").GetSingle()==0,"Lower player climbed through upper slab.");
        });
        Check("Top Rules kicked lower bomb passes under bridge without hitting upper player", () =>
        {
            var original=BlastStages.Get(BlastStages.TopRules);
            var fixture=new StageLayout{Id=original.Id,Width=original.Width,Height=original.Height,Cells=original.Cells,Underpasses=original.Underpasses,Spawns=[(4,4),(5,4)]};
            var match=new LocalMatch(new MatchOptions{Stage=BlastStages.TopRules,HumanPlayers=2,BotPlayers=0,Practice=true},fixture);Fight(match);
            match.SetInput(0,new PlayerInput(1,0));Step(match,1);
            match.SetInput(0,new PlayerInput(0,0,A:true));Step(match,2);match.SetInput(0,default);Step(match,2);
            match.SetInput(0,new PlayerInput(0,0,A:true));Step(match,2);match.SetInput(0,default);
            bool crossed=false;
            for(int tick=0;tick<15;tick++)
            {
                match.Tick();
                foreach(var bomb in State(match).GetProperty("bombs").EnumerateArray())
                    crossed|=bomb.GetProperty("x").GetSingle()>6&&bomb.GetProperty("y").GetSingle()<.2f;
            }
            Assert(crossed,"Lower kick stopped at bridge wall or teleported onto deck.");
            Assert(!Player(match,1).GetProperty("stunned").GetBoolean()&&Player(match,1).GetProperty("y").GetSingle()==2,"Lower bomb hit upper player.");
        });
        Check("Top Rules lower throw hits ceiling and shared projection agrees with authority landing", () =>
        {
            var original=BlastStages.Get(BlastStages.TopRules);
            var fixture=new StageLayout{Id=original.Id,Width=original.Width,Height=original.Height,Cells=original.Cells,Underpasses=original.Underpasses,Spawns=[(4,4)]};
            var match=Match(fixture);Fight(match);Walk(match,5.5f,4.5f);
            var stage=original;var soft=new SoftBlockGrid(stage.Width,stage.Height);
            var projection=BombThrowRules.Project(new(5.5f,BombThrowRules.LaunchHeight,4.5f),BombThrowRules.AimedVelocity(1,0,4.5f),
                (x,z)=>stage.SurfaceAt(x,z),_=>false,cellKind:(x,z)=>stage.At(x,z).Kind,hasCover:(_,_)=>false,
                layeredSurface:stage.SurfaceAt,deckIntersects:stage.DeckIntersects);
            Assert(projection.Last.Y==0&&projection.Points.Max(p=>p.Y)<2,"Lower advisory throw passed through deck ceiling.");
            match.SetInput(0,new PlayerInput(0,0,A:true,B:true));Step(match,2);
            match.SetInput(0,new PlayerInput(0,0,B:true,Aim:true));Step(match,2);
            match.SetInput(0,default);
            bool landed=false;
            for(int tick=0;tick<45;tick++)
            {
                match.Tick();
                foreach(var bomb in State(match).GetProperty("bombs").EnumerateArray())
                    if(bomb.GetProperty("state").GetInt32()==0)
                    {Assert(bomb.GetProperty("y").GetSingle()==0,"Lower throw landed on upper deck.");landed=true;}
            }
            Assert(landed,"Lower throw never settled on the court.");
        });
        Check("Top Rules fast falling kick lands on the upper slab instead of tunneling to the court", () =>
        {
            var match=Match();Fight(match);
            var session=(BlastSession)typeof(LocalMatch).GetField("_session",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(match)!;
            // Isolated contact witness: this descending shot crosses the entire
            // slab in one authority tick. The native journey uses ordinary inputs.
            var bomb=new BlastBomb{Id="fast-descent",X=5.5f,Z=6.5f,CellX=5,CellZ=6,Tier=2,
                State=BombState.Flying,ResumeRollingAfterFall=true,FlyY=2.05f,FlyVY=-12};
            session.AiTestAddBomb(bomb);Step(match,2);
            Assert(bomb.State==BombState.Resting&&bomb.Tier==2&&Math.Abs(bomb.FlyY-2)<.001f,"Falling kick selected support after crossing below the deck.");
        });
        Check("Top Rules authored castle walls stop low lobs and block elevated blasts", () =>
        {
            var stage=BlastStages.Get(BlastStages.TopRules);
            var flight=BombThrowRules.AdvanceStatic(new(5.5f,2.45f,1.5f),new(0,0,-6),.25f,
                (x,z)=>stage.SurfaceAt(x,z),(x,z)=>stage.At(x,z).Kind,(_,_)=>false,
                obstacleHeight:(x,z)=>stage.At(x,z).ObstacleHeight??BombThrowRules.PillarHeight);
            Assert(flight.Position.Z>=1.3f&&flight.Velocity.Z==0,"Low lob passed through tall rear masonry.");
            var soft=new SoftBlockGrid(stage.Width,stage.Height);
            Assert(!BlastGeometry.Contains(stage,soft,5.5f,2.2f,1.5f,3,5.5f,2.2f,-.5f,true),"Elevated blast passed through rear wall.");
            var cells=Data(Match().StageJson!).GetProperty("cells");
            Assert(Math.Abs(cells[5].GetProperty("oh").GetSingle()-3.1f)<.001f,"Wall crown was lost on the network boundary.");
        });
        return passed;
    }
}
