using System;
using Newtonsoft.Json;
using Vintagestory.API.Common;

namespace ComplexNutrition.Core
{
    /// <summary>Tracking rules from config/tracking.json. Per-nutrient numbers (cap, start, drain, sitting limit) live in config/nutrients.json.</summary>
    public class TrackingSettings
    {
        public const string AssetPath = "config/tracking.json";

        /// <summary>Food eaten within this many in-game hours of the previous food counts as the same sitting.</summary>
        [JsonProperty("sittingHours")] public float SittingHours = 1f;

        /// <summary>
        /// How fast absorption falls above a nutrient's sitting limit. The extra x (in % of a day) adds
        /// scale x ln(1 + x / scale), so with 100 the first extra day's worth gives about 69%.
        /// </summary>
        [JsonProperty("diminishingScale")] public float DiminishingScale = 100f;

        /// <summary>In-game days the diet ledger keeps.</summary>
        [JsonProperty("ledgerDays")] public int LedgerDays = 28;

        /// <summary>Real seconds between reserve updates.</summary>
        [JsonProperty("updateSeconds")] public float UpdateSeconds = 3f;

        /// <summary>Reserves drain once at least this many in-game hours have passed since the last drain.</summary>
        [JsonProperty("minDrainHours")] public float MinDrainHours = 0.1f;

        /// <summary>The nutrition window lists the calorie reserve as running low below this many days.</summary>
        [JsonProperty("lowCaloriesDays")] public float LowCaloriesDays = 1f;

        /// <summary>The nutrition window lists a nutrient of a <see cref="LowGroups"/> group as running low below this many days.</summary>
        [JsonProperty("lowDays")] public float LowDays = 2f;

        /// <summary>
        /// Nutrient groups that can run low. Carbs, fats, protein, fiber, vitamin C and the B vitamins refresh within a day or two,
        /// so they get no warning.
        /// </summary>
        [JsonProperty("lowGroups")] public string[] LowGroups = { "FatSolubleVitamin", "Mineral" };

        /// <summary>Real seconds between checks of what each player is holding, for the foods they know.</summary>
        [JsonProperty("heldScanSeconds")] public float HeldScanSeconds = 5f;

        /// <summary>
        /// The calorie cost of healing (v0.3.3). A recovering body needs <see cref="RecoveryKcalPerKgMin"/> to
        /// <see cref="RecoveryKcalPerKgMax"/> kcal per kg a day (more the worse the injury), against <see cref="RestingKcalPerKg"/>
        /// when well. The difference, for a body of <see cref="BodyKg"/>, spread over the health vanilla regenerates in a day
        /// (<see cref="RegenHpPerDay"/>), is the cost of each health point healed naturally. Healing items cost nothing.
        /// </summary>
        [JsonProperty("bodyKg")] public float BodyKg = 80f;
        [JsonProperty("restingKcalPerKg")] public float RestingKcalPerKg = 25f;
        [JsonProperty("recoveryKcalPerKgMin")] public float RecoveryKcalPerKgMin = 33f;
        [JsonProperty("recoveryKcalPerKgMax")] public float RecoveryKcalPerKgMax = 44f;

        /// <summary>Health vanilla regenerates in an in-game day at the default regen speed (0.000333 a game second).</summary>
        [JsonProperty("regenHpPerDay")] public float RegenHpPerDay = 28.8f;

        /// <summary>
        /// Vitamin K used up by healing (since v0.4.1), in % of a day's need for each health point healed naturally. With 10, a
        /// full heal of 15 health uses a day and a half of vitamin K. Healing items use none. An empty store never stops
        /// healing; it only slows it (vitamin K's effect, config/effects.json).
        /// </summary>
        [JsonProperty("vitaminKPerHp")] public float VitaminKPerHp = 50f;

        /// <summary>
        /// Respawn effects: Calories/Diet Tier, "Lose calories on death" (since v0.4.2): the share of the calorie reserve a character keeps
        /// when they die. With 0.1, a reserve of 8,000kcal at death respawns with 800kcal (then raised to the floor below).
        /// </summary>
        [JsonProperty("respawnKeptShare")] public float RespawnKeptShare = 0.1f;

        /// <summary>
        /// The least a character respawns with under "Lose calories on death", in kcal, so a player who starved to death
        /// doesn't come back starving. It never goes over the reserve's size.
        /// </summary>
        [JsonProperty("respawnMinKcal")] public float RespawnMinKcal = 1000f;

        /// <summary>
        /// What "Reset calorie progress on death" leaves in the calorie reserve, in kcal. On a world with a Rock Bottom start it
        /// leaves Rock Bottom's share instead, almost nothing.
        /// </summary>
        [JsonProperty("respawnResetKcal")] public float RespawnResetKcal = 1000f;

        /// <summary>
        /// The calorie reserve a character respawns with, in % of a day, for a Respawn effects: Calories/Diet Tier choice (see
        /// <see cref="WorldSettings.RespawnCaloriesOptions"/>). <paramref name="atDeath"/> is what the reserve held at death and
        /// <paramref name="reserveSize"/> how much it holds, both in % of a day; <paramref name="rockBottomStart"/> is whether
        /// the world starts characters at Rock Bottom. Keep returns <paramref name="atDeath"/> unchanged, even above the size.
        /// </summary>
        public float CaloriesOnRespawn(string choice, float atDeath, float reserveSize, NutrientDefinition calories, bool rockBottomStart)
        {
            switch (choice)
            {
                case WorldSettings.RespawnKeep:
                    return atDeath;
                case WorldSettings.RespawnReset:
                    float reset = rockBottomStart
                        ? reserveSize * WorldSettings.StoreShare(WorldSettings.RockBottomValue)
                        : calories.ToPercent(RespawnResetKcal);
                    return Math.Min(reset, reserveSize);
                default:
                    float kept = Math.Max(atDeath * RespawnKeptShare, calories.ToPercent(RespawnMinKcal));
                    return Math.Min(kept, reserveSize);
            }
        }

        /// <summary>Extra calories a day while healing, with <paramref name="missing"/> (0 to 1) of max health missing.</summary>
        public float HealingKcalPerDay(float missing)
        {
            float perKg = RecoveryKcalPerKgMin + (RecoveryKcalPerKgMax - RecoveryKcalPerKgMin) * Math.Clamp(missing, 0f, 1f);
            return Math.Max(0f, perKg - RestingKcalPerKg) * BodyKg;
        }

        /// <summary>Calories for one health point healed naturally, with <paramref name="missing"/> (0 to 1) of max health missing.</summary>
        public float KcalPerHp(float missing) => RegenHpPerDay > 0 ? HealingKcalPerDay(missing) / RegenHpPerDay : 0f;

        /// <summary>About how many calories healing naturally from <paramref name="health"/> back to <paramref name="max"/> costs.</summary>
        public float KcalToHeal(float health, float max)
        {
            if (max <= 0 || health >= max) return 0f;
            float kcal = 0f;
            const int steps = 50;
            float step = (max - health) / steps;
            for (int i = 0; i < steps; i++)
            {
                float mid = health + step * (i + 0.5f);
                kcal += KcalPerHp((max - mid) / max) * step;
            }
            return kcal;
        }

        public static TrackingSettings Load(ICoreAPI api) => ConfigAsset.Load<TrackingSettings>(api, AssetPath);
    }
}
