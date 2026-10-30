using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using DiIiS_NA.GameServer.Core.Types.Math;
using DiIiS_NA.GameServer.GSSystem.ActorSystem;
using DiIiS_NA.GameServer.GSSystem.ActorSystem.Movement;
using DiIiS_NA.GameServer.GSSystem.GameSystem;
using DiIiS_NA.GameServer.GSSystem.MapSystem;
using DiIiS_NA.GameServer.GSSystem.PowerSystem;

static class MovementChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("a walk step puts the monster where the client walks it, tick by tick", () =>
        {
            var (game, monster) = Fixture(new Vector3D(0, 0, 0), 0.65f);
            var step = new WalkStep(monster, new Vector3D(0, 19.5f, 0));
            SetTick(game, 15);
            step.Follow();
            Assert(Near(monster.Position, 0, 9.75f), $"Half way it was at {monster.Position.X}, {monster.Position.Y}");
            SetTick(game, 45);
            step.Follow();
            Assert(Near(monster.Position, 0, 19.5f), "It walked past the end of the step");
        });

        check("a get-hit pause during a step does not leave the monster at the step's start", () =>
        {
            // The recorded fight: the client walked a full 19.5 yard step while the server still had the monster
            // where the step began, so Arcane Torrent landed 19.5 yards behind it.
            var (game, monster) = Fixture(new Vector3D(395.2f, 2010.1f, 0), 0.65f);
            var step = new WalkStep(monster, new Vector3D(398.4f, 2028.8f, 0));
            monster.WalkSpeed = 0f;
            SetTick(game, 30);
            step.Finish();
            Assert(Near(monster.Position, 398.4f, 2028.8f), $"It stayed at {monster.Position.X}, {monster.Position.Y}");
        });

        check("a walk step gives way when something else moves the monster", () =>
        {
            var (game, monster) = Fixture(new Vector3D(0, 0, 0), 0.65f);
            var step = new WalkStep(monster, new Vector3D(0, 19.5f, 0));
            SetTick(game, 6);
            step.Follow();
            monster.Position = new Vector3D(-10, 0, 0); // knocked back
            SetTick(game, 12);
            step.Follow();
            step.Finish();
            Assert(Near(monster.Position, -10, 0), "The walk pulled the monster out of its knockback");
        });
    }

    static (Game, Actor) Fixture(Vector3D position, float walkSpeed)
    {
        var game = Empty<Game>();
        var world = Empty<World>();
        SetField(world, "<Game>k__BackingField", game);
        var monster = Empty<EffectActor>();
        monster.World = world;
        monster.Position = position;
        monster.WalkSpeed = walkSpeed;
        return (game, monster);
    }

    static void SetTick(Game game, int tick) => SetField(game, "_tickCounter", tick);

    static bool Near(Vector3D v, float x, float y) => Math.Abs(v.X - x) < 0.01f && Math.Abs(v.Y - y) < 0.01f;

    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
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
