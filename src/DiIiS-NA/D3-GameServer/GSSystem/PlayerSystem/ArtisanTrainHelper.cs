using DiIiS_NA.Core.Storage.AccountDataBase.Entities;
using DiIiS_NA.D3_GameServer.GSSystem.ActorSystem.Implementations.Artisans;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DiIiS_NA.D3_GameServer.GSSystem.PlayerSystem
{
    internal class ArtisanTrainHelper
    {
        internal const int MaxLevel = 12;
        private static readonly ArtisanType[] canBeTrained = new[] { ArtisanType.Blacksmith, ArtisanType.Jeweler, ArtisanType.Mystic };

        private static readonly Dictionary<ArtisanType, string> recipeTemplates = new()
        {
            [ArtisanType.Blacksmith] = "BlackSmith_Train_Level{0}",
            [ArtisanType.Jeweler] = "Jeweler_Train_Level{0}",
            [ArtisanType.Mystic] = "Mystic_Train_Level{0}"
        };

        private static readonly Dictionary<ArtisanType, long[]> achievements = new()
        {
            [ArtisanType.Blacksmith] = new[] { 74987243307767, 74987243307768, 74987243307769, 74987251817289 },
            [ArtisanType.Jeweler] = new[] { 74987243307781, 74987243307782, 74987243307783, 74987257153995 },
            [ArtisanType.Mystic] = new[] { 74987253584575, 74987256660015, 74987248802163, 74987251397159 }
        };

        private static readonly Dictionary<ArtisanType, long> criteriaForLevel10 = new()
        {
            [ArtisanType.Blacksmith] = 74987249071497,
            [ArtisanType.Jeweler] = 74987245845978,
            [ArtisanType.Mystic] = 74987259424359
        };

        // Tags in the workshop's anim set (Blacksmith_Crafting_Backbone etc.), one per level and 0x10 apart:
        // the transition that builds the workshop up to a level, and the idle look at that level.
        // Levels 11 and 12 look like level 10.
        private const int firstTransitionTag = 0x00011500;
        private const int firstIdleTag = 0x00011210;

        private readonly ArtisanType artisanType;

        internal ArtisanTrainHelper(DBCraft dBCraft, ArtisanType type)
        {
            if (!canBeTrained.Contains(type))
                throw new ArgumentException("Unsupported artisan type", nameof(type));

            DbRef = dBCraft ?? throw new ArgumentNullException(nameof(dBCraft));
            artisanType = type;
        }

        internal DBCraft DbRef { get; }

        internal string TrainRecipeName => string.Format(recipeTemplates[artisanType], Math.Min(DbRef.Level, MaxLevel - 1));

        internal bool HasMaxLevel => DbRef.Level >= MaxLevel;

        internal ulong? Achievement => DbRef.Level switch
        {
            // index by level: 2 -> 0, 5 -> 1, 10 -> 2, 12 -> 3
            2 or 5 or 10 or 12 => (ulong)achievements[artisanType][DbRef.Level / 4],
            _ => null,
        };

        internal ulong? Criteria => DbRef.Level == 10 ? (ulong)criteriaForLevel10[artisanType] : null;

        internal int AnimationTag => firstTransitionTag + 0x10 * (Math.Clamp(DbRef.Level, 1, MaxLevel) - 1);

        internal int IdleAnimationTag => firstIdleTag + 0x10 * (Math.Clamp(DbRef.Level, 1, MaxLevel) - 1);

        internal int Type => Array.IndexOf(canBeTrained, artisanType);
    }
}