using System.Reflection;
using DiIiS_NA.GameServer.Core.Types.Math;
using DiIiS_NA.GameServer.GSSystem.ObjectsSystem;
using DiIiS_NA.GameServer.GSSystem.PowerSystem;
using DiIiS_NA.GameServer.GSSystem.PowerSystem.Implementations;
using DiIiS_NA.GameServer.GSSystem.PowerSystem.Payloads;
using DiIiS_NA.GameServer.GSSystem.SkillsSystem;
using DiIiS_NA.GameServer.MessageSystem;
using static EtchedSigilChecks;

static class TwistedSwordChecks
{
    const int TwisterPower = Skills.Wizard.Offensive.EnergyTwister;
    const int SwordPower = 484709;

    public static void Run(Action<string, Action> check)
    {
        check("Cube powers grant the Sword rune and multiply with Sigil and Mantle without adding equipped duplicates", () =>
        {
            var f = SwordFixture();
            Set(f.Player, "<CubedPowerValues>k__BackingField", new Dictionary<int, float> {
                [SwordPower] = 1.5f, [484596] = 2.5f, [446640] = .25f
            });
            var parent = Cast(f, 1);
            Cast(f, 2);
            Cast(f, 3);
            f.Channel.Run().ToArray();
            Near(Damage(f, parent), 2406.25f); // 100 * 5.5 * 3.5 * 1.25; equipped copies do not add.
            f.Items.Clear();
            Near(Damage(f, parent), 2406.25f);
            Assert(Absorb(parent, Cast(f, 4)), "Cubed sword did not grant Raging Storm");
        });

        check("Twisted Sword publishes the client Energy Twister slot 6 icon, capped at three", () =>
        {
            var f = SwordFixture();
            var parent = Cast(f, 1);
            Near(f.Player.Attributes[GameAttributes.Buff_Icon_Count6, TwisterPower], 1);
            Near(f.Player.Attributes[GameAttributes.Buff_Icon_End_Tick6, TwisterPower], 360);
            Set(f.Game, "_tickCounter", 60);
            Assert(Absorb(parent, Cast(f, 2)), "Merge failed");
            f.Player.World.BuffManager.Update();
            Near(f.Player.Attributes[GameAttributes.Buff_Icon_Count6, TwisterPower], 2);
            Near(f.Player.Attributes[GameAttributes.Buff_Icon_End_Tick6, TwisterPower], 420);
            Cast(f, 3);
            Cast(f, 4);
            Near(f.Player.Attributes[GameAttributes.Buff_Icon_Count6, TwisterPower], 3);
            Near(f.Player.Attributes[GameAttributes.Buff_Icon_Count0, SwordPower], 0);
            Set(f.Game, "_tickCounter", 420);
            f.Player.World.BuffManager.Update();
            Near(f.Player.Attributes[GameAttributes.Buff_Icon_Count6, TwisterPower], 0);
            Near(f.Player.Attributes[GameAttributes.Buff_Icon_End_Tick6, TwisterPower], 0);
        });

        check("Twisted Sword clears its icon on unequip, breakage, death, world change, and removal", () =>
        {
            Action<FixtureData>[] stop = {
                f => f.Items.Clear(), f => f.Item.Attributes[GameAttributes.Durability_Cur] = 0,
                f => f.Player.Dead = true, f => f.Player.World = Fixture(0).Player.World,
                f => f.Player.World.BuffManager.RemoveAllBuffs(f.Player)
            };
            foreach (var interrupt in stop)
            {
                var f = SwordFixture();
                Cast(f, 1);
                var world = f.Player.World;
                interrupt(f);
                world.BuffManager.Update();
                Near(f.Player.Attributes[GameAttributes.Buff_Icon_Count6, TwisterPower], 0);
            }
        });

        check("Twisted Sword's saved 134% roll increases a real Energy Twister hit", () =>
        {
            var f = SwordFixture();
            Near(Damage(f, Cast(f, 1)), 234f);
        });

        check("Twisted Sword caps damage at three casts, reads equipment changes, and only boosts Energy Twister", () =>
        {
            var f = SwordFixture();
            EnergyTwister context = null;
            foreach (var (id, damage) in new[] { (1u, 234f), (2u, 368f), (3u, 502f), (4u, 502f), (5u, 502f) })
            {
                context = Cast(f, id);
                Near(Damage(f, context), damage);
            }
            Near(Damage(f, new PowerContext { User = f.Player, World = f.Player.World,
                PowerSNO = Skills.Wizard.Offensive.ArcaneTorrent }), 100f);
            f.Item.Attributes[GameAttributes.Item_Power_Passive, SwordPower] = 1.5f;
            Near(Damage(f, context), 550f);
            f.Item.Attributes[GameAttributes.Durability_Cur] = 0;
            Near(Damage(f, context), 100f);
            f.Item.Attributes[GameAttributes.Durability_Cur] = 100;
            f.Items.Clear();
            Near(Damage(f, context), 100f);
        });

        check("Twisted Sword multiplies independently with the equipped Etched Sigil roll", () =>
        {
            var f = SwordFixture();
            var context = Cast(f, 1);
            Cast(f, 2);
            Cast(f, 3);
            f.Channel.Run().ToArray();
            Near(Damage(f, context), 1721.86f); // 100 * (1 + 3 * 1.34) * (1 + 2.43)
            f.Channel.CloseChannel();
            Near(Damage(f, context), 502f);
        });

        check("Twisted Sword ignores foreign, destroyed, and expired casts even before actor cleanup", () =>
        {
            var f = SwordFixture();
            var context = Cast(f, 1);
            var foreign = SwordFixture();
            foreign.Player.World = f.Player.World;
            Cast(foreign, 2);
            Near(Damage(f, context), 234f);
            var destroyed = Cast(f, 3);
            Actor(destroyed).World = null;
            Near(Damage(f, context), 234f);
            Set(f.Game, "_tickCounter", 360);
            Near(Damage(f, context), 100f);
            Assert(Actor(context).World != null, "Fixture did not exercise delayed actor cleanup");
            f.Player.World.BuffManager.Update();
            Assert(f.Player.World.BuffManager.GetFirstBuff<TwistedSwordBuff>(f.Player) == null,
                "Empty tracker remained after the last twister expired");
        });

        check("Twisted Sword drops its bonus on death and world changes", () =>
        {
            foreach (bool worldChange in new[] { false, true })
            {
                var f = SwordFixture();
                Cast(f, 1);
                var buff = f.Player.World.BuffManager.GetFirstBuff<TwistedSwordBuff>(f.Player);
                if (worldChange) f.Player.World = Fixture(0).Player.World;
                else f.Player.Dead = true;
                Near(buff.DamageBonus, 0f);
                Assert(buff.Update(), "Invalid owner's tracker was not removed");
            }
        });

        check("Twisted Sword counts all casts inside a tornado and preserves normal and Wicked Wind merge damage", () =>
        {
            foreach (bool wicked in new[] { false, true })
            {
                var f = SwordFixture();
                if (wicked) f.Player.Attributes[GameAttributes.Rune_E, TwisterPower] = 1;
                var parent = Cast(f, 1, baseDamage: wicked ? 8.35f : 15.25f);
                var expected = wicked ? new[] { 32f, 40.35f, 48.7f, 57.05f } : new[] { 32f, 47.25f, 62.5f, 77.75f };
                for (uint id = 2; id <= 5; id++)
                {
                    var next = Cast(f, id, itemProc: true, baseDamage: wicked ? 8.35f : 15.25f);
                    Assert(Absorb(parent, next), "Eligible single cast did not merge");
                    Near(Field<float>(parent, "_totalDamage"), expected[id - 2]);
                    Near(Count(parent), id);
                    Near(Count(next), 0);
                    Near(Damage(f, parent), id == 2 ? 368f : 502f);
                    Assert(!parent.IsItemProc, "Automatic child changed manual parent's proc status");
                }
                var sixth = Cast(f, 6);
                Assert(!Absorb(parent, sixth) && Count(sixth) == 1, "A sixth cast was consumed");
                Near(Field<float>(parent, "_totalDamage"), expected[3]);
            }
        });

        check("Twisted Sword grants merging without changing the selected rune or element", () =>
        {
            foreach (var (rune, element) in new[] { (0, DamageType.Arcane), (1, DamageType.Fire),
                (2, DamageType.Arcane), (3, DamageType.Lightning), (4, DamageType.Cold), (5, DamageType.Arcane) })
            {
                var f = SwordFixture();
                var attributes = new[] { GameAttributes.Rune_A, GameAttributes.Rune_B, GameAttributes.Rune_C,
                    GameAttributes.Rune_D, GameAttributes.Rune_E };
                if (rune > 0) f.Player.Attributes[attributes[rune - 1], TwisterPower] = 1;
                var parent = Cast(f, 1, itemProc: true);
                Assert(Absorb(parent, Cast(f, 2)), "Sword did not grant merging to selected rune");
                Assert(Field<DamageType>(parent, "_damageType") == element, "Merged element changed");
                Assert(Field<bool>(parent, "_stationary") == (rune == 5), "Wicked Wind movement flag changed");
                Assert(Field<bool>(parent, "_galeForce") == (rune == 1), "Gale Force callback flag changed");
                Assert(parent.IsItemProc, "Manual child enabled secondary procs on an item-generated parent");
                Near(parent.GetProcCoefficient(), 0);
                for (int index = 0; index < attributes.Length; index++)
                    Near(f.Player.Attributes[attributes[index], TwisterPower], rune == index + 1 ? 1 : 0);
            }
        });

        check("Accepted merges refresh six seconds; a full tornado cannot refresh from a sixth cast", () =>
        {
            var f = SwordFixture();
            var parent = Cast(f, 1);
            for (uint id = 2; id <= 5; id++)
            {
                var now = (int)(id - 1) * 300;
                Set(f.Game, "_tickCounter", now);
                Assert(Absorb(parent, Cast(f, id)), "Tornado expired despite an accepted merge");
                Near(Actor(parent).Timeout.TimeoutTick, now + 360);
            }
            Set(f.Game, "_tickCounter", 1500);
            var sixth = Cast(f, 6);
            Assert(!Absorb(parent, sixth), "Full tornado accepted a sixth cast");
            Near(Actor(parent).Timeout.TimeoutTick, 1560);
            Set(f.Game, "_tickCounter", 1560);
            Near(Damage(f, sixth), 234f); // Only the unmerged sixth cast remains active.
        });

        check("Raging Storm without the sword still merges only two casts; ordinary twisters do not merge", () =>
        {
            var f = Fixture(0);
            var ordinary = Cast(f, 1);
            Assert(!Absorb(ordinary, Cast(f, 2)), "Unruned casts merged without the sword");
            f.Player.Attributes[GameAttributes.Rune_B, TwisterPower] = 1;
            var raging = Cast(f, 3);
            Assert(Absorb(raging, Cast(f, 4)), "Native Raging Storm stopped merging");
            Assert(!Absorb(raging, Cast(f, 5)), "Native Raging Storm exceeded two casts");
            Near(Damage(f, raging), 100f);
        });

        check("Merge selection retains original cast order after actor replacement and uses the latest cast aim", () =>
        {
            var f = SwordFixture();
            var parent = Cast(f, 1);
            Assert(Absorb(parent, Cast(f, 2)), "First pair did not merge");
            Set(Actor(parent), "_globalID", 100u); // First merge replaces the visual actor.
            var next = Cast(f, 3);
            var later = Cast(f, 4);
            next.TargetPosition.X = 999f; // Wandering must not overwrite its original cast aim (30, 0, 0).
            var find = typeof(EnergyTwister).GetMethod("FindRagingStormPartner", BindingFlags.Static | BindingFlags.NonPublic)!;
            var selected = find.Invoke(null, new object[] { Actor(parent), new[] { Actor(later), Actor(parent), Actor(next) } });
            Assert(ReferenceEquals(selected, Actor(next)), "Replacement actor ID broke cast ordering");
            Assert(Absorb(parent, next), "Third cast did not merge");
            Near(parent.TargetPosition.X, 30f);
            Assert(!Absorb(parent, next), "Consumed cast was merged twice");
            var expired = Cast(f, 5);
            Set(f.Game, "_tickCounter", 360);
            Assert(!Absorb(parent, expired), "Expired casts were merged");
        });
    }

