# Wizard combat lag investigation — 2026-10-25

The evidence points to several independent simulation and input-handling problems. It does not show sustained server CPU overload during the most recent recorded fight. The 12:57 server rebuild includes the earlier source fixes, but the reported client playback freeze persists. The clock correction below is compiled and tested separately; the running server still locks the normal output. No fix has yet been confirmed by a before/after live gameplay comparison.

## Recorded evidence

Source: `src/DiIiS-NA/bin/Debug/net10.0/logs/2026-10-25_00-41/packet.log` (local, ignored by Git). Times below are the log's timestamps.

- The equipped Twister rune is Mistral Breeze: power 77113 has Rune_D = 1 and Rune_E = 0. This uses moving Twisters.
- At 00:45:31.023, the 30-second update summary reports 580 updates, average 1.6 ms, maximum 458.0 ms, and one update over the 50 ms budget.
- The 458 ms stall at 00:45:12.395 follows waypoint handling and world-entry messages. It does not establish a sustained combat bottleneck.
- At 00:45:33.957, 117 received chunks averaged 0.0 ms waiting to start parsing, maximum 0.4 ms. This excludes subsequent waits for the game update lock, network transit, and client rendering.
- The update starting at 00:45:27.438 emits floating-number messages for 22 distinct actors. There is no five-target server cap in this fight. These logs record messages before socket transmission; they do not prove what the client displays.

## Confirmed defects corrected

### Input can be lost, duplicated, or reordered

`Connection.ReadCallback` passes a lazy view of a reusable receive buffer to `GameClient.Parse`, then immediately starts another read. Previously, Parse copied the bytes only inside a queued task, after another receive could overwrite them. One task per receive also made processing order depend on which worker acquired the lock first. Each receive created a fresh bit buffer, discarding any incomplete TCP packet.

Regression checks against the existing server assembly reproduced all three defects:

- Split packet: expected three replies, received one.
- Reused buffer: expected counters 0, 1, 2; received 0, 2, 2.
- Burst behind a blocked handler: counters arrived out of order.

The fix copies bytes before returning to the socket, drains an ordered per-client queue with one worker, and preserves incomplete packets. Invalid lengths and messages crossing a packet boundary disconnect with a logged error. The existing 10-bit opcode encoding is unchanged.

### Moving Twisters damage the wrong location

The Twister callback created an ActorMover, called Move, and discarded it. Move sends the client a destination; Update advances the server position. The callback never called Update. The visual tornado therefore moved while its server damage query stayed at its spawn position. The merged Raging Storm tornado had the same problem.

The fix retains each mover and advances it on world updates. Damage and direction changes keep their existing 0.5-second interval and multiplier. Wicked Wind remains stationary. No new damage cadence was guessed.

### Target IDs could resolve to a different actor

The normal TargetMessage handler already converts the client's dynamic actor ID to the server's global ID. PowerManager then searched the dynamic-ID map again using that global ID, overwriting the correct target if another actor's client ID happened to equal it. A regression using mappings 42→9 and 77→42 reproduces selection of actor 77 when actor 42 was clicked.

PowerManager now accepts global IDs consistently. The secondary-animation handler performs its client-ID conversion at the input boundary, including preserving the -1/no-target sentinel. This fixes the same targeting path for all skills. The collision is reproduced in isolation; its frequency in the reported session is not established.

### Spell aim shared mutable actor coordinates

PowerManager assigned the actor's Vector3D directly into a power's TargetPosition. The regression proves that changing the spell's aim could mutate the actor's actual coordinates without a movement message or position-change event. Context setup now copies the position, for both targeted and ground casts. This establishes position ownership; it does not prove that this was responsible for every reported enemy teleport.

### Arcane Torrent's effects used a stale target marker

The channel-update callback was empty. Each volley played its effect against the previous marker position, then sent a gradual movement command at speed 8 while damage used the new position immediately. The Arcane Mines branch skipped the marker update entirely and leaked its infinite-lifetime user proxy.

