# Combat/input regression checks

Run with the repository's .NET 10 SDK:

```powershell
dotnet run --project tests/CombatRegression
```

This console harness uses the production packet encoder and GameClient parser. It does not start the server, connect to a database, or load game data. A nonzero exit code indicates failure. Expected malformed-packet cases log parse errors before passing.

The receive-buffer and backlog checks deliberately block a handler, reproducing ordering and ownership requirements while the game is busy. The fragmentation check covers every byte boundary of a packet.

Targeting checks also cover client/global ID collisions, position ownership, secondary cast resolution (including no target), and Arcane Torrent marker updates sent to the client. Movement checks cover a monster's server position along a walk step, during a get-hit pause, and after a knockback. They use inert fixtures and do not start the server or access character databases.

Etched Sigil checks use equipped-item attributes and the production channel/buff code to cover activation, published 60/65/66/70/72/76-frame proc intervals, batched updates, cursor ownership, rune selection, interruption, changed rolls, and channel restart. Channel periods come from a probe; the harness does not execute the asset-backed Wizard skills. Damage checks calculate the equipped 243% bonus as a 3.43 multiplier and apply hits to verify that automatic twisters do not grant Life on Hit while manual hits still do. Merge checks cover same-owner pair selection and manual/automatic cast ordering. See [implementation evidence and remaining parity work](../../docs/etched-sigil.md) for the limits of these checks. Actor spawning, client visuals, and exact live parity remain unverified.

Twisted Sword checks calculate actual hits with the saved 134% roll, the three-cast damage cap, and independent Etched Sigil multiplication. They exercise cast tracking, expiration before actor cleanup, ownership, equipment changes, five-cast merge damage (including Wicked Wind), rune flags/elements, original cast order and aim, proc provenance, and six-second lifetime refreshes. The original one-cast damage assertion failed with 100 instead of 234 before implementation. These tests use inert actors and inspect merge state; they do not run the actor spawn/movement callbacks. See [Twisted Sword evidence and remaining work](../../docs/twisted-sword.md).

Wizard channeling checks cover Mantle's immediate rolled multiplier and 25% reduction, multiplication with Flame Ward, channel interruptions/restarts, Flame Ward's element and defensive ramp, Deflection capacity/rate/independent expiry/absorption, and the installed Twisted Sword and Galvanizing Ward icon slots. Incoming reduction tests attach the production buffs to an inert actor to avoid unrelated Toon initialization; they do not reproduce the entire player damage pipeline. See the [selected-rune audit and Wizard work queue](../../docs/wizard-status.md).