    static FixtureData SwordFixture()
    {
        var f = Fixture(Skills.Wizard.Offensive.ArcaneTorrent);
        f.Item.Attributes[GameAttributes.Item_Power_Passive, SwordPower] = 1.34f;
        return f;
    }

    static EnergyTwister Cast(FixtureData f, uint id, bool itemProc = false, float baseDamage = 15.25f)
    {
        var context = new EnergyTwister { User = f.Player, World = f.Player.World,
            PowerSNO = TwisterPower, TargetPosition = new Vector3D(id * 10, 0, 0), IsItemProc = itemProc };
        typeof(EnergyTwister).GetMethod("InitializeTwister", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(context, new object[] { baseDamage });
        var actor = Empty<EffectActor>();
        actor.Context = context;
        actor.World = context.World;
        actor.Timeout = context.WaitSeconds(6f);
        Set(actor, "_globalID", id);
        Set(context, "<ActiveTwister>k__BackingField", actor);
        Set(context, "_firstCastId", id);
        TwistedSwordBuff.Track(context);
        return context;
    }

    static EffectActor Actor(EnergyTwister context) => (EffectActor)typeof(EnergyTwister)
        .GetProperty("ActiveTwister", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context)!;
    static int Count(EnergyTwister context) => (int)typeof(EnergyTwister)
        .GetProperty("CastCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context)!;
    static T Field<T>(EnergyTwister context, string name) => (T)typeof(EnergyTwister)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context)!;
    static bool Absorb(EnergyTwister parent, EnergyTwister child) => (bool)typeof(EnergyTwister)
        .GetMethod("TryAbsorb", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(parent, new object[] { child, 32f, 6f })!;
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }

    static float Damage(FixtureData f, PowerContext context)
    {
        f.Player.Attributes[GameAttributes.Level] = 70;
        Set(f.Player, "<LegacyOfDreamsDamage>k__BackingField", 1f);
        var target = Empty<EffectActor>();
        target.World = f.Player.World;
        target.Position = new Vector3D(10, 0, 0);
        Set(target, "<Visible>k__BackingField", true);
        Set(target, "<Attributes>k__BackingField", new GameAttributeMap(target));
        var attack = new AttackPayload(context);
        attack.AddDamage(100f, 0f, DamageType.Arcane);
        return new HitPayload(attack, false, target).TotalDamage;
    }

    static void Near(float actual, float expected)
    {
        if (Math.Abs(actual - expected) > 0.001f)
            throw new Exception($"Expected {expected}, got {actual}");
    }
}
