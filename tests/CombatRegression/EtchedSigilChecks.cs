using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using DiIiS_NA.D3_GameServer.Core.Types.SNO;
using DiIiS_NA.GameServer.Core.Types.Math;
using DiIiS_NA.GameServer.Core.Types.SNO;
using DiIiS_NA.GameServer.GSSystem.ActorSystem;
using DiIiS_NA.GameServer.GSSystem.GameSystem;
using DiIiS_NA.GameServer.GSSystem.ItemsSystem;
using DiIiS_NA.GameServer.GSSystem.MapSystem;
using DiIiS_NA.GameServer.GSSystem.ObjectsSystem;
using DiIiS_NA.GameServer.GSSystem.PlayerSystem;
using DiIiS_NA.GameServer.GSSystem.PowerSystem;
using DiIiS_NA.GameServer.GSSystem.PowerSystem.Implementations;
using DiIiS_NA.GameServer.GSSystem.PowerSystem.Payloads;
using DiIiS_NA.GameServer.GSSystem.SkillsSystem;
using DiIiS_NA.GameServer.GSSystem.TickerSystem;
using DiIiS_NA.GameServer.MessageSystem;

static class EtchedSigilChecks
{
    const int ItemPower = 484596; // P610_ItemPassive_Unique_Ring_002, installed 2.8.0 client
    static readonly int[] Channels = { Skills.Wizard.Offensive.ArcaneTorrent,
        Skills.Wizard.Offensive.Disintegrate, Skills.Wizard.Offensive.RayOfFrost };

