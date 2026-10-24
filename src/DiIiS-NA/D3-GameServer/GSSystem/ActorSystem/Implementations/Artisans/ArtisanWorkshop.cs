using DiIiS_NA.D3_GameServer.Core.Types.SNO;
using DiIiS_NA.GameServer.Core.Types.TagMap;
using DiIiS_NA.GameServer.GSSystem.ActorSystem;
using DiIiS_NA.GameServer.GSSystem.MapSystem;
using DiIiS_NA.GameServer.GSSystem.PlayerSystem;
using DiIiS_NA.GameServer.MessageSystem.Message.Definitions.Animation;

namespace DiIiS_NA.D3_GameServer.GSSystem.ActorSystem.Implementations.Artisans
{
	/// <summary>
	/// The workshop next to an artisan in town. It looks different at each artisan level, and each player sees it at
	/// their own artisan's level.
	/// </summary>
	[HandledSNO(
		ActorSno._blacksmith_crafting_backbone /* Blacksmith_Crafting_Backbone.acr */,
		ActorSno._jeweler_crafting_backbone /* Jeweler_Crafting_Backbone.acr */,
		ActorSno._mystic_crafting_backbone /* Mystic_Crafting_Backbone.acr */
	)]
	public class ArtisanWorkshop : ServerProp
	{
		public ArtisanWorkshop(World world, ActorSno sno, TagMap tags)
			: base(world, sno, tags)
		{
		}

		private ArtisanType Artisan => SNO switch
		{
			ActorSno._blacksmith_crafting_backbone => ArtisanType.Blacksmith,
			ActorSno._jeweler_crafting_backbone => ArtisanType.Jeweler,
			_ => ArtisanType.Mystic
		};

		public override bool Reveal(Player player)
		{
			if (!base.Reveal(player))
				return false;

			UpdateLook(player);
			return true;
		}

		/// <summary>
		/// Shows the workshop at <paramref name="player"/>'s artisan level. While the artisan is locked it keeps its default look.
		/// </summary>
		public void UpdateLook(Player player)
		{
			var idleTag = player.GetArtisanWorkshopIdleTag(Artisan);
			if (idleTag == null || !IsRevealedToPlayer(player))
				return;

			// The client reads this field as a tag in the actor's anim set, not as an animation SNO.
			player.InGameClient.SendMessage(new SetIdleAnimationMessage
			{
				ActorID = DynamicID(player),
				AnimationSNO = idleTag.Value
			});
		}
	}
}
