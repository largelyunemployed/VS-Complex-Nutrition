using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ComplexNutrition.Modules.FoodData;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ComplexNutrition.Core
{
    /// <summary>
    /// The core: owns the config, the nutrient registry and the food database,
    /// and starts the enabled feature modules. It is the only part that touches vanilla code.
    /// </summary>
    public class ComplexNutritionModSystem : ModSystem
    {
        public const string ModId = "complexnutrition";

        public ComplexNutritionConfig Config { get; private set; } = new();
        public NutrientRegistry Nutrients { get; } = new();
        public FoodDatabase Foods { get; } = new();
        public CookingRules Cooking { get; private set; } = new();

        private readonly List<INutritionModule> modules = new();
        private ICoreAPI api;

        public IReadOnlyList<INutritionModule> Modules => modules;

        public override void StartPre(ICoreAPI api)
        {
            this.api = api;
            modules.Clear();
            modules.Add(new FoodDataModule());
            LoadConfig(api);
        }

        public override void StartServerSide(ICoreServerAPI sapi)
        {
            sapi.ChatCommands.Create("cnutrition")
                .WithDescription("Complex Nutrition tools")
                .RequiresPrivilege(Privilege.controlserver)
                .BeginSubCommand("coverage")
                    .WithDescription("List vanilla foods that have no profile of their own")
                    .HandleWith(_ => TextCommandResult.Success(CoverageReport(sapi, verbose: true)))
                .EndSubCommand()
                .BeginSubCommand("info")
                    .WithDescription("Show the nutrient block for an item or block code, as the tooltip shows it")
                    .WithArgs(sapi.ChatCommands.Parsers.Word("code"))
                    .HandleWith(args => InfoCommand(sapi, (string)args[0]))
                .EndSubCommand()
                .BeginSubCommand("meal")
                    .WithDescription("Work out a dish: /cnutrition meal <container code> <servings> <ingredient code>... (use code*N for N of one ingredient)")
                    .WithArgs(sapi.ChatCommands.Parsers.Word("container"), sapi.ChatCommands.Parsers.Float("servings"), sapi.ChatCommands.Parsers.All("ingredients"))
                    .HandleWith(args => MealCommand(sapi, (string)args[0], (float)args[1], (string)args[2]))
                .EndSubCommand()
                .BeginSubCommand("dump")
                    .WithDescription("Write every food item, its vanilla values and its profile to ModData/complexnutrition/foods.json")
                    .HandleWith(_ => TextCommandResult.Success(DumpFoods(sapi)))
                .EndSubCommand();
        }

        public override void AssetsFinalize(ICoreAPI api)
        {
            Nutrients.Load(api);
            Foods.Load(api, Nutrients);
            Cooking = CookingRules.Load(api);
            api.Logger.Notification("[complexnutrition] Loaded {0} nutrients and {1} food profiles.", Nutrients.All.Count, Foods.ProfileCount);

            foreach (var module in OrderByDependencies(modules))
            {
                bool wanted = Config.IsModuleEnabled(module.Id, module.EnabledByDefault);
                var missing = module.Requires.Where(r => !modules.Any(m => m.Id == r && m.IsEnabled)).ToArray();
                if (!wanted) continue;
                if (missing.Length > 0)
                {
                    api.Logger.Notification("[complexnutrition] Module {0} stays off: needs {1}.", module.Id, string.Join(", ", missing));
                    continue;
                }
                module.Enable(api, this);
                api.Logger.Notification("[complexnutrition] Module {0} enabled.", module.Id);
            }
        }

        public override void Dispose()
        {
            if (api == null) return;
            foreach (var module in modules.Where(m => m.IsEnabled).Reverse()) module.Disable(api);
        }

        private void LoadConfig(ICoreAPI api)
        {
            try
            {
                Config = api.LoadModConfig<ComplexNutritionConfig>(ComplexNutritionConfig.FileName);
            }
            catch (Exception e)
            {
                api.Logger.Error("[complexnutrition] Could not read {0}, using defaults: {1}", ComplexNutritionConfig.FileName, e.Message);
                Config = null;
            }

            bool write = Config == null;
            Config ??= new ComplexNutritionConfig();
            foreach (var m in modules)
            {
                if (!Config.Modules.ContainsKey(m.Id))
                {
                    Config.Modules[m.Id] = m.EnabledByDefault;
                    write = true;
                }
            }
            if (write) api.StoreModConfig(Config, ComplexNutritionConfig.FileName);
        }

        private static IEnumerable<INutritionModule> OrderByDependencies(List<INutritionModule> all)
        {
            var done = new HashSet<string>();
            var pending = new List<INutritionModule>(all);
            while (pending.Count > 0)
            {
                var ready = pending.FirstOrDefault(m => m.Requires.All(r => done.Contains(r) || all.All(o => o.Id != r)))
                            ?? pending[0];
                pending.Remove(ready);
                done.Add(ready.Id);
                yield return ready;
            }
        }

        /// <summary>Summary of how vanilla foods are covered: own profile, fallback, or composite.</summary>
        public string CoverageReport(ICoreAPI api, bool verbose)
        {
            int own = 0, fallback = 0, composite = 0, none = 0;
            var lines = new List<string>();
            foreach (var (coll, vanilla, resolved) in Foods.Survey(api.World))
            {
                if (FoodDatabase.IsComposite(coll)) { composite++; continue; }
                if (resolved == null) { none++; lines.Add($"  no data: {coll.Code} ({vanilla.Category}, satiety {vanilla.Satiety})"); }
                else if (resolved.IsFallback) { fallback++; lines.Add($"  fallback: {coll.Code} ({vanilla.Category}, satiety {vanilla.Satiety})"); }
                else own++;
            }
            string head = $"Food coverage: {own} with own profile, {fallback} on category fallback, {none} without data, {composite} composite (computed from ingredients).";
            return verbose && lines.Count > 0 ? head + "\n" + string.Join("\n", lines) : head;
        }

        private TextCommandResult InfoCommand(ICoreServerAPI sapi, string code)
        {
            var loc = new AssetLocation(code);
            CollectibleObject coll = (CollectibleObject)sapi.World.GetItem(loc) ?? sapi.World.GetBlock(loc);
            if (coll == null || coll.Id == 0) return TextCommandResult.Error("No item or block called " + loc);
            var resolved = Foods.Resolve(coll);
            if (resolved == null) return TextCommandResult.Success(loc + " has no nutrient data (not a food, or a meal made from ingredients).");
            // Run the item's real tooltip pipeline, so this also checks that our behaviour is attached.
            var sb = new System.Text.StringBuilder();
            coll.GetHeldItemInfo(new DummySlot(new ItemStack(coll)), sb, sapi.World, true);
            string text = sb.ToString();
            bool attached = coll.CollectibleBehaviors.Any(b => b is CollectibleBehaviorNutrientInfo);
            return TextCommandResult.Success($"{loc} (nutrient behaviour attached: {attached}) full tooltip:\n{text}");
        }

        /// <summary>
        /// Builds a dish the way the cooking pot stores it (one serving's ingredients plus a serving count)
        /// and shows its full tooltip. For pies, the ingredients are the whole pie's and servings is the slice count.
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

            var sb = new System.Text.StringBuilder();
            dish.Collectible.GetHeldItemInfo(new DummySlot(dish), sb, sapi.World, false);
            return TextCommandResult.Success($"{containerCode} with {ingredientList}:\n{sb}");
        }

        private string DumpFoods(ICoreServerAPI sapi)
        {
            var rows = Foods.Survey(sapi.World)
                .OrderBy(t => t.coll.Code.ToString())
                .Select(t => new
                {
                    code = t.coll.Code.ToString(),
                    type = t.coll.ItemClass.ToString(),
                    cls = t.coll.Class,
                    name = t.coll.GetHeldItemName(new ItemStack(t.coll)),
                    satiety = t.vanilla.Satiety,
                    category = t.vanilla.Category,
                    kind = t.vanilla.Kind,
                    health = t.coll.NutritionProps?.Health ?? 0,
                    composite = FoodDatabase.IsComposite(t.coll),
                    profile = t.resolved == null ? null : (t.resolved.IsFallback ? "fallback" : t.resolved.Profile.Code),
                    portionGrams = t.resolved?.PortionGrams,
                    kcalPerPortion = t.resolved?.PerPortion(NutrientCodes.Calories)
                })
                .ToList();

            string dir = sapi.GetOrCreateDataPath(Path.Combine("ModData", ModId));
            string path = Path.Combine(dir, "foods.json");
            File.WriteAllText(path, JsonConvert.SerializeObject(rows, Formatting.Indented));
            return $"Wrote {rows.Count} foods to {path}";
        }
    }
}