    public static void Run(Action<string, Action> check)
    {
        check("Etched Sigil activates on all three supported channels, once per player", () =>
        {
            foreach (var power in Channels)
            {
                var f = Fixture(power);
                f.Channel.Run().ToArray();
                var buff = FindBuff(f.Player);
                Near(Bonus(buff), 2.43f, "Saved item roll was not used");
                f.Channel.Run().ToArray();
                Assert(ReferenceEquals(buff, FindBuff(f.Player)), "Repeated channel input replaced the proc timer");
            }
            var other = Fixture(0);
            other.Channel.Run().ToArray();
            Assert(other.Player.World.BuffManager.GetAllBuffs(other.Player).Count == 0, "Unsupported channel activated Sigil");
            var absent = Fixture(Channels[0]);
            absent.Items.Clear();
            absent.Channel.Run().ToArray();
            Assert(absent.Player.World.BuffManager.GetAllBuffs(absent.Player).Count == 0, "Sigil activated without equipped item");
        });

        check("Etched Sigil procs on eligible channel ticks using the latest aim and selected rune", () =>
        {
            var f = Fixture(Channels[0]);
            f.Player.Attributes[GameAttributes.Rune_D, Skills.Wizard.Offensive.EnergyTwister] = 1;
            f.Channel.Run().ToArray();
            var buff = FindBuff(f.Player);
            Assert(NextTwister(buff) == null, "Proc fired immediately on channel open");
            Tick(f.Game, 59);
            f.Channel.Run().ToArray();
            Assert(NextTwister(buff) == null, "Proc fired before one second");
            f.Channel.TargetPosition = new Vector3D(18, 24, 3);
            Tick(f.Game, 60);
            f.Channel.Run().ToArray();
            var twister = NextTwister(buff);
            Assert(twister != null && twister.User == f.Player && twister.World == f.Player.World,
                "Timer did not create a twister owned by the channeling player");
            Assert(twister.PowerSNO == Skills.Wizard.Offensive.EnergyTwister && twister.Rune_D == 1,
                "Triggered cast lost Energy Twister's selected rune");
            Near(twister.TargetPosition.Y, 24, "Triggered cast used stale aim");
            twister.TargetPosition.Y = 500;
            Near(f.Channel.TargetPosition.Y, 24, "Twister mutated the channel's aim");
            Assert(NextTwister(buff) == null, "Multiple procs occurred during one tick");
            Tick(f.Game, 120);
            f.Channel.Run().ToArray();
            Assert(NextTwister(buff) != null, "Second one-second proc was missing");
            Tick(f.Game, 360);
            f.Channel.Run().ToArray();
            Assert(NextTwister(buff) != null && NextTwister(buff) == null, "A late update replayed a burst of procs");
            Near(twister.GetProcCoefficient(), 0, "Item-generated twisters can trigger coefficient-based procs");
            Near(new EnergyTwister { PowerSNO = Skills.Wizard.Offensive.EnergyTwister }.GetProcCoefficient(),
                0.08f, "Manual twister proc coefficient changed");
        });

        check("Etched Sigil reproduces the published 60, 65, 66, 70, 72, and 76 frame breakpoints", () =>
        {
            // Published live measurements, not values calculated by the implementation.
            foreach (var (frames, interval) in new[] { (12, 60), (13, 65), (11, 66), (14, 70), (18, 72), (20, 60), (19, 76) })
            {
                var f = Fixture(Channels[0]);
                ((EtchedSigilChannelProbe)f.Channel).TickFrames = frames;
                f.Channel.Run().ToArray();
                var buff = FindBuff(f.Player);
                var casts = new List<int>();
                for (int tick = 1; tick <= interval * 4; tick++)
                {
                    Tick(f.Game, tick);
                    f.Channel.Run().ToArray();
                    if (NextTwister(buff) != null) casts.Add(tick);
                }
                Assert(casts.SequenceEqual(new[] { interval, interval * 2, interval * 3, interval * 4 }),
                    $"{frames}-frame channel gave casts at {string.Join(",", casts)}");
            }
        });

        check("Etched Sigil uses the cursor before the channel extends or randomizes its target", () =>
        {
            var f = Fixture(Channels[1]);
            ((EtchedSigilChannelProbe)f.Channel).ChangeTarget = true;
            f.Channel.Run().ToArray();
            Tick(f.Game, 60);
            f.Channel.TargetPosition = new Vector3D(7, 9, 0);
            f.Channel.Run().ToArray();
            var twister = NextTwister(FindBuff(f.Player));
            Near(twister.TargetPosition.X, 7f, "Twister used the channel's projected target instead of the cursor");
            Near(twister.TargetPosition.Y, 9f, "Twister lost the cursor's position");
        });

        check("Etched Sigil keeps channel phase across batched updates and requires channel ticks", () =>
        {
            var f = Fixture(Channels[0]);
            ((EtchedSigilChannelProbe)f.Channel).TickFrames = 13;
            f.Channel.Run().ToArray();
            var buff = FindBuff(f.Player);
            var casts = new List<int>();
            for (int tick = 3; tick <= 261; tick += 3)
            {
                Tick(f.Game, tick);
                f.Channel.Run().ToArray();
                if (NextTwister(buff) != null) casts.Add(tick);
            }
            Assert(casts.SequenceEqual(new[] { 66, 132, 195, 261 }), "World batching changed the channel's frame phase");
            Tick(f.Game, 600);
            Assert(NextTwister(buff) == null, "Elapsed world time procced without a channel tick");
        });

        check("Etched Sigil stops on cancel, unequip, breakage, death, crowd control, or world change", () =>
        {
            Action<FixtureData>[] stop = {
                f => f.Channel.CloseChannel(),
                f => f.Items.Clear(),
                f => f.Item.Attributes[GameAttributes.Durability_Cur] = 0,
                f => f.Player.Dead = true,
                f => f.Player.Attributes[GameAttributes.Disabled] = true,
                f => f.Player.Attributes[GameAttributes.Stunned] = true,
                f => f.Player.Attributes[GameAttributes.Frozen] = true,
                f => f.Player.Attributes[GameAttributes.Feared] = true,
                f => f.Player.World = Empty<World>()
            };
            foreach (var interrupt in stop)
            {
                var f = Fixture(Channels[0]);
                f.Channel.Run().ToArray();
                var buff = FindBuff(f.Player);
                interrupt(f);
                Tick(f.Game, 60);
                Near(Bonus(buff), 0, "Damage bonus survived interruption");
                Assert(NextTwister(buff) == null, "Triggered cast survived interruption");
                Assert(buff.Update(), "Inactive buff was not removed");
            }
        });

        check("Etched Sigil reads changed rolls and restarts its timer after a new channel", () =>
        {
            var f = Fixture(Channels[0]);
            f.Channel.Run().ToArray();
            var oldBuff = FindBuff(f.Player);
            f.Item.Attributes[GameAttributes.Item_Power_Passive, ItemPower] = 2.12f;
            Near(Bonus(oldBuff), 2.12f, "Old item roll was cached");
            f.Channel.CloseChannel();
            f.Player.World.BuffManager.Update();
            Tick(f.Game, 60);
            f.Channel.Run().ToArray();
            var buff = FindBuff(f.Player);
            Assert(!ReferenceEquals(buff, oldBuff), "Reopened channel reused its old timer");
            Assert(NextTwister(buff) == null, "Reopened channel procced immediately");
            Tick(f.Game, 120);
            f.Channel.Run().ToArray();
            Assert(NextTwister(buff) != null, "Reopened channel never procced");
        });

        check("Etched Sigil multiplies actual twister hit damage only during the equipped channel", () =>
        {
            var f = Fixture(Channels[0]);
            var target = Empty<EffectActor>();
            target.World = f.Player.World;
            target.Position = new Vector3D(10, 0, 0);
            Set(target, "<Visible>k__BackingField", true);
            Set(target, "<Attributes>k__BackingField", new GameAttributeMap(target));
            f.Player.Attributes[GameAttributes.Level] = 70;
            Set(f.Player, "<LegacyOfDreamsDamage>k__BackingField", 1f);

            float Damage(int power)
            {
                var context = new PowerContext { User = f.Player, World = target.World, PowerSNO = power };
                var attack = new AttackPayload(context);
                attack.AddDamage(100f, 0f, DamageType.Arcane);
                return new HitPayload(attack, false, target).TotalDamage;
            }

            Near(Damage(Skills.Wizard.Offensive.EnergyTwister), 100f, "Idle Sigil altered damage");
            f.Channel.Run().ToArray();
            Near(Damage(Skills.Wizard.Offensive.EnergyTwister), 343f, "243% roll did not multiply the hit by 3.43");
            Near(Damage(Channels[0]), 100f, "Sigil also boosted the channel's damage");
            f.Items.Clear();
            Near(Damage(Skills.Wizard.Offensive.EnergyTwister), 100f, "Unequipped Sigil still boosted hits");
            f.Items.Add(1, f.Item);
            f.Channel.CloseChannel();
            Near(Damage(Skills.Wizard.Offensive.EnergyTwister), 100f, "Bonus was snapshotted after channel ended");
        });

        check("Item-generated twister hits do not grant Life on Hit; manual hits still do", () =>
        {
            var f = Fixture(Channels[0]);
            f.Player.Attributes[GameAttributes.Level] = 70;
            f.Player.Attributes[GameAttributes.Hitpoints_On_Hit] = 25f;
            Set(f.Player, "<LegacyOfDreamsDamage>k__BackingField", 1f);
            var target = Empty<EffectActor>();
            target.World = f.Player.World;
            target.Position = new Vector3D(10, 0, 0);
            Set(target, "<Visible>k__BackingField", true);
            Set(target, "<Attributes>k__BackingField", new GameAttributeMap(target));
            target.Attributes[GameAttributes.Hitpoints_Max] = 1000f;
            target.Attributes[GameAttributes.Hitpoints_Cur] = 1000f;

            void Hit(bool itemProc)
            {
                var context = new EnergyTwister { User = f.Player, World = target.World,
                    PowerSNO = Skills.Wizard.Offensive.EnergyTwister, IsItemProc = itemProc };
                var attack = new AttackPayload(context);
                attack.AddDamage(100f, 0f, DamageType.Arcane);
                var hit = new HitPayload(attack, false, target) { AutomaticHitEffects = false };
                Near(hit.TotalDamage, 100f, "Hit fixture did not deal the expected damage");
                hit.Apply();
            }

            Hit(true);
            Near(((EtchedSigilPlayerProbe)f.Player).Healing, 0f, "Item proc granted Life on Hit");
            Near(target.Attributes[GameAttributes.Hitpoints_Cur], 900f, "Suppressing procs also suppressed damage");
            Hit(false);
            Near(((EtchedSigilPlayerProbe)f.Player).Healing, 25f, "Manual hit lost Life on Hit");
            Near(target.Attributes[GameAttributes.Hitpoints_Cur], 800f, "Manual hit did not damage the target");
        });

        check("Raging Storm pairs the oldest same-owner casts without consuming unruned or foreign twisters", () =>
        {
            var f = Fixture(Channels[0]);
            EffectActor Twister(uint id, Player owner, bool itemProc, ActorSno sno = ActorSno._wizard_tornado_obsidian)
            {
                var actor = Empty<EffectActor>();
                actor.World = f.Player.World;
                Set(actor, "_globalID", id);
                owner.Attributes[GameAttributes.Rune_B, Skills.Wizard.Offensive.EnergyTwister] =
                    sno == ActorSno._wizard_tornado_obsidian ? 1 : 0;
                var context = new EnergyTwister { User = owner, World = actor.World, IsItemProc = itemProc,
                    PowerSNO = Skills.Wizard.Offensive.EnergyTwister, TargetPosition = new Vector3D(10, 0, 0) };
                actor.Context = context;
                actor.Timeout = context.WaitSeconds(6f);
                typeof(EnergyTwister).GetMethod("InitializeTwister", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(context, new object[] { 15.25f });
                Set(context, "<ActiveTwister>k__BackingField", actor);
                Set(context, "_firstCastId", id);
                Set(actor, "<ActorSNO>k__BackingField", new SNOHandle((int)sno));
                return actor;
            }
            var find = typeof(EnergyTwister).GetMethod("FindRagingStormPartner", BindingFlags.Static | BindingFlags.NonPublic)!;
            EffectActor Partner(EffectActor actor, params EffectActor[] nearby) =>
                (EffectActor)find.Invoke(null, new object[] { actor, nearby });

            var manual = Twister(1, f.Player, false);
            var automatic = Twister(2, f.Player, true);
            var later = Twister(3, f.Player, true);
            var foreignOwner = Fixture(Channels[0]).Player;
            foreignOwner.World = f.Player.World;
            var foreign = Twister(4, foreignOwner, true);
            var unruned = Twister(5, f.Player, true, ActorSno._wizard_tornado);
            Assert(Partner(manual, later, foreign, unruned, automatic, manual) == automatic,
                "Merge did not choose the next cast by the same player");
            Assert(Partner(automatic, manual, foreign, unruned) == null,
                "Newer cast took over the older cast's proc provenance, or merged an ineligible actor");
            Assert(Partner(automatic, later) == later && automatic.Context.IsItemProc,
                "Automatic parent lost its proc provenance");
        });
    }

    internal record FixtureData(Game Game, Player Player, ChanneledSkill Channel, Item Item, Dictionary<uint, Item> Items);

    internal static FixtureData Fixture(int power)
    {
        var game = Empty<Game>();
        game.Working = true;
        Set(game, "UpdateFrequency", 50L);
        Set(game, "TickRate", 3);
        var world = Empty<World>();
        Set(world, "<WorldSNO>k__BackingField", new SNOHandle(0));
        Set(world, "<Game>k__BackingField", game);
        Set(world, "_buffManager", new BuffManager());
        Set(world, "_actors", new ConcurrentDictionary<uint, Actor>());
        Set(world, "_players", new ConcurrentDictionary<uint, Player>());
        var player = Empty<EtchedSigilPlayerProbe>();
        player.World = world;
        player.Position = new Vector3D();
        player.RevealedObjects = new();
        Set(player, "<Attributes>k__BackingField", new GameAttributeMap(player));
        var inventory = new Inventory(player);
        Set(player, "<Inventory>k__BackingField", inventory);
        Set(inventory, "<Loaded>k__BackingField", true);
        var equipment = typeof(Inventory).GetField("_equipment", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(inventory);
        var items = (Dictionary<uint, Item>)equipment.GetType().GetProperty("Items")!.GetValue(equipment)!;
        var item = Empty<Item>();
        Set(item, "<Attributes>k__BackingField", new GameAttributeMap(item));
        item.Attributes[GameAttributes.Item_Power_Passive, ItemPower] = 2.43f;
        item.Attributes[GameAttributes.Durability_Max] = 100;
        item.Attributes[GameAttributes.Durability_Cur] = 100;
        items.Add(1, item);
        var channel = new EtchedSigilChannelProbe { User = player, World = world, PowerSNO = power,
            TargetPosition = new Vector3D(10, 0, 0) };
        return new(game, player, channel, item, items);
    }

    static Buff FindBuff(Player player) => player.World.BuffManager.GetAllBuffs(player).Keys
        .SingleOrDefault(b => b.GetType().Name == "EtchedSigilBuff") ?? throw new Exception("Equipped Etched Sigil never activated");
    static float Bonus(Buff buff) => (float)buff.GetType().GetProperty("DamageBonus")!.GetValue(buff)!;
    static EnergyTwister NextTwister(Buff buff) => (EnergyTwister)buff.GetType()
        .GetMethod("TryCreateTwister", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(buff, null);
    static void Tick(Game game, int tick) => Set(game, "_tickCounter", tick);
    static void Near(float actual, float expected, string message) => Assert(Math.Abs(actual - expected) < 0.0001f, message);
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    internal static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    internal static void Set(object target, string name, object value)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null) continue;
            field.SetValue(target, value);
            return;
        }
        throw new Exception("Missing fixture field " + name);
    }
}

public class EtchedSigilChannelProbe : ChanneledSkill
{
    public int TickFrames = 12;
    public bool ChangeTarget;
    protected override int GetChannelTickFrames() => TickFrames;
    public override void OnChannelUpdated() { if (ChangeTarget) TargetPosition.X = 100; }
    public override IEnumerable<TickTimer> Main() { yield break; }
}

public class EtchedSigilPlayerProbe : Player
{
    private EtchedSigilPlayerProbe() : base(null, null, null) { }
    public float Healing;
    public override void AddHP(float quantity, bool guidingLight = false) => Healing += quantity;
}
