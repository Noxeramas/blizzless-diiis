using System.Reflection;
using DiIiS_NA.GameServer.Core.Types.Math;
using DiIiS_NA.GameServer.GSSystem.ActorSystem;
using DiIiS_NA.GameServer.GSSystem.ObjectsSystem;
using DiIiS_NA.GameServer.GSSystem.ItemsSystem;
using DiIiS_NA.GameServer.ClientSystem;
using DiIiS_NA.GameServer.MessageSystem.Message.Fields;
using DiIiS_NA.GameServer.Core.Types.TagMap;
using DiIiS_NA.GameServer.GSSystem.PowerSystem;
using DiIiS_NA.GameServer.GSSystem.PowerSystem.Implementations;
using DiIiS_NA.GameServer.GSSystem.PowerSystem.Payloads;
using DiIiS_NA.GameServer.MessageSystem;
using static EtchedSigilChecks;

static class EquippedLegendaryChecks
{
    const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type Kind(string name) => typeof(MagicWeapon).Assembly.GetType("DiIiS_NA.GameServer.GSSystem.PowerSystem.Implementations." + name)!;
    static object Call(object target, string method, params object[] args) =>
        (target is Type type ? type : target.GetType()).GetMethod(method, Hidden)!.Invoke(target is Type ? null : target, args)!;
    static float Value(object target, string name) => (float)target.GetType().GetProperty(name, Hidden)!.GetValue(target)!;
    static Buff Add(FixtureData f, string type, params object[] args)
    {
        var buff = (Buff)Activator.CreateInstance(Kind(type), Hidden, null, args, null)!;
        f.Player.World.BuffManager.AddBuff(f.Player, f.Player, buff);
        return buff;
    }
    static FixtureData Wizard(int power, float roll = 1)
    {
        var f = Fixture(0);
        f.Item.Attributes[GameAttributes.Item_Power_Passive, 484596] = 0;
        f.Item.Attributes[GameAttributes.Item_Power_Passive, power] = roll;
        f.Player.Attributes[GameAttributes.Level] = 70;
        f.Player.Attributes[GameAttributes.Hitpoints_Max] = 1000;
        f.Player.Attributes[GameAttributes.Hitpoints_Max_Percent_Bonus_Multiplicative] = 1;
        f.Player.Attributes[GameAttributes.Attacks_Per_Second] = 1;
        Set(f.Player, "<LegacyOfDreamsDamage>k__BackingField", 1f);
        return f;
    }
    static HitPayload Hit(FixtureData f, DamageType element)
    {
        var enemy = Empty<EffectActor>();
        enemy.World = f.Player.World;
        enemy.Position = new Vector3D(10, 0, 0);
        Set(enemy, "<Attributes>k__BackingField", new GameAttributeMap(enemy));
        Set(enemy, "<Visible>k__BackingField", true);
        enemy.Attributes[GameAttributes.Level] = 70;
        var attack = new AttackPayload(new PowerContext { User = f.Player, World = f.Player.World });
        attack.AddDamage(100, 0, element);
        return new HitPayload(attack, false, enemy);
    }
    static void Near(float actual, float expected)
    {
        if (!float.IsFinite(actual) || Math.Abs(actual - expected) > .001f)
            throw new Exception($"Expected {expected}, got {actual}");
    }
    public static void Run(Action<string, Action> check)
    {
        check("Ashnagarr's saved 99% roll increases new Deflection capacity, without inflating existing layers", () =>
        {
            var f = Wizard(449043, .99f);
            var buff = (Buff)Activator.CreateInstance(typeof(MagicWeapon).GetNestedType("MagicWeaponBuff", Hidden)!, true)!;
            // Use the actual attack-triggered creation method, with installed Deflection values.
            buff.User = f.Player; buff.Target = f.Player; buff.World = f.Player.World;
            Call(buff, "TryCreateShield", .04f, 3f);
            Near(f.Player.Attributes[GameAttributes.Damage_Shield_Amount], 79.6f);
            f.Item.Attributes[GameAttributes.Item_Power_Passive, 449043] = 0;
            Set(f.Game, "_tickCounter", 60);
            Call(buff, "TryCreateShield", .04f, 3f);
            Near(f.Player.Attributes[GameAttributes.Damage_Shield_Amount], 119.6f);
        });
        check("Convention multiplies only the current element and rotates Arcane Cold Fire Lightning every four seconds", () =>
        {
            var f = Wizard(430674, 1.78f);
            Add(f, "ConventionBuff", new int[] {5, 3, 1, 2});
            var types = new[] {DamageType.Arcane, DamageType.Cold, DamageType.Fire, DamageType.Lightning};
            for (int phase = 0; phase < 8; phase++)
            {
                Set(f.Game, "_tickCounter", phase * 240);
                f.Player.World.BuffManager.Update();
                for (int element = 0; element < 4; element++)
                    Near(Hit(f, types[element]).TotalDamage, phase % 4 == element ? 278 : 100);
            }
            Set(f.Game, "_tickCounter", 1920); f.Player.World.BuffManager.Update();
            var attack = new AttackPayload(new PowerContext {User = f.Player, World = f.Player.World});
            attack.AddDamage(100, 0, DamageType.Arcane);
            attack.AddDamage(100, 0, DamageType.Arcane);
            attack.AddDamage(100, 0, DamageType.Fire);
            Near(new HitPayload(attack, false, Hit(f, DamageType.Fire).Target).TotalDamage, 656);
        });
        check("Aquila uses the equipped threshold, quarter-second checks, and stops when broken", () =>
        {
            var f = Wizard(449064, .93f);
            f.Player.Attributes[GameAttributes.Resource_Max, 1] = 100;
            f.Player.Attributes[GameAttributes.Resource_Percent, 1] = 0;
            f.Player.Attributes[GameAttributes.Resource_Cur, 1] = 94;
            Near(f.Player.Attributes[GameAttributes.Resource_Max_Total, 1], 100);
            var buff = Add(f, "AquilaBuff", 1);
            Near(Value(buff, "DamageReduction"), .5f);
            f.Player.Attributes[GameAttributes.Resource_Cur, 1] = 93;
            Set(f.Game, "_tickCounter", 14); buff.Update();
            Near(Value(buff, "DamageReduction"), .5f);
            Set(f.Game, "_tickCounter", 15); buff.Update();
            Near(Value(buff, "DamageReduction"), 0);
            f.Player.Attributes[GameAttributes.Resource_Cur, 1] = 93.01f;
            Set(f.Game, "_tickCounter", 30); buff.Update();
            Near(Value(buff, "DamageReduction"), .5f);
            f.Item.Attributes[GameAttributes.Durability_Cur] = 0;
            Near(Value(buff, "DamageReduction"), 0);
            f.Player.World.BuffManager.Update();
            Near(f.Player.Attributes[GameAttributes.Buff_Icon_Count1, 449064], 0);
        });
        check("Hergbrash reduces each channel tick before prorated flat costs and respects Cube overrides", () =>
        {
            var f = Wizard(449048, .59f);
            f.Player.Attributes[GameAttributes.Resource_Cost_Reduction_Amount] = 3;
            f.Player.Attributes[GameAttributes.Resource_Cost_Reduction_Percent_All] = .1f;
            float Cost(int power, float cost, float fraction) => (float)Call(Kind("EquippedLegendaryPowers"), "ChannelCost", f.Player, power, cost, fraction);
            Near(Cost(134456, 16f / 3, 1f / 3), (16 * .41f * .9f - 3) / 3);
            Near(Cost(91549, 18f / 3, 1f / 3), (18 * .41f * .9f - 3) / 3);
            Near(Cost(93395, 11f / 2, .5f), (11 * .41f * .9f - 3) / 2);
            Set(f.Player, "<CubedPowerValues>k__BackingField", new Dictionary<int, float> { [449048] = .65f });
            Near(Cost(134456, 16f / 3, 1f / 3), (16 * .35f * .9f - 3) / 3);
            f.Player.Attributes[GameAttributes.Resource_Cost_Reduction_Amount] = 10;
            Near(Cost(134456, 16f / 3, 1f / 3), 0);
        });
        check("Stone Gauntlets caps at five additive armor stacks and Ice Climbers suppresses both penalties", () =>
        {
            var f = Wizard(483519);
            f.Player.Attributes[GameAttributes.Armor] = 1000;
            f.Player.Attributes[GameAttributes.Armor_Bonus_Percent] = .25f;
            f.Item.Attributes[GameAttributes.Item_Power_Passive, 318741] = 1;
            for (int i = 0; i < 8; i++) Add(f, "StoneGauntletsBuff");
            Near(f.Player.Attributes[GameAttributes.Armor_Total], 3750);
            Near(f.Player.Attributes[GameAttributes.Attacks_Per_Second_Percent_Reduction], 0);
            Near(f.Player.Attributes[GameAttributes.Movement_Scalar_Reduction_Percent], 0);
            Near(f.Player.Attributes[GameAttributes.Buff_Icon_Count1, 483519], 5);
            f.Item.Attributes[GameAttributes.Item_Power_Passive, 318741] = 0;
            f.Player.World.BuffManager.Update();
            Near(f.Player.Attributes[GameAttributes.Attacks_Per_Second_Percent_Reduction], 1);
            Near(f.Player.Attributes[GameAttributes.Movement_Scalar_Reduction_Percent], .75f);
            Set(f.Game, "_tickCounter", 60); f.Player.World.BuffManager.Update();
            Near(f.Player.Attributes[GameAttributes.Attacks_Per_Second_Percent_Reduction], 0);
            Near(f.Player.Attributes[GameAttributes.Armor_Total], 3750);
            Set(f.Game, "_tickCounter", 300); f.Player.World.BuffManager.Update();
            Near(f.Player.Attributes[GameAttributes.Armor_Total], 1250);
            Near(f.Player.Attributes[GameAttributes.Buff_Icon_Count1, 483519], 0);
        });
        check("Ice Climbers rejects freeze and immobilize without granting immunity to unrelated statuses", () =>
        {
            var f = Wizard(318741);
            var enemy = Hit(f, DamageType.Arcane).Target;
            var context = new PowerContext {World = f.Player.World};
            if (f.Player.World.BuffManager.AddBuff(enemy, f.Player, new DebuffFrozen(context.WaitSeconds(2))) ||
                f.Player.World.BuffManager.AddBuff(enemy, f.Player, new DebuffRooted(context.WaitSeconds(2))))
                throw new Exception("Ice Climbers allowed freeze/root");
            if (f.Player.Attributes[GameAttributes.Frozen] || f.Player.Attributes[GameAttributes.IsRooted] ||
                f.Player.Attributes[GameAttributes.Stun_Immune] || f.Player.Attributes[GameAttributes.Slowdown_Immune])
                throw new Exception("Wrong status or immunity applied");
        });
        check("Stone of Jordan shares the highest static element, excludes Convention, and does not compound", () =>
        {
            var f = Wizard(484282);
            var elements = new[] {5, 3, 1, 2};
            f.Item.Attributes[GameAttributes.Damage_Dealt_Percent_Bonus, 5] = .37f;
            f.Item.Attributes[GameAttributes.Damage_Dealt_Percent_Bonus, 1] = .2f;
            Call(Kind("EquippedLegendaryPowers"), "ApplyStoneOfJordan", f.Player, elements);
            foreach (var type in new[] {DamageType.Arcane, DamageType.Cold, DamageType.Fire, DamageType.Lightning}) Near(Hit(f, type).TotalDamage, 137);
            Call(Kind("EquippedLegendaryPowers"), "ApplyStoneOfJordan", f.Player, elements);
            Near(Hit(f, DamageType.Fire).TotalDamage, 137);
            f.Item.Attributes[GameAttributes.Item_Power_Passive, 430674] = 1.78f;
            Add(f, "ConventionBuff", elements);
            Near(Hit(f, DamageType.Fire).TotalDamage, 137);
            Near(Hit(f, DamageType.Arcane).TotalDamage, 137 * 2.78f);
            f.Item.Attributes[GameAttributes.Item_Power_Passive, 484282] = 0;
            Call(Kind("EquippedLegendaryPowers"), "ApplyStoneOfJordan", f.Player, elements);
            Near(Hit(f, DamageType.Fire).TotalDamage, 120);
            Near(Hit(f, DamageType.Cold).TotalDamage, 100);
            Set(f.Player, "<CubedPowerValues>k__BackingField", new Dictionary<int, float> { [484282] = 1 });
            Call(Kind("EquippedLegendaryPowers"), "ApplyStoneOfJordan", f.Player, elements);
            Near(Hit(f, DamageType.Fire).TotalDamage, 137);
        });
        check("Squirt's stacks once a second, caps at ten, and resets only after damage penetrates a shield", () =>
        {
            var f = Wizard(483552);
            var squirt = Add(f, "SquirtsBuff");
            Set(f.Game, "_tickCounter", 59); squirt.Update(); Near(Value(squirt, "DamageBonus"), 0);
            Set(f.Game, "_tickCounter", 60); squirt.Update(); Near(Hit(f, DamageType.Fire).TotalDamage, 110);
            Set(f.Game, "_tickCounter", 600); squirt.Update(); Near(Hit(f, DamageType.Fire).TotalDamage, 200);
            Near(Value(squirt, "DamageTakenBonus"), .5f);
            var incoming = Wizard(0);
            incoming.Player.World = f.Player.World;
            var target = Hit(incoming, DamageType.Fire).Target;
            target.Attributes[GameAttributes.Hitpoints_Max] = 1000;
            target.Attributes[GameAttributes.Hitpoints_Cur] = 1000;
            // Move this same buff to an inert target to exercise the real HP-write path without loading Toon/CASC.
            f.Player.World.BuffManager.RemoveBuff(f.Player, squirt);
            squirt = (Buff)Activator.CreateInstance(Kind("SquirtsBuff"), true)!;
            f.Player.World.BuffManager.AddBuff(f.Player, target, squirt);
            Set(f.Game, "_tickCounter", 1200); squirt.Update();
            f.Player.World.BuffManager.AddBuff(f.Player, target, new MagicWeapon.DeflectionBuff(200, squirt.WaitSeconds(3)));
            void Damage()
            {
                var attack = new AttackPayload(new PowerContext { User = incoming.Player, World = f.Player.World });
                attack.AddDamage(100, 0, DamageType.Fire);
                var hit = new HitPayload(attack, false, target) {AutomaticHitEffects = false};
                Near(hit.TotalDamage, 150);
                hit.Apply();
            }
            Damage(); Near(Value(squirt, "DamageBonus"), 1); Near(target.Attributes[GameAttributes.Hitpoints_Cur], 1000);
            Damage(); Near(Value(squirt, "DamageBonus"), 0); Near(target.Attributes[GameAttributes.Hitpoints_Cur], 900);
            Set(f.Game, "_tickCounter", 1319); squirt.Update(); Near(Value(squirt, "DamageBonus"), 0);
            Set(f.Game, "_tickCounter", 1320); squirt.Update(); Near(Value(squirt, "DamageBonus"), .1f);
            f.Items.Clear(); f.Player.World.BuffManager.Update();
            Near(target.Attributes[GameAttributes.Buff_Icon_Count5, 483552], 0);
        });
        check("Ashnagarr strengthens Galvanizing Ward and non-weapon hits consume its displayed capacity", () =>
        {
            var f = Wizard(449043, .99f);
            var ward = Add(f, "GalvShieldBuff");
            Near(f.Player.Attributes[GameAttributes.Damage_Shield_Amount], 1194);
            var hit = Empty<HitPayload>(); hit.Target = f.Player; hit.TotalDamage = 200;
            ward.OnPayload(hit);
            Near(hit.TotalDamage, 0);
            Near(f.Player.Attributes[GameAttributes.Damage_Shield_Amount], 994);
            f.Player.World.BuffManager.RemoveBuff(f.Player, ward);
            Near(f.Player.Attributes[GameAttributes.Damage_Shield_Amount], 0);
        });
    }

