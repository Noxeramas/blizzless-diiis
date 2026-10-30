using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DiIiS_NA.GameServer.ClientSystem;
using DiIiS_NA.GameServer.MessageSystem.Message.Definitions.ACD;
using DiIiS_NA.GameServer.MessageSystem.Message.Definitions.Animation;
using DiIiS_NA.GameServer.Core.Types.Math;
using DiIiS_NA.GameServer.Core.Types.SNO;
using DiIiS_NA.GameServer.GSSystem.ActorSystem;
using DiIiS_NA.GameServer.GSSystem.GameSystem;
using DiIiS_NA.GameServer.GSSystem.MapSystem;
using DiIiS_NA.GameServer.GSSystem.ObjectsSystem;
using DiIiS_NA.GameServer.GSSystem.PlayerSystem;
using DiIiS_NA.GameServer.GSSystem.PowerSystem;
using DiIiS_NA.GameServer.GSSystem.PowerSystem.Implementations;
using DiIiS_NA.GameServer.GSSystem.SkillsSystem;
using DiIiS_NA.GameServer.GSSystem.TickerSystem;
using DiIiS_NA.GameServer.MessageSystem;

static class TargetingChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("global target IDs are not reinterpreted as another actor's client ID", () =>
        {
            var (world, player) = Fixture();
            var intended = Target(world, 42, new Vector3D(10, 20, 3));
            Target(world, 77, new Vector3D(80, 90, 7));
            player.RevealedObjects = new() { [42] = 9, [77] = 42 };
            var globalId = world.GetGlobalId(player, 9);
            RunProbe(player, globalId);
            if (TargetProbe.Last.Target != intended)
                throw new Exception("The client ID was translated twice and selected another actor");
        });
        check("secondary casts resolve client IDs and preserve untargeted casts", () =>
        {
            var (world, player) = Fixture();
            var intended = Target(world, 42, new Vector3D(10, 20, 3));
            Target(world, 77, new Vector3D(80, 90, 7));
            player.RevealedObjects = new() { [42] = 9, [77] = 42 };
            WithProbe(() =>
            {
                var handler = typeof(Player).GetMethod("OnSecondaryPowerMessage", BindingFlags.Instance | BindingFlags.NonPublic)!;
                handler.Invoke(player, new object[] { null, new SecondaryAnimationPowerMessage { PowerSNO = 0, annTarget = 9 } });
                if (TargetProbe.Last.Target != intended) throw new Exception("Secondary cast selected the wrong actor");
                // A separate channel is needed so the probe captures the next dispatch on open.
                SetField(world, "_powerManager", new PowerManager());
                handler.Invoke(player, new object[] { null, new SecondaryAnimationPowerMessage { PowerSNO = 0, annTarget = -1 } });
                if (TargetProbe.Last.Target != null) throw new Exception("Untargeted cast acquired an actor");
            });
        });
        check("target positions are snapshots, not mutable actor positions", () =>
        {
            var (world, player) = Fixture();
            var target = Target(world, 42, new Vector3D(10, 20, 3));
            RunProbe(player, 42);
            TargetProbe.Last.TargetPosition.Z += 5;
            if (target.Position.Z != 3)
                throw new Exception("Changing a spell's aim also moved its target actor");
        });
        check("Arcane Torrent aim updates move its visual marker before another damage tick", () =>
        {
            var (world, player) = Fixture();
            var marker = Target(world, 42, new Vector3D(5, 0, 0));
            var skill = new WizardArcaneTorrent { User = player, World = world, PowerSNO = 0,
                TargetPosition = new Vector3D(0, 80, 0), IsChannelOpen = true };
            SetField(skill, "_targetProxy", marker);
            var recorder = new PositionRecordingClient();
            player.InGameClient = recorder;
            player.RevealedObjects[marker.GlobalID] = 9;
            world.Players[1] = player;
            skill.OnChannelUpdated();
            var update = recorder.Messages.OfType<ACDWorldPositionMessage>().Single();
            if (update.ActorID != 9 || Math.Abs(update.WorldLocation.Transform.Vector3D.Y - 50f) > 0.001f)
                throw new Exception("The corrected marker position was not sent to the client");
            if (Math.Abs(marker.Position.X) > 0.001f || Math.Abs(marker.Position.Y - 50f) > 0.001f)
                throw new Exception($"Target marker stayed at {marker.Position.X}, {marker.Position.Y}; expected 0, 50");
            skill.TargetPosition.Y = 20;
            if (Math.Abs(marker.Position.Y - 50f) > 0.001f)
                throw new Exception("Target marker aliases the next aim position");
        });
    }

    static void RunProbe(Player player, uint globalId)
    {
        WithProbe(() =>
        {
            if (!new PowerManager().RunPower(player, 0, globalId))
                throw new Exception("Targeted power was rejected");
        });
    }

    static void WithProbe(Action run)
    {
        // Register a no-op channel so the actual PowerManager resolver is exercised
        // without loading MPQ skills, starting a server, or touching the database.
        var implementations = (Dictionary<int, Type>)typeof(PowerLoader)
            .GetField("_implementations", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        implementations.Add(0, typeof(TargetProbe));
        try { run(); }
        finally { implementations.Remove(0); }
    }

    static (World, Player) Fixture()
    {
        var world = Empty<World>();
        SetField(world, "<WorldSNO>k__BackingField", new SNOHandle(0));
        SetField(world, "<Game>k__BackingField", Empty<Game>());
        SetField(world, "_powerManager", new PowerManager());
        SetField(world, "_buffManager", new BuffManager());
        SetField(world, "_actors", new ConcurrentDictionary<uint, Actor>());
        SetField(world, "_players", new ConcurrentDictionary<uint, Player>());
        var player = Empty<Player>();
        player.World = world;
        player.Position = new Vector3D();
        player.RevealedObjects = new();
        SetField(player, "<Attributes>k__BackingField", new GameAttributeMap(player));
        var skills = Empty<SkillSet>();
        SetField(skills, "<Player>k__BackingField", player);
        SetField(player, "<SkillSet>k__BackingField", skills);
        return (world, player);
    }

    static Actor Target(World world, uint id, Vector3D position)
    {
        var actor = Empty<EffectActor>();
        actor.World = world;
        actor.Position = position;
        actor.RotationAxis = new Vector3D();
        actor.RotationW = 1;
        actor.Scale = 1;
        actor.GlobalIDOverride = id;
        world.Actors[id] = actor;
        return actor;
    }

    static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    static void SetField(object target, string name, object value)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null) continue;
            field.SetValue(target, value);
            return;
        }
        throw new Exception("Missing fixture field " + name);
    }
}

public class TargetProbe : ChanneledSkill
{
    public static TargetProbe Last;
    public override void OnChannelOpen() => Last = this;
    public override IEnumerable<TickTimer> Main() { yield break; }
}

sealed class PositionRecordingClient : GameClient
{
    public readonly List<GameMessage> Messages = new();
    public PositionRecordingClient() : base(null) { }
    public override void SendMessage(GameMessage message) => Messages.Add(message);
}
