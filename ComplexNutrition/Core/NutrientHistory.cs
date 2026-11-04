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
        /// <summary>Reserves in % of a day's need, at the end of the day (for today: now). Empty when nothing was recorded.</summary>
        public Dictionary<string, float> Reserves = new();
        public Dictionary<string, float> KcalByCategory = new();

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

        private ITreeAttribute Days => tree.GetOrAddTreeAttribute("days");
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

        /// <summary>Records the reserves a day ended with.</summary>
        public void RecordDay(int day, IReadOnlyDictionary<string, float> reserves)
        {
            var dayTree = new TreeAttribute();
            Copy(dayTree, reserves);
            Days["d" + day] = dayTree;
        }

        public Dictionary<string, float> DayReserves(int day) => Read(Days.GetTreeAttribute("d" + day));

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
        }

        /// <summary>
        /// The window's history: <paramref name="keepDays"/> days ending today (today's reserves are <paramref name="current"/>),
        /// with the calories eaten from the diet ledger.
        /// </summary>
        public HistoryData Build(DietLedger ledger, IReadOnlyDictionary<string, float> current, int today, double nowHours, int keepDays)
        {
            var data = new HistoryData { Today = today };
            for (int day = today - keepDays + 1; day <= today; day++)
            {
                var entry = new HistoryDay
                {
                    Day = day,
                    Reserves = day == today ? new Dictionary<string, float>(current) : DayReserves(day),
                };
                if (ledger != null) entry.KcalByCategory = ledger.Get(day).KcalByCategory;
                data.Days.Add(entry);
            }
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
