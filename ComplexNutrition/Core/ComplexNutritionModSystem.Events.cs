using System;
using System.Collections.Generic;
using Vintagestory.API.Server;

namespace ComplexNutrition.Core
{
    /// <summary>What the tracking module added to a player's reserves from one food.</summary>
    public class FoodAbsorbedInfo
    {
        public FoodEatenInfo Eaten;

        /// <summary>Nutrient code to the amount added to the reserve, in % of the daily target, after
        /// absorption multipliers and diminishing returns (before the reserve cap).</summary>
        public Dictionary<string, float> Absorbed = new();

        /// <summary>Nutrient code to the absorption multipliers that applied, by modifier id.</summary>
        public Dictionary<string, Dictionary<string, float>> Multipliers = new();
    }

    public partial class ComplexNutritionModSystem
    {
        /// <summary>Server: the tracking module added a food to a player's reserves.</summary>
        public event Action<FoodAbsorbedInfo> FoodAbsorbed;

        /// <summary>Server: a new in-game day started for a player. The ledger day passed is the one that just ended.</summary>
        public event Action<IServerPlayer, LedgerDay> DailyRollover;

        /// <summary>Server: a reserve changed (player, nutrient code, old %, new %). Fired for eating, draining and commands.</summary>
        public event Action<IServerPlayer, string, float, float> ReserveChanged;

        public void PublishFoodAbsorbed(FoodAbsorbedInfo info) => Safe(() => FoodAbsorbed?.Invoke(info));

        public void PublishDailyRollover(IServerPlayer player, LedgerDay day) => Safe(() => DailyRollover?.Invoke(player, day));

        public void PublishReserveChanged(IServerPlayer player, string nutrient, float oldValue, float newValue)
        {
            if (oldValue != newValue) Safe(() => ReserveChanged?.Invoke(player, nutrient, oldValue, newValue));
        }

        private void Safe(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                api.Logger.Error("[complexnutrition] Error in an event handler: {0}", e);
            }
        }
    }
}