Arcane Torrent now clamps aim before creating the marker, updates the marker on channel input, and publishes an exact ACDWorldPositionMessage before each volley. Its damage query uses a copy of that marker's position. Death Blossom still chooses a random impact point per volley, and existing damage/resource formulas and tick periods are preserved. Arcane Mines now destroys its temporary user proxy on the early-return path, as the ordinary branch already did.

## Client damage-number investigation

The observation concerns rendered numbers, not the number of server targets. Sending 22 messages is not proof that 22 numbers were visible.

The existing 2.8.0.99920 snapshot and installed executable were inspected with the repository's protocol tools and Visual Studio dumpbin:

- FloatingNumberMessage is opcode 243, with a 32-bit actor ID, 32-bit float amount, and six-bit type in range 0–38. The server's layout matches the client descriptor.
- The handler at RVA 0x5B80F6 resolves the actor and calls the display routine at RVA 0x1C0420. It uses the same amount/type offsets that the descriptor specifies.
- The display routine applies actor visibility checks, gameplay display settings, and type-dependent merging/suppression. The server's current normal/critical types (0 and 2) bypass the merging path; blindly changing them would change display semantics.
- The downstream effect-creation routine also has resource/budget rejection paths. No five-enemy limit was established, and no captured rendering trace identifies which path rejected the missing numbers in this session.

Follow-up: each number is its own particle instance (0x1C6930 → 0x1C5A70 → 0x3CE280), and the update at 0x1C6340 drops a number whose particle was refused. 0x3CE280 refuses a particle in two cases:

- 1024 particle instances are already alive (budget at RVA 0x13EFE54), unless the particle has flag 0x8000. Damage numbers don't.
- The particle's own instance limit is reached: definition +0x318 (file offset 0x328), counted per particle SNO in [[context+0xA60]+0x3C8]. 0x3CEE70 lowers the count on destroy. 805 of the 21,593 particles have a limit, mostly 3–25 (get-hit sprays 5, death dust 4).

0x1C7C10 maps types 0 and 1 to g_damageNumbers_done (111224) and type 2 to g_damageNumbers_crit_done (111226). Types 3 and 4 map to _taken, 5 to _crit_taken, 10 to _heal, and most status texts to _done. Orange hits use g_damageNumbers_veryLarge (449056). All of these have a limit of 8. In the 12:57 fight the server sent about 104 monster numbers per second to 25 actors, 7–8 per monster per second, with no duplicates within an update. With at most 8 normal and 8 critical numbers alive, most are refused, and whichever arrive first after one expires are shown. The limit is in the installed client's data, so live has it too.

A read-only capture of the running client (2026-10-27 00:40, 100 ms samples of the counts above) confirms it. For the 18 s fight the server sent up to 553 numbers a second to 41 monsters, 13–14 per monster per second: up to 7 Mistral Breeze twisters were alive, and each one hit every monster in the pack every 0.5 s. The client held 8 normal and 8 critical numbers for most of the fight, while the total particle count stayed at 134–285 of the 1024 budget. The normal-number slots freed together about once a second (0.98 s apart), and the next burst of hits took all of them, so numbers appeared in one pulse per second. Whether live's Energy Twister hits as often, or sends its ticks as merged numbers (type 1), has not been checked.

Storm Armor adds hits that live doesn't. DiIiS shocks up to 8 enemies within 20 yd every second. The tooltip says "a nearby enemy". The power's formulas are Lightning Refresh Rate 0.2 (SF_13), Lightning Starting Chance 0.06 (SF_24), Lightning Chance Per Tick 0.06 (SF_10) and Lightning Search Radius 50 (SF_16). Not changed yet.

No guessed enum, forced-display attribute, artificial number staggering, damage amount, or client binary patch was applied. Local disassembly and descriptor evidence is saved in artifacts/combat-regression/floating-number-client-evidence.txt and float-effect-create-disasm.txt (ignored artifacts).

## Other causes still present

