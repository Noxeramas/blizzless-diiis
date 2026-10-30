using System;
using DiIiS_NA.Core.Helpers.Hash;
using DiIiS_NA.Core.Helpers.Math;
using DiIiS_NA.Core.Logging;
using DiIiS_NA.Core.MPQ;
using DiIiS_NA.Core.MPQ.FileFormats;
using DiIiS_NA.D3_GameServer.Core.Types.SNO;
using DiIiS_NA.GameServer.Core.Types.Math;
using DiIiS_NA.GameServer.Core.Types.SNO;
using DiIiS_NA.GameServer.GSSystem.ItemsSystem;
using DiIiS_NA.GameServer.MessageSystem;
using DiIiS_NA.GameServer.MessageSystem.Message.Definitions.Artisan;
using DiIiS_NA.GameServer.MessageSystem.Message.Definitions.Base;
using System.Collections.Generic;
using System.Linq;
using static DiIiS_NA.Core.MPQ.FileFormats.GameBalance;

namespace DiIiS_NA.GameServer.GSSystem.PlayerSystem
{
	/// <summary>
	/// Kanai's Cube transmutes (<see cref="TransmuteItemsMessage"/>). The client sends only what's in the cube, so the
	/// recipe is worked out from those items and materials, using the game's TransmuteRecipes table.
	/// </summary>
	internal static partial class KanaiCube
	{
		private static readonly Logger Logger = LogManager.CreateLogger();

		// TransmuteResultsMessage.Type
		private const int ResultItem = 1;
		private const int ResultCurrency = 2;
		private const int ResultPower = 0;

		// Error codes the client shows for a failed transmute (TransumteFailedMessage), from its table of errors
		private const int Done = 0;
		private const int ErrorSocketedItem = 160; // TransmuteCannotUseItemWithFilledSockets
		private const int ErrorUnidentifiedItem = 161; // TransmuteCannotUseUnidentifiedItem
		private const int ErrorMissingCoreIngredients = 162; // TransmuteMissingCoreIngredients
		private const int ErrorTransmuteFailed = 164; // TransmuteFailed
		private const int ErrorPowerAlreadyExtracted = 165;
		private const int ErrorPowerUnavailable = 166;
		private const int ErrorCannotFindItem = 170; // TransmuteFailedCannotFindItem
		private const int ErrorNotEnoughItemsInSet = 173; // TransmuteFailedNotEnoughItemsInSet

		// TransmuteRecipeIngredient.Type values, from the recipes that use them
		private const int IngredientItemGbid = 1;
		private const int IngredientLegendary = 2;
		private const int IngredientLegendaryPower = 3;
		private const int IngredientRare = 4;
		private const int IngredientSetItem = 5;
		private const int IngredientNonLegendary = 8;

		private static List<TransmuteRecipesTable> _recipes;

		private static List<TransmuteRecipesTable> Recipes => _recipes ??= MPQStorage.Data.Assets[SNOGroup.GameBalance].Values
			.Select(asset => asset.Data as GameBalance)
			.Where(data => data != null && data.Type == BalanceType.TransmuteRecipes)
			.SelectMany(data => data.TransmuteRecipesTables)
			.ToList();

		public static void Transmute(Player player, TransmuteItemsMessage message)
		{
			if (message.annItems == null || message.GBIDCurrencies == null ||
				message.ItemsCount < 0 || message.ItemsCount > message.annItems.Length ||
				message.CurrenciesCount < 0 || message.CurrenciesCount > message.GBIDCurrencies.Length ||
				message.annItems.Take(message.ItemsCount).Distinct().Count() != message.ItemsCount ||
				message.GBIDCurrencies.Take(message.CurrenciesCount).Distinct().Count() != message.CurrenciesCount)
			{
				Fail(player, ErrorMissingCoreIngredients, "invalid cube input counts or repeated ingredients");
				return;
			}
			var items = message.annItems.Take(message.ItemsCount)
				.Select(id => player.Inventory.GetItemByDynId(player, (uint)id))
				.ToList();
			var currencies = message.GBIDCurrencies.Take(message.CurrenciesCount).ToHashSet();
			if (items.Count == 0 || items.Any(item => item == null))
			{
				Fail(player, ErrorCannotFindItem, "an item that isn't in the backpack");
				return;
			}
			if (items.Any(item => item.Unidentified))
			{
				Fail(player, ErrorUnidentifiedItem, "an unidentified item");
				return;
			}
			if (items.Any(item => item.Attributes[GameAttributes.Sockets_Filled] > 0))
			{
				Fail(player, ErrorSocketedItem, "an item with socketed gems");
				return;
			}

			var recipe = Recipes.FirstOrDefault(r => Matches(player, r, items, currencies));
			if (recipe == null)
			{
				Fail(player, ErrorMissingCoreIngredients, "no recipe for " +
					string.Join(", ", items.Select(i => i.ItemDefinition.Name).Concat(currencies.Select(CurrencyName))));
				return;
			}

			Logger.Debug("Transmuting {0}: {1}.", recipe.Name, string.Join(", ", items.Select(i => i.ItemDefinition.Name)));
			var result = recipe.TransmuteType switch
			{
				TransmuteType.EXTRACT_LEGENDARY_POWER => ExtractPower(player, recipe, items[0]),
				TransmuteType.REFORGE_LEGENDARY => Replace(player, recipe, items[0],
					ItemGenerator.CreateItemForClass(player, items[0].ItemDefinition, ItemGenerator.ClassFor(player, items[0].ItemType), 9)),
				TransmuteType.UPGRADE_RARE_ITEM => Replace(player, recipe, items[0], UpgradedRare(player, items[0])),
				TransmuteType.CONVERT_SET_ITEM => ConvertSetItem(player, recipe, items[0]),
				TransmuteType.CONVERT_CRAFTING_MATS_FROM_NORMAL or
				TransmuteType.CONVERT_CRAFTING_MATS_FROM_MAGIC or
				TransmuteType.CONVERT_CRAFTING_MATS_FROM_RARE => ConvertCraftingMaterials(player, recipe, items[0]),
				TransmuteType.OPEN_PORTAL_TO_COW => OpenCowPortal(player, recipe, items[0]),
				_ => ErrorTransmuteFailed
			};
			if (result != Done)
			{
				Fail(player, result, $"{recipe.Name}, which isn't supported yet or had no result");
				return;
			}

			player.GrantCriteria(74987245494264);
			player.GrantCriteria(74987258962046);
		}

