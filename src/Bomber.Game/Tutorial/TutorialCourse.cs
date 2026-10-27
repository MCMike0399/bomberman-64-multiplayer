namespace Bomber.Game.Tutorial;

/// <summary>Product lessons shared by the local authority and the lesson browser. Tokens are device labels.</summary>
public sealed record TutorialLesson(string Id, string Chapter, string Title, string Description, string[] Objectives, string Tip);

public static class TutorialCourse
{
    public static IReadOnlyList<TutorialLesson> Lessons { get; } = Array.AsReadOnly(new TutorialLesson[]
    {
        new("move","Getting started","Find your feet","Move with {move}. Reach the gold circle on the practice court.",
            ["Reach the gold circle"],"You can move freely in every direction. Diagonal movement has the same speed."),
        new("bomb","Getting started","Light the fuse","Tap {bomb} to plant, then move away. Stay alive until the blast disappears.",
            ["Plant a bomb","Escape the blast"],"Explosions spread in a sphere. Your own bombs can eliminate you. A normal fuse lasts 2.5 seconds."),
        new("carry","Bomb handling","Pick up and throw","Hold {carry}, tap {bomb} to ready a bomb overhead, then release {carry} to throw.",
            ["Carry a bomb","Throw it"],"Holding a bomb pauses its fuse. Holding carry beside a resting bomb picks it up too."),
        new("primary-hold","Bomb handling","One-button pickup","Hold {bomb} without moving to plant and lift the same bomb. Release it, then press {carry} to throw.",
            ["Lift with a held bomb press","Throw with carry"],"Walking away cancels this pickup. A bomb lifted this way stays held until you press carry."),
        new("pump","Bomb handling","Power it up","Hold {carry} and tap {bomb} to ready. Tap {bomb} three more times, letting each charge finish. Release {carry}.",
            ["Charge to level 3","Throw the charged bomb"],"Each tap requests one charge. Charging takes 0.45 seconds per level. On Top Rules, a large blast beneath a bridge can hit players above. Leave more escape space."),
        new("aim","Bomb handling","Place your shot","Ready a bomb with {carry} + {bomb}. Keep {carry} held and {point} to place the landing ring, or hold {aim} and steer it with {move}. Release {carry} to throw.",
            ["Steer the aiming cursor","Throw your aimed bomb"],"Pointing lets you keep walking; holding aim plants your feet for fine nudges. Either way the target stays until you throw. The warning X marks an unsafe or blocked landing."),
        new("kick","Bomb handling","Kick and stop","Tap {bomb}, walk away, then walk back into the bomb to kick it. Press {stop} while it rolls.",
            ["Kick a resting bomb","Stop your rolling bomb"],"A fresh bomb lets its owner walk out once. Only the last kicker can stop it. The fuse keeps burning."),
        new("chain","Bomb handling","Start a chain reaction","Plant two bombs close together, then retreat. Let the first explosion set off the second.",
            ["Trigger a chain reaction","Survive both blasts"],"Explosions can ignite other bombs early. Kicking one bomb into another transfers its roll, so check the whole escape route."),
        new("cover","Equipment","Break a path","Destroy a marked block with a bomb and retreat safely.",
            ["Destroy a block","Survive the blast"],"Stone pillars block blasts; breakable blocks can reveal items. Drops are random in battles, and items expire."),
        new("items","Equipment","Build your loadout","Collect the flame and bomb pickups ahead of you.",
            ["Collect Blast up","Collect Bomb up"],"Blast up increases reach; Bomb up increases active capacity. Both normally cap at five. Death resets upgrades."),
        new("remote","Equipment","Choose the moment","Collect Remote, place a bomb, retreat, then press {remote}.",
            ["Collect Remote","Detonate your remote bomb"],"Remote replaces the bomb slot and waits for your command. Each press detonates the oldest eligible bomb. Losing Remote resumes its normal fuse."),
        new("red","Equipment","Power bombs","Collect Power bomb, then destroy a block with its blue blast.",
            ["Collect Power bomb","Break cover with a power blast"],"Power bombs pierce breakable cover and have a wider blast. They replace Remote or Bounce in your bomb slot."),
        new("bounce","Equipment","Make it bounce","Collect Bounce. Ready a bomb, charge it at least once, then throw it onto the lawn.",
            ["Collect Bounce","Land a charged bouncing throw"],"Charged throws rebound up to three times. Uncharged throws settle; kicked bombs roll. The fuse is 3.5 seconds."),
        new("heart","Equipment","A second chance","Collect Heart, plant a bomb, and let one blast hit you.",
            ["Collect Heart","Absorb one explosion"],"Heart shares the ability slot with Focus. It absorbs one blast and briefly protects you; it cannot save you from water or a fall."),
        new("focus","Equipment","Focus your charge","Collect Focus, then carry and fully charge a bomb.",
            ["Collect Focus","Finish a faster charge"],"Focus charges each level in 0.15 seconds. A neutral aimed cursor gently assists toward visible rivals; manual steering wins."),
        new("effects","Equipment","Temporary changes","Collect all four pickups along the marked lane: Tiny, Big, Haste and Slow.",
            ["Try Tiny","Try Big","Try Haste","Try Slow"],"Tiny is smaller and faster; Big is larger and slower. Haste gives 50% more movement speed; Slow reduces it. Your bombs stay the same. One timed effect replaces another; collision size stays fair."),
        new("swap","Equipment","Two equipment slots","Collect Remote, Heart, Power bomb, then Focus along the lane.",
            ["Equip Remote and Heart","Swap to Power bomb and Focus"],"Bomb and ability slots are independent. Replaced equipment drops for others; you cannot reclaim your own drop for 1.5 seconds."),
        new("expiry","Equipment","Watch the timer","Collect Tiny and watch its timer. Wait until your size and movement return to normal.",
            ["Collect Tiny","Let the effect expire"],"Temporary effects normally last 10 seconds. Ground items also disappear, blinking for their last three seconds. Room settings can change durations and allowed items."),
        new("terrain","The arena","Take the high ground","Climb the broad central staircase to the gold summit circle, then return to the lawn.",
            ["Reach the upper terrace","Return to the lawn"],"Ramps connect floors; bridges also have separate lower passages. Bombs and players can fall off ledges. Water is dangerous even with a Heart."),
        new("recovery","Rivals and recovery","Get back up","You start knocked down. Make fresh direction changes or tap {bomb} repeatedly to recover sooner.",
            ["Make three recovery efforts","Get back up"],"Holding one direction or button does not repeat recovery effort. Knockdown protects briefly against impacts, never against explosions."),
        new("impact","Rivals and recovery","Knock a rival down","Kick a bomb into the training partner ahead of you.",
            ["Hit the partner with a rolling bomb"],"A rolling or thrown bomb knocks a rival down. An explosion eliminates them. Impact protection is brief and does not block blasts."),
        new("grapple","Rivals and recovery","Lift a rival","The partner starts knocked down, as after a bomb impact. Move beside them, hold {carry} to lift, then release to throw.",
            ["Pick up the downed partner","Throw the partner"],"Approach from the side if a resting bomb is in the way: carry picks up nearby bombs first. A held player can struggle free."),
        new("escape","Rivals and recovery","Break the hold","The partner is holding you. Make fresh direction changes or tap {bomb} repeatedly to escape.",
            ["Make three escape efforts","Escape the partner"],"Throws and holds are temporary. Recovery presses are consumed so you do not accidentally plant a bomb as you get up."),
        new("ghost","Rivals and recovery","Ghost comeback","Let your bomb eliminate you. As a ghost, move into the training partner to latch on.",
            ["Become a ghost","Latch onto the partner"],"Ghosts move through bombs, cannot use them, and destroy pickups they touch. Latching is temporary. Water deaths return ghosts to dry land."),
        new("shake","Rivals and recovery","Shake off a ghost","A ghost is riding you. Reverse direction six times quickly to shake it off.",
            ["Shake off the ghost"],"A latch also expires after four seconds. Practice the faster escape: alternate left and right, allowing a moment between turns."),
        new("flood","Ready to battle","Beat the rising water","Hurry! Climb the central stairs to the upper terrace and stay above the flood.",
            ["Reach high ground","Stay dry as water rises"],"In Sunken Temple, water rises immediately during sudden death. Ghosts are removed. Up & Down also floods. Top Rules closes its walls; Rock Garden marks falling fireballs on the ground. Escape the warnings."),
        new("duel","Ready to battle","Graduation match","Collect the Heart and Remote supplies, then defeat the Easy training rival with an explosion. Keep an escape route.",
            ["Defeat the training rival"],"Normal battles are first to two round wins by default. Last living bomber wins; time running out is a draw. Room leaders choose the rules.")
    });
    public static bool Contains(string id) => Lessons.Any(lesson => lesson.Id == id);
    public static TutorialLesson Get(string id) => Lessons.FirstOrDefault(lesson => lesson.Id == id)
        ?? throw new ArgumentException("Unknown tutorial lesson.", nameof(id));
}
