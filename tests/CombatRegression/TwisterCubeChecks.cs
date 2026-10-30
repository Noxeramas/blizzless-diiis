using System.Reflection;
using DiIiS_NA.GameServer.Core.Types.Math;
using DiIiS_NA.GameServer.Core.Types.SNO;
using DiIiS_NA.GameServer.GSSystem.ActorSystem;
using DiIiS_NA.GameServer.GSSystem.ObjectsSystem;
using DiIiS_NA.GameServer.GSSystem.PowerSystem;
using DiIiS_NA.GameServer.GSSystem.PowerSystem.Implementations;
using DiIiS_NA.GameServer.GSSystem.PowerSystem.Payloads;
using DiIiS_NA.GameServer.MessageSystem;
using static EtchedSigilChecks;
using MonsterData = DiIiS_NA.Core.MPQ.FileFormats.Monster;

static class TwisterCubeChecks
{
    const int Valthek = 484597, Ranslor = 478491, Twister = 77113;

    public static void Run(Action<string, Action> check)
    {
        check("Cubed Valthek and Ranslor multiply real manual and item-generated Twister hits by twenty", () =>
        {
            var f = Fixture(134456);
            Set(f.Player, "<CubedPowerValues>k__BackingField", new Dictionary<int, float> { [Valthek] = 4f, [Ranslor] = 3f });
            foreach (var itemProc in new[] { false, true }) Near(Damage(f, Twister, itemProc), 2000f);
            Near(Damage(f, 134456), 100f);
            f.Channel.Run().ToArray();
            Near(Damage(f, Twister, true), 6860f); // Also retains the equipped Sigil's 243% bonus.
        });

        check("Valthek and Ranslor use equipment rolls, ignore broken gear and do not add Cube duplicates", () =>
        {
            var f = Fixture(0);
            f.Item.Attributes[GameAttributes.Item_Power_Passive, Valthek] = 3.28f;
            f.Item.Attributes[GameAttributes.Item_Power_Passive, Ranslor] = 2.49f;
            Near(Damage(f, Twister), 1493.72f);
            Set(f.Player, "<CubedPowerValues>k__BackingField", new Dictionary<int, float> { [Valthek] = 4f, [Ranslor] = 3f });
            Near(Damage(f, Twister), 2000f);
            f.Items.Clear();
            Near(Damage(f, Twister), 2000f);
            Set(f.Player, "<CubedPowerValues>k__BackingField", new Dictionary<int, float>());
            Near(Damage(f, Twister), 100f);
            f.Items.Add(1, f.Item);
            f.Item.Attributes[GameAttributes.Durability_Cur] = 0;
            Near(Damage(f, Twister), 100f);
        });

        check("Valthek retains selected runes and Wicked Wind stays stationary with the Cube power", () =>
        {
            var runes = new[] { GameAttributes.Rune_A, GameAttributes.Rune_B, GameAttributes.Rune_C, GameAttributes.Rune_D, GameAttributes.Rune_E };
            for (int rune = -1; rune < runes.Length; rune++)
            {
                var f = Fixture(0);
                Set(f.Player, "<CubedPowerValues>k__BackingField", new Dictionary<int, float> { [Valthek] = 4f, [484709] = 1.5f });
                if (rune >= 0) f.Player.Attributes[runes[rune], Twister] = 1;
                var cast = Cast(f);
                Assert(Field<bool>(cast, "_straightPath") == (rune != 4), "Wrong Valthek movement mode");
                Assert(Field<bool>(cast, "_stationary") == (rune == 4), "Wicked Wind no longer stationary");
                Near(Field<int>(cast, "_mergeLimit"), 5);
                for (int index = 0; index < runes.Length; index++) Near(f.Player.Attributes[runes[index], Twister], index == rune ? 1 : 0);
            }
            Assert(!Field<bool>(Cast(Fixture(0)), "_straightPath"), "Ordinary Twister acquired Valthek movement");
        });

        check("Valthek travels beyond a nearby click in a straight line and handles zero-distance aim", () =>
        {
            var f = Fixture(0);
            var cast = Cast(f);
            cast.TargetPosition = new Vector3D(3, 4, 20);
            var endpoint = (Vector3D)Call(cast, "StraightPathDestination", f.Player.Position, 90f);
            Near(endpoint.X, 54); Near(endpoint.Y, 72); Near(endpoint.Z, 0);
            var tornado = Empty<EffectActor>();
            tornado.World = f.Player.World;
            tornado.Position = new Vector3D();
            var mover = new ActorMover(tornado);
            mover.Move(endpoint, .25f);
            Set(f.Game, "_tickCounter", 60);
            mover.Update();
            Near(tornado.Position.X, 9); Near(tornado.Position.Y, 12);
            Set(f.Game, "_tickCounter", 180);
            mover.Update();
            Near(tornado.Position.X, 27); Near(tornado.Position.Y, 36);
            cast.TargetPosition = new Vector3D();
            f.Player.SetFacingRotation((float)Math.PI / 2);
            endpoint = (Vector3D)Call(cast, "StraightPathDestination", f.Player.Position, 90f);
            Near(endpoint.X, 0); Near(endpoint.Y, 90);
        });

        check("Ranslor rejects elites, large monsters, immunity, dead targets and targets beyond thirty yards", () =>
        {
            var f = Fixture(0);
            var cast = Cast(f);
            foreach (var size in Enum.GetValues<MonsterData.MonsterSize>())
            {
                var monster = Monster(f, size);
                Assert(CanPull(cast, monster) == (size is MonsterData.MonsterSize.Standard or MonsterData.MonsterSize.Ranged or MonsterData.MonsterSize.Swarm), "Wrong size eligibility: " + size);
            }
            var target = Monster(f);
            target.Position.X = 30; Assert(CanPull(cast, target), "Thirty-yard boundary excluded");
            target.Position.X = 30.01f; Assert(!CanPull(cast, target), "Padded radius pulled past thirty yards");
            target.Position.X = 10;
            target.Attributes[GameAttributes.Immune_To_Knockback] = true;
            Assert(!CanPull(cast, target), "Immune target accepted");
            target.Attributes[GameAttributes.Immune_To_Knockback] = false;
            target.Dead = true; Assert(!CanPull(cast, target), "Dead target accepted");
            target.Dead = false;
            for (int quality = 1; quality <= 7; quality++)
            {
                target.TestQuality = quality;
                Assert(!CanPull(cast, target), "Elite quality accepted: " + quality);
            }
            Assert(!CanPull(cast, f.Player), "Pull accepted a player");
        });

        check("Ranslor lockout is shared across one Wizard's casts but isolated between Wizards", () =>
        {
            var f = Fixture(0);
            var target = Monster(f);
            var first = Cast(f);
            var automatic = Cast(f); automatic.IsItemProc = true;
            var other = Fixture(0);
            other.Player.World = f.Player.World;
            var otherCast = Cast(other);
            var lockoutType = typeof(EnergyTwister).GetNestedType("RanslorPullLockout", BindingFlags.NonPublic)!;
            void Lock(EnergyTwister cast)
            {
                var buff = (TimedBuff)Activator.CreateInstance(lockoutType, true)!;
                buff.Timeout = cast.WaitSeconds(6);
                f.Player.World.BuffManager.AddBuff(cast.User, target, buff);
            }
            Lock(first);
            Assert(!CanPull(first, target) && !CanPull(automatic, target), "Another cast bypassed target lockout");
            Assert(CanPull(otherCast, target), "A different Wizard inherited the lockout");
            Set(f.Game, "_tickCounter", 60);
            Lock(otherCast);
            Near(f.Player.World.BuffManager.GetAllBuffs(target).Count, 2);
            Near(target.Attributes[GameAttributes.Buff_Icon_Count0, Twister], 0);
            Assert(!target.Attributes[GameAttributes.Buff_Exclusive_Type_Active, Twister], "Internal lockout published an Energy Twister effect");
            Set(f.Game, "_tickCounter", 359); Assert(!CanPull(first, target), "Lockout ended early");
            Set(f.Game, "_tickCounter", 360);
            Assert(CanPull(first, target) && !CanPull(otherCast, target), "Independent expiry failed");
            Set(f.Game, "_tickCounter", 420);
            f.Player.World.BuffManager.Update();
            Assert(CanPull(otherCast, target), "Expired lockout remained");
            Near(f.Player.World.BuffManager.GetAllBuffs(target).Count, 0);
        });

        check("Ranslor's centered knockback snapshots the pull point and rejects unwalkable landings without a lockout", () =>
        {
            var center = new Vector3D(5, 8, 0);
            var pull = new KnockbackBuff(center);
            center.X = 90;
            var destination = (Vector3D)typeof(KnockbackBuff).GetField("_destination", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pull)!;
            Near(destination.X, 5); Near(destination.Y, 8);
            var f = Fixture(0);
            Set(f.Player.World, "_scenes", new System.Collections.Concurrent.ConcurrentDictionary<uint, DiIiS_NA.GameServer.GSSystem.MapSystem.Scene>());
            var cast = Cast(f);
            var target = Monster(f);
            Assert(!(bool)Call(cast, "TryRanslorPull", target, new Vector3D(), 30f, 6f), "Unwalkable destination was accepted");
            Near(f.Player.World.BuffManager.GetAllBuffs(target).Count, 0);
            Assert(CanPull(cast, target), "Failed movement consumed the lockout");
            Assert(!target.Attributes[GameAttributes.Buff_Exclusive_Type_Active, 70432], "Rejected movement left a knockback effect active");
        });
    }

