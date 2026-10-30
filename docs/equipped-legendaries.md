# Remaining equipped Wizard legendary powers

Target PC client: **2.8.0.99920**. Loadout and rolls checked read-only from toon 2 on 2026-10-29. See the [power registry](legendary-powers.md) for the complete tracked list.

## Why these powers were inactive

The inventory/Cube layer supplied the correct values, but there were no consumers for eight `Item_Power_Passive` keys. Leoric's Crown used a different representation: attribute 727 was saved on the helm but never applied to socket effects. This required separate handlers for different mechanics, with shared effective-power lookup so a Cube maximum replaces an equipped duplicate.

## Implemented behavior and evidence

The installed `.pow` files and `BuffTooltips` supplied the following values. Their compiled server scripts are empty; this is data evidence, not recovered Blizzard server logic.

| Item | Installed power | Data used |
|---|---|---|
| Aquila | `P4_ItemPassive_Unique_Ring_075` | SF0 `Tickrate` 0.25; SF1 `Damage Reduction` 0.5; icon slot 1. Effective roll is the threshold. |
| Stone Gauntlets | `P66_ItemPassive_Unique_Ring_928` | SF0 `Slow Duration` 1; SF1 `Slow Amount` 0.15; SF2 `Armor Percentage` 0.5; SF3 `Attack Speed Reduction` 0.20; SF4 `Attack Cooldown Increase` 0.40; SF5 `Armor Duration` 5; icon slot 1. |
| Hergbrash | `P4_ItemPassive_Unique_Ring_072` | Item roll and the three named channels; remaining formulas in this passive are unrelated shared data. |
| Ice Climbers | `ItemPassive_Unique_Ring_559_x1` | Freeze/immobilize immunity; no standalone icon. |
| Ashnagarr | `P4_ItemPassive_Unique_Ring_070` | Item roll; no standalone icon. |
| Stone of Jordan | `P69_ItemPassive_Unique_Ring_001` | Static elemental equalization; no standalone icon. |
| Convention | `P2_ItemPassive_Unique_Ring_038` | SF0 4 seconds; BuffTooltips map Arcane/Cold/Fire/Holy/Lightning/Physical/Poison to slots 1–7. Wizard uses 1, 2, 3, 5. |
| Squirt's | `P66_ItemPassive_Unique_Ring_931` | SF0 damage 0.1; SF1 incoming 0.05; SF2 max stacks 10; SF3 `Hit Reset` 2; icon slot 5. |
| Leoric's Crown | `Unique_Helm_002_p1` | Saved `Gem_Attributes_Multiplier` 0.93; normal helm socket effect becomes 1.93×. |

Additional primary references:

