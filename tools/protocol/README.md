# Protocol and data tools

Scripts for comparing a Diablo III client's network protocol and data-file layouts with DiIiS. They read the
player's own installed game; nothing here contains Blizzard data. Run them with `python -I`.

## Snapshot the client

The client builds its type descriptors (message and data-file layouts) and attribute encoders at startup, so
they're read from a running game:

```
python -I snapshot.py "<Diablo III>\x64\Diablo III64.exe" <snapshot-dir> 25
```

This starts the game, waits 25 seconds, saves its `.data`/`.rdata` plus every attribute encoder object to
`<snapshot-dir>`, and closes it.

## Compare with DiIiS

| Script | What it checks |
|---|---|
| `dumptypes.py <snap> <Type>...` | Prints a type's fields: offset, kind, bit count, range, arrays; recurses into nested types. |
| `compare_sizes.py <snap> <Opcodes.cs>` | Message sizes against the `//SIZE` comments in DiIiS's opcode list; writes every layout to `types-2.8.txt`. |
| `compare_bitwidths.py <snap> <D3-GameServer>` | Order and width of every narrow (< 32-bit) field against DiIiS's `Encode` methods. Catches same-size changes. |
| `compare_attrs.py <snap> <GameAttribute.List.cs>` | Attribute ids and names. |
| `compare_attr_encodings.py <snap> <GameAttribute.List.cs>` | How each attribute's value is packed (raw, bounded int range and bits, float16, variable float). |
| `gamebalance_tables.py <snap>` | Every GameBalance table's element type and record size, in file order. |
| `dump_field_type.py <snap> <Type> <field>...` | The type of given fields of a type (picks the descriptor with the most fields when names repeat). |
| `protodump.py <exe> <proto-name>...` | Embedded protobuf definitions (Battle.net services and messages). |

## Game data

| Script | What it does |
|---|---|
| `casc_read.py <CascLib.dll> <game dir> <out-dir> <file>...` | Reads files from the installed game's CASC storage. |
| `compare_toc.py <CoreTOC.dat> <Dicts.cs> <SNOGroup.cs>` | Asset names and SNO ids in the game's table of contents against DiIiS's built-in lists. |
| `casc_sample.py <CascLib.dll> <game dir> <out> <per-group> <Group>...` | Exports an even sample of each group's files from the installed game, and their names to `<out>\names.txt`. |
| `mpq_sample.ps1 -Out <out>` | Exports the same files from DiIiS's bundled 2.7.4 MPQs (PowerShell, uses DiIiS's CrystalMpq). |
| `cmp_formats.py <out>` | Per group: files identical, same size, or resized between the versions. Consistent resizing means a layout change. |
| `diff_offsets.py <out> <Group>` | For same-size files that differ, which offsets changed and how (layout change vs. edited values). |
| `export_gam.py <CascLib.dll> <game dir> <out>` | Exports every GameBalance file from the installed game. |
| `cmp_all_tables.py <dir>` / `map_item_fields.py <dir> <table>` | Finds GameBalance fields that moved between 2.7.4 (`mpq_*.gam`) and 2.8.0 (`Base_GameBalance_*.gam`) by matching values for the same named records. |

A full data comparison, from the repository root (`<out>` is any empty folder):

```
python -I tools\protocol\casc_sample.py external\CascLib\bin\CascLib_dll\x64\Release\CascLib.dll "<Diablo III>" <out> 150 Actor Monster Power Scene Worlds LevelArea Quest
powershell -File tools\protocol\mpq_sample.ps1 -Out <out>
python -I tools\protocol\cmp_formats.py <out>
```

## Message dispatcher

`find_jumptables.py` finds large `switch` jump tables in the executable; `find_switch_users.py` finds the code
that indexes one (giving the case range); `align_opcodes.py` compares which opcodes the client handles with
DiIiS's message directions to look for numbering shifts. In 2.8.0.99920 the receive dispatcher's table is at
RVA `0x5ba910`, covering opcodes 2..559, with the default case at `0x5ba5e1`.

`unhandled_msgs.py <Diablo III64.exe> <DiIiS-NA dir>` lists the messages the client sends (the opcodes its
dispatcher never receives) that DiIiS has no handler for. It uses the table address above, so update it for
another client build.
