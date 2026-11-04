using System;
using Vintagestory.API.Common;

namespace ComplexNutrition.Core
{
    /// <summary>
    /// Loads one of the mod's JSON config assets. A missing or broken file (a typo, or a bad patch from another mod)
    /// is logged and the defaults in code are used, so the world still loads.
    /// </summary>
    public static class ConfigAsset
    {
        public static T Load<T>(ICoreAPI api, string path) where T : class, new()
        {
            var asset = api.Assets.TryGet(new AssetLocation(ComplexNutritionModSystem.ModId, path));
            if (asset == null)
            {
                api.Logger.Warning("[complexnutrition] Missing {0}; using the built-in defaults.", path);
                return new T();
            }
            try
            {
                return asset.ToObject<T>() ?? new T();
            }
            catch (Exception e)
            {
                api.Logger.Error("[complexnutrition] Could not read {0}, using the built-in defaults: {1}", path, e.Message);
                return new T();
            }
        }
    }
}