    static EnergyTwister Cast(FixtureData f)
    {
        var cast = new EnergyTwister { User = f.Player, World = f.Player.World, PowerSNO = Twister, TargetPosition = new Vector3D(10, 0, 0) };
        Call(cast, "InitializeTwister", 15.25f);
        return cast;
    }

    public static void RunInstalled(Action<string, Action> check)
    {
        check("Ranslor moves a real knockback to the twister center on an installed walkable scene", () =>
        {
            var entry = DiIiS_NA.Core.MPQ.MPQStorage.Data.Assets[SNOGroup.Scene].First(pair =>
                pair.Value.Data is DiIiS_NA.Core.MPQ.FileFormats.Scene data && data.NavMesh.SquaresCountX > 0 &&
                data.NavMesh.Squares.Any(square => (square.Flags & DiIiS_NA.Core.MPQ.FileFormats.Scene.NavCellFlags.AllowWalk) != 0));
            var mesh = ((DiIiS_NA.Core.MPQ.FileFormats.Scene)entry.Value.Data).NavMesh;
            var index = mesh.Squares.FindIndex(square => (square.Flags & DiIiS_NA.Core.MPQ.FileFormats.Scene.NavCellFlags.AllowWalk) != 0);
            var center = new Vector3D((index % mesh.SquaresCountX + .5f) * 2.5f, (index / mesh.SquaresCountX + .5f) * 2.5f, mesh.Squares[index].Z);
            var scene = Empty<DiIiS_NA.GameServer.GSSystem.MapSystem.Scene>();
            Set(scene, "<SceneSNO>k__BackingField", new SNOHandle(SNOGroup.Scene, entry.Key));
            Set(scene, "<Bounds>k__BackingField", new System.Drawing.RectangleF(0, 0, mesh.SquaresCountX * 2.5f, mesh.SquaresCountY * 2.5f));
            Set(scene, "<Subscenes>k__BackingField", new List<DiIiS_NA.GameServer.GSSystem.MapSystem.Scene>());
            var f = Fixture(0);
            Set(f.Player.World, "_scenes", new System.Collections.Concurrent.ConcurrentDictionary<uint, DiIiS_NA.GameServer.GSSystem.MapSystem.Scene>(new[] { new KeyValuePair<uint, DiIiS_NA.GameServer.GSSystem.MapSystem.Scene>(1, scene) }));
            var cast = Cast(f);
            var target = Monster(f);
            target.Position = center + new Vector3D(10, 0, 0);
            Assert((bool)Call(cast, "TryRanslorPull", target, center, 30f, 6f), "Valid pull was rejected");
            var knockback = target.World.BuffManager.GetFirstBuff<KnockbackBuff>(target);
            Assert(knockback != null, "Pull did not use the movement system");
            Set(f.Game, "_tickCounter", knockback.ArrivalTime.TimeoutTick);
            target.World.BuffManager.Update();
            Near(target.Position.X, center.X); Near(target.Position.Y, center.Y); Near(target.Position.Z, center.Z);
            Assert(!(bool)Call(cast, "TryRanslorPull", target, center, 30f, 6f), "Second twister bypassed the pull lockout");
            Set(f.Game, "_tickCounter", 360);
            target.World.BuffManager.Update();
            Assert((bool)Call(cast, "TryRanslorPull", target, center, 30f, 6f), "Pull did not recover at six seconds");
        });
    }

