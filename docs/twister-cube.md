# Valthek's Rebuke and Ranslor's Folly

Target: Diablo III PC **2.8.0.99920**. Both powers were correctly assigned in
the saved Wizard's Cube, but neither had an Energy Twister combat handler.
Extraction/assignment alone did not activate their effects.

## Saved assignments and client evidence

Read-only inspection of toon 2 on 2026-10-29 found:

| Cube slot | Item GBID / installed definition | Passive power | Cube roll |
| --- | --- | --- | --- |
| Weapon | -1801145918 / P610_Unique_Staff_102 | 484597 / P610_ItemPassive_Unique_Ring_003 | 4.0 (+400%) |
| Armor | 27086075 / P61_Unique_Bracer_108_x1 | 478491 / P61_ItemPassive_Unique_Ring_018 | 3.0 (+300%) |

`CubeRegression --assets` resolves these exact saved IDs through the production
catalog and verifies their best rolls and Wizard slot eligibility. The character
and collection records were not changed.

Blizzard describes [Valthek's straight path and 300–400% damage bonus](https://eu.diablo3.blizzard.com/en-us/item/valtheks-rebuke-P610_Unique_Staff_102)
and [Ranslor's 225–300% bonus and 30-yard lesser-enemy pull](https://eu.diablo3.blizzard.com/en-gb/item/ranslors-folly-P61_Unique_Bracer_108_x1).
The current Valthek power was introduced in
[patch 2.6.10](https://news.blizzard.com/en-gb/article/23548757/season-22-shades-of-the-nephalem-has-ended).

The installed Ranslor power and its P2 predecessor (423240) contain these
formula values **and labels**: SF0 = 2, `Tickrate`; SF1 = 30, `Radius`;
SF2 = 6, `Per Target ICD`. Energy Twister SF24/25/26 reference the P2 values.
The optional asset check evaluates those references as 2, 30 and 6.
The historical wiki claim of five seconds conflicts with this installed data;
the implementation uses the client's six-second value.

## Implementation

- Each Energy Twister hit reads both effective item powers through the existing
  equipment/Cube lookup. The multipliers are independent: **5 × 4 = 20** at
  Cube rolls. They multiply with Sword, Sigil and Mantle; Cube copies replace
  equipped duplicates. Broken/removed equipment stops supplying its bonus.
- The same hit path covers manual, Sigil-generated and merged twisters.
  The new multipliers do not change their proc provenance or rune element.
  Both appear by name, multiplier and source in the combat meter.
- Valthek casts travel toward and beyond the aim point for the remaining
  lifetime at the existing skill speed. They do not choose random turns.
  Merges retain the existing redirection toward the latest absorbed cast's aim.
  Wicked Wind remains stationary, consistent with the
  [2024 player report and response](https://us.forums.blizzard.com/en/d3/t/valthek%E2%80%99s-rebuke-not-working-from-kanais-cube/64599).
- Ranslor checks for pulls every two seconds, starting two seconds after spawn.
  Merging preserves the parent's pulse phase. It pulls eligible monsters to the
  twister's center using the server's knockback movement, with a six-second
  lockout shared by that Wizard's twisters. Other Wizards have separate lockouts.
- Pull eligibility excludes elite qualities (including rare minions), bosses,
  large monsters, dead/foreign-world actors, knockback-immune actors and actors
  beyond 30 yards. Standard, Ranged and Swarm monster categories are eligible.
  This follows [player observations of size exclusions and tightly packed pulls](https://us.forums.blizzard.com/en/d3/t/about-meteor-explosive-blast-black-hole-and-tal-rasha/25175).
  Invalid landing cells and already active knockbacks do not consume the lockout.
- The centered knockback overload preserves existing magnitude-based behavior.
  Landing validation now precedes publishing its active attribute, so a rejected
  movement does not leave a ghost knockback effect. Internal Ranslor cooldowns
  publish no client icon or Energy Twister buff attribute.

## Verification and remaining parity work

Before the change, the new real-hit checks failed: cubed powers dealt **100**
instead of **2,000**; equipped 328%/249% rolls dealt **100** instead of **1,493.72**.
Regression coverage checks those bonuses, duplicate suppression, removal and
breakage, Sigil interaction, meter factors, rune preservation, straight movement,
zero-distance aim, pull exclusions, range boundaries, independent Wizard
lockouts, expiry, rejected landings and centered movement on installed navigation
data. These are server checks, not a live-game comparison.

Validation on 2026-10-29: all **78 CombatRegression checks with `--assets`** and
**7 CubeRegression checks with `--assets`** passed. The tested DLL was deployed;
all four listeners (83, 1119, 2001, 9800) were ready with no startup Error/Fatal
entries. Saved Cube IDs were unchanged after restart. In-game acceptance is pending.

Exact parity is **not certified**. Still compare on current live PC:

- first pull phase, pulse/merge ordering, radius treatment at monster hitboxes,
  movement animation/speed, multiplayer cooldown ownership and power changes
  while a twister exists;
- full merged actor lifecycle, item-generated casts, cast origin and redirection;
- walls, corners and blocked paths: the inherited Energy Twister mover does not
  implement complete PC collision behavior. Blizzard's
  [2.4.2 notes](https://news.blizzard.com/en-us/article/20210379/patch-2-4-2-now-live)
  specify despawning non-Wicked-Wind twisters when they stop moving;
- global crowd-control resistance/decay and interactions such as Strongarm.
  The server's existing knockback subsystem does not implement that complete
  system; the per-target Ranslor lockout is not a replacement for it.

The server updates in three-frame steps, which also limits execution timing.
Legacy equipped item revisions are outside this pass; extraction resolves the
current replacements through the existing Cube catalog.
