# Field of Grass

Field of Grass is a battle arena shared by local, dedicated-server and player-hosted
sessions. Its `field-of-grass` ID is selectable in Local battle, Practice and room
settings. Original procedural art supplies a soft meadow diorama with earth banks,
timber fencing, an orchard, mill, seed barn, hay cart, butterflies and a moving kite.
No reference textures, models, audio or scanned pages ship with the game.

## Reference evidence and adaptations

The primary reference inspected on 2026-11-15 is printed page 103 of *Baku Bomberman
Official Guidebook*, Shogakukan, Hudson supervision/cooperation, ISBN
4-09-102598-6. The scanned copy is the fifth printing, 1998-03-10 (first printing
1997-11-10). The right page of `guide-51.JPG` in the
[reference archive](https://www.randomhoohaas.net/bomb/book/baku1/bakuguide.zip)
shows the flat grass valley, two brown side slopes, four corner bank starts,
irregular breakable rocks, front/back fencing and falling fireballs in Hurry.
The archive SHA-256 is
`e2c31cd2c669a57b08298406b7b7858f97fb3840353fa34e12b3f0e691ccc38d`.

The guide explicitly describes grass concealing the lower half of players and
normal bombs, while large pumped bombs cannot be hidden. It recommends kicking
ordinary bombs down from the side slopes. These are the retained mechanic and
topology cues; the scanned material is research evidence outside the repository.
The [all-stage battle recording](https://www.youtube.com/watch?v=88xIYg29HlA)
around 4:54–5:45 is a supplementary gameplay reference.

The 21×19 cell court, two-unit-high banks, two one-tier ramp columns per bank,
an eleven-column meadow (about 58% of the interior), eight ledge starts,
28 randomly sampled rocks and clear center crossing are
explicit Blast Circuit tuning for eight players. The reference does not establish
these dimensions or counts. There is no invented permanent pillar grid, extra
floor, water, moving wall or grass collision rule.

## Authority and presentation contract

`BlastStages.FieldOfGrass` owns the floor, continuous slopes, enclosing walls,
spawns, regional cover and `1.05` cover height. Rocks occupy the north/south meadow
regions; approaches and the middle crossing stay clear. The normal Block density
setting scales the budget, seeds reproduce the layout and Practice clears cover.

Grass conceals through dense overlapping curved ribbon leaves, with jittered
roots and independently varied yaw, height, spread and tints. The first native
review rejected the original narrow valley and regular pointed-tuft carpet;
the revised valley width follows the guide silhouette and bank rails follow
continuous slopes. Grass conceals through opaque leaf geometry. It never changes collision, bomb
placement, blast shielding, simulation state or the snapshot protocol. Every
graphics preset retains the same gameplay canopy; Low can hide exterior scenery.
Player heads and enlarged bombs rise above the leaves. Advisory landing markers
and meteor warnings sit above the canopy, while their authority coordinates and
actual bomb landing heights retain the original ground surface. A pausable CPU
clock drives wind through the shared foliage shader.

Hurry uses the existing seeded Rock Garden meteor schedule instead of closing
columns. Impacts, warning presentation and CPU danger use the actual support
height, so a meteor aimed at a raised bank hits that bank. Flat Rock Garden
behavior is retained. The exact-release admission token already prevents an old
client from joining a newly released unsupported stage set; release identity and
deployment remain the release owner's work.

## Verification

`Bomber.Game.Tests -- --field-of-grass` checks continuous crossing in every row,
eight supported ledge starts, ordinary downhill/uphill walking, the enclosing
boundary, sixteen seeded layouts at three cover densities, grass not shielding
blasts, elevated fireball casualties and matching CPU danger. Client peer-frame
checks accept the complete stage and real Hurry state and retain the room setting.

`field-of-grass` is an ordinary-input journey covering the menu miniature, eight
starts, canopy persistence across presets, four lighting looks, bank descent,
normal placement/fuse, three pump presses and actual landing, eight-player CPU
combat and meteor impact. Its normal/pumped captures need actual image review;
node counts and a headless pass do not certify visual concealment or art quality.
`field-of-grass-network` checks real dedicated and player-hosted rooms with a
second client observing the stage, descended player and planted floor bomb.

Source evidence on 2026-11-15: 4,048 complete game checks, 494 client checks,
the four focused authority checks and twelve 2,700-tick AI soaks passed. The
focused headless journey initially passed 47 assertions and the real dedicated/peer network
journey passed 103 assertions, both with zero runtime errors. Public QA and
source layout, mesh inventory and documentation checks passed. Source headless
exit logs retain ObjectDB cleanup warnings (eleven local, two network), requiring
separate exported/native classification. The
first fresh-worktree attempt retained a failed receipt for unimported fonts and
sandbox-blocked Godot user logs; the imported repeat is the passing run. The first online attempt retained missing-WebRTC-extension errors; a second
clean functional attempt was rejected when its catalog changed during execution.
The final matching-catalog run with the pinned extension is the passing network
receipt. Native graphical review, exported-client qualification and delivery
remain separate.

The first exported native review passed 93 assertions with 15 captures and clean
runtime logs, but its regular pointed grass carpet and narrow valley were
rejected in visual review. The wider organic-leaf revision passes the focused
authority checks, twelve 2,700-tick AI soaks (longest eligible open idle 1.27 s)
and 50 headless journey assertions, including advancing and paused wind. That
repeat has no runtime errors or warnings. Revised native visual review remains
pending; the earlier functional result does not approve the revised art.