| Symptom | Evidence and implication |
| --- | --- |
| Damage arrives in pulses | Ordinary and merged Twisters explicitly apply damage every 0.5 seconds, even when updates finish quickly. Matching live cadence needs client/game-data evidence before changing it. |
| Monsters stand still under overlapping damage | HitPayload has a 33% hit-reaction chance, plays a 40-tick animation, and applies a 0.3-second HitRecoveryDebuff that sets walk speed to zero. Further hits extend it. The earlier working-tree fix removes spinning thread-pool tasks and restores speed correctly, but retains repeated immobilization. |
| Enemy reactions are slow | MonsterBrain.Think runs its main decision logic only at exact multiples of 60 ticks and also uses a one-second power-delay timer. Movement actions commit positions in discrete path steps. These are simulation behaviors, not proof of transport lag. |
| The game clock can run slow | Corrected in the follow-up below: the old loop compensated for slow work but lost sleep/scheduling time. The 2026-10-24_21-04 log repeatedly reports about 482 updates per 30 seconds with sub-millisecond work: roughly 24.1 simulated seconds per 30 real seconds. The latest fight is much closer to real time, so this defect alone does not establish the cause of a one-second playback cadence. |
| Half-second spell delay outside combat | A general fixed input delay is not established by the logs. The input bugs above are real; the working tree already contains a Teleport delay correction. Wormhole follow-up Teleports still explicitly wait 0.3 seconds. A new live reproduction is needed to distinguish these effects from other casting/client delays. |

Per-message synchronous socket sends and enabled packet dumps add costs, but current timings do not establish them as the dominant combat bottleneck. No speculative network, damage-number, or hit-chance tuning was applied.

## Validation

Run the dependency-free console checks with the repository's .NET 10 SDK:

```powershell
dotnet run --project tests/CombatRegression
```

The harness exercises the actual GameClient parser and packet encoder: coalescing, every split point in a packet, receive-buffer reuse while a handler is blocked, a 200-packet backlog, empty packets, invalid lengths, and packet-boundary violations. It also exercises PowerManager with colliding ID namespaces, mutable aim ownership, secondary casts (including no target), and an Arcane Torrent aim update that must broadcast the corrected marker position before another damage tick. The targeting fixture uses inert actors and a no-op power without starting a server or loading a database.

The existing assembly failed three of the four original input checks. The patched assembly passes all seven checks. The full server also builds successfully through MSBuild (zero errors, 89 warnings in existing code/dependencies). All seven checks pass against that build. Build output is isolated under artifacts/combat-regression/msbuild; the usual server binaries were not replaced.

The follow-up baseline failed all three new defect regressions (target-ID collision, shared coordinates, stale Torrent marker). The final suite passes all eleven checks. The secondary-cast and outgoing-marker assertions additionally check the affected boundaries. The full follow-up build succeeds with zero errors (88 existing warnings on the final build). Output remains isolated under artifacts/combat-regression/followup-patched; the usual server binary was not replaced. Final results are recorded in artifacts/combat-regression/followup-patched-results.txt.

The Twister callbacks and Torrent volley ordering/cleanup were compiled and reviewed, but have not been exercised in a live game. No client executable, protocol layout, damage multiplier, hit-reaction probability, or unrelated working-tree change was modified.

## Live follow-up

Rebuild and restart the server before comparing behavior. Repeat Mistral Breeze against a pack at the tornado's visible destination, then compare stationary Wicked Wind and a first Teleport versus a Wormhole follow-up. Also channel Arcane Torrent across widely separated enemies without releasing the button, and compare the effect impact with health changes. Record the session time and retain its packet log. A fresh reproduction is required before claiming the remaining movement pauses, visible damage-number suppression, or casting delay are resolved.

## Follow-up: one-second client playback report

The newest session is `src/DiIiS-NA/bin/Debug/net10.0/logs/2026-10-25_12-57/packet.log`. The normal assembly was rebuilt at 12:57:22, before this session. An earlier 12:52 test belonged to the older 00:46 process; it must not be substituted for the newer reproduction.

From **12:58:41 through 12:59:01** (20 seconds, after world entry):