- [Reported PC Stone Gauntlets/Ice Climbers interaction](https://us.forums.blizzard.com/en/d3/t/stone-gauntlets-and-ice-climbers/9338): both movement and attack penalties are suppressed. Armor remains additive with other percentage armor bonuses; the [D3Planner implementation](https://github.com/d07RiV/d3planner/blob/master/scripts/data/item_special.js) is supporting model evidence, not a live trace.
- [Official Ashnagarr item](https://us.diablo3.blizzard.com/en-gb/item/ashnagarrs-blood-bracer-P4_Unique_Bracer_004) supplies the rolled shield amplification. [Wizard shield testing/discussion](https://us.forums.blizzard.com/en/d3/t/question-about-shield/3049) identifies the five eligible skills and excludes non-Wizard shield sources; use installed skill formulas for base capacities.
- [D3Planner Wizard channel definitions](https://github.com/d07RiV/d3planner/blob/master/scripts/data/skill_wizard.js) and its [resource-cost computation](https://github.com/d07RiV/d3planner/blob/master/scripts/skilldata.js) put percentage reductions before flat reductions, then scale by the base tick fraction. This fixes the old subtraction of the full Power of the Storm amount on every channel tick. Other skills retain their existing resource path.
- [Squirt's PC explanation and shield behavior](https://us.forums.blizzard.com/en/d3/t/squirts-need-some-explaination/21262) describes stack increments, the incoming penalty, and shields preserving stacks. The reset hook observes the actual final HP reduction after shields and avoidance.
- [Official Squirt's item](https://eu.diablo3.blizzard.com/en-gb/item/squirts-necklace-P66_Unique_Amulet_010) and the installed description establish its maximum bonuses. Convention's temporary multiplier stays outside the static-element attributes shared by Stone of Jordan.

`EquippedLegendaryPowers.cs` contains the new passive controllers. `HitPayload` applies named independent damage/mitigation factors, triggers Stone Gauntlets on a landed damaging hit before absorption, and clears Squirt's after health damage. Ashnagarr scales each newly created Wizard shield rather than rescaling existing capacity. Galvanizing Ward now contributes to the visible shield amount and absorbs non-weapon damage too.

## Verification

`tests/CombatRegression/EquippedLegendaryChecks.cs` exercises real damage payloads, buff lifecycle, cost calculation, equipped/Cube replacement, broken gear, static-element removal, stack/timeout boundaries, shield absorption, and installed gem effects. The initial checks failed before combat wiring: 99% Ashnagarr produced 40 rather than 79.6 shield, and 178% Convention produced 100 rather than 278 matching-element damage. Both pass after the change.

The combined CombatRegression build passes **81 checks without assets / 89 with installed assets**, including existing Sigil/Sword/Mantle/Twister-Cube, LoD, movement, transport and meter checks. The installed pass also evaluates the new power formulas/icons and normal helmet gems from all five color families. Local artifacts are under `artifacts/equipped-legendaries/`, including the saved loadout, extracted formulas, before-source copies, build/test logs, scoped diff, and server readiness report. These artifacts are ignored by Git; this document retains the relevant findings.

The existing **seven CubeRegression groups** also pass with installed assets. Convention is tested with two Arcane entries plus one Fire entry in the same hit: only the Arcane component receives its multiplier, once. `git diff --check` passes. No character data or Cube assignments are changed by the implementation.

Deployed 2026-10-29: old server 60424 exited through `!quit` after a fresh idle-session check. The tested DLL/PDB replaced the runtime after binary/database backups. New server **58124** listens on **83, 1119, 2001, 9800**, with **zero startup Error/Fatal entries**. Combat meter **55976** remained running. Deployed DLL SHA-256: `327A21D729A1455E3EBC59ACBA895CD789BDF91E2FD84188CD6CC946FF981AF5`. This verifies startup, not client-rendered acceptance or live parity.

## Limits requiring live comparison

- Aquila uses strict `current > maximum × threshold`, sampled every 15 simulation ticks. Confirm equality and first-check phase on PC.
- Stone Gauntlets has distinct one-second slow/five-second armor labels. The implementation uses those durations and caps armor at five stacks. Its unprotected slowdown refresh/stack behavior and SF4 attack-cooldown semantics are **not established** by those labels. The inherited attack-speed calculation also clamps speed above zero. The user's equipped Ice Climbers suppresses these penalties; an unprotected Stone Gauntlets build is not certified.
- Convention starts at the first class element when its controller is created and recreates after death/world cleanup. Initial live phase and persistence rules are unverified. Wizard's full cycle is regression tested; other class element lists are present but not gameplay tested.
- Squirt's initially gains a stack after one second and regains its first stack two seconds after HP damage. The installed reset constant and player reports support this, but exact phase and exceptional environment/self-damage routes need live captures. Damage that bypasses `HitPayload` is not newly audited.
- Ashnagarr is connected to all five existing Wizard shield sources. Deflection and Galvanizing Ward capacity/absorption are checked. Dominance, Barrier Blades and Diamond Skin retain their inherited underlying mechanics; shield priority and Galvanizing Ward recharge/refresh remain separate Wizard work. No amplification is applied to non-Wizard shield sources.
- Stone of Jordan uses static equipment elemental bonuses for the class. Unusual static set effects or other unimplemented legendary modifiers are outside the verified scope.
- Leoric's equipped normal-gem behavior is implemented. Its direct-attribute representation is still unsupported by the Cube extraction catalog; that is separate from the equipped helm requested here.
- Installed formulas plus these regressions do **not** prove exact live parity. The full Wizard class audit remains open.
