# Etched Sigil

**Status: equipped current-item implementation; exact live parity remains unverified.**
The code follows installed 2.8.0.99920 data and published live measurements where
available. No live PC comparison or in-game acceptance test has been performed.

## Why the old server did not activate it

The current item (`P610_Unique_Orb_002`, GBID -1573100240) saves its roll in
`Item_Power_Passive[484596]`, identified by the installed CoreTOC as
`P610_ItemPassive_Unique_Ring_002`. Saving this attribute does not execute the
legendary effect. The server needs code to generate twisters and apply the bonus.

No current Etched Sigil handler was found in the working source, local history,
or locally available upstream master, dev, test-stable, and community branches.
The bundled MPQ did not supply this power asset. The installed power asset contains
no compiled server script to import. Old DiIiS therefore cannot supply this missing
implementation merely by restoring its pre-2.8 logic.

## Version evidence

- [The 2.6.8 live notes](https://news.blizzard.com/en-gb/article/23319443/patch-2-6-8-now-live)
  added a one-second startup delay to the then-current Sigil. The later redesign
  uses a different power. Whether that delay carries over cannot be inferred
  solely from the unchanged channeling wording.
- [Blizzard's 2.6.10 notes](https://news.blizzard.com/en-gb/article/23529004/ptr-2-6-10-has-ended)
  introduced the twister-only power and 200–250% bonus, prevented stacking old and
  new versions, and confirmed Twister's half-second damage interval. The
  [current item description](https://us.diablo3.blizzard.com/en-us/item/etched-sigil-P610_Unique_Orb_002)
  retains the three channels and twister-only effect.
- [The 2.7.0 notes](https://news.blizzard.com/en-us/article/23647866/season-23-the-disciples-of-sanctuary-has-ended)
  fix unintended bonuses from the legacy Sigil, which is a different power.
- A related effect changed after the emulator's 2.7.4 target:
  [2.7.7 reduced Twisted Sword's damage-stack cap from five to three](https://news.blizzard.com/en-us/article/24046224/season-30-the-lords-of-hell-has-concluded).
  Its additional-merge rule is separate. Older build advice cannot be copied wholesale.
- [Cratic and Dolynick's measurements](https://us.forums.blizzard.com/en/d3/t/etched-sigil-proc-mechanics-as-icd/11355)
  report a 59-frame cooldown followed by an eligible channel tick.
  [Cratic reaffirmed the breakpoints on live 2.6.10](https://us.forums.blizzard.com/en/d3/t/help-still-not-understanding-ias-and-channeled-et/24398).
  These are older player measurements, not a fresh 2.8.0 capture.

The local comparison used `tools/protocol/mpq_sample.ps1` and
`tools/protocol/casc_read.py` to export the four Wizard channel/twister power files.
Channel formula expressions agree between bundled and installed data; rune
attribute IDs differ in the bytecode. Twister formulas 19 and 20 swap constants,
but their product, and the half-second interval, remain unchanged. Audit exports
and the comparison report are in the ignored `artifacts/etched-sigil/live-data`.

| Installed power data | Channel period | Arcane Power per tick |
| --- | --- | --- |
| Disintegrate | SF18 = 1 / (APS × 3) | SF22 = 18 / 3 |
| Arcane Torrent | SF10 = 1 / (APS × SF1) | SF30 = 16 / SF1 |
| Ray of Frost | SF15 = 1 / (APS × 2) | SF19 = (16 − rune reduction) / 2 |

Torrent SF1 is 3 normally, 2 for Death Blossom, and 1.5 for Arcane Mines. Periods
are floored to whole 60 Hz frames, matching the published breakpoints. Twister
SF11 supplies its 0.5-second interval, SF7 its 8-yard normal radius, SF6 its 15-yard
merged radius, SF0 its total normal damage, and SF2 its total merged damage.

## Implemented behavior

- A server-only buff follows the three supported channels. The first cast waits
  at least 60 frames; subsequent casts occur on channel ticks more than 59 frames
  after the preceding cast. Repeated input does not reset the timer. Late updates
  execute at most one cast. Twelve-frame channel ticks give 60-frame intervals;
  thirteen-frame ticks give 65-frame intervals.
- Channel schedules use client formulas and preserve their logical frame phase
  across batched updates. Resource costs now use the per-tick formulas.
- Automatic casts preserve cursor aim before the channel extends a beam or
  randomizes its impact point. They retain Twister's selected rune and bypass
  manual casting animation and resource cost.
- Item-generated hits have zero proc coefficient and cannot trigger existing
  item on-hit effects or Life on Hit. Damage modifiers and intrinsic rune callbacks
  still execute. Manual-cast proc behavior is preserved.
- Twister hits read the equipped, non-broken item's roll while channeling. A 2.43
  roll multiplies damage by 3.43, without boosting the channel itself. Unequipping,
  breakage, cancellation, death, disabling crowd control, and world changes
  invalidate the bonus and automatic casts.
- Twisters use client damage, radius, duration, and interval formulas. Raging Storm
  selects one matching same-owner twister using the correct obsidian actor. The
  older cast owns the merged context and retains its manual/item-generated status.
  It no longer consumes every nearby unruned tornado.

## Verification

The server and `tests/CombatRegression` build successfully. All 27 checks pass,
including ten Sigil checks. The original five Sigil checks failed against the
pre-implementation server before passing with the initial implementation.

The current harness exercises production channel/buff scheduling at the published
60/65/66/70/72/76-frame intervals, batching, aim ownership, rune selection, equipment
and interruption boundaries, roll changes, and restart. It calculates real hit
damage, applies hits to check damage and Life on Hit, and checks merge selection
across owners and cast types. It uses inert fixtures, not spawned game actors;
channel periods come from a probe rather than the asset-backed Wizard skills.
Client formula inspection and these tests are distinct kinds of evidence.

## Remaining live-parity work

- **Startup and transitions:** confirm first-cast timing, damage-bonus activation
  and removal delay, cancellation/restart, APS changes, and resource exhaustion
  against current live PC. The implementation activates damage immediately and
  starts a fresh 60-frame wait on a new channel; these choices are not certified.
  A [2025 player response](https://us.forums.blizzard.com/en/d3/t/wizard-twister-mechanics-based-on-supporting-legendaries/65246)
  suggests immediate damage activation but explicitly expresses uncertainty.
- **World update resolution:** the server updates every 50 ms (three game frames).
  Logical phase is preserved, but effects execute at the next available update.
  Input delivery can add further delay. Execution is not frame-exact.
- **Full twister behavior:** verify spawning, movement, collision/merge timing,
  first and last damage ticks, every rune, and mixed manual/automatic casts in-game.
  The inherited movement and damage-tick phase remain unverified. Pair-selection
  tests do not test the full merge lifecycle.
- **Other effects:** verify snapshot/consumption rules for Arcane Attunement and
  Arcane Dynamo and other buff-specific interactions. Zero proc coefficients and
  on-hit guards do not prove all existing buffs follow live rules. Area Damage
  is not covered by these checks.
- **Other power sources:** The handler now uses the effective equipped/Cube power
  lookup without adding duplicate copies. Cube extraction/assignment require
  their separate validation; legacy Sigils remain unimplemented.
  [The equipped Twisted Sword now has its own handler](twisted-sword.md).
  This covers the
  equipped current Sigil. Other legendary powers need their own handlers and
  evidence; there is no switch that enables all missing powers.

For acceptance, record the live build, item revision/roll, skills/runes, APS, and
other active powers. Compare cast frames and damage before/during/after each
channel, interruptions, resource use, moving/stationary twisters, and secondary
procs. Add regression expectations from observations. Patch notes and old source
reduce the work; absence of a documented change does not prove undocumented
server behavior is identical.
