using System.Reflection;
using System.Runtime.CompilerServices;
using D3.GameBalance;
using DiIiS_NA.Core.Helpers.Hash;
using DiIiS_NA.Core.MPQ;
using DiIiS_NA.Core.Storage.AccountDataBase.Entities;
using DiIiS_NA.GameServer.ClientSystem;
using DiIiS_NA.GameServer.GSSystem.ItemsSystem;
using DiIiS_NA.GameServer.GSSystem.ObjectsSystem;
using DiIiS_NA.GameServer.GSSystem.PlayerSystem;
using DiIiS_NA.GameServer.MessageSystem;
using DiIiS_NA.GameServer.MessageSystem.Message.Definitions.Artisan;
using DiIiS_NA.GameServer.MessageSystem.Message.Definitions.Base;
using DiIiS_NA.GameServer.MessageSystem.Message.Definitions.Skill;
using DiIiS_NA.LoginServer.Toons;
using static DiIiS_NA.Core.MPQ.FileFormats.GameBalance;

var cube = typeof(Player).Assembly.GetType("DiIiS_NA.GameServer.GSSystem.PlayerSystem.KanaiCube")!;
var failures = 0;
var filter = args.FirstOrDefault(a => a.StartsWith("--only="))?[7..];
void Check(string name, Action test)
{
    if (filter != null && !name.Contains(filter)) return;
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + (e.InnerException ?? e)); }
}
object Call(string name, params object[] values) => cube.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, values)!;
void Assert(bool ok, string reason) { if (!ok) throw new Exception(reason); }
void Near(float actual, float expected) => Assert(Math.Abs(actual - expected) < 0.00001f, $"Expected {expected}, got {actual}");

