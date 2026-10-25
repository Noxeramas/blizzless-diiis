using System;
using System.Linq;
using DiIiS_NA.Core.Logging;
using DiIiS_NA.GameServer.GSSystem.ItemsSystem;
using DiIiS_NA.LoginServer.AccountsSystem;
using DiIiS_NA.LoginServer.Battle;
using DiIiS_NA.LoginServer.Toons;

namespace DiIiS_NA.GameServer.CommandManager;

[CommandGroup("lootpool", "Lists the legendaries an item type can roll for a class (drops, gambling, Kanai's Cube).\nUsage: !lootpool <item type> [class] [level], for example !lootpool Boots, or !lootpool Boots Wizard 70 from the server console",
    Account.UserLevels.GM)]
public class LootPoolCommand : CommandGroup
{
    private static readonly Logger Logger = LogManager.CreateLogger();

    [DefaultCommand]
    public string LootPool(string[] @params, BattleClient invokerClient)
    {
        if (@params == null || @params.Length == 0)
            return "Usage: !lootpool <item type> [class] [level], for example !lootpool Boots";

        var player = invokerClient?.InGameClient?.Player;
        var type = ItemGroup.FromString(@params[0]);
        if (type == null)
            return $"No item type called {@params[0]}.";

        var toonClass = player?.Toon.Class ?? ToonClass.Unknown;
        if (@params.Length > 1 && !Enum.TryParse(@params[1], true, out toonClass))
            return $"No class called {@params[1]}.";
        if (player == null && toonClass == ToonClass.Unknown)
            return "Name a class from the server console, for example !lootpool Boots Wizard";
        var level = player?.Level ?? 70;
        if (@params.Length > 2 && !int.TryParse(@params[2], out level))
            return $"{@params[2]} isn't a level.";

        var pool = ItemGenerator.LegendaryPool(level, type, toonClass, player?.Toon.IsHardcore ?? false)
            .Select(d => d.Name).OrderBy(n => n).ToList();
        Logger.Info("Legendary pool for {0} ({1}, level {2}): {3}", type.Name, toonClass, level, string.Join(", ", pool));
        return $"{pool.Count} {type.Name} legendaries for a level {level} {toonClass}: {string.Join(", ", pool.Take(20))}{(pool.Count > 20 ? ", ..." : "")}";
    }
}
