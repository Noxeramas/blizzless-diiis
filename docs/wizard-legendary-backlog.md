# Wizard legendary power backlog

Audit date: 2026-10-29. Target: Diablo III PC **2.8.0.99920**.

**35 Wizard gameplay powers remain unimplemented**, plus one cosmetic power.
This scope includes powers that modify Wizard skills/passives and procs on
Wizard-only equipment. It excludes the powers already tracked in
[the implementation registry](legendary-powers.md), ordinary item stats, set
bonuses, seasonal powers, legendary gems, and the wider shared-item pool.
“Unimplemented” here concerns the special effect: the item can still provide
its ordinary damage, attributes, sockets, and other supported affixes.

This is a static source/client-data audit, not an in-game test of every item.
The table records missing handlers, not a promise of exact future behavior.
Before implementing each entry, establish its timing, proc rules, stacking,
rune interactions, and equipped/Cube behavior using the registry's evidence
requirements. A tooltip is an inventory of intended effects, not proof of
Blizzard's server logic.

## Missing gameplay powers

| Group | Item | Current power SNO | Missing special behavior |
|---|---|---:|---|
| Channeling | Deathwish | 449063 | Damage multiplier after sustained Wizard channeling. |
| Channeling | Light of Grace | 318855 | Ray of Frost piercing. |
| Hydra | Serpent's Sparker | 484117 | Additional active Hydra and Hydra damage multiplier. |
| Hydra | The Magistrate | 484116 | Hydra Frost Novas and damage multiplier. |
| Hydra / signature spells | Fragment of Destiny | 484599 | Signature attack speed/damage and Spectral stacks increasing Hydra damage. |
| Hydra / Blizzard | Winter Flurry | 484598 | Cold-kill Frost Nova proc and Hydra bonus against enemies in Blizzard. |
| Meteor | The Grand Vizier | 478553 | Meteor resource reduction and damage multiplier. |
| Meteor | The Smoldering Core | 487677 | Luring lesser enemies and consecutive-hit damage stacks. Current installed text caps stacks at three. |
| Meteor | Nilfur's Boast | 478554 | Meteor multiplier and bonus for hitting three or fewer enemies. |
| Meteor | Mempo of Twilight | 487972 | Granted Meteor Shower effect and Meteor damage multiplier. |
| Arcane Orb | Unstable Scepter | 478479 | Arcane Orb multiplier and additional explosion. |
| Arcane Orb | Triumvirate | 434849 | Signature-hit stacks increasing Arcane Orb damage. |
| Arcane Orb | Wizardspike | 484600 | Frozen Orb attack proc and Arcane Orb multiplier. |
| Explosive Blast | Wand of Woh | 478480 | Additional blasts and Explosive Blast multiplier. |
| Explosive Blast | Orb of Infinite Depth | 478481 | Damage and damage-reduction stacks from Explosive Blast hits. |
| Signature spells | Mirrorball | 487928 | Additional Magic Missiles and current damage multiplier. |
| Signature spells | Myken's Ball of Hate | 318775 | Electrocute can chain back to previously hit enemies. |
| Signature spells | Velvet Camaral | 318740 | Increased Electrocute chain count. |
| Signature spells | The Shame of Delsere | 445427 | Signature attack speed and Arcane Power restoration. |
| Archon | Fazula's Improbable Chain | 437854 | Starting Archon stacks. |
| Archon | The Swami | 440336 | Retaining Archon stack bonuses after Archon ends. |
| Teleport | Aether Walker | 397788 | Replace Teleport cooldown with Arcane Power cost. |
| Teleport | Cosmic Strand | 318733* | Grant Wormhole alongside the selected rune. |
| Teleport | The Oculus | 374308 | Chance to reset Teleport when taking damage. Its ordinary flat Teleport cooldown affix has an existing attribute path; that is separate from this missing proc. |
| Slow Time | Gesture of Orpheus | 318376 | Item-specific Slow Time cooldown reduction. |
| Slow Time | Crown of the Primus | 487653 | Following permanent Slow Time and all runes except Point of No Return. |
| Armor spells | Halo of Arlyse | 429648 | Ice Armor melee reduction and damage-triggered Frost Novas. |
| Armor spells | Halo of Karini | 478538 | Temporary damage reduction from a qualifying distant Storm Armor hit. |
| Lightning / passive | Manald Heal | 454930 | Paralysis-triggered Lightning damage. |
| Lightning | Starfire | 451242 | Distance-based Lightning damage multiplier. |
| Passive | Primordial Soul | 448998 | Increased Elemental Exposure bonus per stack. |
| Black Hole | Jang's Envelopment | 318795 | Additional slow on enemies damaged by Black Hole. |
| Mirror Image | Archmage's Vicalyke | 318777 | Images multiplying when killed by enemies. |
| Defense | Dark Mage's Shade | 318788 | Automatic Diamond Skin below the health threshold, with its internal cooldown. |
| Attack proc | Storm Crow | 364338 | Chance to launch a fiery projectile when attacking. |

