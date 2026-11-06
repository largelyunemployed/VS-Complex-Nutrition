using System.Reflection;
using Cairo;
using ComplexNutrition.Core;
using ComplexNutrition.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

public class Fake : DispatchProxy
{
    public static object LoggerObj;
    protected override object Invoke(MethodInfo m, object[] args)
    {
        if (m.Name == "get_Logger") return LoggerObj;
        var rt = m.ReturnType;
        return rt.IsValueType && rt != typeof(void) ? Activator.CreateInstance(rt) : null;
    }
}

/// <summary>
/// Draws the nutrition window's tabs for a made-up player (a week or so in) to window-*.png in the folder given,
/// (or, with "percent" as the second argument, amounts in % of a day), using the mod's own page code, lang file and config, with a rough stand-in for the dialog frame. Needs the game's
/// libraries (VINTAGE_STORY) and a system font; it fakes just enough of the client API for the pages to draw.
/// </summary>
static class P
{
    static string Mod;

    static void Main(string[] a)
    {
        string outDir = a.Length > 0 ? a[0] : "preview-out";
        Directory.CreateDirectory(outDir);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(System.IO.Path.Combine(dir.FullName, "ComplexNutrition", "modinfo.json"))) dir = dir.Parent;
        if (dir == null) throw new Exception("Run the preview from inside the repository.");
        Mod = System.IO.Path.Combine(dir.FullName, "ComplexNutrition", "assets", "complexnutrition") + "/";
        RuntimeEnv.GUIScale = 1.5f;
        Fake.LoggerObj = DispatchProxy.Create<ILogger, Fake>();
        var capi = DispatchProxy.Create<ICoreClientAPI, Fake>();

