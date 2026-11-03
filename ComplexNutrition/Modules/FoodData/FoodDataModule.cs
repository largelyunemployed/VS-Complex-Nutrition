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

            // Attached on both sides so server and client agree on each item's behaviours;
            // the tooltip itself only renders on the client.
            {
                foreach (var coll in api.World.Collectibles)
                {
                    if (coll?.Code == null || core.Foods.Resolve(coll) == null) continue;
                    if (coll.CollectibleBehaviors.Any(b => b is CollectibleBehaviorNutrientInfo)) continue;
                    var behavior = new CollectibleBehaviorNutrientInfo(coll, core);
                    coll.CollectibleBehaviors = coll.CollectibleBehaviors.Append(behavior).ToArray();
                    patched.Add(coll);
                }
                api.Logger.Notification("[complexnutrition] Nutrient info added to {0} foods.", patched.Count);
            }

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
            IsEnabled = false;
        }
    }
}
