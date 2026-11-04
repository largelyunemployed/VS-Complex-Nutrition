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
            var start = attrs.First(a => (string)a["code"] == WorldSettings.StartConditionKey);
            Assert.Equal(string.Join(",", WorldSettings.StartOptions.Select(o => o.Value)), string.Join(",", start["values"].Select(v => (string)v)), "start values");
            Assert.Equal(string.Join(",", WorldSettings.StartOptions.Select(o => o.Name)), string.Join(",", start["names"].Select(v => (string)v)), "start names");
            Assert.Equal(WorldSettings.DefaultStart, (string)start["default"], "start default");
            foreach (var key in new[] { WorldSettings.AdvancedDisplayKey, WorldSettings.MeatInPotsKey })
                Assert.True(attrs.Any(a => (string)a["code"] == key), "worldconfig.json has " + key);

            var lang = Repo.Json(Repo.Mod("assets", "game", "lang", "worldconfig-en.json"));
            foreach (var o in WorldSettings.StartOptions) Assert.True(lang[o.LangKey] != null, "worldconfig-en.json has " + o.LangKey);
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
            var g = new NutrientDefinition { Code = "protein", Unit = "g", DailyTarget = 64, DisplayUnit = "g" };
            var mg = new NutrientDefinition { Code = "vitaminC", Unit = "%DV", DailyTarget = 100, DisplayUnit = "mg", DvAmount = 90 };
            var b = new NutrientDefinition { Code = "vitaminB", Unit = "%DV", DailyTarget = 100 };
            Assert.Equal("32g", g.Format(50f, false), "grams");
            Assert.Equal("50%", g.Format(50f, true), "percent");
            Assert.Equal("45mg", mg.Format(50f, false), "mg from %DV");
            Assert.Equal("50% DV", b.Format(50f, false), "no unit to show");
            Assert.Near(32f, g.FromPercent(50f), 1e-4, "FromPercent");
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
