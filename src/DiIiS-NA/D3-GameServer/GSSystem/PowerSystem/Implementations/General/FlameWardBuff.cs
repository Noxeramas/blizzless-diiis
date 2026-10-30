namespace DiIiS_NA.GameServer.GSSystem.PowerSystem.Implementations
{
	public sealed class FlameWardBuff : Buff
	{
		private readonly ChanneledSkill _channel;
		private readonly float _initialReduction, _reductionPerSecond;
		private readonly int _maximumSeconds;

		public FlameWardBuff(ChanneledSkill channel, float initialReduction, float reductionPerSecond, int maximumSeconds)
		{
			_channel = channel;
			_initialReduction = initialReduction;
			_reductionPerSecond = reductionPerSecond;
			_maximumSeconds = maximumSeconds;
		}

		public float DamageReduction => !Removed && _channel.IsChannelActive && _channel.Rune_A > 0
			? _initialReduction + _reductionPerSecond * System.Math.Min(_maximumSeconds, _channel.ChannelSeconds) : 0f;
		public override bool Update() => DamageReduction <= 0f;
	}
}
