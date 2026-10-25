if(args.Contains("--stage-integration")){StageIntegrationChecks.Run();return;}
if(args.Contains("--tutorial-review")){TutorialChecks.ReviewRegressions();return;}
if(args.Contains("--tutorial")){TutorialChecks.Run();return;}
if(args.Contains("--top-rules")){TopRulesChecks.Run();return;}
if(args.Contains("--up-and-down"))
{
    int count=0;
    UpAndDownChecks.Run((name,check)=>{check();count++;Console.WriteLine("PASS "+name);});
    Console.WriteLine($"Passed {count} Up & Down integration checks.");return;
}

if(args.Contains("--snapshot-performance")){SnapshotPerformance.Run(args);return;}
if(args.Contains("--online-rooms")){OnlineRoomChecks.Run();return;}

if(args.Contains("--pyramid-ledges")){PyramidLedgeChecks.Run();return;}

if(args.Contains("--party-polish")){PartyPolishChecks.Run();return;}

if(args.Contains("--advanced")){AdvancedRulesChecks.Run();return;}

if(args.Contains("--item-expiration")){ItemExpirationChecks.Run();return;}

if(args.Contains("--sandbox")){SandboxEquipmentChecks.Run();return;}

if(args.Contains("--cover-layout")){RegionalCoverChecks.Run();return;}

var passed = StageIntegrationChecks.Run() + TutorialChecks.Run() + TutorialChecks.ReviewRegressions() + TopRulesChecks.Run() + AdvancedRulesChecks.Run() + ItemExpirationChecks.Run() + RegionalCoverChecks.Run() + SandboxEquipmentChecks.Run() + PyramidLedgeChecks.Run() + PartyPolishChecks.Run() + PartySessionChecks.Run() + LootAvailabilityChecks.Run() + AimedThrowChecks.Run() + CarryChargeChecks.Run() + BombPlacementChecks.Run() + PowerUpModeChecks.Run() + BombTravelChecks.Run() + KickTimingChecks.Run() + BodyContactChecks.Run() + BombImpactChecks.Run() + TraversalAndGhostChecks.Run() + BombActionsChecks.Run() + GrappleChecks.Run();
SnapshotChecks.Run(Run);
passed += OnlineRoomChecks.Run();
MatchRulesChecks.Run(Run);
TerrainChecks.Run(Run);
UpAndDownChecks.Run(Run);
BotChecks.Run(Run);
PracticeAndOutcomeChecks.Run(Run);
ItemsAndHazardsChecks.Run(Run);
AppearanceChecks.Run(Run);
PresentationEventChecks.Run(Run);
Console.WriteLine($"Passed {passed} game integration checks.");

void Run(string name, Action check) { check(); passed++; Console.WriteLine($"PASS {name}"); }
