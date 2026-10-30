# Combat meter

The Windows floating overlay reads the local server's actual `HitPayload` trace.
Run `combat-meter.bat`, or use `play.bat` after building the meter. **F8** shows or
hides the window. Drag its header to move it; expand **Inspect hit** for the
calculation, reference buckets, and hit-history slider. Disable **Follow latest
hit** to hold a hit while combat continues. **Reset encounter** clears the meter's
totals only. Select outgoing or incoming damage and, for multiplayer, the player.

Use Diablo III's **Windowed (Fullscreen)** or windowed display mode. A separate
Windows window cannot reliably cover exclusive fullscreen. No client executable,
opcode, or memory layout is patched. Close the meter with × to release F8.

```powershell
dotnet build tools/CombatMeter/CombatMeter.csproj -c Release
./combat-meter.bat
```

The server must include this change and be restarted. The overlay reconnects
automatically and starts a fresh encounter after a disconnect. It uses a
same-Windows-user named pipe, `D3Offline.CombatMeter.v1`; it opens no network port.
On Windows this also [requires matching elevation](https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.pipeoptions?view=net-10.0). Launch the server, `play.bat`
and `combat-meter.bat` normally, without **Run as administrator**. The game launcher
requests administrator access only to enable and later remove the hosts redirect;
the game and meter inherit your normal user account. If an older launcher left an
elevated meter open, close it before starting `combat-meter.bat` again. This does
not require restarting the server or the game.
One viewer can connect at a time. Tracing is disabled when no viewer is connected.
Game state is copied on the game thread; pipe IO and serialization happen outside
the game loop. Snapshots are requested four times a second. The server retains
2,048 hits; the viewer keeps 1,000 detailed hits and 30 seconds of chart samples.
Encounter totals are independent of detailed-hit retention. Buffer loss is visible
and marks totals incomplete instead of silently presenting a complete encounter.

## What the numbers mean

- Damage and DPS use the target's **actual health removed**, measured at the HP
  write. Shield callbacks, mitigation, critical damage and overkill are accounted
  for. The trace includes automatic/item-proc hits and owned minions. Destructible
  scenery is excluded. Damage that bypasses `HitPayload` is outside its coverage.
- Rolling DPS is health removed in the trailing three seconds divided by elapsed
  encounter time (up to three seconds, with a 0.25-second minimum at first impact).
  Encounter average includes idle and paused wall time. The chart uses 0.5-second
  bins. All timestamps come from the server's monotonic clock, not packet arrival.
- Buff stacks, remaining game-tick durations and current shield capacity are live
  snapshots. A buff's presence does not certify damage inclusion. The selected
  hit's factors show the values actually used, including effective Cube/equipment
  rolls for Sword, Sigil and Mantle.
- **Buffs and effects** groups Galvanizing Ward's recharge controller and shield
  into one row. Only the shield reports remaining absorption; the controller alone
  says **Recharging**. An effect without its selected passive is flagged rather
  than hidden. Storm Armor and its Power of the Storm resource-cost helper share
  one row. The installed `BuffTooltips` entries `Wizard_Passive_GalvanizingWard_1`
  and `Wizard_StormArmor_3` support those identities. Unclassified server objects
  say **Observed**, not **Active**. Stack counts are labeled **stacks**, not `×`.
  The separate shield readout is explicitly Deflection: that is the only current
  writer of `Damage_Shield_Amount`, so it is not a total of all Wizard shields.
- **Recorded bonus factors** is the product of traced outgoing factors after the
  weapon roll. Critical damage, primary stat, target mitigation and later payload
  adjustments have separate calculation steps. Weapon totals may already include
  buffs; this is not a claim to reconstruct an unbuffed weapon or a universal
  character multiplier.
- **Legacy of Dreams** shows rank, legendary/ancient counts, the rank-25 unlock,
  set-bonus restriction, and the applied damage and reduction totals. Its hit factors
  use the same explanation. See [LoD display evidence and checks](legacy-of-dreams.md).

## Bucket reference and evidence

Recorded server arithmetic and published bucket grouping are separate. The
reference adds captured A+B+C contributions, adds contributions within an
elemental bucket, and multiplies independent factors. A difference is displayed
as a bucket mismatch. This comparison never changes combat mechanics.

The base grouping follows the author's [Diablo III damage guide, version
2.7.1](https://us.forums.blizzard.com/en/d3/t/comprehensive-damage-guide-a-modern-dhs-perspective/25426).
Wizard classifications are cross-checked against the original
[D3Planner Wizard definitions](https://github.com/d07RiV/d3planner/blob/master/scripts/data/skill_wizard.js):
Glass Cannon, Cold Blooded, Gale Force, Snow Blast, Intensify and Spellsteal use
the shared additive grouping; Flame Blades and Absolute Zero use elemental;
Spark and Arcane Attunement are independent despite being element-restricted.
Audacity's independent multiplier also has
[first-person damage measurements](https://us.forums.blizzard.com/en/d3/t/d3-planner-audacity/16067).
See [Twisted Sword](twisted-sword.md), [Etched Sigil](etched-sigil.md), and
[Mantle](wizard-status.md) for installed-client identities, rolls and limitations.

These are published historical references, **not a verified full 2.8.0.99920
retail model**. The UI says so. Unknown attribute mappings, arbitrary payload
callbacks, Static Pulse, snapshot/pet exceptions and missing item handlers are
unclassified rather than assigned a guessed bucket. Element attributes remain
unclassified until their two server attribute sources are mapped independently.
For multi-entry attacks, per-element operations are component steps rather than
global factors. The comparison covers only classified captured factors; missing
upstream bonuses can still make the full server result wrong even when those
factors agree. This diagnostic does not certify all current emulator mechanics.

## Validation

Run `powershell -NoProfile -File tests/LauncherRegression.ps1` as a normal user
to check game/meter launch permissions, hosts cleanup, failed or cancelled UAC,
and Undo. These checks stub process starts and never modify hosts or certificates.

`tests/CombatRegression` includes actual hit/pipe integration checks, tracing
disabled behavior, Sigil multipliers, shield callbacks, HP/overkill accounting,
immunity, bounded-buffer loss reporting, additive reference grouping, idle DPS,
history retention, reset and disconnect behavior. The tests use their own unique
pipe, so they can run while the game server and meter are connected:

```powershell
dotnet run --project tests/CombatRegression -p:BuildInParallel=false
```

The UI has a `--smoke-test <absolute-png-path>` mode for checking hotkey
registration, hide/show handling and expanded rendering. It exits after the
check and does not inject combat data into a game. Unit checks and a screenshot
do not verify physical in-game key delivery or live retail formula parity.
