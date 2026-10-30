using DiIiS_NA.GameServer.Core.Types.Math;
using DiIiS_NA.GameServer.GSSystem.PlayerSystem;
using DiIiS_NA.GameServer.GSSystem.SkillsSystem;
using DiIiS_NA.GameServer.MessageSystem;

namespace DiIiS_NA.GameServer.GSSystem.PowerSystem.Implementations
{
	// P610_Unique_Orb_002 / P610_ItemPassive_Unique_Ring_002 in the installed 2.8.0 data.
	// This server-side buff follows the channel; its damage bonus is checked on each twister hit.
	public sealed class EtchedSigilBuff : Buff
	{
		public const int ItemPowerSNO = 484596;
		private readonly ChanneledSkill _channel;
		private int _nextCastTick;

		private EtchedSigilBuff(ChanneledSkill channel) => _channel = channel;

		public float DamageBonus
		{
			get
			{
				if (Removed || !_channel.IsChannelActive || _channel.User is not Player player)
					return 0f;

				// Read the effective equipped/Cube power on each hit; do not snapshot equipment.
				return player.GetLegendaryPowerValue(ItemPowerSNO);
			}
		}

		public static void Activate(ChanneledSkill channel)
		{
			if (channel.PowerSNO != Skills.Wizard.Offensive.ArcaneTorrent &&
				channel.PowerSNO != Skills.Wizard.Offensive.Disintegrate &&
				channel.PowerSNO != Skills.Wizard.Offensive.RayOfFrost)
				return;

			var buffs = channel.World.BuffManager;
			var existing = buffs.GetFirstBuff<EtchedSigilBuff>(channel.User);
			if (existing != null)
			{
				if (existing._channel == channel && existing.DamageBonus > 0f)
					return;
				buffs.RemoveBuff(channel.User, existing);
			}

			var buff = new EtchedSigilBuff(channel);
			if (buff.DamageBonus > 0f)
				buffs.AddBuff(channel.User, channel.User, buff);
		}

		public override void Init() => _nextCastTick = World.Game.TickCounter + 60;

		internal EnergyTwister TryCreateTwister()
		{
			if (DamageBonus <= 0f || _channel.LastEffectTick < _nextCastTick)
				return null;

			// The 59-frame ICD must have elapsed at a channel tick (strictly more than 59 frames).
			// e.g. 12-frame ticks proc every 60 frames; 13-frame ticks proc every 65 frames.
			_nextCastTick = _channel.LastEffectTick + 60;
			return new EnergyTwister
			{
				PowerSNO = Skills.Wizard.Offensive.EnergyTwister,
				User = User,
				World = World,
				TargetPosition = new Vector3D(_channel.AimPosition),
				IsItemProc = true
			};
		}

		public override bool Update()
		{
			if (DamageBonus <= 0f)
				return true;

			// Bypass Skill.Run and Main: no cast animation, channel interruption, or Arcane Power cost.
			TryCreateTwister()?.SpawnTwister();
			return false;
		}
	}
}
