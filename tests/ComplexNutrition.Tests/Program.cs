using System.Reflection;
using System.Text.RegularExpressions;
using ComplexNutrition.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Complex Nutrition tests: the digestion maths and the shipped config agreeing with the code.
// Each test is a method below marked [Test]; the runner calls them all and exits 1 if any fails.

namespace ComplexNutrition.Tests
{
    [AttributeUsage(AttributeTargets.Method)]
    sealed class TestAttribute : Attribute { }

    static class Program
    {
        static int Main()
        {
            int failed = 0, passed = 0;
            foreach (var m in typeof(Tests).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.GetCustomAttribute<TestAttribute>() != null))
            {
                try
                {
                    m.Invoke(null, null);
                    passed++;
                    Console.WriteLine("  pass  " + m.Name);
                }
                catch (TargetInvocationException e)
                {
                    failed++;
                    Console.WriteLine("  FAIL  " + m.Name + ": " + e.InnerException?.Message);
                }
            }
            Console.WriteLine($"{passed} passed, {failed} failed.");
            return failed == 0 ? 0 : 1;
        }
    }

    static class Assert
    {
        public static void True(bool condition, string what)
        {
            if (!condition) throw new Exception(what);
        }

        public static void Near(double expected, double actual, double tolerance, string what)
        {
            if (Math.Abs(expected - actual) > tolerance) throw new Exception($"{what}: expected {expected}, got {actual}");
        }

        public static void Equal<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{what}: expected {expected}, got {actual}");
        }
    }

    /// <summary>Paths in the repository, found by walking up from the runner.</summary>
    static class Repo
    {
        public static readonly string Root = Find();

        static string Find()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ComplexNutrition", "modinfo.json"))) dir = dir.Parent;
            return dir?.FullName ?? throw new Exception("Run the tests from inside the repository.");
        }

        public static string Mod(params string[] parts) => Path.Combine(new[] { Root, "ComplexNutrition" }.Concat(parts).ToArray());
        public static string Config(string file) => Mod("assets", "complexnutrition", "config", file);
        public static JObject Json(string path) => JObject.Parse(File.ReadAllText(path), new JsonLoadSettings { CommentHandling = CommentHandling.Ignore });
        public static T Load<T>(string path) => JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
    }

    static class Tests
    {
        static readonly Dictionary<string, DigestionPool> Pools = DigestionSettings.DefaultPools();
        static DigestionPool Carbs => Pools[NutrientCodes.Carbohydrate];
        static DigestionPool Protein => Pools[NutrientCodes.Protein];
        static DigestionPool Fat => Pools[NutrientCodes.Fat];

        // ---- Gut pools: absorption ----

        [Test]
        public static void EmptyPoolAbsorbsNothing()
        {
            Assert.Equal(0f, Carbs.Rate(0f, 1f), "rate at 0 g");
            Assert.Equal(0f, Carbs.Absorb(0f, 5f, 1f), "absorb from 0 g");
        }

        [Test]
        public static void RateIsHalfAtHalfRateGrams()
        {
            foreach (var p in Pools.Values) Assert.Near(p.MaxRateGrams / 2f, p.Rate(p.HalfRateGrams, 1f), 1e-4, "half rate");
        }

        [Test]
        public static void RateLevelsOffBelowMaximum()
        {
            float big = Carbs.Rate(Carbs.MaxGrams, 1f);
            Assert.True(big < Carbs.MaxRateGrams && big > Carbs.MaxRateGrams * 0.9f, "a full pool absorbs close to, but under, its fastest rate");
            Assert.True(Carbs.Rate(10f, 1f) < Carbs.Rate(100f, 1f), "more in the gut absorbs faster");
        }

        [Test]
        public static void FiberSpeedsAbsorption()
        {
            float plain = Fat.Absorb(100f, 2f, 1f);
            float fast = Fat.Absorb(100f, 2f, 1f + Fat.FiberSpeedBonus);
            Assert.True(fast < plain, "fibre leaves less in the gut after the same time");
        }

        [Test]
        public static void AbsorbNeverGoesNegativeOrUp()
        {
            foreach (var p in Pools.Values)
            {
                float g = 50f;
                for (int i = 0; i < 200; i++)
                {
                    float next = p.Absorb(g, 0.37f, 1.3f);
                    Assert.True(next >= 0f && next <= g, "absorb stays between 0 and what was there");
                    g = next;
                }
                Assert.Equal(0f, g, "a pool empties in the end");
            }
        }

        [Test]
        public static void ZeroHoursChangesNothing()
        {
            Assert.Equal(42f, Protein.Absorb(42f, 0f, 1f), "no time, no absorption");
        }

        [Test]
        public static void HoursToEmptyMatchesAbsorb()
        {
            foreach (var p in Pools.Values)
            {
                float start = p.MaxGrams * 0.6f;
                float hours = p.HoursToEmpty(start, 1f);
                Assert.True(hours > 0f, "a filled pool takes time to empty");
                Assert.True(p.Absorb(start, hours * 0.9f, 1f) > 0f, "still something left at 90% of the time");
                Assert.Equal(0f, p.Absorb(start, hours * 1.02f, 1f), "empty just after the time");
            }
            Assert.Equal(0f, Fat.HoursToEmpty(DigestionPool.EmptyGrams, 1f), "a near-empty pool counts as empty");
        }

        // ---- Diminishing returns ----

        [Test]
        public static void FullYieldBelowTheLine()
        {
            foreach (var p in Pools.Values) Assert.Near(p.DiminishingAboveGrams, p.Effective(0f, p.DiminishingAboveGrams), 1e-3, "full yield up to the line");
        }

        [Test]
        public static void ProteinTwoTierCurve()
        {
            // 64 to 128 g falls in a straight line from 100% to 80% a gram (average 90%); past 128 g each gram is worth 50%.
            Assert.Near(64 * 0.9, Protein.Effective(64f, 64f), 1e-3, "64 to 128 g");
            Assert.Near(5.0, Protein.Effective(128f, 10f), 1e-3, "10 g past 128 g");
            Assert.Near(64 + 57.6 + 5, Protein.Effective(0f, 138f), 1e-3, "all at once");
        }

        [Test]
        public static void LogCurveAboveTheLine()
        {
            float extra = Carbs.Effective(Carbs.DiminishingAboveGrams, 100f);
            Assert.True(extra < 100f && extra > 50f, "carbs past the line give less, but not nothing");
        }

        [Test]
        public static void EffectiveIsAdditive()
        {
            foreach (var p in Pools.Values)
            {
                float whole = p.Effective(0f, 250f);
                float split = p.Effective(0f, 90f) + p.Effective(90f, 160f);
                Assert.Near(whole, split, 1e-2, "eating in two parts yields the same as at once");
            }
        }

        // ---- Settings ----

        [Test]
        public static void NetCarbsAndMacroCalories()
        {
            var amounts = new Dictionary<string, float> { ["carbohydrate"] = 10, ["fiber"] = 2, ["protein"] = 10, ["fat"] = 10 };
            float Get(string c) => amounts.TryGetValue(c, out var v) ? v : 0f;
            Assert.Equal(8f, DigestionSettings.PoolGrams(NutrientCodes.Carbohydrate, Get), "net carbs");
            Assert.Equal(0f, DigestionSettings.PoolGrams(NutrientCodes.Carbohydrate, c => c == "fiber" ? 5f : 1f), "net carbs never below 0");
            var s = new DigestionSettings { Pools = DigestionSettings.DefaultPools() };
            Assert.Near(8 * 4 + 10 * 4 + 10 * 9, s.KcalFromMacros(Get), 1e-3, "kcal from macros");
        }

        [Test]
        public static void ProteinNumbersComeFromThePool()
        {
            var s = new DigestionSettings { Pools = DigestionSettings.DefaultPools() };
            Assert.Equal(64f, s.ProteinNeedGrams, "protein need");
            Assert.Equal(128f, s.ProteinMaxGrams, "protein bar top");
        }

        [Test]
        public static void DigestionJsonMatchesCodeDefaults()
        {
            var file = Repo.Load<DigestionSettings>(Repo.Config("digestion.json"));
            var code = new DigestionSettings();
            foreach (var f in typeof(DigestionSettings).GetFields().Where(f => f.GetCustomAttribute<JsonPropertyAttribute>() != null && f.Name != nameof(DigestionSettings.Pools)))
            {
                Assert.Equal(JsonConvert.SerializeObject(f.GetValue(code)), JsonConvert.SerializeObject(f.GetValue(file)), "digestion.json " + f.Name);
            }
            var json = Repo.Json(Repo.Config("digestion.json"));
            foreach (var key in new[] { "proteinNeedGrams", "proteinMaxGrams", "zeroAtGrams" })
                Assert.True(!json.ToString().Contains("\"" + key + "\""), key + " is gone since v0.3.1");
        }

        [Test]
        public static void DigestionJsonPoolsMatchDefaultPools()
        {
            var file = Repo.Load<DigestionSettings>(Repo.Config("digestion.json"));
            Assert.Equal(Pools.Count, file.Pools.Count, "pool count");
            foreach (var kv in Pools)
            {
                Assert.True(file.Pools.ContainsKey(kv.Key), "digestion.json has pool " + kv.Key);
                Assert.Equal(JsonConvert.SerializeObject(kv.Value), JsonConvert.SerializeObject(file.Pools[kv.Key]), "pool " + kv.Key);
            }
        }

        [Test]
        public static void TrackingJsonMatchesCodeDefaults()
        {
            var file = Repo.Load<TrackingSettings>(Repo.Config("tracking.json"));
            var code = new TrackingSettings();
            foreach (var f in typeof(TrackingSettings).GetFields().Where(f => f.GetCustomAttribute<JsonPropertyAttribute>() != null))
            {
                Assert.Equal(JsonConvert.SerializeObject(f.GetValue(code)), JsonConvert.SerializeObject(f.GetValue(file)), "tracking.json " + f.Name);
            }
        }

        [Test]
        public static void NutrientsAgreeWithDigestion()
        {
            var nutrients = Repo.Load<NutrientRegistryFile>(Repo.Config("nutrients.json")).Nutrients;
            var raw = (JArray)Repo.Json(Repo.Config("nutrients.json"))["nutrients"];
            foreach (var code in Pools.Keys)
            {
                Assert.True(nutrients.Any(n => n.Code == code), "nutrients.json has gut pool " + code);
                var entry = raw.First(t => (string)t["code"] == code);
                Assert.True(entry["cap"] == null, code + " takes its cap from digestion.json, not nutrients.json");
            }
            var protein = nutrients.First(n => n.Code == NutrientCodes.Protein);
            Assert.Equal(Protein.DiminishingAboveGrams, protein.DailyTarget, "protein dailyTarget matches the protein pool's line");
            foreach (var code in new[] { NutrientCodes.Calories, NutrientCodes.Fiber, NutrientCodes.VitaminB, NutrientCodes.VitaminC, NutrientCodes.VitaminK, NutrientCodes.Calcium })
                Assert.True(nutrients.Any(n => n.Code == code), "nutrients.json has " + code);
        }

        [Test]
        public static void WorldConfigMatchesStartOptions()
        {
            var attrs = (JArray)Repo.Json(Repo.Mod("worldconfig.json"))["worldConfigAttributes"];
            var lang = Repo.Json(Repo.Mod("assets", "game", "lang", "worldconfig-en.json"));
            foreach (var (key, def) in new[] { (WorldSettings.StartConditionKey, WorldSettings.DefaultStart), (WorldSettings.RespawnConditionKey, WorldSettings.DefaultRespawn) })
            {
                var attr = attrs.First(a => (string)a["code"] == key);
                Assert.Equal(string.Join(",", WorldSettings.StartOptions.Select(o => o.Value)), string.Join(",", attr["values"].Select(v => (string)v)), key + " values");
                Assert.Equal(string.Join(",", WorldSettings.StartOptions.Select(o => o.Name)), string.Join(",", attr["names"].Select(v => (string)v)), key + " names");
                Assert.Equal(def, (string)attr["default"], key + " default");
                foreach (var o in WorldSettings.StartOptions) Assert.True(lang[o.LangKey(key)] != null, "worldconfig-en.json has " + o.LangKey(key));
                Assert.True(lang["worldattribute-" + key] != null && lang["worldattribute-" + key + "-desc"] != null, "worldconfig-en.json names and describes " + key);
            }
            Assert.Near(0.7, WorldSettings.StoreShare(700f), 1e-6, "Easy keeps 70% of each store");
            Assert.Near(0.001, WorldSettings.StoreShare(1f), 1e-6, "Rock Bottom keeps 0.1%");
            foreach (var key in new[] { WorldSettings.AdvancedDisplayKey, WorldSettings.MeatInPotsKey })
                Assert.True(attrs.Any(a => (string)a["code"] == key), "worldconfig.json has " + key);

        }

        [Test]
        public static void OldGutBatchesMoveOnce()
        {
            var tree = new Vintagestory.API.Datastructures.TreeAttribute();
            var gut = tree.GetOrAddTreeAttribute("gut");
            gut["fat-g"] = new Vintagestory.API.Datastructures.FloatArrayAttribute(new[] { 10f, 20f });
            gut["fat-r"] = new Vintagestory.API.Datastructures.FloatArrayAttribute(new[] { 0f, 0f });
            gut.SetFloat("fat-h", 5f);
            var state = NutritionState.FromTree(tree);
            Assert.Equal((float?)30f, state.TakeOldGutBatches("fat"), "old batches summed");
            Assert.True(!gut.HasAttribute("fat-g") && !gut.HasAttribute("fat-r"), "old batches removed");
            Assert.Equal(5f, state.GetGutHours("fat"), "hours to empty kept");
            Assert.Equal((float?)null, state.TakeOldGutBatches("fat"), "nothing to move the second time");
        }

        // ---- History (v0.3.2) ----

        [Test]
        public static void HistoryKeepsDaysAndBuildsFourWeeks()
        {
            var history = NutrientHistory.FromTree(new Vintagestory.API.Datastructures.TreeAttribute());
            for (int day = 1; day <= 40; day++) history.RecordDay(day, new Dictionary<string, float> { ["vitaminC"] = day });
            history.Prune(41, 28);
            Assert.Equal(0, history.DayReserves(13).Count, "day 13 pruned");
            Assert.Near(14, history.DayReserves(14)["vitaminC"], 1e-6, "day 14 kept");

            var data = history.Build(null, new Dictionary<string, float> { ["vitaminC"] = 99 }, 41, 41 * 24 + 5, 28);
            Assert.Equal(28, data.Days.Count, "28 days");
            Assert.Equal(14, data.Days[0].Day, "oldest first");
            Assert.Near(99, data.Days[^1].Reserves["vitaminC"], 1e-6, "today is now");
            Assert.True(data.DayAgo == null, "no hourly readings, no trend");
        }

        [Test]
        public static void HistoryTrendsUseTheOldestReadingOfTheLastDay()
        {
            var history = NutrientHistory.FromTree(new Vintagestory.API.Datastructures.TreeAttribute());
            for (long hour = 100; hour <= 160; hour++) history.RecordHour(hour, new Dictionary<string, float> { ["iron"] = hour });
            var ago = history.DayAgo(160.5);
            Assert.True(ago != null, "a reading a day old");
            Assert.Near(136, ago.Value.reserves["iron"], 1e-6, "24 hours back");
            Assert.Near(24.5, ago.Value.hoursAgo, 1e-4, "hours ago");

            history.ClearHours();
            history.RecordHour(161, new Dictionary<string, float> { ["iron"] = 1 });
            Assert.True(history.DayAgo(163) == null, "too recent for a trend");

            Assert.Equal(1, NutrientHistory.Trend(100, 130, 24), "rising");
            Assert.Equal(0, NutrientHistory.Trend(100, 95, 24), "steady");
            Assert.Equal(-1, NutrientHistory.Trend(100, 60, 24), "falling");
            Assert.Equal(-1, NutrientHistory.Trend(100, 90, 6), "10 in 6 hours is 40 a day");
        }

        [Test]
        public static void HistoryCountsCaloriesGainedAndBurned()
        {
            var history = NutrientHistory.FromTree(new Vintagestory.API.Datastructures.TreeAttribute());
            for (int h = 0; h < 30; h++) history.AddFlow(100 * 24 + h + 0.5, 100 + (h + 0) / 24, 50, 100);
            var (gained, burned) = history.DayFlow(100);
            Assert.Near(24 * 50, gained, 1e-3, "a day's gains");
            Assert.Near(24 * 100, burned, 1e-3, "a day's burn");
            double now = 100 * 24 + 29.25;
            Assert.Near(100 + 100 * 0.75, history.BurnedOver(now, 1), 1e-3, "the last hour: this hour and the rest of the one before");
            Assert.Near(24 * 100, history.BurnedOver(now, 24), 1e-3, "the last 24 hours");
            history.Prune(140, 28);
            Assert.Equal(0f, history.DayFlow(100).burned, "old days pruned");
        }

        [Test]
        public static void HealingCostsTheExtraOverRestingNeed()
        {
            var t = new TrackingSettings();
            Assert.Near(640, t.HealingKcalPerDay(0f), 1e-3, "lightly hurt: (33 - 25) x 80");
            Assert.Near(1520, t.HealingKcalPerDay(1f), 1e-3, "badly hurt: (44 - 25) x 80");
            Assert.Near(640 / 28.8, t.KcalPerHp(0f), 1e-3, "a point when barely hurt");
            Assert.True(t.KcalToHeal(1, 15) > 2 * t.KcalToHeal(8, 15), "more damage costs more than its share");
            Assert.Equal(0f, t.KcalToHeal(15, 15), "nothing to heal");
        }

        [Test]
        public static void HistoryScaleRoundsUp()
        {
            Assert.Near(5000, ComplexNutrition.UI.NutritionPages.NiceCeiling(3500), 1e-3, "3,500 to 5,000");
            Assert.Near(200, ComplexNutrition.UI.NutritionPages.NiceCeiling(179), 1e-3, "179 to 200");
            Assert.Near(250, ComplexNutrition.UI.NutritionPages.NiceCeiling(201), 1e-3, "201 to 250");
            Assert.Near(1000, ComplexNutrition.UI.NutritionPages.NiceCeiling(1000), 1e-3, "exact stays");
        }

        // ---- Text ----

        [Test]
        public static void NumbersFormatTheSameEverywhere()
        {
            Assert.Equal("0", Fmt.Number(0f), "zero");
            Assert.Equal("7.5", Fmt.Number(7.46f), "one decimal under 10");
            Assert.Equal("0.4", Fmt.Number(0.4f), "small");
            Assert.Equal("12", Fmt.Number(12.4f), "whole from 10");
            Assert.Equal("1,920", Fmt.Number(1920.2f), "thousands");
        }

        [Test]
        public static void NutrientAmountsFormat()
        {
            var g = new NutrientDefinition { Code = "protein", Unit = "g", DailyTarget = 64 };
            var mg = new NutrientDefinition { Code = "vitaminC", Unit = "mg", DailyTarget = 90, Group = "WaterSolubleVitamin" };
            var b = new NutrientDefinition { Code = "vitaminB", Unit = "%DV", DailyTarget = 100 };
            Assert.Equal("32g", g.Format(50f, false), "grams");
            Assert.Equal("50%", g.Format(50f, true), "percent");
            Assert.Equal("45mg", mg.Format(50f, false), "mg");
            Assert.Equal("50% DV", b.Format(50f, false), "no unit to show");
            Assert.Near(32f, g.FromPercent(50f), 1e-4, "FromPercent");
        }

        [Test]
        public static void DailyValuesTurnIntoAmounts()
        {
            var reg = new NutrientRegistry();
            reg.Set(Repo.Load<NutrientRegistryFile>(Repo.Config("nutrients.json")));
            var p = new FoodProfile { Code = "x:food", PortionGrams = 100, Per100g = new() { ["iron"] = 2f }, Per100gDV = new() { ["vitaminC"] = 50f, ["iron"] = 100f, ["vitaminB"] = 30f } };
            FoodDatabase.ApplyDailyValues(null, reg, p, "test");
            Assert.Near(45f, p.Per100("vitaminC"), 1e-4, "50% DV of vitamin C is 45mg");
            Assert.Equal(2f, p.Per100("iron"), "per100g wins over per100gDV");
            Assert.Near(30f, p.Per100("vitaminB"), 1e-4, "the B vitamins stay in %DV");
            Assert.True(p.Per100gDV == null, "per100gDV is used up");
        }

        [Test]
        public static void VitaminsAndMineralsUseRealUnits()
        {
            var nutrients = Repo.Load<NutrientRegistryFile>(Repo.Config("nutrients.json")).Nutrients;
            foreach (var n in nutrients.Where(n => n.IsMicronutrient))
            {
                if (n.Code == NutrientCodes.VitaminB) Assert.Equal("%DV", n.Unit, "vitamin B unit");
                else Assert.True(n.Unit == "mg" || n.Unit == "mcg", n.Code + " is in mg or mcg, not " + n.Unit);
            }
            // Our profiles store real units: no shipped profile uses per100gDV, and a sample matches USDA.
            foreach (var file in Directory.EnumerateFiles(Repo.Mod("assets", "complexnutrition", "config", "foodprofiles"), "*.json"))
                Assert.True(!File.ReadAllText(file).Contains("per100gDV"), Path.GetFileName(file) + " uses real units");
            var dairy = Repo.Load<FoodProfileFile>(Repo.Config("foodprofiles/dairy.json"));
            var butter = dairy.Profiles.First(p => p.Code == "game:butter-salted");
            Assert.Near(684f, butter.Per100("vitaminA"), 0.5, "salted butter has 684mcg vitamin A per 100g (USDA 173410)");
        }

        [Test]
        public static void EveryLangKeyInCodeExists()
        {
            var lang = Repo.Json(Repo.Mod("assets", "complexnutrition", "lang", "en.json"));
            var missing = new SortedSet<string>();
            var direct = new Regex("\"complexnutrition:([a-z0-9-]+)\"");
            var helper = new Regex("\\bL\\(\"([a-z0-9-]+)\"");
            foreach (var file in Directory.EnumerateFiles(Repo.Mod(), "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains("/obj/") && !f.Contains("/bin/")))
            {
                string text = File.ReadAllText(file);
                foreach (Match m in direct.Matches(text).Concat(helper.Matches(text)))
                {
                    string key = m.Groups[1].Value;
                    if (key.EndsWith("-")) continue;   // built from parts
                    if (lang[key] == null) missing.Add(Path.GetFileName(file) + ": " + key);
                }
            }
            Assert.True(missing.Count == 0, "missing in en.json: " + string.Join(", ", missing));
        }

        // ---- Files ----

        [Test]
        public static void EveryAssetJsonParses()
        {
            var bad = new List<string>();
            foreach (var file in Directory.EnumerateFiles(Repo.Mod("assets"), "*.json", SearchOption.AllDirectories)
                         .Concat(new[] { Repo.Mod("modinfo.json"), Repo.Mod("worldconfig.json") }))
            {
                try { JToken.Parse(File.ReadAllText(file), new JsonLoadSettings { CommentHandling = CommentHandling.Ignore }); }
                catch (Exception e) { bad.Add(Path.GetRelativePath(Repo.Root, file) + ": " + e.Message); }
            }
            Assert.True(bad.Count == 0, string.Join("; ", bad));
        }

        [Test]
        public static void ChangelogHasThisVersion()
        {
            string version = (string)Repo.Json(Repo.Mod("modinfo.json"))["version"];
            string changelog = File.ReadAllText(Path.Combine(Repo.Root, "CHANGELOG.md"));
            Assert.True(changelog.Contains("## v" + version) || changelog.Contains("## " + version) || changelog.Contains("[" + version + "]"), "CHANGELOG.md has a section for " + version);
        }
    }
}