| Logged message | Count | Median gap between distinct timestamps | Maximum gap |
| --- | ---: | ---: | ---: |
| GameTickMessage | 392 | 51 ms | 52 ms |
| FloatingNumberMessage | 1,740 | 49 ms | 510 ms |
| ACDTranslateNormalMessage | 349 | 51 ms | 508 ms |

At 12:58:57.866, the 30-second summary reports 582 world updates, average 1.5 ms, maximum 454.9 ms, and one update over budget. The long update was at world entry (12:58:40.215). The fight has no one-second server tick gap. These are send-side logs, not proof of delivery or display timing. The packet dump previously omitted every GameTickMessage's tick value; it now prints that value.

The client's own `x64/D3Debug.txt` contains 7,631 missing-Items game-balance lookup errors between 12:58:31 and 12:59:11. The repeatedly named GBID 491159985 is the Wizard's class GBID in its ACDEnterKnownMessage, not one of the equipped items. This is an additional client error, but neither its execution cost nor a causal link to the playback freeze is established. It is not fixed by inventing a replacement item ID.

### Clock correction

The loop now uses a monotonic, absolute update schedule. Ordinary updates still advance three ticks every 50 ms; late wakeups shorten the next sleep instead of accumulating drift. A long stall advances elapsed update slots once rather than replaying a burst of world updates. Advances remain multiples of three, preserving the existing tick quantum. Existing exact-modulo consumers can still miss a boundary during a long stall, as they could with the former missed-tick compensation; this patch does not redesign AI scheduling or damage cadence.

For example, the old sleep calculation with 1 ms of cheap work and 1 ms of oversleep repeats every 51 ms, taking 61.2 seconds to execute 60 seconds of simulated updates. With 12 ms of oversleep it takes 74.4 seconds. The new scheduler's deterministic checks hold the same 1,200 updates to 60 seconds plus only the final wakeup overshoot. The 454 ms work-stall case and delayed-wakeup recovery also pass.

The full isolated build succeeds with zero errors and 91 warnings from existing code/dependencies. All **14** parser, targeting, and scheduler checks pass against it. Binaries are in `artifacts/combat-regression/clock-patched`; results are in `artifacts/combat-regression/clock-patched-results.txt`. A normal build was attempted, but the active process locks both Blizzless.exe and Blizzless.dll. Close the server, rebuild, and restart before testing this clock correction. The running process has not been replaced.

### Client queue evidence and capture

Inspection of the installed 2.8.0.99920 executable established that GameTick already synthesizes the client's EndOfTick queue entries:

- RVA 0x5B266F passes the received tick to 0x1C9E80 and 0x4E5430.
- 0x4E5430 sets the previous marker's next tick, then inserts opcode 161 with the new tick and next-tick sentinel -1. The server should not add guessed duplicate EndOfTick messages.
- 0x1C9E80 stores the latest received tick at clock-state +4. 0x1C9910 resolves that state through the pointer at image RVA 0x16F5250, then +0x7B0.
- 0x1CF2C0 advances the playback tick at context +0x54. 0x4E51B0 reads the pending queue count at network-context +0xC8; that context comes from image RVA 0x16C6410. It gates queued effects/movement at the tick markers.

`tools/protocol/trace_client_clock.py` reads just those counters every 100 ms from an already-running client. It never writes process memory or starts/stops the game, and verifies the exact executable SHA-256 before using these offsets. Samples are approximate because the client continues running. Its argument handling and no-running-client path were exercised; a live capture is still needed.

Run from the repository root while the game is open, then reproduce the stall:

```powershell
python -I tools/protocol/trace_client_clock.py artifacts/combat-regression/client-clock.jsonl --seconds 120
```

Use a new output filename for each capture. Compare the trace with the server packet log: steady received ticks with stalled playback and a growing queue locates the backlog inside the client; received ticks themselves arriving in bursts requires tracing transport/receive processing. Neither outcome is assumed in advance.

Local evidence: `artifacts/combat-regression/latest-session-timing.json` and `client-tick-queue-evidence.txt`.
