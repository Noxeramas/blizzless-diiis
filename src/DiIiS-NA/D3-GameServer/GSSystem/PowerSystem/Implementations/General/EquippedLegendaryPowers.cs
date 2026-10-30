using System;
using System.Linq;
using DiIiS_NA.GameServer.GSSystem.ActorSystem;
using DiIiS_NA.GameServer.GSSystem.PlayerSystem;
using DiIiS_NA.GameServer.GSSystem.SkillsSystem;
using DiIiS_NA.GameServer.GSSystem.TickerSystem;
using DiIiS_NA.GameServer.MessageSystem;
using DiIiS_NA.LoginServer.Toons;

namespace DiIiS_NA.GameServer.GSSystem.PowerSystem.Implementations
{
	// Values and display slots: installed PC 2.8.0.99920 powers/BuffTooltips.
	// Evidence and remaining live-comparison limits: docs/equipped-legendaries.md.
	internal static class EquippedLegendaryPowers
	{
		internal const int Aquila = 449064, StoneGauntlets = 483519, Hergbrash = 449048,
			IceClimbers = 318741, Ashnagarr = 449043, StoneOfJordan = 484282,
			Convention = 430674, Squirts = 483552;

		internal static bool Has(Actor actor, int power) => actor is Player player && player.GetLegendaryPowerValue(power) > 0f;
		internal static float ShieldMultiplier(Actor actor) => 1f + (actor is Player player ? player.GetLegendaryPowerValue(Ashnagarr) : 0f);
		internal static bool IsWizardChannel(int power) => power == Skills.Wizard.Offensive.ArcaneTorrent ||
			power == Skills.Wizard.Offensive.Disintegrate || power == Skills.Wizard.Offensive.RayOfFrost;

		// Percent reductions precede flat reductions. The caller supplies a channel tick's
		// fraction of the base cost, so flat reductions must use that same fraction.
		internal static float ChannelCost(Player player, int power, float baseCost, float tickFraction)
		{
			float reduced = baseCost * (1f - player.GetLegendaryPowerValue(Hergbrash)) *
				(1f - player.Attributes[GameAttributes.Resource_Cost_Reduction_Percent_All]) *
				(1f - player.Attributes[GameAttributes.Resource_Cost_Reduction_Percent, 1]) *
				(1f - player.DecreaseUseResourcePercent);
			return Math.Max(0f, reduced - tickFraction * (player.Attributes[GameAttributes.Resource_Cost_Reduction_Amount] +
				player.Attributes[GameAttributes.Power_Resource_Reduction, power]));
		}

		internal static int[] Elements(ToonClass heroClass) => heroClass switch
		{
			ToonClass.Wizard => new[] { 5, 3, 1, 2 },
			ToonClass.Barbarian => new[] { 3, 1, 2, 0 },
			ToonClass.Crusader => new[] { 1, 6, 2, 0 },
			ToonClass.DemonHunter => new[] { 3, 1, 2, 0 },
			ToonClass.Monk => new[] { 3, 1, 6, 2, 0 },
			ToonClass.WitchDoctor => new[] { 3, 1, 0, 4 },
			ToonClass.Necromancer => new[] { 3, 0, 4 },
			_ => Array.Empty<int>()
		};

		internal static void ApplyStoneOfJordan(Player player, int[] elements)
		{
			// Re-read static gear so Cube assignment/removal also clears old equalization.
			foreach (int element in elements)
			{
				player.Attributes[GameAttributes.Damage_Dealt_Percent_Bonus, element] = player.Inventory.GetItemBonus(GameAttributes.Damage_Dealt_Percent_Bonus, element);
				player.Attributes[GameAttributes.Damage_Type_Percent_Bonus, element] = player.Inventory.GetItemBonus(GameAttributes.Damage_Type_Percent_Bonus, element);
			}
			if (!Has(player, StoneOfJordan) || elements.Length == 0) return;
			float Bonus(int element) => player.Attributes[GameAttributes.Damage_Dealt_Percent_Bonus, element] +
				player.Attributes[GameAttributes.Damage_Type_Percent_Bonus, element];
			float highest = elements.Max(Bonus);
			foreach (int element in elements)
				player.Attributes[GameAttributes.Damage_Dealt_Percent_Bonus, element] += highest - Bonus(element);
		}