*Cosmic Strand is crafted. Its base item record does not contain an
`Item_Power_Passive` entry; power 318733 has the matching installed Wormhole
description, and Blizzard's item guide identifies the granted rune. Recipe
affix assignment needs tracing when implementing it. The current Teleport
handler only uses the selected Wormhole rune and has no Cosmic Strand grant.

## Separate queues

- **Cosmetic:** Slorak's Madness, power 248686, is missing its death-laughter
  effect. Its ordinary Disintegrate damage affix is a separate stat and is not
  counted as a missing gameplay power here.
- **Shared items with Wizard-specific effects:** Halcyon's Ascent's Archon
  trigger (374505) and Hellfire Amulet's additional Wizard passive are also
  missing. `SkillSet.HasPassive` only checks the selected passive slots; it
  does not resolve the Hellfire passive-power affixes. These two shared items
  are outside the 35-item count.
- **Wizard set audit:** Tal Rasha's Elements, Firebird's Finery, Delsere's
  Magnum Opus, Vyr's Amazing Arcana, The Typhon's Veil, and Chantodo's Resolve.
  No complete current-PC implementation has been certified in our registry.
  Audit each bonus independently; generic set-stat application does not prove
  its triggered mechanics work. This pass did not classify every set tier.
- **Other shared legendary powers, gems, and seasonal/Ethereal powers:** need
  their own inventory. Absence from our registry means not audited unless a
  specific missing handler is established.

## Audit evidence

- Installed item and affix records from
  `artifacts/legendary-audit/gamebalance/`, item names and passive descriptions
  from that directory's extracted string tables, and power IDs from the
  installed CASC `CoreTOC.dat`.
- Reproducible read-only scanner:
  `artifacts/wizard-legendary-backlog/audit.py`; raw candidate inventory:
  `catalog.json`; item records including replacement links: `all-items.json`.
  These are candidate inventories, not finished support lists: they include
  legacy items, test items, and random affix possibilities on Ethereals. The
  table above selects actual current items and excludes those duplicates.
- Numeric decimal/hex source-reference scan across `GSSystem`, followed by
  inspection of `Wizard.cs`, `PowerContext.cs`, `PowerManager.cs`,
  `HitPayload.cs`, `DeathPayload.cs`, `Player.cs`, and `SkillSet.cs`.
  The listed gameplay power IDs have no combat handlers. Starfire's single
  numeric reference is a hardcoded `Trait` assignment in Player.cs, not a
  distance/damage handler. The generic Cube catalogue resolves data and rolls;
  it does not execute missing effects.
- Blizzard's [Wands](https://us.diablo3.blizzard.com/en-us/item/wand/),
  [Sources](https://us.diablo3.blizzard.com/en-us/item/orb/), and
  [Wizard Hats](https://us.diablo3.blizzard.com/en-us/item/wizard-hat/) guides
  corroborate item identities and effect categories, including Cosmic Strand.
  Some guide values lag patches: use installed current data and the
  [2.7.7 patch notes](https://news.blizzard.com/en-us/article/24046224/season-30-the-lords-of-hell-has-concluded)
  for The Smoldering Core's three-stack cap, rather than its older ten-stack
  web item tooltip.

No gameplay code, character data, or running server was changed by this audit.
