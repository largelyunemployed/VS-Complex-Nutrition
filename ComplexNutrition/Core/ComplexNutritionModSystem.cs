using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ComplexNutrition.Modules.FoodData;
using ComplexNutrition.Modules.Tracking;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ComplexNutrition.Core
{
    /// <summary>
    /// The core: owns the config, the nutrient registry and the food database,
    /// and starts the enabled feature modules. It is the only part that touches vanilla code.
    /// </summary>
    public partial class ComplexNutritionModSystem : ModSystem
    {
        public const string ModId = "complexnutrition";

        public ComplexNutritionConfig Config { get; private set; } = new();
        public NutrientRegistry Nutrients { get; } = new();
        public FoodDatabase Foods { get; } = new();
        public CookingRules Cooking { get; private set; } = new();

        private readonly List<INutritionModule> modules = new();
        private ICoreAPI api;

        public IReadOnlyList<INutritionModule> Modules => modules;

        /// <summary>
        /// Server: a player ate or drank something. Published whenever a module has asked for the eating hooks
        /// (<see cref="RequireEatingHooks"/>), whatever else is switched on.
        /// </summary>
        public event Action<FoodEatenInfo> FoodEaten;

        private readonly Dictionary<string, System.Func<IServerPlayer, string, float>> absorptionModifiers = new();

        /// <summary>True when the module with this id is loaded and running.</summary>
        public bool IsModuleEnabled(string id) => modules.Any(m => m.Id == id && m.IsEnabled);

        /// <summary>Server: start working out what players eat (see <see cref="EatingHooks"/>). Safe to call more than once.</summary>
        public void RequireEatingHooks()
        {
            if (api?.Side == EnumAppSide.Server) EatingHooks.Apply(this);
        }

        public void PublishFoodEaten(FoodEatenInfo info)
        {
            try
            {
                // Discover Nutrition (v0.3.4): every food eaten is remembered, whatever the world setting.
                var entity = info.Player?.Entity;
                if (entity != null && info.Source != null) Discovery.Record(entity, Discovery.CodesOf(entity.World, info.Source));
                FoodEaten?.Invoke(info);
            }
            catch (Exception e)
            {
                api.Logger.Error("[complexnutrition] Error while handling food eaten by {0}: {1}", info.Player?.PlayerName, e);
            }
        }

        /// <summary>
        /// Registers a source of absorption multipliers: given a player and a nutrient code it returns a
        /// multiplier on how much of that nutrient is absorbed (1 = no change). Used from v1.01; none ship in v0.2.
        /// </summary>
        public void RegisterAbsorptionModifier(string id, System.Func<IServerPlayer, string, float> modifier) => absorptionModifiers[id] = modifier;

        public void UnregisterAbsorptionModifier(string id) => absorptionModifiers.Remove(id);

        /// <summary>Every registered absorption multiplier for this player and nutrient, by modifier id.</summary>
        public Dictionary<string, float> AbsorptionMultipliers(IServerPlayer player, string nutrient)
        {
            var result = new Dictionary<string, float>();
            foreach (var kv in absorptionModifiers)
            {
                float m = kv.Value(player, nutrient);
                if (m != 1f) result[kv.Key] = m;
            }
            return result;
        }

        public override void StartPre(ICoreAPI api)
        {
            this.api = api;
            modules.Clear();
            modules.Add(new FoodDataModule());
            modules.Add(new TrackingModule());
            LoadConfig(api);
        }

        public override void StartClientSide(Vintagestory.API.Client.ICoreClientAPI capi)
        {
            StartClientSettings(capi);

            // Guide pages load in asset order, which would put ours above the game's own Starter Guide; move them after.
            var handbook = capi.ModLoader.GetModSystem<Vintagestory.GameContent.ModSystemSurvivalHandbook>();
            if (handbook != null)
            {
                handbook.OnInitCustomPages += pages =>
                {
                    var ours = pages.Where(p => p.PageCode != null && p.PageCode.StartsWith(ModId + "-")).ToList();
                    pages.RemoveAll(ours.Contains);
                    pages.AddRange(ours);
                    // Amounts on our pages in their unit colours, as in the hovers and food tooltips. The pages are
                    // already initialised (text looked up and laid out), so they are laid out again with the new text.
                    foreach (var page in ours.OfType<Vintagestory.GameContent.GuiHandbookTextPage>())
                    {
                        string painted = UnitColors.PaintVtml(page.Text);
                        if (painted == page.Text) continue;
                        page.Text = painted;
                        page.Init(capi);
                    }
                };
            }
        }

        public override void StartServerSide(ICoreServerAPI sapi)
        {
            StartServerSettings(sapi);

            sapi.ChatCommands.GetOrCreate("cnutrition")
                .WithDescription("Complex Nutrition: /cnutrition stats, settings and display for every player; the rest for admins")
                .RequiresPrivilege(Privilege.chat)
                .BeginSubCommand("coverage")
                    .WithDescription("List vanilla foods that have no profile of their own")
                    .RequiresPrivilege(Privilege.controlserver)
                    .HandleWith(_ => TextCommandResult.Success(CoverageReport(sapi, verbose: true)))
                .EndSubCommand()
                .BeginSubCommand("template")
                    .WithDescription("Write a ready-to-edit profile file for every food on the category estimate, optionally only one mod's (by domain), to ModData/complexnutrition/")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(sapi.ChatCommands.Parsers.OptionalWord("domain"))
                    .HandleWith(args => TextCommandResult.Success(WriteTemplate(sapi, args.Parsers[0].IsMissing ? null : (string)args[0])))
                .EndSubCommand()
                .BeginSubCommand("dump")
                    .WithDescription("Write every food item, its vanilla values and its profile to ModData/complexnutrition/foods.json")
                    .RequiresPrivilege(Privilege.controlserver)
                    .HandleWith(_ => TextCommandResult.Success(DumpFoods(sapi)))
                .EndSubCommand();
        }

        public override void AssetsFinalize(ICoreAPI api)
        {
            Nutrients.Load(api);
            Foods.Load(api, Nutrients);
            Cooking = CookingRules.Load(api);
            NutrientEffects.Settings = EffectSettings.Load(api);
            DietScore.Settings = DietSettings.Load(api);
            api.Logger.Notification("[complexnutrition] Loaded {0} nutrients and {1} food profiles.", Nutrients.All.Count, Foods.ProfileCount);

            foreach (var module in modules) module.Start(api, this);
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
            DisposeSettings();
            foreach (var module in modules.Where(m => m.IsEnabled).Reverse()) module.Disable(api);
            if (api.Side == EnumAppSide.Server) EatingHooks.Remove();
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

            Config ??= new ComplexNutritionConfig();
            foreach (var m in modules)
            {
                if (!Config.Modules.ContainsKey(m.Id)) Config.Modules[m.Id] = m.EnabledByDefault;
            }
            // Written back every start, so settings added in a new version show up in the file.
            api.StoreModConfig(Config, ComplexNutritionConfig.FileName);
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

        /// <summary>
        /// Writes a foodprofiles file covering every food that has no profile of its own (optionally one domain),
        /// pre-filled with the category estimate it gets today, so a modder only has to correct the numbers.
        /// </summary>
        private string WriteTemplate(ICoreServerAPI sapi, string domain)
        {
            var profiles = new List<object>();
            foreach (var (coll, vanilla, resolved) in Foods.Survey(sapi.World).OrderBy(t => t.coll.Code.ToString()))
            {
                if (resolved == null || !resolved.IsFallback) continue;
                if (domain != null && !string.Equals(coll.Code.Domain, domain, StringComparison.OrdinalIgnoreCase)) continue;
                profiles.Add(new
                {
                    code = coll.Code.ToString(),
                    portionGrams = (float)Math.Round(resolved.PortionGrams),
                    portionUnit = resolved.IsLiquid ? "litre" : "item",
                    per100g = resolved.Profile.Per100g,
                    notes = $"TODO {coll.GetHeldItemName(new ItemStack(coll))}: {vanilla.Category} category estimate, sized from satiety {vanilla.Satiety}. Set a real weight and real values, or replace per100g with \"like\": \"<vanilla item code>\"."
                });
            }
            if (profiles.Count == 0) return domain == null ? "Every food has a profile; nothing to write." : $"No foods from '{domain}' are on the category estimate.";

            var file = new Dictionary<string, object>
            {
                ["_comment"] = "Complex Nutrition food profiles. Put this file in assets/<your mod id>/config/foodprofiles/. See docs/COMPATIBILITY.md in the Complex Nutrition repo.",
                ["profiles"] = profiles
            };
            string dir = sapi.GetOrCreateDataPath(Path.Combine("ModData", ModId));
            string path = Path.Combine(dir, $"template-{domain ?? "all"}.json");
            File.WriteAllText(path, JsonConvert.SerializeObject(file, Formatting.Indented));
            return $"Wrote {profiles.Count} foods to {path}";
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
