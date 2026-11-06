using System;
using System.Collections.Generic;
using System.Linq;
using ComplexNutrition.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ComplexNutrition.Modules.FoodData
{
    /// <summary>
    /// v0.1 module: makes the food data visible. Adds a nutrient block to every food's tooltip
    /// and, on the server, logs foods that fall back to a category template. Its admin commands
    /// (/cnutrition info and meal) work whether it runs or not.
    /// Switching it off removes the tooltip; nothing else depends on it.
    /// </summary>
    public class FoodDataModule : INutritionModule
    {
        public string Id => ModuleIds.FoodData;
        public string[] Requires => new string[0];
        public bool EnabledByDefault => true;
        public bool IsEnabled { get; private set; }

        private readonly List<CollectibleObject> patched = new();
        private ComplexNutritionModSystem core;

        public void Start(ICoreAPI api, ComplexNutritionModSystem core)
        {
            this.core = core;
            if (api is ICoreServerAPI sapi) RegisterCommands(sapi);
        }

        public void Enable(ICoreAPI api, ComplexNutritionModSystem core)
        {
            this.core = core;
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
                PieTooltipPatch.Apply(core);
                NutritionFactsPatch.Apply(core);
                TooltipCtrlPatch.Apply((Vintagestory.API.Client.ICoreClientAPI)api, core);
            }

            if (api.Side == EnumAppSide.Server)
            {
                // Eating teaches foods for Discover Nutrition, so the eating hooks run even with tracking off.
                core.RequireEatingHooks();
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
            if (api.Side == EnumAppSide.Client)
            {
                PieTooltipPatch.Remove();
                NutritionFactsPatch.Remove();
                TooltipCtrlPatch.Remove();
            }
            IsEnabled = false;
        }

        public void OnSwitchedOff(ICoreAPI api) { }

        // ---- Commands ----

        private void RegisterCommands(ICoreServerAPI sapi)
        {
            sapi.ChatCommands.GetOrCreate("cnutrition")
                .BeginSubCommand("info")
                    .WithDescription("Show the nutrient block for an item or block code, as the tooltip shows it")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(sapi.ChatCommands.Parsers.Word("code"))
                    .HandleWith(args => InfoCommand(sapi, (string)args[0]))
                .EndSubCommand()
                .BeginSubCommand("meal")
                    .WithDescription("Work out a dish: /cnutrition meal <container code> <servings> <ingredient code>... (use code*N for N of one ingredient)")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(sapi.ChatCommands.Parsers.Word("container"), sapi.ChatCommands.Parsers.Float("servings"), sapi.ChatCommands.Parsers.All("ingredients"))
                    .HandleWith(args => MealCommand(sapi, (string)args[0], (float)args[1], (string)args[2]))
                .EndSubCommand();
        }

        private TextCommandResult InfoCommand(ICoreServerAPI sapi, string code)
        {
            var loc = new AssetLocation(code);
            CollectibleObject coll = (CollectibleObject)sapi.World.GetItem(loc) ?? sapi.World.GetBlock(loc);
            if (coll == null || coll.Id == 0) return TextCommandResult.Error("No item or block called " + loc);
            var resolved = core.Foods.Resolve(coll);
            if (resolved == null) return TextCommandResult.Success(loc + " has no nutrient data (not a food, or a meal made from ingredients).");
            // The tooltip behaviour lives on the client only, so build the same block directly.
            return TextCommandResult.Success($"{loc}:" + CollectibleBehaviorNutrientInfo.BuildTooltip(core.Nutrients, resolved, core.ShowPercent, true, coll));
        }

        /// <summary>
        /// Builds a dish the way the cooking pot stores it (one serving's ingredients plus a serving count)
        /// and shows its nutrient block. For pies, the ingredients are the whole pie's and servings is the slice count.
        /// </summary>
        private TextCommandResult MealCommand(ICoreServerAPI sapi, string containerCode, float servings, string ingredientList)
        {
            var block = sapi.World.GetBlock(new AssetLocation(containerCode));
            if (block == null || block.Id == 0 || !MealNutrition.IsMealContainer(block))
                return TextCommandResult.Error(containerCode + " is not a meal container (try game:bowl-blue-meal, game:claypot-blue-cooked or game:pie-perfect).");

            var stacks = new List<ItemStack>();
            foreach (var token in (ingredientList ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = token.Split('*');
                int count = parts.Length > 1 && int.TryParse(parts[1], out var c) ? c : 1;
                var loc = new AssetLocation(parts[0]);
                CollectibleObject coll = (CollectibleObject)sapi.World.GetItem(loc) ?? sapi.World.GetBlock(loc);
                if (coll == null || coll.Id == 0) return TextCommandResult.Error("Unknown ingredient " + loc);
                stacks.Add(new ItemStack(coll, count));
            }
            if (stacks.Count == 0) return TextCommandResult.Error("Give at least one ingredient.");

            var dish = new ItemStack(block);
            var container = (Vintagestory.GameContent.IBlockMealContainer)block;
            if (block is Vintagestory.GameContent.BlockPie)
            {
                container.SetContents(null, dish, stacks.ToArray());
                dish.Attributes.SetInt("pieSize", (int)Math.Clamp(servings, 1, 4));
            }
            else
            {
                // Any recipe code marks the dish as cooked; the stew recipe is used for its name only.
                container.SetContents("meatystew", dish, stacks.ToArray(), servings);
            }

            var meal = MealNutrition.Compute(core, sapi.World, dish)?.AllServings();
            if (meal == null) return TextCommandResult.Error("Could not work out that dish.");
            return TextCommandResult.Success($"{containerCode} with {ingredientList}:" + CollectibleBehaviorNutrientInfo.BuildMealTooltip(core.Nutrients, meal, core.ShowPercent, false));
        }
    }
}
