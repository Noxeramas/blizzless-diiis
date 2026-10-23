"""For every GameBalance table present in both versions, find fields whose offset moved within the record.

Usage: python -I cmp_all_tables.py <dir with Base_GameBalance_*.gam (2.8.0) and mpq_*.gam (2.7.4)>
Records are matched by the name in their first 256 bytes (tables without names are skipped). The 2.7.4
record size is inferred as the divisor of the old table size that matches the most names.
"""
import glob
import os
import struct
import sys
from collections import Counter, defaultdict

# GameBalance table header struct offsets (file offset = struct offset + 16) and 2.8.0 record sizes.
TABLES = [(24, "ItemType", 336), (40, "Item", 1408), (56, "ExperienceLevel", 496), (72, "ExperienceLevelAlt", 128),
          (88, "HelpCodes", 640), (104, "MonsterLevelDefinition", 240), (120, "AffixTableEntry", 784),
          (136, "HeroData", 504), (152, "MovementStyleDefinition", 392), (168, "LabelGBID", 272),
          (184, "LootDistribution", 92), (200, "RareItemNamesEntry", 280), (216, "MonsterAffixesEntry", 904),
          (232, "RareMonsterNamesEntry", 400), (248, "SocketedEffectsTableEntry", 1416),
          (264, "ItemDropTableEntry", 1144), (280, "ItemLevelMod", 92), (296, "QualityClass", 360),
          (312, "HandicapLevel", 32), (328, "ItemSalvageLevel", 16), (344, "HirelingEntry", 328),
          (360, "SetItemBonusTableEntry", 464), (376, "EliteModifierData", 352), (392, "ItemTierData", 32),
          (408, "PowerFormulaTableEntry", 1328), (424, "RecipeEntry", 336), (440, "ScriptedAchievementEvent", 264),
          (456, "LootRunQuestTierEntry", 520), (472, "ParagonBonus", 736), (488, "LegacyItemConversion", 280),
          (504, "EnchantItemAffixUseCountCostScalar", 8), (520, "TieredLootRunLevel", 56),
          (536, "TransmuteRecipe", 440), (552, "CurrencyConversionTableEntry", 304)]


def table(d, struct_off):
    off, size = struct.unpack_from("<ii", d, struct_off + 16)
    return (off + 16, size) if size > 0 and off > 0 and off + 16 + size <= len(d) else (None, 0)


def named(d, base, size, rec):
    out = {}
    for k in range(size // rec):
        r = base + k * rec
        raw = d[r:r + 256].split(b"\0", 1)[0]
        if raw and all(32 <= c < 127 for c in raw):
            out[raw] = d[r:r + rec]
    return out


rows = defaultdict(lambda: [{}, {}, None])  # struct offset -> [new records, old records, old record size]
for new_path in glob.glob(os.path.join(sys.argv[1], "Base_GameBalance_*.gam")):
    old_path = os.path.join(sys.argv[1], "mpq_" + os.path.basename(new_path)[len("Base_GameBalance_"):])
    if not os.path.exists(old_path):
        continue
    nd, od = open(new_path, "rb").read(), open(old_path, "rb").read()
    for soff, tname, rec in TABLES:
        nb, ns = table(nd, soff)
        ob, os_ = table(od, soff)
        if not ns or not os_ or ns % rec:
            continue
        new = named(nd, nb, ns, rec)
        if len(new) < max(1, ns // rec // 2):
            continue
        best = max((c for c in range(max(8, rec - 256), rec + 257, 4) if os_ % c == 0),
                   key=lambda c: (len(set(named(od, ob, os_, c)) & set(new)), -abs(c - rec)), default=None)
        if best is None:
            continue
        old = named(od, ob, os_, best)
        rows[soff][0].update(new)
        rows[soff][1].update(old)
        rows[soff][2] = best

for soff, tname, rec in TABLES:
    new, old, orec = rows[soff]
    common = [n for n in old if n in new]
    if len(common) < 5:
        continue
    moved = []
    for o in range(256, min(orec, rec), 4):
        oc = [struct.unpack_from("<i", old[n], o)[0] for n in common]
        if len(set(oc)) < 3:
            continue  # constant-ish columns can't be placed reliably
        hits = Counter()
        for p in range(256, rec - 3, 4):
            hits[p] = sum(struct.unpack_from("<i", new[n], p)[0] == v for n, v in zip(common, oc))
        best, h = hits.most_common(1)[0]
        if best != o and h > 0.95 * len(common) and hits[o] < 0.95 * len(common):
            moved.append((o, best))
    status = "OK" if not moved and orec == rec else f"record {orec} -> {rec}"
    print(f"{tname:<34} {len(common):>5} named records  {status}")
    if moved:
        runs, start = [], moved[0]
        for (a, b), (c, d) in zip(moved, moved[1:] + [(None, None)]):
            if c is None or c != a + 4 or d - c != b - a:
                runs.append((start[0], a, b - a))
                start = (c, d)
        for s, e, shift in runs:
            print(f"      fields @{s}..@{e} moved {shift:+d}")
