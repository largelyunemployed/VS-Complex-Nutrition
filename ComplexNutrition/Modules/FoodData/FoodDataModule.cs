using System.Collections.Generic;
using System.Linq;
using ComplexNutrition.Core;
using Vintagestory.API.Common;

namespace ComplexNutrition.Modules.FoodData
{
    /// <summary>
    /// v0.1 module: makes the food data visible. Adds a nutrient block to every food's tooltip
    /// and, on the server, logs foods that fall back to a category template.
    /// Switching it off removes the tooltip; nothing else depends on it.
    /// </summary>
    public class FoodDataModule : INutritionModule
    {
        public string Id => "fooddata";
        public string[] Requires => new string[0];
        public bool EnabledByDefault => true;
        public bool IsEnabled { get; private set; }

        private readonly List<CollectibleObject> patched = new();

        public void Enable(ICoreAPI api, ComplexNutritionModSystem core)
        {
            IsEnabled = true;

            // Client only: tooltips are drawn on the client, and the server sends each item's
            // behaviours to clients by registered class name, which this one doesn't have.
            if (api.Side == EnumAppSide.Client)
            {
                foreach (var coll in api.World.Collectibles)
                {
                    if (coll?.Code == null) continue;
                    if (core.Foods.Resolve(coll) == null && !MealNutrition.IsMealContainer(coll)) continue;
                    if (coll is Vintagestory.GameContent.BlockPie) continue; // handled by PieTooltipPatch
                    if (coll.CollectibleBehaviors.Any(b => b is CollectibleBehaviorNutrientInfo)) continue;
                    var behavior = new CollectibleBehaviorNutrientInfo(coll, core);
                    coll.CollectibleBehaviors = coll.CollectibleBehaviors.Append(behavior).ToArray();
                    patched.Add(coll);
                }
                api.Logger.Notification("[complexnutrition] Nutrient info added to {0} foods and meal containers.", patched.Count);
            }

            PieTooltipPatch.Apply(core);

            if (api.Side == EnumAppSide.Server)
            {
                api.Logger.Notification("[complexnutrition] " + core.CoverageReport(api, verbose: core.Config.LogFallbacks));
            }
        }

        public void Disable(ICoreAPI api)
        {
            foreach (var coll in patched)
            {
                coll.CollectibleBehaviors = coll.CollectibleBehaviors.Where(b => b is not CollectibleBehaviorNutrientInfo).ToArray();
            }
            patched.Clear();
            PieTooltipPatch.Remove();
            IsEnabled = false;
        }
    }
}