        var ts = new TranslationService("en", (ILogger)Fake.LoggerObj);
        var cache = new Dictionary<string, string>();
        foreach (var kv in JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(Mod + "lang/en.json"))) cache["complexnutrition:" + kv.Key] = kv.Value;
        foreach (var kv in JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(Mod + "../game/lang/worldconfig-en.json"))) cache["game:" + kv.Key] = kv.Value;
        typeof(TranslationService).GetField("entryCache", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ts, cache);
        typeof(TranslationService).GetField("loaded", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ts, true);
        Lang.AvailableLanguages["en"] = ts;
        typeof(Lang).GetProperty("CurrentLocale").SetValue(null, "en");

        var reg = new NutrientRegistry();
        reg.Set(JsonConvert.DeserializeObject<NutrientRegistryFile>(File.ReadAllText(Mod + "config/nutrients.json")));
        var digest = JsonConvert.DeserializeObject<DigestionSettings>(File.ReadAllText(Mod + "config/digestion.json"));
        foreach (var kv in digest.Pools) { var d = reg.Get(kv.Key); d.Cap = kv.Value.MaxGrams / d.DailyTarget * 100f; }
        var tracking = JsonConvert.DeserializeObject<TrackingSettings>(File.ReadAllText(Mod + "config/tracking.json"));

        // A player a week or so in: fat-soluble vitamins and calcium sliding, something in the gut.
        var tree = new TreeAttribute();
        var st = NutritionState.FromTree(tree);
        var r = new Dictionary<string, float> { ["calories"] = 340, ["fat"] = 40f / 80 * 100, ["protein"] = 22f / 64 * 100, ["carbohydrate"] = 94f / 300 * 100, ["fiber"] = 55,
            ["vitaminC"] = 80, ["vitaminB"] = 120, ["vitaminA"] = 900, ["vitaminD"] = 140, ["vitaminE"] = 1100, ["vitaminK"] = 600, ["calcium"] = 40, ["iron"] = 380, ["potassium"] = 260 };
        foreach (var kv in r) st.SetReserve(kv.Key, kv.Value);
        st.TrackingOn = true;
        st.Protein24 = 41;
        st.SetGutHours("carbohydrate", 2.6f);
        st.SetGutHours("fat", 13f);
        st.SetGutHours("protein", 4.1f);
        var logC = new float[24]; var logF = new float[24]; var logP = new float[24];
        for (int i = 0; i < 8; i++) { logC[i] = 21; logF[i] = 3; logP[i] = 5; }
        st.SetAbsorbedLog("carbohydrate", logC); st.SetAbsorbedLog("fat", logF); st.SetAbsorbedLog("protein", logP);
        st.AbsorbedHour = 7;

        var known = new List<KnownFood>();
        var wanted = new Dictionary<string, string> { ["game:vegetable-carrot"] = "Carrot", ["game:cheese-cheddar-1slice"] = "Cheddar cheese", ["game:fruit-blueberry"] = "Blueberry",
            ["game:vegetable-cabbage"] = "Cabbage", ["game:bread-spelt-perfect"] = "Spelt bread", ["game:milkportion"] = "Milk", ["game:grain-flax"] = "Flax grain",
            ["game:redmeat-cooked"] = "Cooked red meat", ["game:fish-smoked"] = "Smoked fish", ["game:egg-chicken-raw"] = "Chicken egg", ["game:mushroom-chanterelle-normal"] = "Chanterelle", ["game:vegetable-pumpkin"] = "Pumpkin slice" };
        foreach (var f in Directory.GetFiles(Mod + "config/foodprofiles"))
            foreach (var p in JObject.Parse(File.ReadAllText(f))["profiles"])
            {
                var fp = p.ToObject<FoodProfile>();
                if (fp.Code != null && wanted.TryGetValue(fp.Code, out var name) && fp.Per100g != null && fp.Per100g.Count > 0)
                    known.Add(new KnownFood { Name = name, Food = new ResolvedFoodProfile { Profile = fp, PortionGrams = fp.PortionGrams } });
            }
        Console.WriteLine("known foods: " + known.Count);

        // Four weeks of history: joined 25 days ago, a lean week of mostly grain, then a mixed diet; vitamin A sliding.
        var history = new HistoryData { Today = 100 };
        var rnd = new Random(3);
        for (int i = 0; i < 28; i++)
        {
            var day = new HistoryDay { Day = 72 + i };
            if (i >= 3)
            {
                bool lean = i >= 10 && i < 17;
                day.KcalByCategory["Grain"] = (lean ? 1500 : 900) + rnd.Next(0, 400);
                day.KcalByCategory["Protein"] = lean ? rnd.Next(0, 200) : 700 + rnd.Next(0, 500);
                day.KcalByCategory["Vegetable"] = lean ? 0 : 250 + rnd.Next(0, 200);
                day.KcalByCategory["Fruit"] = lean ? 80 : 150 + rnd.Next(0, 250);
                day.KcalByCategory["Dairy"] = lean ? 0 : 300 + rnd.Next(0, 300);
                day.Gained = day.TotalKcal * 0.85f;
                day.Burned = 2000 + rnd.Next(0, 1200);
            }
            history.Days.Add(day);
        }
        history.DayAgo = r.ToDictionary(kv => kv.Key, kv => kv.Key == "vitaminC" ? kv.Value - 40 : kv.Key == "calcium" ? kv.Value + 60 : kv.Key == "vitaminA" ? kv.Value + 30 : kv.Value - 5);
        history.DayAgoHours = 24;
        history.BurnedHour = 96;
        history.Burned24 = 2410;

        bool percent = a.Length > 1 && a[1] == "percent";   // second argument "percent": amounts in % of a day
        var data = new NutritionPageData { Nutrients = reg, Digest = digest, Tracking = tracking, State = st, Fullness = 240, MaxFullness = 300, Health = 9, MaxHealth = 15, Known = known, ShowPercent = percent, History = history };
        Console.WriteLine("writing to " + System.IO.Path.GetFullPath(outDir));
        var tabs = new[] { ("overview", NutritionPages.Overview(data)), ("digestion", NutritionPages.DigestionPage(data)), ("vitamins", NutritionPages.Vitamins(data)), ("minerals", NutritionPages.Minerals(data)),
            ("history", NutritionPages.History(data)), };
        string[] names = { "Overview", "Digestion", "Vitamins", "Minerals", "History" };
        for (int t = 0; t < tabs.Length; t++)
        {
            using var page = PageRenderer.Render(capi, tabs[t].Item2, GuiDialogNutrition.ContentWidth - 4);
            Frame(page, names, Math.Min(t, 4), System.IO.Path.Combine(outDir, "window-" + tabs[t].Item1 + ".png"));
        }
        // Hover tooltips (v0.3.5): a day on each History chart and the dashed line's "i", drawn where the mouse would be.
        {
            var spots = new List<Hotspot>();
            using var page = PageRenderer.Render(capi, NutritionPages.History(data), GuiDialogNutrition.ContentWidth - 4, spots);
            using var ctx = new Context(page);
            var days = spots.Where(sp => sp.Highlight).ToList();
            var picks = new List<Hotspot> { days[days.Count / 2 - 3], days[^2], spots.First(sp => !sp.Highlight) };
            foreach (var sp in picks)
            {
                if (sp.Highlight) { ctx.SetSourceRGBA(1, 1, 1, 28 / 255.0); ctx.Rectangle(sp.X, sp.Y, sp.W, sp.H); ctx.Fill(); }
                using var tip = PageRenderer.RenderTip(capi, sp.Vtml, GuiElementNutritionPage.TipWidth);
                double tx = Math.Min(sp.X + sp.W / 2 + S(14), page.Width - tip.Width - 2);
                ctx.SetSourceSurface(tip, (int)tx, (int)(sp.Y + sp.H / 2 + S(14))); ctx.Paint();
            }
            page.WriteToPng(System.IO.Path.Combine(outDir, "window-history-tips.png"));
            Console.WriteLine("hotspots: " + spots.Count);
        }
        // Trend arrow hovers on the Overview (v0.3.6): the calories and fullness arrows' tooltips.
        {
            var spots = new List<Hotspot>();
            using var page = PageRenderer.Render(capi, NutritionPages.Overview(data), GuiDialogNutrition.ContentWidth - 4, spots);
            using var ctx = new Context(page);
            double y = 0;
            foreach (var sp in spots.Take(2))
            {
                using var tip = PageRenderer.RenderTip(capi, sp.Vtml, GuiElementNutritionPage.TipWidth);
                ctx.SetSourceSurface(tip, (int)(sp.X + sp.W + S(6)), (int)Math.Max(y, sp.Y + sp.H)); ctx.Paint();
                y = sp.Y + sp.H + tip.Height + S(4);
            }
            page.WriteToPng(System.IO.Path.Combine(outDir, "window-overview-tips.png"));
        }
        // The running-low icon in its three states, side by side.
        using (var icons = new ImageSurface(Format.Argb32, (int)S(3 * 40), (int)S(40)))
        using (var ictx = new Context(icons))
        {
            ictx.SetSourceRGB(0.35, 0.42, 0.30); ictx.Paint();
            var cols = new[] { RunningLowIcon.FineColor, RunningLowIcon.LowColor, RunningLowIcon.EmptyColor };
            for (int i = 0; i < 3; i++)
            {
                using var one = RunningLowIcon.Draw(cols[i], S(RunningLowIcon.Size));
                ictx.SetSourceSurface(one, (int)S(5 + 40 * i), (int)S(5)); ictx.Paint();
            }
            icons.WriteToPng(System.IO.Path.Combine(outDir, "lowicon.png"));
        }
        var off = new NutritionPageData { Nutrients = reg, Digest = digest, Tracking = tracking, State = null };
        using (var page = PageRenderer.Render(capi, NutritionPages.Overview(off), GuiDialogNutrition.ContentWidth - 4)) Frame(page, names, 0, System.IO.Path.Combine(outDir, "window-tracking-off.png"));
    }

    static double S(double v) => v * RuntimeEnv.GUIScale;

    // A rough stand-in for the dialog frame (title bar, tabs, Settings button, inset), so the page is seen at its real size.
    static void Frame(ImageSurface page, string[] tabs, int active, string path)
    {
        double pad = S(15), w = S(500 + 40) + 2 * pad, h = S(75 + 500 + 55) + pad;
        using var surf = new ImageSurface(Format.Argb32, (int)w, (int)h);
        using var ctx = new Context(surf);
        ctx.SetSourceRGBA(0.20, 0.17, 0.14, 1); ctx.Paint();
        ctx.SetSourceRGBA(0.29, 0.24, 0.19, 1); ctx.Rectangle(0, 0, w, S(30)); ctx.Fill();
        ctx.SetSourceRGB(0.9, 0.85, 0.75); ctx.SelectFontFace("sans", FontSlant.Normal, FontWeight.Bold); ctx.SetFontSize(S(15)); ctx.MoveTo(pad, S(20)); ctx.ShowText("Nutrition");
        ctx.SelectFontFace("sans", FontSlant.Normal, FontWeight.Normal); ctx.SetFontSize(S(16));
        double x = pad;
        for (int i = 0; i < tabs.Length; i++)
        {
            var ext = ctx.TextExtents(tabs[i]);
            if (i == active) { ctx.SetSourceRGBA(0.38, 0.31, 0.24, 1); ctx.Rectangle(x - S(6), S(35) + pad, ext.Width + S(12), S(25)); ctx.Fill(); }
            ctx.SetSourceRGB(i == active ? 1.0 : 0.75, i == active ? 0.88 : 0.72, i == active ? 0.6 : 0.66);
            ctx.MoveTo(x, S(53) + pad); ctx.ShowText(tabs[i]); x += ext.Width + S(24);
        }
        ctx.SetSourceRGBA(0.33, 0.27, 0.21, 1); ctx.Rectangle(pad, S(590) + pad, S(80), S(26)); ctx.Fill();
        ctx.SetSourceRGB(0.9, 0.85, 0.75); ctx.SetFontSize(S(13)); ctx.MoveTo(pad + S(14), S(608) + pad); ctx.ShowText("Settings");
        ctx.SetSourceRGBA(0, 0, 0, 0.35); ctx.Rectangle(pad, S(72) + pad, S(506), S(506)); ctx.Fill();
        ctx.SetSourceRGBA(0.45, 0.38, 0.30, 1); ctx.Rectangle(pad + S(517), S(72) + pad, S(20), S(506)); ctx.Fill();
        double ph = Math.Min(page.Height, S(500));
        ctx.SetSourceRGBA(0.6, 0.52, 0.42, 1); ctx.Rectangle(pad + S(517), S(72) + pad, S(20), S(506) * Math.Min(1, S(500) / page.Height)); ctx.Fill();
        ctx.Save(); ctx.Rectangle(pad + S(3), S(75) + pad, S(500), S(500)); ctx.Clip();
        ctx.SetSourceSurface(page, (int)(pad + S(3)), (int)(S(75) + pad)); ctx.Paint(); ctx.Restore();
        ctx.SetSourceRGBA(0.33, 0.27, 0.21, 1); ctx.Rectangle(w - pad - S(80), S(590) + pad, S(70), S(26)); ctx.Fill();
        ctx.SetSourceRGB(0.9, 0.85, 0.75); ctx.MoveTo(w - pad - S(65), S(608) + pad); ctx.ShowText("Close");
        surf.WriteToPng(path);
        // and the full page, unclipped, for reading
        page.WriteToPng(path.Replace(".png", "-full.png"));
    }
}
