using ComplexNutrition.Core;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace ComplexNutrition.Modules.Tracking
{
    /// <summary>
    /// Client: while the server tracks this player, vanilla's own "When eaten: N fullness" lines, and the
    /// fullness of a meal's contents, show our fullness from weight instead of vanilla satiety, so every
    /// number the player sees agrees with what eating does. Only what the client displays changes: the
    /// server works out eating itself (see <see cref="EatingHooks"/>).
    /// </summary>
    public static class FullnessDisplayPatch
    {
        private const string HarmonyId = "complexnutrition.tracking.fullnessdisplay";
        private static Harmony harmony;
        private static ComplexNutritionModSystem core;

        public static void Apply(ComplexNutritionModSystem system)
        {
            core = system;
            if (harmony != null) return;
            harmony = new Harmony(HarmonyId);
            harmony.Patch(AccessTools.Method(typeof(CollectibleObject), nameof(CollectibleObject.GetNutritionProperties)),
                postfix: new HarmonyMethod(typeof(FullnessDisplayPatch), nameof(FoodPostfix)));
            harmony.Patch(AccessTools.Method(typeof(BlockLiquidContainerBase), nameof(BlockLiquidContainerBase.GetNutritionPropertiesPerLitre)),
                postfix: new HarmonyMethod(typeof(FullnessDisplayPatch), nameof(PerLitrePostfix)));
            harmony.Patch(AccessTools.Method(typeof(BlockMeal), nameof(BlockMeal.GetContentNutritionProperties),
                    new[] { typeof(IWorldAccessor), typeof(ItemSlot), typeof(ItemStack[]), typeof(EntityAgent), typeof(bool), typeof(float), typeof(float) }),
                postfix: new HarmonyMethod(typeof(FullnessDisplayPatch), nameof(MealPostfix)));
        }

        public static void Remove()
        {
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
            core = null;
        }

        private static bool Applies(IWorldAccessor world, Entity forEntity) =>
            core != null && world?.Side == EnumAppSide.Client && Digestion.IsActiveFor(forEntity);

        public static void FoodPostfix(CollectibleObject __instance, ref FoodNutritionProperties __result, IWorldAccessor world, Entity forEntity)
        {
            if (__result == null || !Applies(world, forEntity)) return;
            var food = core.Foods.Resolve(__instance);
            if (food == null || food.IsLiquid) return;
            __result = __result.Clone();
            __result.Satiety = Digestion.FoodFullness(Digestion.Settings, food, __instance);
        }

        public static void PerLitrePostfix(BlockLiquidContainerBase __instance, ref FoodNutritionProperties __result, IWorldAccessor world, ItemStack itemstack, Entity forEntity)
        {
            if (__result == null || !Applies(world, forEntity)) return;
            var content = __instance.GetContent(itemstack);
            var food = content == null ? null : core.Foods.Resolve(content.Collectible);
            if (food == null) return;
            __result = __result.Clone();
            __result.Satiety = Digestion.FoodFullness(Digestion.Settings, food, content.Collectible);
        }

        public static void MealPostfix(FoodNutritionProperties[] __result, IWorldAccessor world, ItemSlot inSlot, ItemStack[] contentStacks, EntityAgent forEntity)
        {
            if (__result == null || __result.Length == 0 || inSlot?.Itemstack == null || !Applies(world, forEntity)) return;
            float fullness = Digestion.MealFullness(core, world, inSlot, inSlot.Itemstack, contentStacks, forEntity, true);
            float sum = 0f;
            int count = 0;
            foreach (var p in __result)
            {
                if (p == null) continue;
                sum += p.Satiety;
                count++;
            }
            if (count == 0) return;
            foreach (var p in __result)
            {
                if (p != null) p.Satiety = sum > 0f ? p.Satiety / sum * fullness : fullness / count;
            }
        }
    }
}
