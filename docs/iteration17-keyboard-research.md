# Iteration seventeen: two hands on the keyboard

Investigated 2026-10-21. User direction: keyboard only, both hands, no mouse requirement; preserve Space hold to carry / release to throw. This is an ergonomics proposal, not a claim of a player-approved layout.

## Persona 5 Royal evidence

The [publisher's Steam listing](https://store.steampowered.com/app/1687950/Persona_5_Royal/) identifies ATLUS/SEGA and describes a turn-based RPG. Its slower command selection cannot establish the best simultaneous inputs for this real-time arena game. The [Steam app-data response](https://store.steampowered.com/api/appdetails?appids=1687950&l=english) supplied no manual field during this check. The [SEGA support index](https://support.sega.com/hc/en-us/sections/360002114338-Persona-5-Royal) did not expose a PC keyboard manual. No official PC manual was found; a console manual or another game's PDF is not evidence for these keys.

The following **actual game UI screenshots** were downloaded and directly viewed, rather than treating a third-party key table as authoritative. They are hosted by a Japanese controls article, so build identity and an untouched default configuration cannot be independently established:

- [Title screen](https://volx.jp/wp-content/uploads/2022/10/persona-5-royal-control.jpg): Config is a direct top-level choice.
- [Config screen](https://volx.jp/wp-content/uploads/2022/10/persona-5-royal-control-2.jpg): a visible key-configuration entry; bottom confirm/back legend.
- [PC keyboard configuration](https://volx.jp/wp-content/uploads/2022/10/persona-5-royal-keyboard-setting.jpg): Keyboard, Mouse and Gamepad tabs; Common, Event/Movie, Field/Dungeon and Battle categories. Visible rows show forward/back/left/right on W/S/A/D; Confirm/Action on the Space glyph; Cancel C; general menu Tab. Each action has a boxed key and there is a bottom legend. This establishes the displayed bindings and discoverable configuration UI, not every binding, successful rebinding, or keyboard-only completion of the whole game.

Source captures are cached under ignored `artifacts/research/iteration17/`. No reference artwork becomes a distributed game asset. This investigation inspected still screenshots; it does not claim a full playthrough or frame analysis of gameplay footage.

## Principles to transfer

**Design inference:** keep a short, contextual action vocabulary and put a clear key next to the action. The observed combined Confirm/Action label and category-specific controls suggest that context can reduce memorization. For Blast Circuit, the existing place / pump / contextual kick on J already applies this principle. Preserve these distinct behaviors rather than adding a dedicated key per verb.

Expose the current binding in help and contextual prompts. Explain carry as an input sequence: hold Space, press J to create/pump, hold Aim to adjust, release Space to throw. The observed configuration categories support discoverability; they do not prove that copying Persona's Space/C/Tab command set would feel good during simultaneous movement and carrying. Rebinding is a useful future capability, with conflicts and prompt synchronization handled together, not an unverified feature claim about this project.

## Recommended project layout

| Hand | Input | Action |
| --- | --- | --- |
| Left | WASD | Move; adjust direction/range while aiming |
| Left thumb | Space held / released | Carry / commit throw |
| Right index | J | Place, pump, contextual kick |
| Right middle | K held | Precision aim |
| Right ring | L | Remote detonation |

This keeps the right hand around adjacent J/K/L keys and leaves the left fingers on WASD. Space+J readies/pumps; Space+K+WASD aims; releasing K restores movement with retained aim intent; releasing Space throws. These are project design choices, not Persona bindings or experimentally proven comfort gains.

K is the iteration16 legacy carry alias, so adopting K for Aim requires deliberately removing that alias everywhere; it must never carry and aim from the same key. Preserve Shift as an optional Aim alias and R as a remote alias if compatibility remains desirable, while primary prompts teach J/K/L. Verify no menu transition or focus loss leaves held carry/aim active. Keep controller behavior, digital precision rates and multiplayer fairness unchanged. Judge comfort with the complete simultaneous sequence, not isolated key presses.