		/// <summary>
		/// Whether the cube holds exactly the recipe's items and materials (as on live, materials count only when they
		/// were put in the cube, which the recipe page's Fill does), and the player has enough of the materials.
		/// </summary>
		private static bool Matches(Player player, TransmuteRecipesTable recipe, List<Item> items, HashSet<int> currencies)
		{
			var ingredients = recipe.TransmuteRecipeIngredients.Take(recipe.IngredientsCount).ToList();
			var materials = ingredients.Where(IsMaterial).ToList();
			var itemIngredients = ingredients.Except(materials).ToList();

			if (materials.Count != currencies.Count)
				return false;
			foreach (var material in materials)
			{
				if (!currencies.Contains(material.TypeValue))
					return false;
				if (GetCurrency(player, material.TypeValue) < material.Quantity)
					return false;
			}

			if (itemIngredients.Sum(i => i.Quantity) != items.Count)
				return false;
			var unmatched = new List<Item>(items);
			foreach (var ingredient in itemIngredients)
				for (var i = 0; i < ingredient.Quantity; i++)
				{
					var item = unmatched.FirstOrDefault(it => IsIngredient(ingredient, it));
					if (item == null)
						return false;
					unmatched.Remove(item);
				}
			return true;
		}

		private static bool IsMaterial(TransmuteRecipeIngredient ingredient) =>
			ingredient.Type == IngredientItemGbid && Inventory.CraftingCurrencies.ContainsKey(CurrencyName(ingredient.TypeValue));

		private static bool IsIngredient(TransmuteRecipeIngredient ingredient, Item item)
		{
			return ingredient.Type switch
			{
				IngredientItemGbid => item.GBHandle.GBID == ingredient.TypeValue,
				IngredientLegendary => IsEquippable(item) && item.Quality >= 9,
				IngredientLegendaryPower => IsEquippable(item) && item.Quality >= 9 && GetPower(item.GBHandle.GBID) != null,
				// Level 70: the level requirement, which comes from the item's affixes; its base item can be lower.
				IngredientRare => IsEquippable(item) && item.Quality is >= 6 and <= 8 &&
				                  Math.Max(item.ItemLevel, item.Attributes[GameAttributes.Requirement, 57]) >= 70,
				IngredientSetItem => IsEquippable(item) && item.ItemDefinition.SNOSet != -1,
				IngredientNonLegendary => IsEquippable(item) && item.Quality < 9,
				_ => false // not handled yet
			};
		}

		private static bool IsEquippable(Item item) =>
			Item.IsArmor(item.ItemType) || Item.IsWeapon(item.ItemType) || Item.IsOffhand(item.ItemType) || Item.IsAccessory(item.ItemType);

		/// <summary>Swaps the transmuted item for <paramref name="result"/> and takes the recipe's materials.</summary>
		private static int Replace(Player player, TransmuteRecipesTable recipe, Item item, Item result)
		{
			if (result == null)
				return ErrorTransmuteFailed;
			player.Inventory.DestroyInventoryItem(item);
			TakeMaterials(player, recipe);
			player.Inventory.GetBag().AddItem(result);
			player.InGameClient.SendMessage(new TransmuteResultsMessage
			{
				annItem = (int)result.DynamicID(player),
				Type = ResultItem,
				GBIDFakeItem = -1,
				GBIDPower = -1,
				FakeItemStackCount = -1
			});
			return Done;
		}

