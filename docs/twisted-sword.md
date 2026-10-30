# The Twisted Sword

**Status: current equipped power implemented; exact live PC parity remains unverified.**
The damage cap and item identity are supported by the installed 2.8.0.99920 client
and Blizzard's patch notes. Merge damage follows published live PC measurements.
The regression harness verifies this implementation, not Blizzard's server.

## Missing behavior and item identity

The equipped sword is `P610_Unique_Sword_1H_107` (GBID -844626822), with its
134% roll saved as `Item_Power_Passive[484709] = 1.34`. Installed CoreTOC identifies
that power as `P610_ItemPassive_Unique_Ring_027`. Its power file contains neither
script formulas nor a compiled server script. The old server saved the attribute
but had no handler to apply it. Its Raging Storm implementation only merged a pair
of explicitly selected Raging Storm casts.

Local evidence is in the ignored `artifacts/legendary-audit/resolved.json` and
`artifacts/twisted-sword/live-data`. The installed item description explicitly
says three damage stacks, automatic Raging Storm, and three additional merges.

## Version and damage evidence

- [Blizzard's 2.6.10 release notes](https://news.blizzard.com/en-gb/article/23548757/season-22-shades-of-the-nephalem-has-ended)
  added automatic Raging Storm and three further merges. They also specify
  half-second Twister damage ticks.
- [Blizzard's 2.7.7 notes](https://news.blizzard.com/en-us/article/24046224/season-30-the-lords-of-hell-has-concluded)
  reduced the sword's damage-stack cap from five to **three**, retaining the merge
  rule. The separate merge limit is still **five casts**: the initial pair plus three.
  [The official item web page](https://us.diablo3.blizzard.com/en-gb/item/the-twisted-sword-P610_Unique_Sword_1H_107)
  still displays the older five-stack damage cap; it conflicts with both the later
  patch notes and the installed description.
- [Cratic's November 2020 live retest](https://us.forums.blizzard.com/en/d3/t/raging-storm-wicked-wind-twisters-bugged/24013?page=3)
  reports 3200% total weapon damage for the first merged pair, including Wicked
  Wind. Subsequent casts add 835% for Wicked Wind or 1525% for the other runes.
  Five casts therefore give 5705% or 7775%, respectively, before item multipliers.
  These are historical player measurements; current-client testing is still needed.
- [Contemporary PTR testing](https://us.forums.blizzard.com/en/d3/t/twisted-sword-raging-storm-wicked-wind-rune-bug/23391?page=2)
  reports that merged constituents count toward the sword bonus and later casts
  redirect moving tornadoes. PTR evidence is weaker than a current live capture.

Installed Energy Twister formulas supply the single-cast damage (SF0), initial
merged damage (SF2 = 32), six-second durations (SF4/SF8), half-second tick period
(SF11), and normal/merged damage radii (SF7 = 8, SF6 = 15). The prior export and
formula comparison are in `artifacts/etched-sigil/live-data`.

| Contributing casts | Normal rune total weapon damage | Wicked Wind total weapon damage | Sword multiplier at 134% |
| --- | --- | --- | --- |
| 1 | 1525% | 835% | 2.34 |
| 2, merged | 3200% | 3200% | 3.68 |
| 3, merged | 4725% | 4035% | 5.02 |
| 4, merged | 6250% | 4870% | 5.02 |
| 5, merged | 7775% | 5705% | 5.02 |

## Implementation

`TwistedSwordBuff` tracks the owner's active casts, including merged constituents.
It excludes expired casts even when the world has not updated/destroyed distant
actors. A hit multiplies damage by `1 + equippedRoll * min(activeCasts, 3)`.
This multiplies independently with Etched Sigil: the saved 134% sword and 243%
Sigil rolls together give `5.02 * 3.43 = 17.2186` at the damage cap while channeling.
Other skills receive neither sword multiplier nor merging behavior.

Energy Twister grants merging separately from the selected rune attributes.
Wicked Wind remains stationary; the selected elemental damage and existing Gale
Force callback are retained in the merged tornado. Storm Chaser charges are
granted on the initial cast, not again when a visual actor is replaced.
The oldest eligible cast remains the parent, retaining manual/item-proc status
and its original ordering even after replacement with a large tornado actor.
Native Raging Storm without the sword remains limited to two casts.

Accepted additional casts refresh the tornado's six-second lifetime and redirect
its aim. The implementation preserves the existing damage-tick phase after the
initial merge; it does not manufacture another immediate hit for every later
absorption. A full five-cast tornado cannot absorb or refresh from a sixth cast.
Merges consume eligible single casts, not other already-merged tornadoes.

## Verification and limits

All **37 combat/input regression checks pass**, including ten sword checks and
the existing ten Sigil checks. Before this change, the actual hit regression
failed with **100 damage instead of 234** for the equipped 134% sword and one
active twister. The same damage assertion now passes.

Checks cover actual hit multipliers, the three-stack cap, independent Sigil
multiplication, equipment changes/breakage, ownership, expiration before actor
cleanup, death/world changes, merge counts and measured damage values, all rune
selection flags/elements, manual/item-proc parentage, cast ordering, original aim,
six-second refreshes, and rejection of the sixth cast. Fixtures use inert actors;
they do not execute actor spawning, client visuals, or the complete world loop.

The following remain unverified or incomplete relative to exact live PC parity:

- The inherited five-yard collision search, checks on half-second damage updates,
  movement around terrain, initial merge placement, first/last damage ticks, and
  50 ms world-update resolution. These are not established by the tooltip.
- Exact merge lifetime/tick-phase behavior, interactions between two already-merged
  tornadoes, mixed rune casts after skill changes, and equipping/removing the sword
  mid-cast. Damage reads current equipment; merge eligibility is captured at spawn.
- Buff snapshot rules (Arcane Dynamo, Arcane Attunement, etc.), Area Damage, and
  the live proc coefficient of each merged rune. Keeping the original context
  preserves the existing implementation; it does not prove those systems correct.
- The sword now publishes Energy Twister's installed slot 6 icon, its capped
  stack count and expiration. Client rendering/countdown still needs an in-game
  check. The effective-power lookup also accepts Cube assignment without adding
  duplicate copies; extraction and assignment are verified separately. Legacy
  sword revisions remain unimplemented.

For an in-game check, use the equipped sword with Wicked Wind and Etched Sigil:
channel at one location, observe stationary twisters combine and grow through
five casts, then stop and observe expiration. Repeat with a moving elemental
rune. Compare the same controlled setup on current live PC to settle timing,
visual, proc, and snapshot details; passing emulator tests alone cannot do so.
