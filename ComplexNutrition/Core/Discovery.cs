using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace ComplexNutrition.Core
{
    /// <summary>
    /// Discover Nutrition (v0.3.4): with the world setting on, a food's tooltip shows "???" until the player has eaten it.
    /// Every food a player eats is remembered on the server whether the setting is on or not (so switching it on later
    /// doesn't hide foods they already know), in WatchedAttributes["complexnutritionEaten"], which is synced to their client.
    /// Eating a meal or pie teaches each of its ingredients.
    /// </summary>
    public static class Discovery
    {
        public const string EatenKey = "complexnutritionEaten";

        /// <summary>The food codes a stack teaches when eaten: the food itself, a container's drink, or a dish's ingredients.</summary>
        public static List<string> CodesOf(IWorldAccessor world, ItemStack stack)
        {
            var codes = new List<string>();
            var coll = stack?.Collectible;
            if (coll?.Code == null) return codes;
            if (coll is IBlockMealContainer meal)
            {
                var contents = world == null ? null : meal.GetNonEmptyContents(world, stack);
                foreach (var c in contents ?? new ItemStack[0]) codes.AddRange(CodesOf(world, c));
                return codes;
            }
            if (coll is BlockLiquidContainerBase container)
            {
                var content = container.GetContent(stack);
                if (content != null) codes.AddRange(CodesOf(world, content));
                return codes;
            }
            codes.Add(coll.Code.ToString());
            return codes;
        }

        /// <summary>The codes this entity has eaten, or an empty array.</summary>
        public static string[] Eaten(Entity entity) =>
            (entity?.WatchedAttributes[EatenKey] as StringArrayAttribute)?.value ?? Array.Empty<string>();

        /// <summary>Server: remembers that the entity ate these codes. Sends the list only when something new is added.</summary>
        public static void Record(Entity entity, IEnumerable<string> codes)
        {
            if (entity == null) return;
            var eaten = Eaten(entity);
            var added = codes.Where(c => c != null && Array.IndexOf(eaten, c) < 0).Distinct().ToList();
            if (added.Count == 0) return;
            entity.WatchedAttributes[EatenKey] = new StringArrayAttribute(eaten.Concat(added).OrderBy(c => c).ToArray());
            entity.WatchedAttributes.MarkPathDirty(EatenKey);
        }

        /// <summary>Whether the entity knows everything in this stack: the food, the drink, or every ingredient of a dish.</summary>
        public static bool Knows(Entity entity, IWorldAccessor world, ItemStack stack)
        {
            var codes = CodesOf(world, stack);
            if (codes.Count == 0) return true;
            var eaten = Eaten(entity);
            return codes.All(c => Array.IndexOf(eaten, c) >= 0);
        }
    }
}
