using System;
using System.Collections.Generic;
using DiIiS_NA.GameServer.GSSystem.ActorSystem;
using DiIiS_NA.GameServer.GSSystem.PlayerSystem;
using DiIiS_NA.GameServer.MessageSystem;

namespace DiIiS_NA.GameServer.GSSystem.PowerSystem.Implementations
{
	// P610_Unique_Sword_1H_107 / P610_ItemPassive_Unique_Ring_027 in the installed 2.8.0 data.
	// Track casts, including the constituents of a merged tornado, without scanning the world per hit.
	public sealed class TwistedSwordBuff : Buff
	{
		public const int ItemPowerSNO = 484709;
		private readonly HashSet<EnergyTwister> _twisters = new HashSet<EnergyTwister>();
		private int _iconCount, _iconEndTick;

		public static float GetEquippedBonus(Actor user) => user is Player player
			? player.GetLegendaryPowerValue(ItemPowerSNO) : 0f;

		public static void Track(EnergyTwister twister)
		{
			if (twister.User is not Player) return;
			var buffs = twister.World.BuffManager;
			var buff = buffs.GetFirstBuff<TwistedSwordBuff>(twister.User);
			if (buff == null)
			{
				buff = new TwistedSwordBuff();
				if (!buffs.AddBuff(twister.User, twister.User, buff)) return;
			}
			buff._twisters.Add(twister);
			buff.UpdateIcon();
		}

		public float DamageBonus
		{
			get
			{
				if (Removed || User.World != World || User.Dead) return 0f;
				var roll = GetEquippedBonus(User);
				if (roll <= 0f) return 0f;
				var count = 0;
				foreach (var twister in _twisters)
				{
					if (!twister.IsActive) continue;
					count += twister.CastCount;
					// 2.7.7 reduced the damage cap to three; the five-cast merge limit is separate.
					if (count >= 3) break;
				}
				return roll * Math.Min(count, 3);
			}
		}

		public override bool Update()
		{
			if (Removed || User.World != World || User.Dead) return true;
			_twisters.RemoveWhere(twister => !twister.IsActive);
			UpdateIcon();
			return _twisters.Count == 0;
		}

		private void UpdateIcon()
		{
			var count = 0;
			var endTick = 0;
			if (!Removed && User.World == World && !User.Dead && GetEquippedBonus(User) > 0f)
				foreach (var twister in _twisters)
					if (twister.IsActive)
					{
						count += twister.CastCount;
						endTick = Math.Max(endTick, twister.ActiveTwister.Timeout.TimeoutTick);
					}
			count = Math.Min(count, 3);
			if (count == _iconCount && endTick == _iconEndTick) return;

			// Installed Wizard_EnergyTwister Buff6Icon and BuffTooltips Wizard_EnergyTwister_6
			// identify the sword's display. The item-passive power itself has no buff icon.
			const int displayPower = SkillsSystem.Skills.Wizard.Offensive.EnergyTwister;
			User.Attributes[GameAttributes.Buff_Icon_Count6, displayPower] = count;
			if (endTick != _iconEndTick)
			{
				User.Attributes[GameAttributes.Buff_Icon_Start_Tick6, displayPower] = count > 0 ? World.Game.TickCounter : 0;
				User.Attributes[GameAttributes.Buff_Icon_End_Tick6, displayPower] = endTick;
			}
			User.Attributes.BroadcastChangedIfRevealed();
			_iconCount = count;
			_iconEndTick = endTick;
		}

		public override void Remove()
		{
			base.Remove();
			UpdateIcon();
		}
	}
}
