using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace ComplexNutrition.Core
{
    /// <summary>One in-game day of the diet ledger: calories eaten per vanilla food category.</summary>
    public class LedgerDay
    {
        /// <summary>Whole in-game day number (Calendar.TotalDays rounded down).</summary>
        public int Day;

        public Dictionary<string, float> KcalByCategory = new();

        public float TotalKcal => KcalByCategory.Values.Sum();
    }

    /// <summary>
    /// The last few weeks of what a player ate, by vanilla food category, one entry per in-game day.
    /// Kept server side only (entity Attributes are saved but not sent to clients). Nothing reads it in v0.2;
    /// the effects module (v0.4) uses it to spot a diet that leans on one category.
    /// </summary>
    public class DietLedger
    {
        public const string AttributeKey = "complexnutrition-ledger";

        private readonly ITreeAttribute tree;

        private DietLedger(ITreeAttribute tree)
        {
            this.tree = tree;
        }

        public static DietLedger Of(Entity entity)
        {
            var tree = entity.Attributes.GetTreeAttribute(AttributeKey);
            if (tree == null)
            {
                tree = new TreeAttribute();
                entity.Attributes.SetAttribute(AttributeKey, tree);
            }
            return new DietLedger(tree);
        }

        private static string Key(int day) => "d" + day;

        public void Add(int day, Dictionary<string, float> kcalByCategory)
        {
            var dayTree = tree.GetOrAddTreeAttribute(Key(day));
            foreach (var kv in kcalByCategory)
            {
                if (kv.Value <= 0) continue;
                dayTree.SetFloat(kv.Key, dayTree.GetFloat(kv.Key) + kv.Value);
            }
        }

        public LedgerDay Get(int day)
        {
            var result = new LedgerDay { Day = day };
            var dayTree = tree.GetTreeAttribute(Key(day));
            if (dayTree == null) return result;
            foreach (var kv in dayTree)
            {
                if (kv.Value is FloatAttribute f) result.KcalByCategory[kv.Key] = f.value;
            }
            return result;
        }

        /// <summary>Every recorded day, oldest first.</summary>
        public List<LedgerDay> All()
        {
            return tree.Select(kv => kv.Key)
                .Where(k => k.StartsWith("d") && int.TryParse(k.Substring(1), out _))
                .Select(k => int.Parse(k.Substring(1)))
                .OrderBy(d => d)
                .Select(Get)
                .ToList();
        }

        /// <summary>Drops days older than <paramref name="keepDays"/> before <paramref name="today"/>.</summary>
        public void Prune(int today, int keepDays)
        {
            foreach (var day in All().Select(d => d.Day).Where(d => d <= today - keepDays || d > today).ToList())
            {
                tree.RemoveAttribute(Key(day));
            }
        }
    }
}
