# Kanai's Cube: legendary extraction

Target: Diablo III PC **2.8.0.99920**. Extraction, collection persistence, and the
normal weapon/armor/jewelry assignments are implemented. This is not a claim of
complete live-game parity: see the validation limits below.

## Behavior

- The extraction recipe comes from the installed `TransmuteRecipes` GameBalance:
  one extractable legendary, five Death's Breaths, and one bounty material from
  each act. The selected currency IDs must match the recipe and the account must
  have the required balances.
- The catalog uses the item's passive power, extraction flags, replacement item,
  and best-roll direction. Old versions that name a replacement learn that
  replacement's power. Powers in fixed legendary affixes are also resolved.
- Item deletion, currency spending, and collection saving share one database
  transaction. An account row lock serializes concurrent extractions. Duplicate
  powers are compared by power SNO, including different versions of the item.
- Collections belong to a game account and are separate for normal/hardcore and
  seasonal/nonseasonal play. Seasonal collections additionally use `SeasonCreated`.
  The existing `craft_data` store uses artisan `KanaiCube`, with `Level` holding
  the season ID (zero for nonseasonal).
- Collection messages contain a direct `D3.GameBalance.BitPackedGbidArray` of
  canonical **item GBIDs**. Join sends the full collection and loads its icons;
  extraction sends a partial update and a power result.
- Each hero saves three assignments in `skills.LegendaryPower0/1/2`, defaulting to
  `-1` for existing and new heroes. Normal slots require the correct equipment
  category, usable item type, and the power's own class restriction. Assignment
  also requires the power to be in this account/mode/season's collection.
- Existing legendary handlers can read cube powers through
  `Player.GetLegendaryPowerValue` / keyed `Inventory.GetItemBonus`. The cube's best
  roll replaces an equipped copy of the same power; the two do not add together.

## Client evidence

Executable SHA-256:
`753D5F79D12B035B7268FDB255B5B2502EC858D149B6EBB62969A6E92DD23CC4`.
Addresses below are RVAs in that executable, inspected with the repository's
`tools/protocol/pe.py` and disassembly. They must not be assumed valid for another build.

| Evidence | Location / observation |
| --- | --- |
| Extraction recipe | `Base/GameBalance/TransmuteRecipes.gam`: 440-byte recipe; ingredient type 3, quantity 1; Death's Breath GBID 2087837753, quantity 5; act materials GBIDs 1948629088–1948629092, quantity 1 each |
| Extraction ingredient | `0x7f57af`: power must resolve and source flags must not contain `0x01000000` |
| Collection eligibility | `0x755a10`: existing actor, legendary/equippable item, extraction flag clear, no replacement on the final collection entry, valid power |
| Power resolution | `0x75ba20`: follow item replacement at offset `0x4d8`; find base `Item_Power_Passive` (attribute 1293), otherwise use the fixed-affix power lookup |
| Best roll | `0x75bc30`: item flag `0x10000000` selects the lower endpoint; otherwise the upper endpoint |
| Duplicate detection | `0x753bb0`: compare resolved power SNOs across stored item GBIDs |
| Categories / class | `0x75bbc0`: weapon and offhand / armor / accessory; `0x75fe20`: usable item type and power class restriction; `0x7dfcc0`: tag `0x507b0` (`PowerKeys.ClassRestriction`) identifies the hero GBID |
| Full / partial collection | Dispatcher `0x5ba910`, opcodes 289/290: parse `BitPackedGbidArray`; `0x754270` imports item GBIDs and deduplicates powers; opcode 291 loads icons |
| Success | Result handler `0x401880`: result type 0 selects the extracted power's category and item GBID |
| Failures | Error table near `0x10902d8`: 160 filled sockets, 161 unidentified, 162 missing ingredients, 165 already extracted, 166 unavailable power, 170 missing item; opcode 361 reports the code |
| Filled sockets | Cube insertion function `0x52cf70` rejects a filled socket with error 160 |

Blizzard's [2.3.0 patch notes](https://news.blizzard.com/en-gb/article/19859662/patch-2-3-0-now-live)
establish destructive extraction and three normal equipment categories. The
[Season 20 announcement](https://news.blizzard.com/en-us/article/23319442/season-20-now-live)
describes the exceptional unrestricted season slots and confirms that copies of
the same power do not stack. Blizzard also describes extracted-power rollover in
the [Season 10 ending notice](https://news.blizzard.com/en-gb/article/20845156/season-10-ending-soon).

## Verification

`tests/CubeRegression` is a standalone console regression suite:

```powershell
dotnet run --project tests/CubeRegression --
dotnet run --project tests/CubeRegression -- --assets
dotnet run --project tests/CubeRegression -- --assets --database-config=<absolute-local-account-config-path>
```

The asset checks require the installed client and a terminal (the existing data
loader calls `Console.Clear`). The opt-in database checks accept only a local
PostgreSQL host, create a uniquely named disposable database, and drop that
database afterward. They do not write live accounts.

Checks cover malformed/repeated wire ingredients; player routing; full-expression
best-roll evaluation; collection protobuf round trips; equipped/cubed nonstacking;
installed costs, replacements, exclusions, and class/category restrictions;
existing-hero schema migration; persisted assignments; exact material consumption;
duplicate and deleted-item requests; account/mode/season isolation; rollback when
item deletion fails after the collection and currency writes; and concurrent
requests for the same power. Assignment routing and malformed-input checks fail
against the pre-change running server DLL and pass against the new DLL.

## Validation limits

- No side-by-side retail session or packet capture has verified every failure's
  precedence, timing, animation, or UI feedback. Client error strings establish
  the codes, not the retail server's complete decision order. An input matching
  no recipe returns 162; a duplicate matched extraction returns 165.
- Many legendary combat effects are still absent from this server. Extracting and
  selecting those powers saves the selection but cannot supply a missing effect
  handler. Existing handlers using the effective-power lookup include Twisted
  Sword, Etched Sigil, and Mantle of Channeling; their combat tests are separate.
- The server does not implement season themes. This change implements the normal
  three slots, not unrestricted-category seasons or a fourth slot. Automatic
  end-of-season collection rollover is also not implemented.
- Cube access/unlock and town-interaction enforcement still use the existing
  game/client flow; this change does not add a new server interaction state machine.
