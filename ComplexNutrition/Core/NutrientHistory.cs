using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace ComplexNutrition.Core
{
    /// <summary>One in-game day of history: each nutrient's reserve at the end of the day, and the calories eaten by food category.</summary>
    public class HistoryDay
    {
        public int Day;
        public Dictionary<string, float> KcalByCategory = new();
        /// <summary>Calories that went into the calorie reserve from digestion that day (v0.3.3).</summary>
        public float Gained;
        /// <summary>Calories burned from the reserve that day: activity and healing (v0.3.3).</summary>
        public float Burned;
        /// <summary>The diet score the player had that day (since 0.4.3b); null for a day with no record.</summary>
        public float? DietScore;
        /// <summary>That day's own score, worked out at its midnight (since 0.4.3b); null until then, or for a day not played.</summary>
        public float? DayScore;

        public float TotalKcal => KcalByCategory.Values.Sum();
    }

    /// <summary>What the server sends the window: the last few weeks, oldest first and ending today, and the reserves about a day ago.</summary>
    public class HistoryData
    {
        public int Today;
        public List<HistoryDay> Days = new();
        /// <summary>Reserves about a day ago (see <see cref="DayAgoHours"/>), for the trend arrows; null when not known yet.</summary>
        public Dictionary<string, float> DayAgo;
        /// <summary>How many in-game hours ago <see cref="DayAgo"/> was taken.</summary>
        public float DayAgoHours;
        /// <summary>Calories burned over the last in-game hour and the last 24 (v0.3.3).</summary>
        public float BurnedHour, Burned24;
    }

    /// <summary>
    /// The nutrition window's history (v0.3.2): each nutrient's reserve at the end of every in-game day, for the
    /// History tab, and once an hour over the last day, for the trend arrows. Kept server side in the entity's
    /// Attributes (saved, never synced), and sent to a player only when their window asks for it.
    /// </summary>
    public class NutrientHistory
    {
        public const string AttributeKey = "complexnutrition-history";
        /// <summary>Hourly slots kept: one more than a day, so the slot from 24 hours ago survives the current hour's write.</summary>
        public const int HourSlots = 25;
        /// <summary>A trend needs at least this many hours between the two readings.</summary>
        public const float MinTrendHours = 6f;
        /// <summary>A store that changed by less than this (in % of a day, scaled to a whole day) counts as steady.</summary>
        public const float SteadyBand = 15f;

        private readonly ITreeAttribute tree;

        private NutrientHistory(ITreeAttribute tree)
        {
            this.tree = tree;
        }

        public static NutrientHistory Of(Entity entity)
        {
            var tree = entity.Attributes.GetTreeAttribute(AttributeKey);
            if (tree == null)
            {
                tree = new TreeAttribute();
                entity.Attributes.SetAttribute(AttributeKey, tree);
            }
            return new NutrientHistory(tree);
        }

        /// <summary>A history over a bare tree (tests).</summary>
        public static NutrientHistory FromTree(ITreeAttribute tree) => new(tree);

        /// <summary>End-of-day reserves, recorded until v0.3.5 for the History line; now only cleared out by <see cref="Prune"/>.</summary>
        private ITreeAttribute Days => tree.GetOrAddTreeAttribute("days");
        private ITreeAttribute Flow => tree.GetOrAddTreeAttribute("flow");
        private ITreeAttribute FlowHours => tree.GetOrAddTreeAttribute("flowhours");

        /// <summary>Adds calories gained (into the reserve from digestion) or burned (out of it) at a game time.</summary>
        public void AddFlow(double nowHours, int day, float gained, float burned)
        {
            if (gained <= 0f && burned <= 0f) return;
            var dayTree = Flow.GetOrAddTreeAttribute("d" + day);
            if (gained > 0f) dayTree.SetFloat("gained", dayTree.GetFloat("gained") + gained);
            if (burned > 0f) dayTree.SetFloat("burned", dayTree.GetFloat("burned") + burned);

            long hour = (long)Math.Floor(nowHours);
            string key = "h" + (int)(((hour % 24) + 24) % 24);
            var slot = FlowHours.GetTreeAttribute(key);
            if (slot == null || slot.GetLong("hour", long.MinValue) != hour)
            {
                slot = new TreeAttribute();
                slot.SetLong("hour", hour);
                FlowHours[key] = slot;
            }
            if (burned > 0f) slot.SetFloat("burned", slot.GetFloat("burned") + burned);
        }

        /// <summary>Records the diet score a player has on a day (since 0.4.3b), for the History chart's diet line.</summary>
        public void SetDietScore(int day, float score) => Flow.GetOrAddTreeAttribute("d" + day).SetFloat("diet", score);

        /// <summary>Records a day's own score once its midnight has passed (since 0.4.3b).</summary>
        public void SetDayScore(int day, float score) => Flow.GetOrAddTreeAttribute("d" + day).SetFloat("dayScore", score);

        /// <summary>A day's diet score and its own score, each null when not recorded.</summary>
        public (float? diet, float? dayScore) DietDay(int day)
        {
            var dayTree = Flow.GetTreeAttribute("d" + day);
            if (dayTree == null) return (null, null);
            float? Read(string key) => dayTree.HasAttribute(key) ? dayTree.GetFloat(key) : null;
            return (Read("diet"), Read("dayScore"));
        }

        public (float gained, float burned) DayFlow(int day)
        {
            var dayTree = Flow.GetTreeAttribute("d" + day);
            return dayTree == null ? (0f, 0f) : (dayTree.GetFloat("gained"), dayTree.GetFloat("burned"));
        }

        /// <summary>
        /// Calories burned over the last <paramref name="hours"/> in-game hours (1 to 24). The oldest hour counts only for the
        /// part of it still inside the span.
        /// </summary>
        public float BurnedOver(double nowHours, int hours)
        {
            long now = (long)Math.Floor(nowHours);
            float into = (float)(nowHours - now);   // how far into the current hour
            float total = 0f;
            foreach (var kv in FlowHours)
            {
                if (kv.Value is not ITreeAttribute slot) continue;
                long h = slot.GetLong("hour", long.MinValue);
                float b = slot.GetFloat("burned");
                if (h > now - hours && h <= now) total += b;
                else if (h == now - hours) total += b * (1f - into);
            }
            return total;
        }
        private ITreeAttribute Hours => tree.GetOrAddTreeAttribute("hours");

        private static void Copy(ITreeAttribute into, IReadOnlyDictionary<string, float> reserves)
        {
            foreach (var kv in reserves) into.SetFloat(kv.Key, kv.Value);
        }

        private static Dictionary<string, float> Read(ITreeAttribute from)
        {
            var result = new Dictionary<string, float>();
            if (from == null) return result;
            foreach (var kv in from)
            {
                if (kv.Value is FloatAttribute f) result[kv.Key] = f.value;
            }
            return result;
        }

        /// <summary>The last whole game hour recorded, or long.MinValue.</summary>
        public long LastHour => tree.GetLong("lastHour", long.MinValue);

        /// <summary>Records the reserves at a whole game hour (once per hour; a repeat call in the same hour does nothing).</summary>
        public void RecordHour(long hour, IReadOnlyDictionary<string, float> reserves)
        {
            if (hour == LastHour) return;
            var slot = new TreeAttribute();
            slot.SetLong("hour", hour);
            Copy(slot, reserves);
            Hours["h" + (int)(((hour % HourSlots) + HourSlots) % HourSlots)] = slot;
            tree.SetLong("lastHour", hour);
        }

        /// <summary>Forgets the hourly readings (a respawned body starts its trends afresh).</summary>
        public void ClearHours()
        {
            tree["hours"] = new TreeAttribute();
            tree.RemoveAttribute("lastHour");
        }

        /// <summary>
        /// The oldest hourly reading from the last 24 hours, and how many hours ago it was; null when there is none at least
        /// <see cref="MinTrendHours"/> old.
        /// </summary>
        public (Dictionary<string, float> reserves, float hoursAgo)? DayAgo(double nowHours)
        {
            long now = (long)Math.Floor(nowHours);
            ITreeAttribute best = null;
            long bestHour = long.MaxValue;
            foreach (var kv in Hours)
            {
                if (kv.Value is not ITreeAttribute slot) continue;
                long h = slot.GetLong("hour", long.MinValue);
                if (h < now - 24 || h > now) continue;
                if (h < bestHour)
                {
                    bestHour = h;
                    best = slot;
                }
            }
            if (best == null) return null;
            float ago = (float)(nowHours - bestHour);
            if (ago < MinTrendHours) return null;
            var reserves = Read(best);
            return (reserves, ago);
        }

        /// <summary>Drops day records older than <paramref name="keepDays"/> before <paramref name="today"/>, and any from the future.</summary>
        public void Prune(int today, int keepDays)
        {
            var drop = new List<string>();
            foreach (var kv in Days)
            {
                if (!kv.Key.StartsWith("d") || !int.TryParse(kv.Key.Substring(1), out int day) || day <= today - keepDays || day > today) drop.Add(kv.Key);
            }
            foreach (var key in drop) Days.RemoveAttribute(key);
            drop.Clear();
            foreach (var kv in Flow)
            {
                if (!kv.Key.StartsWith("d") || !int.TryParse(kv.Key.Substring(1), out int day) || day <= today - keepDays || day > today) drop.Add(kv.Key);
            }
            foreach (var key in drop) Flow.RemoveAttribute(key);
        }

        /// <summary>
        /// The window's history: <paramref name="keepDays"/> days ending today, with the calories eaten from the diet ledger
        /// and the calories gained and burned.
        /// </summary>
        public HistoryData Build(DietLedger ledger, int today, double nowHours, int keepDays)
        {
            var data = new HistoryData { Today = today };
            for (int day = today - keepDays + 1; day <= today; day++)
            {
                var entry = new HistoryDay
                {
                    Day = day,
                };
                if (ledger != null) entry.KcalByCategory = ledger.Get(day).KcalByCategory;
                (entry.Gained, entry.Burned) = DayFlow(day);
                (entry.DietScore, entry.DayScore) = DietDay(day);
                data.Days.Add(entry);
            }
            data.BurnedHour = BurnedOver(nowHours, 1);
            data.Burned24 = BurnedOver(nowHours, 24);
            var ago = DayAgo(nowHours);
            if (ago != null)
            {
                data.DayAgo = ago.Value.reserves;
                data.DayAgoHours = ago.Value.hoursAgo;
            }
            return data;
        }

        /// <summary>
        /// Rising (1), steady (0) or falling (-1): the change from <paramref name="then"/> to <paramref name="now"/> over
        /// <paramref name="hours"/>, scaled to a whole day, against <see cref="SteadyBand"/>.
        /// </summary>
        public static int Trend(float then, float now, float hours)
        {
            if (hours <= 0) return 0;
            float perDay = (now - then) * 24f / hours;
            if (perDay > SteadyBand) return 1;
            if (perDay < -SteadyBand) return -1;
            return 0;
        }
    }
}
