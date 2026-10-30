using DiIiS_NA.GameServer.Core.Types.Math;

namespace DiIiS_NA.GameServer.GSSystem.ActorSystem.Movement
{
	/// <summary>
	/// One straight walk an actor was sent with <see cref="Actor.Move"/>. The client walks it at the speed in the
	/// message from the moment it gets it, so the server follows the same line tick by tick. Otherwise the server only
	/// knows where a monster is at the end of each step, and anything aimed at the monster lands where it was.
	/// </summary>
	public class WalkStep
	{
		private readonly Actor _owner;
		private readonly Vector3D _start;
		private readonly Vector3D _end;
		private readonly float _speed;
		private readonly int _startTick;
		private Vector3D _position;

		/// <summary>
		/// Starts following a walk from the actor's position to <paramref name="end"/> at its current walk speed.
		/// </summary>
		public WalkStep(Actor owner, Vector3D end)
		{
			_owner = owner;
			_start = owner.Position;
			_end = end;
			_speed = owner.WalkSpeed;
			_startTick = owner.World.Game.TickCounter;
			_position = owner.Position;
		}

		/// <summary>
		/// Puts the actor where the client shows it on this walk. Once something else has moved the actor (a knockback,
		/// a teleport), the walk was broken off on the client too, and the actor is left where that put it.
		/// </summary>
		public void Follow()
		{
			if (_owner.Position != _position) return;

			float distance = MovementHelpers.GetDistance(_start, _end);
			float travelled = _speed * (_owner.World.Game.TickCounter - _startTick);
			if (travelled >= distance)
				_position = _end;
			else
			{
				float part = travelled / distance;
				_position = new Vector3D(_start.X + (_end.X - _start.X) * part,
					_start.Y + (_end.Y - _start.Y) * part,
					_start.Z + (_end.Z - _start.Z) * part);
			}
			_owner.Position = _position;
		}

		/// <summary>
		/// Puts the actor at the end of the walk, where the client has it by the time the walk's ticks have passed,
		/// unless something else has moved it.
		/// </summary>
		public void Finish()
		{
			if (_owner.Position != _position) return;
			_owner.Position = _position = _end;
		}
	}
}
