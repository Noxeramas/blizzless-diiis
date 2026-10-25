using DiIiS_NA.Core.Logging;
using DiIiS_NA.D3_GameServer.Core.Types.SNO;
using DiIiS_NA.GameServer.Core.Types.TagMap;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiIiS_NA.GameServer.GSSystem.ActorSystem.Implementations
{
	[HandledSNO(ActorSno._x1_randomitemnpc)]
	public class Kadala : Vendor
	{
		private static readonly Logger logger = LogManager.CreateLogger();
		private static readonly int[] itemGbIds = new int[]
		{
			-1492848355,
			-594428401,
			2050033703,
			-2026108002,
			-537237168,
			-1493063970,
			-2010009315,
			1281756953,
			-1492484569,
			1816611999,
			-767866790,
			-1099096773,
			-1780286480,
			215071258,
			-1492657844,
			-1843121997,
			1060845884 // PH_Phylactery
		};
		public Kadala(MapSystem.World world, ActorSno sno, TagMap tags)
			: base(world, sno, tags)
		{
		}

		protected override List<ItemsSystem.Item> GetVendorItems()
		{
			return itemGbIds.Select(x => ItemsSystem.ItemGenerator.CookFromDefinition(World, ItemsSystem.ItemGenerator.GetItemDefinition(x), 1, false)).ToList();
		}

		public override bool Reveal(PlayerSystem.Player player)
		{
			if (World.Game.CurrentAct != 3000) return false;
			return base.Reveal(player);
		}

		public override void OnRequestBuyItem(PlayerSystem.Player player, uint itemId)
		{
			ItemsSystem.Item item = _vendorGrid.GetItemByDynId(player, itemId);
			if (item == null)
				return;
			Gamble(player, item);
		}

		/// <summary>
		/// A gamble request (<c>GambleMessage</c>), which names the placeholder item that was clicked by its GBID.
		/// </summary>
		public void OnGamble(PlayerSystem.Player player, int gbId)
		{
			var item = _vendorGrid.Items.Values.FirstOrDefault(x => x.GBHandle.GBID == gbId);
			if (item == null)
			{
				logger.Warn("Gamble request for {0}, which Kadala doesn't sell.", gbId);
				return;
			}
			Gamble(player, item);
		}

		private void Gamble(PlayerSystem.Player player, ItemsSystem.Item item)
		{
			if (!player.Inventory.HasInventorySpace(item))
			{
				return;
			}

			int cost = item.ItemDefinition.CostAlt;
			//Check shards here
			if (player.Inventory.GetBloodShardsAmount() < cost)
				return;

			//Remove the shards
			player.Inventory.RemoveBloodShardsAmount(cost);
			logger.Debug("Gambled {0} for {1} blood shards.", item.ItemDefinition.Name, cost);

			player.Inventory.BuyItem(this, item, false);
			player.Inventory.RefreshInventoryToClient();
			if (player.Toon.IsSeasoned)
			{
				player.GrantCriteria(74987248526596);
			}
		}
	}
}
