using System;
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

        /// <summary>
        /// Every food the player has held, as item codes, in WatchedAttributes under this key (outside our tree, so it is
        /// only sent when something new is added). The nutrition window suggests only foods from it.
        /// </summary>
        public const string HeldKey = "complexnutritionHeld";

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

        // Since v0.3.1 a gut pool's contents are its reserve (grams in % of the daily target), like every other nutrient.

        /// <summary>
        /// Saves from v0.2.2 to v0.3.0 also kept each gut pool as a list of batches (grams and rates). Returns the grams
        /// they held and removes them, or null when there are none (already moved, or a new player).
        /// </summary>
        public float? TakeOldGutBatches(string pool)
        {
            var grams = (Gut[pool + "-g"] as FloatArrayAttribute)?.value;
            bool had = Gut.HasAttribute(pool + "-g") || Gut.HasAttribute(pool + "-r");
            if (!had) return null;
            Gut.RemoveAttribute(pool + "-g");
            Gut.RemoveAttribute(pool + "-r");
            float sum = 0f;
            foreach (var g in grams ?? new float[0]) sum += g;
            return sum;
        }

        /// <summary>Grams of fiber in the gut (since v0.3.5): digested at the carbs pool's rate into fiberKcalPerGram calories each.</summary>
        public float FiberGut
        {
            get => Gut.GetFloat("fiber-g");
            set => Gut.SetFloat("fiber-g", value);
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

        // ---- Effects (v0.4.0) ----

        private ITreeAttribute Effects => Sub("effects");

        /// <summary>A timed nutrient's counter: in-game hours of tracked play since its store last held a full day.</summary>
        public float GetHoursWithoutFullDay(string nutrient) => Effects.GetFloat("without-" + nutrient);

        public void SetHoursWithoutFullDay(string nutrient, float hours) => Effects.SetFloat("without-" + nutrient, hours);

        /// <summary>Vitamin D made by sunlight on the in-game day <see cref="SunDay"/>, in % of a day's need.</summary>
        public float SunMadeToday
        {
            get => Effects.GetFloat("sunMade");
            set => Effects.SetFloat("sunMade", value);
        }

        /// <summary>The in-game day <see cref="SunMadeToday"/> belongs to.</summary>
        public int SunDay
        {
            get => Effects.GetInt("sunDay", int.MinValue);
            set => Effects.SetInt("sunDay", value);
        }

        // ---- Diet score and tier (scaffolding since v0.4.1, used since v0.4.3) ----

        /// <summary>
        /// The diet score, the diet tier and its counter, and today's record for the score. Respawning leaves this part alone
        /// (only the "Reset calorie progress on death" and "Lose one diet tier on death" respawn choices change it).
        /// </summary>
        private ITreeAttribute Diet => Sub("diet");

        /// <summary>Today's digested carbs, fats, protein and fiber, for the diet score, in % of a day's need.</summary>
        private ITreeAttribute DigestedToday => Diet.GetOrAddTreeAttribute("digested");

        /// <summary>Each weighted nutrient's share (0 to 1) of a day's need on the last scored day.</summary>
        private ITreeAttribute LastDayShares => Diet.GetOrAddTreeAttribute("lastShares");

        /// <summary>The lowest diet tier (Starving since v0.4.3).</summary>
        public const int FirstDietTier = 0;

        /// <summary>True once the character has a diet score (every character tracked since v0.4.3).</summary>
        public bool HasDietScore => Diet.HasAttribute("score");

        /// <summary>The diet score, 0 to 100: how well the player has eaten lately. It slowly follows each day's score.</summary>
        public float DietScoreValue
        {
            get => Diet.GetFloat("score");
            set => Diet.SetFloat("score", Math.Clamp(value, 0f, 100f));
        }

        /// <summary>The diet score before the last scored day, so the window can say whether it is rising or falling.</summary>
        public float DietScoreBefore
        {
            get => Diet.GetFloat("scoreBefore", DietScoreValue);
            set => Diet.SetFloat("scoreBefore", Math.Clamp(value, 0f, 100f));
        }

        /// <summary>The last scored day's score, 0 to 100, or -1 before any day has been scored.</summary>
        public float LastDayScore
        {
            get => Diet.GetFloat("lastDay", -1f);
            set => Diet.SetFloat("lastDay", value);
        }

        /// <summary>
        /// The player's diet tier, from <see cref="FirstDietTier"/> (Starving) up. Its effects apply, and from v0.4.4 it sets the
        /// calorie reserve's size (<see cref="NutrientDefinition.CapFor"/>).
        /// </summary>
        public int DietTier
        {
            get => Math.Max(FirstDietTier, Diet.GetInt("tier", FirstDietTier));
            set => Diet.SetInt("tier", Math.Max(FirstDietTier, value));
        }

        /// <summary>The tier the diet score points to, when it differs from <see cref="DietTier"/>.</summary>
        public int PendingDietTier
        {
            get => Diet.GetInt("pendingTier", DietTier);
            set => Diet.SetInt("pendingTier", value);
        }

        /// <summary>Days in a row the diet score has pointed above (or below) <see cref="DietTier"/>. Saved as a float before v0.4.3.</summary>
        public int DaysAtPendingTier
        {
            get => Diet["pendingDays"] is FloatAttribute old ? (int)Math.Round(old.value) : Diet.GetInt("pendingDays");
            set => Diet.SetInt("pendingDays", Math.Max(0, value));
        }

        /// <summary>The tier and where it is heading, as one value for <see cref="DietScore.NextTier"/>.</summary>
        public DietTierProgress DietProgress
        {
            get => new(DietTier, PendingDietTier, DaysAtPendingTier);
            set
            {
                DietTier = value.Tier;
                PendingDietTier = value.PendingTier;
                DaysAtPendingTier = value.PendingDays;
            }
        }

        /// <summary>Puts the player on a tier straight away, with this diet score and nothing pending (start, respawn and test commands).</summary>
        public void SetDiet(float score, int tier)
        {
            DietScoreValue = score;
            DietScoreBefore = score;
            DietProgress = new DietTierProgress(tier, tier, 0);
        }

        /// <summary>True once the player's body has run (alive, in survival) at some point today, so today counts towards the diet score.</summary>
        public bool PlayedToday
        {
            get => Diet.GetBool("played");
            set => Diet.SetBool("played", value);
        }

        /// <summary>Today's digested amount of a macro or fiber, for the diet score, in % of a day's need.</summary>
        public float GetDigestedToday(string nutrient) => DigestedToday.GetFloat(nutrient);

        public void AddDigestedToday(string nutrient, float percent) => DigestedToday.SetFloat(nutrient, DigestedToday.GetFloat(nutrient) + percent);

        /// <summary>A nutrient's share (0 to 1) of a day's need on the last scored day.</summary>
        public float GetLastDayShare(string nutrient) => LastDayShares.GetFloat(nutrient);

        public void SetLastDayShare(string nutrient, float share) => LastDayShares.SetFloat(nutrient, share);

        /// <summary>Starts today's diet record again (at midnight): nothing digested, not played yet.</summary>
        public void ClearDietToday()
        {
            Diet["digested"] = new TreeAttribute();
            PlayedToday = false;
        }

        public void MarkDirty() => entity?.WatchedAttributes.MarkPathDirty(TreeKey);
    }
}
