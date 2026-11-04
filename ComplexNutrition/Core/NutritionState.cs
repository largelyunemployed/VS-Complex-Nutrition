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

        /// <summary>A read-only view of a state tree that belongs to no entity (UI previews). <see cref="MarkDirty"/> does nothing on it.</summary>
        public static NutritionState FromTree(ITreeAttribute tree) => tree == null ? null : new NutritionState(null, tree);

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

        /// <summary>The player's own nutrition display choice: 0 = follow the world setting, 1 = advanced, 2 = simple.</summary>
        public int DisplayChoice
        {
            get => tree.GetInt("display");
            set => tree.SetInt("display", value);
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

        // ---- Digestion (v0.2.2) ----

        private ITreeAttribute Gut => Sub("gut");
        private ITreeAttribute AbsorbedLog => Sub("absorbed");

        /// <summary>A gut pool's batches: grams left in each, and each one's absorption rate in g per in-game hour.</summary>
        public void GetBatches(string pool, out float[] grams, out float[] rates)
        {
            grams = (Gut[pool + "-g"] as FloatArrayAttribute)?.value ?? new float[0];
            rates = (Gut[pool + "-r"] as FloatArrayAttribute)?.value ?? new float[0];
            if (rates.Length != grams.Length)
            {
                grams = new float[0];
                rates = new float[0];
            }
        }

        public void SetBatches(string pool, float[] grams, float[] rates)
        {
            Gut[pool + "-g"] = new FloatArrayAttribute(grams);
            Gut[pool + "-r"] = new FloatArrayAttribute(rates);
        }

        /// <summary>In-game hours until a gut pool is empty, as of the last digestion step.</summary>
        public float GetGutHours(string pool) => Gut.GetFloat(pool + "-h");

        public void SetGutHours(string pool, float hours) => Gut.SetFloat(pool + "-h", hours);

        /// <summary>
        /// Grams of a macro absorbed per in-game hour over the last 24 hours, in a ring indexed by the whole
        /// game hour mod 24. <see cref="AbsorbedHour"/> is the last hour written; older slots are cleared on the way.
        /// </summary>
        public float[] GetAbsorbedLog(string pool)
        {
            var v = (AbsorbedLog[pool] as FloatArrayAttribute)?.value;
            return v != null && v.Length == 24 ? v : new float[24];
        }

        public void SetAbsorbedLog(string pool, float[] hours) => AbsorbedLog[pool] = new FloatArrayAttribute(hours);

        public long AbsorbedHour
        {
            get => AbsorbedLog.GetLong("hour", long.MinValue);
            set => AbsorbedLog.SetLong("hour", value);
        }

        /// <summary>Protein absorbed over the last 24 in-game hours, in g.</summary>
        public float Protein24
        {
            get => tree.GetFloat("protein24");
            set => tree.SetFloat("protein24", value);
        }

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

        public void MarkDirty() => entity?.WatchedAttributes.MarkPathDirty(TreeKey);
    }
}
