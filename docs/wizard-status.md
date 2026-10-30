# Wizard implementation status

Audit target: Diablo III PC **2.8.0.99920**, using the installed CASC powers,
BuffTooltips strings, the saved Wizard (toon 2), and the sources below. This is
a work queue, not certification of complete live parity.

## Selected skills and runes

| Skill | Saved rune | Finding / change |
| --- | --- | --- |
| Energy Twister | Mistral Breeze (D) | Cold damage and reduced cost are selected. Twisted Sword grants Raging Storm separately, retaining Mistral Breeze. Existing merge checks cover this combination; movement, collision and exact tick timing still need live comparison. |
| Arcane Torrent | Flame Ward (A) | Replaced obsolete Disruption with fire damage and 15% damage reduction, rising by 5 percentage points per second to 25%. Added the missing base damage ramp from 400% to 1010% weapon damage. |
| Teleport | Wormhole (E) | Consumes the single extra teleport before the follow-up delay and starts cooldown. Previously the three-second window permitted repeated casts. Full movement/cooldown timing still needs an in-game check. |
| Familiar | Arcanot (D) | The selected rune adds/removes the asset's 4.5 Arcane Power/second. Familiar's inherited autonomous attack behavior still needs correction/verification. |
| Magic Weapon | Deflection (E) | Replaced chance-on-hit/single-shield logic with attack-triggered 4%-of-life shields lasting three seconds independently. Triggers are limited by attack speed; item-generated twisters cannot generate shields. Remaining capacity is published through Damage_Shield_Amount. Exact channel breakpoints and all cast eligibility need live comparison. |
| Storm Armor | Power of the Storm (D) | Applies/removes the asset's three-point flat resource-cost reduction. Channel costs now prorate this flat reduction after percentage reductions, including Hergbrash. Base Storm Armor targeting/cadence remain unverified. |

The equipped **Twisted Sword is the only equipped item granting another rune**.
No rune attributes are overwritten to grant its Raging Storm behavior.

## Mantle of Channeling

The current item power is **446640**, `P4_ItemPassive_Unique_Ring_039`.
The saved shoulders roll is **0.21**. While channeling Arcane Torrent,
Disintegrate or Ray of Frost, the handler multiplies outgoing damage by **1.21**
and incoming damage by **0.75**. It reads the effective equipped/Cube power on
each hit and multiplies independently with Etched Sigil and Twisted Sword.
Flame Ward's reduction also multiplies independently.

Activation is immediate: Blizzard removed the one-second startup requirement in
[patch 2.6.10](https://news.blizzard.com/en-gb/article/23548757/season-22-shades-of-the-nephalem-has-ended).
The handler stops on channel cancellation, disabling control, death, world
change, or loss of the effective item power. Other classes' Mantle channels are
outside this Wizard pass and are not wired by this handler.

## Buff displays and selected passives

- **Twisted Sword:** publish stack count (capped at three) and expiration under
  Energy Twister power **77113, buff slot 6**. Installed `Buff6Icon=1716942913`
  and `Wizard_EnergyTwister_6` identify this display. Merged constituents count;
  expiration, unequip, breakage, death and world changes clear the icon.
- **Galvanizing Ward:** move the actual shield's icon from slot 2 to the
  installed **208541, slot 1**, `Buff1Icon=-501465245`. Its existing recharge
  logic is still incomplete; correcting the icon does not fix that logic.
- **Mantle and Etched Sigil:** their item powers have no standalone buff icon
  in the installed data. Do not invent one under the item-power SNO.
- **Deflection:** Magic Weapon retains its main icon. The installed power has
  no shield icon in slot 2; shield capacity is shown through the health-globe
  attribute instead.
- **Astral Presence:** correct regeneration from +2 to **+2.5 AP/second**
  (208472, SF1); +20 maximum AP already exists.
- **Audacity:** correct the within-15-yard multiplier from 1.15 to **1.30**
  (341540, SF0=0.3, SF1=15).
- **Illusionist:** still incomplete. The existing handler checks individual
  hits, omits Mirror Image from cooldown resets, and lacks the movement bonus.

## Next Wizard work

Valthek's Rebuke and Ranslor's Folly now have handlers for their current item
powers, including the saved Cube assignments. See [the item audit](twister-cube.md)
for damage, movement/pull behavior, tests and remaining live-parity limits.

The remaining equipped item handlers and Crown socket amplification are now
tracked in the [legendary power registry](legendary-powers.md), with their
[implementation and verification limits](equipped-legendaries.md).

1. Shield interactions: Galvanizing Ward recharge, shield absorption order,
   and the underlying mechanics of the other Wizard shields. Ashnagarr's 99%
   roll now amplifies newly created Deflection/Ward shields by 1.99; it is also
   connected to the three other existing Wizard shield creation paths.
2. Finish selected-rune behavior: Familiar attacks, Storm Armor targeting and
   Illusionist, and the timing boundaries above. Compare the corrected channel
   resource accounting against live traces.
3. Resolve the equipped powers' remaining live timing/CC/phase questions listed
   in the registry. A functioning Cube assignment alone is not a combat handler.
4. Audit every remaining Wizard skill/rune/passive and rune-granting item.
   Existing `Complete` comments in Wizard.cs are not evidence of live parity;
   Arcane Torrent's old Power Stone path, for example, still needs replacement
   by Static Discharge.

## Evidence and validation limits

- Installed power data: Arcane Torrent SF0=4, SF5=.15, SF6=.05, SF7/SF40=2,
  SF41=3.05; Magic Weapon SF12=.04, SF13=3; Mantle SF0=.25.
- Blizzard's [Arcane Torrent](https://eu.diablo3.blizzard.com/en-us/class/wizard/active/arcane-torrent),
  [Magic Weapon](https://eu.diablo3.blizzard.com/en-us/class/wizard/active/magic-weapon),
  and [Teleport](https://eu.diablo3.blizzard.com/en-us/class/wizard/active/teleport)
  descriptions support the rune behavior and values above.
- [First-person Deflection measurements](https://us.forums.blizzard.com/en/d3/t/clarification-on-the-deflect-magic-weapon/25986)
  support independently expiring layers and attack-speed scaling without
  skill-specific speed multipliers. They are historical player evidence, not
  Blizzard server source or a current controlled comparison.

CombatRegression exercises actual hit multipliers, channel lifecycle, stacking,
shield absorption/expiration and outgoing icon attributes with inert fixtures.
All **51 checks pass**, including the combined equipped/Cube lookup for Sword,
Sigil and Mantle and the existing targeting/movement checks.
Against the previous running DLL, the same icon checks fail with count **0
instead of 1** for both Twisted Sword and Galvanizing Ward. Deflection's
non-weapon absorption check also fails (50 damage remains instead of 0).
The combined Wizard/Cube build was started on 2026-10-28; all four server
listeners came up, the Cube schema migration succeeded, and no startup
Error/Fatal entries were logged. This is a startup check, not in-game acceptance.
The regression harness does not run the complete game loop, actor spawning, installed skill formulas,
client rendering or a live PC reference. In particular, the one-second ramp
steps, Deflection's channel/APS boundaries, pet/snapshot interactions, and the
rendered icon countdown still require observation. Do not claim exact live parity
from these tests alone.
