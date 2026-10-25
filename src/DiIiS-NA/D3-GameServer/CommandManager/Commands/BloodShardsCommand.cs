using DiIiS_NA.LoginServer.AccountsSystem;
using DiIiS_NA.LoginServer.Battle;

namespace DiIiS_NA.GameServer.CommandManager;

[CommandGroup("shards", "Blood shards for your character, to gamble with Kadala.\nOptionally specify the number of shards: !shards [count]",
    Account.UserLevels.GM, inGameOnly: true)]
public class BloodShardsCommand : CommandGroup
{
    [DefaultCommand(inGameOnly: true)]
    public string Shards(string[] @params, BattleClient invokerClient)
    {
        var player = invokerClient.InGameClient.Player;
        var amount = 100;

        if (@params != null)
            if (!int.TryParse(@params[0], out amount))
                amount = 100;

        player.SaveCollectedBloodShards();
        player.Toon.GameAccount.BloodShards += amount;
        player.Inventory.UpdateCurrencies();

        return $"Added {amount} blood shards";
    }
}