		/// <summary>
		/// A legendary of the rare's equipment type, for the player's class or, if none of that type drops for it, a class
		/// one drops for (<see cref="ItemGenerator.LegendaryClassFor"/>). Rare helms, chests and belts are of
		/// "Generic" types (GenericHelm) that no legendary has; their legendaries are of the parent type (Helm).
		/// </summary>
		private static Item UpgradedRare(Player player, Item rare)
		{
			var type = rare.ItemType;
			if (type.Name.StartsWith("Generic") && ItemGroup.FromHash(type.I0) is { } parent)
				type = parent;
			return ItemGenerator.GenerateRandomEquip(player, 70, 9, type: type, ownerClass: ItemGenerator.LegendaryClassFor(player, type, 70));
		}

		/// <summary>
		/// Another piece of the item's set, at random. As on live, sets of two pieces (Focus and Restraint) can't be
		/// converted.
		/// </summary>
		private static int ConvertSetItem(Player player, TransmuteRecipesTable recipe, Item item)
		{
			var pieces = SetPieces(item.ItemDefinition.SNOSet);
			if (pieces.Count < 3)
				return ErrorNotEnoughItemsInSet;
			var piece = RandomHelper.RandomItem(pieces.Where(def => def.SNOActor != item.ItemDefinition.SNOActor).ToList());
			return Replace(player, recipe, item,
				ItemGenerator.CreateItemForClass(player, piece, ItemGenerator.ClassFor(player, ItemGroup.FromHash(piece.ItemTypesGBID)), 9));
		}

		/// <summary>
		/// The pieces of a set, one version of each (versions of a piece share its actor): the version that drops, if
		/// one does.
		/// </summary>
		private static List<ItemTable> SetPieces(int set) => ItemGenerator.AllowedItems.Values
			.Where(def => def.SNOSet == set && UniqueItems.UniqueItemStats.ContainsKey(def.Hash))
			.GroupBy(def => def.SNOActor)
			.Select(versions => versions.OrderByDescending(ItemGenerator.DropWeight).First())
			.ToList();

		/// <summary>
		/// Turns the recipe's 100 crafting materials into the kind that matches the item's quality: Reusable Parts for a
		/// normal item, Arcane Dust for a magic one, Veiled Crystal for a rare one.
		/// </summary>
		private static int ConvertCraftingMaterials(Player player, TransmuteRecipesTable recipe, Item item)
		{
			var target = item.Quality switch
			{
				< 3 => "Crafting_AssortedParts_01",
				< 6 => "Crafting_Magic_01",
				_ => "Crafting_Rare_01"
			};
			var source = recipe.TransmuteRecipeIngredients.Take(recipe.IngredientsCount)
				.FirstOrDefault(i => IsMaterial(i) && i.Quantity > 1);
			if (source == null || CurrencyName(source.TypeValue) == target)
				return ErrorTransmuteFailed;

			player.Inventory.DestroyInventoryItem(item);
			TakeMaterials(player, recipe);
			var (get, set) = Inventory.CraftingCurrencies[target];
			var account = player.Toon.GameAccount;
			set(account, get(account) + source.Quantity);
			player.Inventory.UpdateCurrencies();
			player.InGameClient.SendMessage(new TransmuteResultsMessage
			{
				annItem = -1,
				Type = ResultCurrency,
				GBIDFakeItem = StringHashHelper.HashItemName(target),
				GBIDPower = -1,
				FakeItemStackCount = source.Quantity
			});
			return Done;
		}

		private static int OpenCowPortal(Player player, TransmuteRecipesTable recipe, Item item)
		{
			player.Inventory.DestroyInventoryItem(item);
			TakeMaterials(player, recipe);
			player.World.SpawnMonster(ActorSno._p2_totallynotacowlevel_portal,
				new Vector3D(player.Position.X + 5, player.Position.Y + 5, player.Position.Z));
			player.InGameClient.SendMessage(new TransmuteResultsMessage
			{
				annItem = -1,
				Type = -1,
				GBIDFakeItem = -1,
				GBIDPower = -1,
				FakeItemStackCount = -1
			});
			return Done;
		}

		private static void TakeMaterials(Player player, TransmuteRecipesTable recipe)
		{
			var account = player.Toon.GameAccount;
			foreach (var material in recipe.TransmuteRecipeIngredients.Take(recipe.IngredientsCount).Where(IsMaterial))
			{
				var (get, set) = Inventory.CraftingCurrencies[CurrencyName(material.TypeValue)];
				set(account, get(account) - material.Quantity);
			}
			player.Inventory.UpdateCurrencies();
		}

		private static int GetCurrency(Player player, int gbid) =>
			Inventory.CraftingCurrencies.TryGetValue(CurrencyName(gbid), out var currency) ? currency.Get(player.Toon.GameAccount) : 0;

		private static string CurrencyName(int gbid) => ItemGenerator.GetItemDefinition(gbid)?.Name ?? gbid.ToString();

		/// <summary>Ends the transmute with the client's error <paramref name="error"/>, as live does.</summary>
		private static void Fail(Player player, int error, string reason)
		{
			if (error == ErrorTransmuteFailed)
				Logger.Warn("Transmute failed: {0}.", reason);
			else
				Logger.Debug("Transmute failed ({0}): {1}.", error, reason);
			player.InGameClient.SendMessage(new IntDataMessage(Opcodes.TransumteFailedMessage) { Field0 = error });
		}
	}
}
