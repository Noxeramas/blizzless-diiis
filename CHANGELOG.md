# Changelog

This is a fork of [Blizzless DiIiS](https://github.com/blizzless/blizzless-diiis), which targets the 2.7.4.84161 client. It runs the current Battle.net client (2.8.0.99920) and reads game data from your own D3 install. All credit for the server itself goes to the DiIiS devs.

This is a work in progress. It's playable, but there's a lot left to do. Versions stay at 0.x until it's in a state I'd call done. New changes go under the newest version. Once that version is tagged, it gets a date and a new version starts above it.

## 0.1.0 (in progress)

The first version: getting DiIiS onto the 2.8.0 client, plus fixes for what I ran into while playing.

### Before you update

- Only the 2.8.0 client works. A 2.7.4 client can't connect anymore.
- Paragon bonuses saved by the old .NET 7 build don't carry over.
- bnetserver.p12 is gone. The cert gets generated on first start.

### Runtime

- Moved from .NET 7 to .NET 10. .NET 7 is out of support.
- DotNetty 0.7.6. The TLS handshake in 0.7.5 hangs on .NET 8+.
- NHibernate 5.5.4. Versions before 5.5.2 reject every entity on .NET 9+. 5.5.4 also fixes a SQL injection advisory.
- Npgsql 7.0.10, for a security advisory.
- Paragon bonuses are saved as plain bytes now. They used to go through BinaryFormatter, which .NET 8 disables.

### Login / offline

- The server makes its own login certificate on first start, signed by a local CA. The old bundled bnetserver.p12 was the same for everyone, and its private key is public. The CA key only ever exists in memory.
- play.bat trusts the CA once and points the Battle.net login hosts at 127.0.0.1 while the game is running.
- The telemetry and crash report hosts are redirected too, so nothing from an offline session gets sent to Blizzard.
- The redirect is removed when the game closes. `play.bat -Undo` removes the CA as well.
- The login server logs the TLS handshake, the websocket upgrade and the first data a client sends. That helps when a new client version won't connect.

### Game data

- The server reads the CASC storage of your installed game through CascLib (MIT). It finds the install by itself, or you can set `Storage.GameDataPath` in config.ini. Setting it to `mpq` still uses the 2.7.4 MPQs.
- Parsers are updated for the 2.8.0 layouts: item records, paragon bonuses, GameBalance tables and cube recipes.
- An offset that points outside a file is rejected. An asset that fails to parse isn't retried on every access.

### Protocol

- 2.8.0 sends game requests through GameUtilities v2. Those get translated to the old v1 requests.
- Updated message layouts:
  - hero state (new field)
  - game-synced flags
  - saved quest updates
  - saved conversations
  - loadouts (4 legendary power slots)
  - loadout items
  - trickle
- Attributes:
  - Narrow ints are packed the way the client reads them.
  - Two attributes changed type.
  - Eight attributes that were being shifted twice are sent as-is.
- Unknown messages get logged and the rest of their packet is dropped. Before, their data was read as garbage messages.
- tools/protocol has the scripts I used to compare the client's message layouts, attribute table and data formats against DiIiS. They should make the next patch easier.

### Performance

- Client messages were cancelling the running game update. The client sends messages nonstop while you move and fight, so most updates got skipped. Messages wait for the update now.
- Updates run every 50 ms instead of 100 ms, still at 60 ticks/s. That halves the delay the loop adds.
- The Windows timer resolution is set to 1 ms, and Nagle is off on game connections.
- Logging was a big chunk of the lag:
  - The console table redrew every row ever logged. It keeps the last 100 now.
  - The log file is written in batches.
  - Packet dumps only get built if something records them.
  - One attribute warning was logged thousands of times, each with a slow source lookup. It's logged once now.

### Fixes from play testing

- Stuck on the loading screen. Item levels were read from the wrong offset, so vendors had nothing to stock and world gen crashed.
- Crash going back to the local map.
- The first waypoint click on the map did nothing.
- Projectiles hit only after they'd already passed the target.
- Ray of Frost aimed into the ground.
- Loot and projectiles ended up inside the ground in outdoor areas.
- No arc or sound on item drops.
- No animation or sound when identifying.
- "Select A Play Mode" popped up at every login. Account settings, options and hero settings are saved now.
- Tutorials showed with tutorials off, and seen ones came back.
- Exceptions and freezes:
  - entering some level areas
  - killing a bounty unique in some areas
  - breaking a container that was already destroyed
  - creating an item with no item definition

### Crafting

- Crafting multiple items made one no matter the count. Now it makes as many as you asked for and pulls materials from the currency tab, backpack and stash. It stops when something runs out or your bag is full.
- Crafting a single item works too. It had no handler before.
- Artisan workshops show your own artisan level instead of always looking like level 1. Training plays the right animation from level 10 up.
- Training an artisan to level 11 no longer needs an extra Death's Breath.

### Kadala

- Gambling works now. Nothing handled the request before.
- Blood shards you pick up count right away. Before, she only counted what you had when the world loaded.
- The phylactery is back in her stock.

### Kanai's Cube

Before this, only the cow portal worked. Everything else either kept your stuff or gave you something for free.

- Recipes come from the game's TransmuteRecipes table.
- These recipes work:
  - Reforge Legendary
  - Upgrade Rare
  - Convert Set Item
  - converting crafting materials
  - the cow level portal
- Rare helms, chests and belts can be upgraded. Their rares use a different item type than the legendaries.
- A failed transmute shows the real client error ("Missing core ingredients." etc.) and resets the cube. Before, it animated and did nothing.

### Loot

- Smart Loot: 85% of drops are for your class, with your main stat. Before, drops ignored class.
- Legendaries come from the game's own drop table (weights and class flags). DiIiS picked them by name, which caused two problems:
  - Firebird's, Raekor's, Marauder's, Inna's, Akkhan's, the Necro sets and items like Nilfur's Boast never dropped.
  - Crafted sets and old item versions did drop.

  The pool now matches maxroll's drop data except for one item, where the installed game is a patch newer.
- Ancestors' Grace is hardcore only, like on live. Season theme items don't drop, since there are no seasons.
- Six legendaries that DiIiS had flagged as bugged (no reason given) are back, because live drops them. I haven't tested these yet.
- Legendary affixes:
  - Fixed affixes are actually applied (Nilfur's has its Int now).
  - Primary and secondary counts are right.
  - Skill bonuses are for your class.
  - Tiers match the item level.
  - CDR and area damage roll again.
- Drops, Kadala and the cube all pull from the same pool.
- Cosmetic rewards with no stats (Conqueror's Shako and the rest of that season set, Helm of the Hierarch) don't drop or come from gambling anymore.
- Level 70 magic and rare items use level 70 bases only. Before, they could get level 60 to 65 bases with less armor or damage.
- Magic and rare items are always identified, and legendary and set items always drop unidentified, like on live. Before, 5% of magic and rare items came unidentified, and only 30% of legendaries did. An existing `config.mods.json` keeps its old values, so set `UnidentifiedDropChances` to 100 and 0 there.

### GM commands

- `!materials [count]` adds every crafting material.
- `!shards [count]` adds blood shards.
- `!lootpool <type> [class] [level]` lists which legendaries an item type can roll for a class.
- `!unlockart [level]` now takes a level.