		internal static void Update(Player player)
		{
			if (player.World == null || !player.Inventory.Loaded || player.Dead) return;
			var buffs = player.World.BuffManager;
			if (Has(player, Aquila) && !buffs.HasBuff<AquilaBuff>(player))
				buffs.AddBuff(player, player, new AquilaBuff(player.PrimaryResourceID));
			if (Has(player, Convention) && !buffs.HasBuff<ConventionBuff>(player))
				buffs.AddBuff(player, player, new ConventionBuff(Elements(player.Toon.Class)));
			if (Has(player, Squirts) && !buffs.HasBuff<SquirtsBuff>(player))
				buffs.AddBuff(player, player, new SquirtsBuff());
			if (Has(player, IceClimbers))
			{
				buffs.RemoveBuffs(player, typeof(DebuffFrozen));
				buffs.RemoveBuffs(player, typeof(DebuffRooted));
			}
		}

		internal static void OnHit(Player player)
		{
			if (Has(player, StoneGauntlets))
				player.World.BuffManager.AddBuff(player, player, new StoneGauntletsBuff());
		}
	}

	[ImplementsPowerSNO(EquippedLegendaryPowers.Aquila)]
	[ImplementsPowerBuff(1)]
	internal sealed class AquilaBuff : PowerBuff
	{
		private readonly int _resource;
		private int _nextCheck;
		private bool _active;
		internal AquilaBuff(int resource) => _resource = resource;
		internal float DamageReduction => !Removed && EquippedLegendaryPowers.Has(User, PowerSNO) && _active ? .5f : 0f;
		public override bool Apply() { base.Apply(); Update(); return true; }
		public override bool Update()
		{
			if (Removed || User.Dead || User.World != World || !EquippedLegendaryPowers.Has(User, PowerSNO)) return true;
			int now = World.Game.TickCounter;
			if (now < _nextCheck) return false;
			_nextCheck = now + 15; // SF0: 0.25 second check interval.
			float max = User.Attributes[GameAttributes.Resource_Max_Total, _resource];
			_active = max > 0 && User.Attributes[GameAttributes.Resource_Cur, _resource] >
				max * ((Player)User).GetLegendaryPowerValue(PowerSNO);
			User.Attributes[GameAttributes.Buff_Icon_Count1, PowerSNO] = _active ? 1 : 0;
			User.Attributes.BroadcastChangedIfRevealed();
			return false;
		}
		public override void Remove() { User.Attributes[GameAttributes.Buff_Icon_Count1, PowerSNO] = 0; base.Remove(); }
	}

	[ImplementsPowerSNO(EquippedLegendaryPowers.Convention)]
	internal sealed class ConventionBuff : PowerBuff
	{
		private readonly int[] _elements;
		private int _start, _phase = -1;
		internal ConventionBuff(int[] elements) => _elements = elements;
		internal int CurrentElement => _elements[(Math.Max(0, World.Game.TickCounter - _start) / 240) % _elements.Length];
		internal float Bonus(int element) => !Removed && !User.Dead && User.World == World && element == CurrentElement
			? ((Player)User).GetLegendaryPowerValue(PowerSNO) : 0f;
		public override bool Apply() { _start = World.Game.TickCounter; Update(); return true; }
		public override bool Update()
		{
			if (Removed || User.Dead || User.World != World || !EquippedLegendaryPowers.Has(User, PowerSNO)) return true;
			int phase = Math.Max(0, World.Game.TickCounter - _start) / 240;
			if (phase == _phase) return false;
			if (_phase >= 0) base.Remove();
			Removed = false;
			_phase = phase;
			// BuffTooltips slots: Arcane 1, Cold 2, Fire 3, Holy 4, Lightning 5, Physical 6, Poison 7.
			BuffSlot = CurrentElement switch { 5 => 1, 3 => 2, 1 => 3, 6 => 4, 2 => 5, 0 => 6, 4 => 7, _ => 0 };
			Timeout = new TickTimer(World.Game, _start + (phase + 1) * 240);
			base.Apply();
			base.Stack(this); // Refresh the displayed start tick when the element changes.
			return false;
		}
	}