    public static void RunInstalled(Action<string, Action> check)
    {
        check("Installed power formulas and icon slots match Aquila, Stone Gauntlets, Convention and Squirt's", () =>
        {
            foreach (var (power, formula, expected) in new[] {
                (449064, 0, .25f), (449064, 1, .5f), (483519, 0, 1f), (483519, 1, .15f),
                (483519, 2, .5f), (483519, 3, .2f), (483519, 5, 5f), (430674, 0, 4f),
                (483552, 0, .1f), (483552, 1, .05f), (483552, 2, 10f), (483552, 3, 2f) })
            {
                var f = Wizard(power);
                var context = new PowerContext {User = f.Player, World = f.Player.World, PowerSNO = power};
                Near(context.ScriptFormula(formula), expected);
            }
            foreach (var (power, key) in new[] { (449064, PowerKeys.Buff1Icon), (483519, PowerKeys.Buff1Icon),
                (430674, PowerKeys.Buff1Icon), (430674, PowerKeys.Buff2Icon), (430674, PowerKeys.Buff3Icon),
                (430674, PowerKeys.Buff5Icon), (483552, PowerKeys.Buff5Icon) })
            {
                var context = new PowerContext {PowerSNO = power};
                if (context.EvalTag(key) == 0) throw new Exception("Missing installed icon for " + power);
            }
        });
        check("Leoric's equipped 93% roll amplifies real helm gem effects and leaves body sockets alone", () =>
        {
            var helm = ItemGenerator.Items.Values.Single(i => i.Name == "Unique_Helm_002_p1");
            var chest = ItemGenerator.Items.Values.Single(i => i.Name == "P4_Unique_Chest_012");
            foreach (string family in new[] { "Ruby_", "Amethyst_", "Topaz_", "Emerald_", "Diamond_" })
            {
                var gemDef = ItemGenerator.Items.Values.Where(i => i.Name?.StartsWith(family) == true)
                    .Where(i => ItemGenerator.GetGemEffectDefinition(i.Hash, 3851110) != null)
                    .OrderByDescending(i => i.ItemLevel).First();
                var effects = ItemGenerator.GetGemEffectDefinition(gemDef.Hash, 3851110);
                var attr = effects.Attribute.First(a => a.AttributeId > 0);
                var attribute = (GameAttributeF)GameAttributes.Attributes[attr.AttributeId];
                float Apply(int equipment, float roll, int? key)
                {
                    var f = Wizard(0);
                    Set(f.Item, "<GBHandle>k__BackingField", new GBHandle { GBID = equipment });
                    f.Item.Gems = new();
                    f.Item.Attributes[GameAttributes.Gem_Attributes_Multiplier] = roll;
                    var gem = Empty<Item>();
                    Set(gem, "<GBHandle>k__BackingField", new GBHandle { GBID = gemDef.Hash });
                    f.Item.Gems.Add(gem);
                    f.Player.InGameClient = Empty<GameClient>(); f.Player.InGameClient.Game = f.Game;
                    float before = f.Player.Attributes[attribute, key];
                    f.Player.Inventory.SetGemBonuses();
                    return f.Player.Attributes[attribute, key] - before;
                }
                int? key = attr.SNOParam == -1 ? null : attr.SNOParam;
                float normal = Apply(helm.Hash, 0, key);
                if (normal == 0) throw new Exception("No gem effect for " + gemDef.Name);
                Near(Apply(helm.Hash, .93f, key), normal * 1.93f);
                Near(Apply(chest.Hash, .93f, key), Apply(chest.Hash, 0, key));
            }
        });
    }
}
