using System;
using System.Linq;
using ComplexNutrition.Core;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace ComplexNutrition.Modules.FoodData
{
    /// <summary>
    /// Hide Poison Effects (v0.4.1), client only. While the world has both Discover Nutrition and Hide Poison Effects on,
    /// a food the player has never eaten shows no health loss: vanilla's "When eaten: 8 fullness, -8 hp" reads
    /// "When eaten: 8 fullness" until they have tried it once. The same goes for a meal holding an untried poisonous ingredient.
    ///
    /// Only what the client shows changes. Eating is worked out on the server, so the poison still hurts.
    /// </summary>
    public static class PoisonHidingPatch
    {
        private const string HarmonyId = "complexnutrition.fooddata.hidepoison";
        private static Harmony harmony;
        private static ComplexNutritionModSystem core;

        /// <summary>Patches vanilla's nutrition lookups for single foods and for meals.</summary>
        public static void Apply(ComplexNutritionModSystem system)
        {
            core = system;
            if (harmony != null) return;
            harmony = new Harmony(HarmonyId);
            harmony.Patch(AccessTools.Method(typeof(CollectibleObject), nameof(CollectibleObject.GetNutritionProperties)),
                postfix: new HarmonyMethod(typeof(PoisonHidingPatch), nameof(HideForFood)));
            harmony.Patch(AccessTools.Method(typeof(BlockMeal), nameof(BlockMeal.GetContentNutritionProperties),
                    new[] { typeof(IWorldAccessor), typeof(ItemSlot), typeof(ItemStack[]), typeof(EntityAgent), typeof(bool), typeof(float), typeof(float) }),
                postfix: new HarmonyMethod(typeof(PoisonHidingPatch), nameof(HideForMeal)));
        }

        public static void Remove()
        {
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
            core = null;
        }

        /// <summary>True when health loss should be hidden from this player: on the client, with the setting in force.</summary>
        private static bool Hiding(IWorldAccessor world, Entity player) =>
            core != null && world?.Side == EnumAppSide.Client && player != null && core.HidePoisonOn;

        /// <summary>Whether the player has eaten this food at least once (Discover Nutrition remembers every food eaten).</summary>
        private static bool HasEaten(Entity player, CollectibleObject food) =>
            food?.Code != null && Array.IndexOf(Discovery.Eaten(player), food.Code.ToString()) >= 0;

        /// <summary>A single food: an untried food's health loss is shown as none.</summary>
        public static void HideForFood(CollectibleObject __instance, ref FoodNutritionProperties __result, IWorldAccessor world, Entity forEntity)
        {
            if (__result == null || __result.Health >= 0f || !Hiding(world, forEntity) || HasEaten(forEntity, __instance)) return;
            // Vanilla hands out its shared copy, so change a clone.
            __result = __result.Clone();
            __result.Health = 0f;
        }

        /// <summary>
        /// A meal: if any ingredient the player hasn't tried takes health, no health loss shows for the meal, so the tooltip
        /// doesn't give away which ingredient it is.
        /// </summary>
        public static void HideForMeal(ref FoodNutritionProperties[] __result, IWorldAccessor world, ItemStack[] contentStacks, EntityAgent forEntity)
        {
            if (__result == null || contentStacks == null || !Hiding(world, forEntity)) return;
            bool untriedPoison = contentStacks.Any(stack =>
                stack?.Collectible != null
                && (stack.Collectible.NutritionProps?.Health ?? 0f) < 0f
                && !HasEaten(forEntity, stack.Collectible));
            if (!untriedPoison) return;
            __result = __result.Select(props =>
            {
                if (props == null || props.Health >= 0f) return props;
                var hidden = props.Clone();
                hidden.Health = 0f;
                return hidden;
            }).ToArray();
        }
    }
}
