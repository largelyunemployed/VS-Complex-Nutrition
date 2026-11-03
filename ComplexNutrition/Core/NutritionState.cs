using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace ComplexNutrition.Core
{
    /// <summary>
    /// A player's nutrient reserves, kept in the entity's WatchedAttributes under "complexnutrition",
    /// so they save with the player and sync to clients. Every amount is in % of the nutrient's daily
    /// target (100 = one day's worth). The tracking module writes it; anything may read it, on either side.
    /// </summary>
    public class NutritionState
    {
        public const string TreeKey = "complexnutrition";

        private readonly Entity entity;
        private readonly ITreeAttribute tree;

        private NutritionState(Entity entity, ITreeAttribute tree)
        {
            this.entity = entity;
            this.tree = tree;
        }

        /// <summary>The state of this entity, or null if it has never been tracked.</summary>
        public static NutritionState Of(Entity entity)
        {
            var tree = entity?.WatchedAttributes.GetTreeAttribute(TreeKey);
            return tree == null ? null : new NutritionState(entity, tree);
        }

        /// <summary>The state of this entity, creating it with every nutrient at its start reserve if missing.</summary>
        public static NutritionState GetOrCreate(Entity entity, NutrientRegistry nutrients, double nowHours, out bool created)
        {
            var tree = entity.WatchedAttributes.GetTreeAttribute(TreeKey);
            created = tree == null;
            if (created)
            {
                tree = new TreeAttribute();
                entity.WatchedAttributes.SetAttribute(TreeKey, tree);
            }
            var state = new NutritionState(entity, tree);
            foreach (var n in nutrients.All)
            {
                if (!state.Reserves.HasAttribute(n.Code)) state.Reserves.SetFloat(n.Code, n.StartReserve);
            }
            if (created) state.LastUpdateHours = nowHours;
            return state;
        }

        /// <summary>True while the server runs tracking for this player. Clients show our bars only then.</summary>
        public bool TrackingOn
        {
            get => tree.GetBool("tracking");
            set => tree.SetBool("tracking", value);
        }

        private ITreeAttribute Sub(string key)
        {
            return tree.GetOrAddTreeAttribute(key);
        }

        private ITreeAttribute Reserves => Sub("reserves");
        private ITreeAttribute Today => Sub("today");
        private ITreeAttribute Sitting => Sub("sitting");

        /// <summary>Stored amount, in % of the daily target.</summary>
        public float GetReserve(string nutrient) => Reserves.GetFloat(nutrient);

        public void SetReserve(string nutrient, float percent) => Reserves.SetFloat(nutrient, percent);

        /// <summary>Absorbed since the start of the current in-game day, in % of the daily target.</summary>
        public float GetAbsorbedToday(string nutrient) => Today.GetFloat(nutrient);

        public void AddAbsorbedToday(string nutrient, float percent) => Today.SetFloat(nutrient, Today.GetFloat(nutrient) + percent);

        public void ClearToday() => tree["today"] = new TreeAttribute();

        /// <summary>Eaten in the current sitting, before diminishing returns, in % of the daily target.</summary>
        public float GetSitting(string nutrient) => Sitting.GetFloat(nutrient);

        public void SetSitting(string nutrient, float percent) => Sitting.SetFloat(nutrient, percent);

        public void ClearSitting() => tree["sitting"] = new TreeAttribute();

        /// <summary>Game time (total hours) of the last thing eaten.</summary>
        public double LastMealHours
        {
            get => tree.GetDouble("lastMealHours", double.MinValue);
            set => tree.SetDouble("lastMealHours", value);
        }

        /// <summary>Game time (total hours) the reserves were last drained to.</summary>
        public double LastUpdateHours
        {
            get => tree.GetDouble("lastUpdateHours");
            set => tree.SetDouble("lastUpdateHours", value);
        }

        /// <summary>The in-game day (whole TotalDays) that <see cref="GetAbsorbedToday"/> belongs to.</summary>
        public int Day
        {
            get => tree.GetInt("day", int.MinValue);
            set => tree.SetInt("day", value);
        }

        public void MarkDirty() => entity.WatchedAttributes.MarkPathDirty(TreeKey);
    }
}