	[ImplementsPowerSNO(EquippedLegendaryPowers.Squirts)]
	[ImplementsPowerBuff(5, true)]
	internal sealed class SquirtsBuff : PowerBuff
	{
		private int _nextStack;
		internal float DamageBonus => Active ? StackCount * .1f : 0f;
		internal float DamageTakenBonus => Active ? StackCount * .05f : 0f;
		private bool Active => !Removed && !User.Dead && User.World == World && EquippedLegendaryPowers.Has(User, PowerSNO);
		public override bool Apply()
		{
			base.Apply();
			StackCount = 0;
			_nextStack = World.Game.TickCounter + 60;
			return true;
		}
		public override bool Update()
		{
			if (!Active) return true;
			int now = World.Game.TickCounter;
			if (now >= _nextStack)
			{
				int count = 1 + (now - _nextStack) / 60;
				_nextStack += count * 60;
				StackCount = Math.Min(10, StackCount + count);
				ShowCount();
			}
			return false;
		}
		internal void OnHealthDamage()
		{
			StackCount = 0;
			_nextStack = World.Game.TickCounter + 120; // SF3: Hit Reset.
			ShowCount();
		}
		private void ShowCount()
		{
			Target.Attributes[GameAttributes.Buff_Icon_Count5, PowerSNO] = StackCount;
			Target.Attributes.BroadcastChangedIfRevealed();
		}
		public override void Remove() { StackCount = 0; ShowCount(); base.Remove(); }
	}

	[ImplementsPowerSNO(EquippedLegendaryPowers.StoneGauntlets)]
	[ImplementsPowerBuff(1, true)]
	internal sealed class StoneGauntletsBuff : PowerBuff
	{
		private float _armor, _slow, _attackSlow;
		private int _slowUntil;
		public override void Init() { MaxStackCount = 5; Timeout = WaitSeconds(5); _slowUntil = World.Game.TickCounter + 60; }
		public override bool Apply() { base.Apply(); UpdateStats(); return true; }
		public override bool Stack(Buff buff)
		{
			base.Stack(buff);
			_slowUntil = World.Game.TickCounter + 60;
			UpdateStats();
			return true;
		}
		public override bool Update()
		{
			if (base.Update() || User.Dead || User.World != World || !EquippedLegendaryPowers.Has(User, PowerSNO)) return true;
			UpdateStats();
			return false;
		}
		private void UpdateStats()
		{
			// SF0/SF5 distinguish the one-second slow from the five-second armor buff.
			bool immune = EquippedLegendaryPowers.Has(User, EquippedLegendaryPowers.IceClimbers) ||
				User.Attributes[GameAttributes.Root_Immune] || User.Attributes[GameAttributes.Immunity];
			float armor = Removed ? 0 : StackCount * .5f;
			float slow = Removed || immune || World.Game.TickCounter >= _slowUntil ? 0 : StackCount * .15f;
			float attack = Removed || immune || World.Game.TickCounter >= _slowUntil ? 0 : StackCount * .2f;
			if (User.Attributes[GameAttributes.AttackSpeed_Reduction_Immune]) attack = 0;
			if (User.Attributes[GameAttributes.Slowdown_Immune]) slow = 0;
			if (armor == _armor && slow == _slow && attack == _attackSlow) return;
			User.Attributes[GameAttributes.Armor_Bonus_Percent] += armor - _armor;
			User.Attributes[GameAttributes.Movement_Scalar_Reduction_Percent] += slow - _slow;
			User.Attributes[GameAttributes.Attacks_Per_Second_Percent_Reduction] += attack - _attackSlow;
			_armor = armor; _slow = slow; _attackSlow = attack;
			User.Attributes.BroadcastChangedIfRevealed();
		}
		public override void Remove() { base.Remove(); UpdateStats(); }
	}
}