    static RanslorMonsterProbe Monster(FixtureData f, MonsterData.MonsterSize size = MonsterData.MonsterSize.Standard)
    {
        var monster = Empty<RanslorMonsterProbe>();
        monster.World = f.Player.World;
        monster.Position = new Vector3D(10, 0, 0);
        Set(monster, "<Attributes>k__BackingField", new GameAttributeMap(monster));
        var data = Empty<MonsterData>();
        Set(data, "<Size>k__BackingField", size);
        var handle = new SNOHandle(0);
        Set(handle, "_isInitialized", true); Set(handle, "_isValid", true); Set(handle, "_target", data);
        Set(monster, "<Monster>k__BackingField", handle);
        return monster;
    }

    static bool CanPull(EnergyTwister cast, Actor enemy) => (bool)Call(cast, "CanRanslorPull", enemy, new Vector3D(), 30f);
    static object Call(EnergyTwister cast, string name, params object[] args) => typeof(EnergyTwister)
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(cast, args)!;
    static T Field<T>(EnergyTwister cast, string name) => (T)typeof(EnergyTwister)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cast)!;
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }

    static float Damage(FixtureData f, int power, bool itemProc = false)
    {
        f.Player.Attributes[GameAttributes.Level] = 70;
        Set(f.Player, "<LegacyOfDreamsDamage>k__BackingField", 1f);
        var target = Empty<EffectActor>();
        target.World = f.Player.World;
        target.Position = new Vector3D(10, 0, 0);
        Set(target, "<Visible>k__BackingField", true);
        Set(target, "<Attributes>k__BackingField", new GameAttributeMap(target));
        var context = new PowerContext { User = f.Player, World = target.World, PowerSNO = power, IsItemProc = itemProc };
        var attack = new AttackPayload(context);
        attack.AddDamage(100f, 0f, DamageType.Cold);
        return new HitPayload(attack, false, target).TotalDamage;
    }

    static void Near(float actual, float expected)
    {
        if (Math.Abs(actual - expected) > .01f) throw new Exception($"Expected {expected}, got {actual}");
    }
}

sealed class RanslorMonsterProbe : Monster
{
    private RanslorMonsterProbe() : base(null, 0, null) { }
    public int TestQuality;
    public override int Quality { get => TestQuality; set => TestQuality = value; }
}