Check("malformed cube counts and duplicate entries return failure 162", () =>
{
    foreach (var message in new[] {
        new TransmuteItemsMessage { annItems = new int[9], ItemsCount = 10, GBIDCurrencies = new int[9] },
        new TransmuteItemsMessage { annItems = new int[9], ItemsCount = -1, GBIDCurrencies = new int[9] },
        new TransmuteItemsMessage { annItems = new int[9], GBIDCurrencies = new int[9], CurrenciesCount = 10 },
        new TransmuteItemsMessage { annItems = new int[9], ItemsCount = 2, GBIDCurrencies = new int[9] },
        new TransmuteItemsMessage { annItems = new int[9], GBIDCurrencies = new int[9], CurrenciesCount = 2 }
    })
    {
        var player = Empty<Player>();
        Set(player, "<Attributes>k__BackingField", new GameAttributeMap(player));
        Set(player, "<Inventory>k__BackingField", new Inventory(player));
        var client = new Recorder(); player.InGameClient = client;
        Call("Transmute", player, message);
        Assert(client.Messages.Count == 1 && client.Messages[0] is IntDataMessage { Field0: 162 }, "Malformed input reached item or currency mutation");
    }
});
Check("power assignment packets route to the player", () =>
{
    Assert(GameMessage.Allocate<AssignLegendaryPowerMessage>(Opcodes.AssignLegendaryPowerMessage).Consumer == Consumers.Player,
        "Assignment is not routed to Player");
});
Check("cube best roll evaluates the complete expression", () =>
{
    // RandomIntMinMax(100,150) / 100: 1.5 in the cube; reversed-roll items use 1.0.
    int F(float value) => BitConverter.SingleToInt32Bits(value);
    var script = new[] { 6, F(100), 6, F(150), 1, 4, 6, F(100), 14, 0 };
    Assert(FormulaScript.Evaluate(script, null, out var max, out _, out _, true), "Formula did not evaluate");
    Assert(FormulaScript.Evaluate(script, null, out var min, out _, out _, false), "Formula did not evaluate");
    Near(max, 1.5f); Near(min, 1f);
    Assert(FormulaScript.Evaluate(new[] { 6, F(1), 0 }, null, out var constant, out _, out _, true), "Constant failed");
    Near(constant, 1f);
});
Check("collection uses item GBIDs in direct BitPackedGbidArray and survives reload", () =>
{
    var ids = new[] { -844626822, 334197455 };
    var bytes = (byte[])Call("CollectionBytes", (object)ids);
    Assert(BitPackedGbidArray.ParseFrom(bytes).ElementsList.SequenceEqual(ids), "Wrong client blob payload");
    Assert(((int[])Call("ReadCollection", new DBCraft { LearnedRecipes = bytes })).SequenceEqual(ids), "Persisted GBIDs changed");
    Assert(((int[])Call("ReadCollection", new object[] { null })).Length == 0, "New account did not have empty collection");
});
Check("cube power replaces equipped roll and clears back to equipment", () =>
{
    var player = Empty<Player>();
    var values = new Dictionary<int, float>();
    Set(player, "<CubedPowerValues>k__BackingField", values);
    Set(player, "<Attributes>k__BackingField", new GameAttributeMap(player));
    var inventory = new Inventory(player);
    Set(player, "<Inventory>k__BackingField", inventory);
    Set(inventory, "<Loaded>k__BackingField", true);
    var equipment = typeof(Inventory).GetField("_equipment", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(inventory)!;
    var items = (Dictionary<uint, Item>)equipment.GetType().GetProperty("Items")!.GetValue(equipment)!;
    var item = Empty<Item>();
    Set(item, "<Attributes>k__BackingField", new GameAttributeMap(item));
    item.Attributes[GameAttributes.Item_Power_Passive, 484709] = 1.34f;
    items.Add(1, item);
    Near(player.GetLegendaryPowerValue(484709), 1.34f);
    values[484709] = 1.5f;
    Near(player.GetLegendaryPowerValue(484709), 1.5f);
    items.Clear(); Near(player.GetLegendaryPowerValue(484709), 1.5f);
    values.Clear(); Near(player.GetLegendaryPowerValue(484709), 0f);
    Assert(new DBActiveSkills().LegendaryPower0 == -1, "New hero had an assigned power");
});

if (args.Contains("--assets"))
{
    // Optional installed-client checks: no account database is opened and no character is altered.
    var tables = MPQStorage.Data.Assets[DiIiS_NA.GameServer.Core.Types.SNO.SNOGroup.GameBalance].Values
        .Select(a => a.Data).OfType<DiIiS_NA.Core.MPQ.FileFormats.GameBalance>().ToArray();
    var recipe = tables.Where(g => g.TransmuteRecipesTables != null).SelectMany(g => g.TransmuteRecipesTables)
        .Single(r => r.TransmuteType == TransmuteType.EXTRACT_LEGENDARY_POWER);
    Check("installed extraction ingredient and material costs", () =>
    {
        var ingredients = recipe.TransmuteRecipeIngredients.Take(recipe.IngredientsCount).ToArray();
        Assert(ingredients[0].Type == 3 && ingredients[0].Quantity == 1, "Unexpected extraction ingredient");
        var account = new DBGameAccount { CraftItem4 = 5, HoradricA1 = 1, HoradricA2 = 1, HoradricA3 = 1, HoradricA4 = 1, HoradricA5 = 1,
            HardCraftItem4 = 5, HardHoradricA1 = 1, HardHoradricA2 = 1, HardHoradricA3 = 1, HardHoradricA4 = 1, HardHoradricA5 = 1 };
        account.HoradricA5 = 0;
        Assert(!(bool)Call("SpendExtractionMaterials", account, false, recipe), "Missing material was accepted");
        Assert(account.CraftItem4 == 5 && account.HoradricA1 == 1, "Failed extraction spent other materials");
        account.HoradricA5 = 1;
        Assert((bool)Call("SpendExtractionMaterials", account, false, recipe), "Valid material balance rejected");
        Assert(account.CraftItem4 == 0 && account.HoradricA1 == 0 && account.HoradricA5 == 0 && account.HardCraftItem4 == 5, "Wrong costs or mode");
        Assert(!(bool)Call("SpendExtractionMaterials", account, false, recipe), "Repeated extraction spent missing materials");
        Assert((bool)Call("SpendExtractionMaterials", account, true, recipe) && account.HardCraftItem4 == 0, "Hardcore costs failed");
    });
    Check("installed power catalogue, replacement, exclusions and class slots", () =>
    {
        object Power(string name) => Call("GetPower", StringHashHelper.HashItemName(name));
        T Property<T>(object power, string name) => (T)power.GetType().GetProperty(name)!.GetValue(power)!;
        var sword = Power("P610_Unique_Sword_1H_107");
        Assert(Property<int>(sword, "PowerSno") == 484709, "Wrong sword power"); Near(Property<float>(sword, "Value"), 1.5f);
        var valthek = Call("GetPower", -1801145918);
        Assert(Property<ItemTable>(valthek, "Item").Name == "P610_Unique_Staff_102", "Wrong saved Valthek assignment");
        Assert(Property<int>(valthek, "PowerSno") == 484597, "Wrong Valthek power"); Near(Property<float>(valthek, "Value"), 4f);
        Assert((bool)Call("CanAssign", valthek, ToonClass.Wizard, 0), "Wizard cannot assign Valthek");
        var ranslor = Call("GetPower", 27086075);
        Assert(Property<ItemTable>(ranslor, "Item").Name == "P61_Unique_Bracer_108_x1", "Wrong saved Ranslor assignment");
        Assert(Property<int>(ranslor, "PowerSno") == 478491, "Wrong Ranslor power"); Near(Property<float>(ranslor, "Value"), 3f);
        Assert((bool)Call("CanAssign", ranslor, ToonClass.Wizard, 1), "Wizard cannot assign Ranslor");
        var caster = Empty<Player>();
        Set(caster, "<Attributes>k__BackingField", new GameAttributeMap(caster));
        var twister = new DiIiS_NA.GameServer.GSSystem.PowerSystem.Implementations.EnergyTwister { PowerSNO = 77113, User = caster };
        Near(twister.ScriptFormula(24), 2f);
        Near(twister.ScriptFormula(25), 30f);
        Near(twister.ScriptFormula(26), 6f);
        Assert((bool)Call("CanAssign", sword, ToonClass.Wizard, 0), "Wizard cannot use sword slot");
        Assert(!(bool)Call("CanAssign", sword, ToonClass.Barbarian, 0), "Power class restriction ignored on a generic sword");
        Assert(!(bool)Call("CanAssign", sword, ToonClass.Wizard, 1), "Weapon power accepted in armor slot");
        var crown = Power("Unique_Helm_002_p1");
        Assert(Property<ItemTable>(crown, "Item").Name == "Unique_Helm_002_p3", "Replacement not resolved");
        Assert(Power("Unique_Dagger_007_x1") == null, "Pig Sticker is extractable");
        Assert(Power("Unique_Sword_2H_104_x1") == null, "Corrupted Ashbringer is extractable");
        var source = Power("P61_Unique_Orb_003");
        Assert((bool)Call("CanAssign", source, ToonClass.Wizard, 0) && !(bool)Call("CanAssign", source, ToonClass.Barbarian, 0), "Class restriction failed");
    });
    var databaseConfig = args.FirstOrDefault(a => a.StartsWith("--database-config="))?[18..];
    if (databaseConfig != null)
        Check("isolated database migration and atomic extraction", () => DatabaseChecks.Run(databaseConfig, cube, recipe));
}
Environment.ExitCode = failures == 0 ? 0 : 1;

static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
static void Set(object target, string field, object value)
{
    for (var type = target.GetType(); type != null; type = type.BaseType)
    {
        var info = type.GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (info != null) { info.SetValue(target, value); return; }
    }
    throw new Exception("Missing fixture field " + field);
}
sealed class Recorder : GameClient
{
    public List<GameMessage> Messages = new();
    public Recorder() : base(null) { }
    public override void SendMessage(GameMessage message) => Messages.Add(message);
}
