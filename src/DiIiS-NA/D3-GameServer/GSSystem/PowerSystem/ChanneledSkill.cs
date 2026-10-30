using DiIiS_NA.GameServer.GSSystem.TickerSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiIiS_NA.GameServer.GSSystem.PowerSystem
{
	public abstract class ChanneledSkill : PowerScript
	{
		public bool IsChannelOpen = false;
		public float EffectsPerSecond = 1.0f;
		public bool WaitForSpawn = false;
		public new float WaitSeconds = 1.0f;

		public virtual void OnChannelOpen() { }
		public virtual void OnChannelClose() { }
		public virtual void OnChannelUpdated() { }
		public abstract IEnumerable<TickTimer> Main();
		// Opt in for channels whose client formulas specify an attack-speed-dependent tick period.
		protected virtual int GetChannelTickFrames() => 0;
		internal int LastEffectTick { get; private set; } = -1;
		internal Core.Types.Math.Vector3D AimPosition { get; private set; }
		internal int ChannelStartTick { get; private set; }
		internal bool IsChannelActive => IsChannelOpen && User.World == World && !User.Dead &&
			!User.Attributes[MessageSystem.GameAttributes.Disabled] &&
			!User.Attributes[MessageSystem.GameAttributes.Stunned] &&
			!User.Attributes[MessageSystem.GameAttributes.Frozen] &&
			!User.Attributes[MessageSystem.GameAttributes.Feared];
		internal int ChannelSeconds => Math.Max(0, World.Game.TickCounter - ChannelStartTick) / 60;

		private TickTimer _effectTimeout = null;
		private TickTimer _waitTimeout = null;

		public sealed override IEnumerable<TickTimer> Run()
		{
			// Channel scripts may extend the beam or randomize its impact point. Item casts use the cursor.
			AimPosition = TargetPosition == null ? null : new Core.Types.Math.Vector3D(TargetPosition);
			// process channeled skill events
			if (IsChannelOpen)
			{
				if (WaitForSpawn)
				{
					if (_waitTimeout == null || _waitTimeout.TimedOut)
					{
						_waitTimeout = WaitSeconds(WaitSeconds);
						OnChannelUpdated();
					}
				}
				else
					OnChannelUpdated();
			}
			else  // first call to this skill's Run(), set channel as open
			{
				ChannelStartTick = World.Game.TickCounter;
				OnChannelOpen();
				IsChannelOpen = true;
			}

			Implementations.EtchedSigilBuff.Activate(this);
			Implementations.MantleOfChannelingBuff.Activate(this);

			// run main script if ready
			if (_effectTimeout == null || _effectTimeout.TimedOut)
			{
				int frames = GetChannelTickFrames();
				if (frames > 0)
				{
					// Retain the channel's frame phase when an input/world update arrives late.
					// Execute at most one tick; do not replay missed damage or resource charges.
					int now = World.Game.TickCounter;
					int due = _effectTimeout?.TimeoutTick ?? now;
					LastEffectTick = due + (now - due) / frames * frames;
					_effectTimeout = new TickTimer(World.Game, LastEffectTick + frames);
					EffectsPerSecond = frames / 60f;
				}
				else
					_effectTimeout = WaitSeconds(EffectsPerSecond);
				Implementations.MagicWeapon.OnCast(this);
				foreach (TickTimer timeout in Main())
					yield return timeout;
			}
		}

		public void CloseChannel()
		{
			OnChannelClose();
			IsChannelOpen = false;
			_effectTimeout = null;
			LastEffectTick = -1;
		}
	}
}
