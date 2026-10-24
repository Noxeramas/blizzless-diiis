using DiIiS_NA.GameServer.GSSystem.PlayerSystem;
using DiIiS_NA.LoginServer.AccountsSystem;
using DiIiS_NA.LoginServer.Battle;

namespace DiIiS_NA.GameServer.CommandManager;

[CommandGroup("materials", "Crafting materials for your character: every material in the currency tab.\nOptionally specify the number of each: !materials [count]",
    Account.UserLevels.GM, inGameOnly: true)]
public class MaterialsCommand : CommandGroup
{
    [DefaultCommand(inGameOnly: true)]
    public string Materials(string[] @params, BattleClient invokerClient)
    {
        var player = invokerClient.InGameClient.Player;
        var amount = 100;

        if (@params != null)
            if (!int.TryParse(@params[0], out amount))
                amount = 100;

        var account = player.Toon.GameAccount;
        foreach (var (get, set) in Inventory.CraftingCurrencies.Values)
            set(account, get(account) + amount);
        player.Inventory.UpdateCurrencies();

        return $"Added {amount} of each crafting material";
    }
}
