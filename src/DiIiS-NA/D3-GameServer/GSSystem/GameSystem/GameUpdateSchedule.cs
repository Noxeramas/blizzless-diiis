using System;

namespace DiIiS_NA.GameServer.GSSystem.GameSystem
{
	/// <summary>
	/// Keeps world updates on a monotonic timeline, including scheduler and sleep delays.
	/// </summary>
	internal sealed class GameUpdateSchedule
	{
		private readonly long _intervalMilliseconds;
		private readonly int _ticksPerUpdate;
		private long _nextUpdateMilliseconds;

		internal GameUpdateSchedule(long intervalMilliseconds, int ticksPerUpdate)
		{
			_intervalMilliseconds = intervalMilliseconds;
			_ticksPerUpdate = ticksPerUpdate;
		}

		internal int Advance(long elapsedMilliseconds)
		{
			// Skip elapsed update slots after a stall instead of replaying a burst of world updates.
			// Keep whole update steps: existing consumers depend on multiples of TickRate.
			long updates = 1 + Math.Max(0, elapsedMilliseconds - _nextUpdateMilliseconds) / _intervalMilliseconds;
			_nextUpdateMilliseconds += updates * _intervalMilliseconds;
			return (int)(updates * _ticksPerUpdate);
		}

		internal int Delay(long elapsedMilliseconds)
		{
			return (int)Math.Max(0, _nextUpdateMilliseconds - elapsedMilliseconds);
		}
	}
}
