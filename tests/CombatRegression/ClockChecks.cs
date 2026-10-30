using System;
using System.Reflection;
using DiIiS_NA.GameServer.GSSystem.GameSystem;

static class ClockChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("sleep overshoot does not accumulate into simulation clock drift", () =>
        {
            foreach (var overshoot in new[] { 1, 12 })
            {
                var schedule = new Schedule();
                long elapsed = 0;
                var ticks = 0;
                for (var update = 0; update < 1200; update++)
                {
                    ticks += schedule.Advance(elapsed);
                    Assert(ticks == (elapsed / 50 + 1) * 3, "Simulation lost elapsed time");
                    // Cheap work followed by an oversleep, as seen in the recorded sessions.
                    elapsed += 1;
                    elapsed += schedule.Delay(elapsed) + overshoot;
                }
                Assert(elapsed == 60000 + overshoot, $"One minute of updates took {elapsed} ms");
            }
        });

        check("work stalls and delayed wakeups advance the clock once without a catch-up burst", () =>
        {
            var schedule = new Schedule();
            Assert(schedule.Advance(0) == 3, "Initial tick changed");
            Assert(schedule.Delay(454) == 0, "Over-budget work requested another sleep");
            Assert(schedule.Advance(454) == 27, "Stalled slots were lost or counted twice");
            Assert(schedule.Delay(455) == 45, "Next update was not aligned to the deadline");
            Assert(schedule.Advance(512) == 3, "Wakeup overshoot changed the tick quantum");
            Assert(schedule.Delay(513) == 37, "Wakeup overshoot was not compensated");
            Assert(schedule.Advance(1000) == 30, "Descheduled time was lost");
            Assert(schedule.Delay(1001) == 49, "Clock did not recover after descheduling");
        });

        check("ordinary updates preserve three ticks per fifty milliseconds", () =>
        {
            var schedule = new Schedule();
            for (long elapsed = 0; elapsed < 60000; elapsed += 50)
            {
                Assert(schedule.Advance(elapsed) == 3, "Ordinary tick increment changed");
                Assert(schedule.Delay(elapsed + 2) == 48, "Work time was not deducted");
            }
        });
    }

    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    // Exercise the server's internal scheduler without widening its production API for tests.
    sealed class Schedule
    {
        static readonly Type Type = typeof(Game).Assembly.GetType(
            "DiIiS_NA.GameServer.GSSystem.GameSystem.GameUpdateSchedule", throwOnError: true);
        static readonly MethodInfo AdvanceMethod = Type.GetMethod("Advance", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly MethodInfo DelayMethod = Type.GetMethod("Delay", BindingFlags.Instance | BindingFlags.NonPublic);
        readonly object _value = Activator.CreateInstance(Type, BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { 50L, 3 }, null);

        public int Advance(long elapsed) => (int)AdvanceMethod.Invoke(_value, new object[] { elapsed });
        public int Delay(long elapsed) => (int)DelayMethod.Invoke(_value, new object[] { elapsed });
    }
}
