using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace ComplexNutrition.Core
{
    /// <summary>
    /// Works out what a player ate, server side, and hands it to <see cref="ComplexNutritionModSystem.FoodEaten"/>.
    ///
    /// Vanilla gives satiety through EntityBehaviorHunger.OnEntityReceiveSaturation, which knows the amount and
    /// category but not the food. So the three vanilla eating paths (plain food, drinks, meals and pies) are
    /// patched to remember what is being eaten, and the satiety received tells how much of it was eaten:
    /// spoiled food gives less satiety, and so fewer nutrients. Meals are counted from the servings the pot,
    /// bowl or pie slice actually lost. Satiety from anywhere else (another mod's eating code) is counted
    /// from its vanilla category's template, sized by the satiety, so it is never lost.
    ///
    /// Only patched while a module asks for it (see <see cref="ComplexNutritionModSystem.RequireEatingHooks"/>).
    /// </summary>
    public static class EatingHooks
    {
        private const string HarmonyId = "complexnutrition.core.eating";
        private static Harmony harmony;
        private static ComplexNutritionModSystem core;

        /// <summary>What is being eaten right now. The server eats on one thread, one player at a time.</summary>
        private class EatContext
        {
            public ItemStack Stack;
            public ResolvedFoodProfile Food;
            /// <summary>Satiety of one whole portion (one item, or one litre), before spoilage.</summary>
            public float FullSatiety;
            public bool Meal;
            public bool Counted;
        }

        [ThreadStatic] private static EatContext current;

        public static bool IsApplied => harmony != null;

        public static void Apply(ComplexNutritionModSystem system)
        {
            core = system;
            if (harmony != null) return;
            harmony = new Harmony(HarmonyId);

            harmony.Patch(AccessTools.Method(typeof(CollectibleObject), "tryEatStop"),
                prefix: new HarmonyMethod(typeof(EatingHooks), nameof(FoodPrefix)),
                finalizer: new HarmonyMethod(typeof(EatingHooks), nameof(ClearFinalizer)));

            harmony.Patch(AccessTools.Method(typeof(BlockLiquidContainerBase), "tryEatStop"),
                prefix: new HarmonyMethod(typeof(EatingHooks), nameof(DrinkPrefix)),
                finalizer: new HarmonyMethod(typeof(EatingHooks), nameof(ClearFinalizer)));

            harmony.Patch(AccessTools.Method(typeof(BlockMeal), nameof(BlockMeal.Consume)),
                prefix: new HarmonyMethod(typeof(EatingHooks), nameof(MealPrefix)),
                postfix: new HarmonyMethod(typeof(EatingHooks), nameof(MealPostfix)),
                finalizer: new HarmonyMethod(typeof(EatingHooks), nameof(ClearFinalizer)));

            harmony.Patch(AccessTools.Method(typeof(EntityBehaviorHunger), nameof(EntityBehaviorHunger.OnEntityReceiveSaturation)),
                prefix: new HarmonyMethod(typeof(EatingHooks), nameof(SaturationPrefix)));
        }

        public static void Remove()
        {
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
            core = null;
            current = null;
        }

        private static bool IsServerPlayer(Entity entity) => entity is EntityPlayer && entity.World?.Side == EnumAppSide.Server;

        public static void FoodPrefix(CollectibleObject __instance, ItemSlot slot, EntityAgent byEntity)
        {
            if (core == null || !IsServerPlayer(byEntity)) return;
            var stack = slot?.Itemstack;
            if (stack == null) return;
            var props = __instance.GetNutritionProperties(byEntity.World, stack, byEntity);
            if (props == null) return;
            current = new EatContext
            {
                Stack = stack.Clone(),
                Food = core.Foods.Resolve(stack.Collectible),
                FullSatiety = props.Satiety
            };
        }

        public static void DrinkPrefix(BlockLiquidContainerBase __instance, ItemSlot slot, EntityAgent byEntity)
        {
            if (core == null || !IsServerPlayer(byEntity)) return;
            var stack = slot?.Itemstack;
            if (stack == null || __instance.IsEmpty(stack)) return;   // an empty container eaten as food goes through FoodPrefix
            var content = __instance.GetContent(stack);
            var props = content == null ? null : BlockLiquidContainerBase.GetContainableProps(content);
            if (props == null) return;

            // Satiety of one litre before spoilage, so spoiled drinks count for less, as they do for satiety.
            float perLitre = props.NutritionPropsPerLitre?.Satiety
                ?? (content.Collectible.GetNutritionProperties(byEntity.World, content, null)?.Satiety ?? 0f) * props.ItemsPerLitre;
            current = new EatContext
            {
                Stack = content.Clone(),
                Food = core.Foods.Resolve(content.Collectible),
                FullSatiety = perLitre
            };
        }

        public static void MealPrefix(IWorldAccessor world, ItemSlot inSlot, out MealNutritionResult __state)
        {
            __state = null;
            if (core == null || world.Side != EnumAppSide.Server || inSlot?.Itemstack == null) return;
            __state = MealNutrition.Compute(core, world, inSlot.Itemstack);
            current = new EatContext { Stack = inSlot.Itemstack.Clone(), Meal = true };
        }

        public static void MealPostfix(BlockMeal __instance, float __result, IPlayer eatingPlayer, ItemSlot inSlot, float remainingServings, MealNutritionResult __state)
        {
            if (core == null || __state == null || eatingPlayer is not IServerPlayer player) return;
            float eaten = remainingServings - __result;
            if (eaten <= 0f) return;

            // Bowls and pots hold one serving's ingredients; a pie (or slice) holds what is left of the whole pie.
            float factor = __state.IsPie ? (remainingServings > 0f ? eaten / remainingServings : 1f) : eaten;
            if (__state.IsPie)
            {
                // Spoilage, the way the pie itself scales satiety (index 1 leaves out the pie's own nutritionMul).
                factor *= __instance.GetNutritionHealthMul(null, inSlot, player.Entity)[1];
            }

            var info = new FoodEatenInfo { Player = player, Source = current?.Stack ?? inSlot.Itemstack?.Clone() };
            foreach (var kv in __state.Totals) info.Nutrients[kv.Key] = kv.Value * factor;
            foreach (var kv in __state.KcalByCategory) info.KcalByCategory[kv.Key] = kv.Value * factor;
            info.Estimated = __state.UnknownIngredients > 0;
            core.PublishFoodEaten(info);
        }

        public static Exception ClearFinalizer(Exception __exception)
        {
            current = null;
            return __exception;
        }

        public static void SaturationPrefix(EntityBehaviorHunger __instance, float saturation, EnumFoodCategory foodCat)
        {
            if (core == null || saturation <= 0f) return;
            var entity = __instance.entity;
            if (!IsServerPlayer(entity)) return;
            if (current != null && current.Meal) return;   // counted from servings in MealPostfix
            if (entity.World.PlayerByUid(((EntityPlayer)entity).PlayerUID) is not IServerPlayer player) return;

            var ctx = current;
            ResolvedFoodProfile food;
            float portions;
            if (ctx != null && !ctx.Counted && ctx.Food != null && ctx.FullSatiety > 0f)
            {
                food = ctx.Food;
                portions = saturation / ctx.FullSatiety;
                ctx.Counted = true;
            }
            else
            {
                // Not one of the vanilla eating paths: estimate from the category, sized by the satiety given.
                food = core.Foods.FallbackFor(foodCat.ToString(), saturation, false);
                portions = 1f;
            }
            if (food == null) return;

            var info = new FoodEatenInfo { Player = player, Source = ctx?.Stack, Estimated = food.IsFallback };
            foreach (var n in core.Nutrients.All) info.Nutrients[n.Code] = food.PerPortion(n.Code) * portions;
            info.KcalByCategory[foodCat.ToString()] = food.PerPortion(NutrientCodes.Calories) * portions;
            core.PublishFoodEaten(info);
        }
    }
}
