# Mod page text, v0.1 release (draft)

Draft for the mod's GitHub page (the mod is GitHub only until further notice; Miles, 2026-11-09). It keeps Miles's wording from [../MODPAGE.md](../MODPAGE.md) for the disclaimer, pitch and credits, and adds sections for what v0.1 actually does. Rewrite freely; nothing here replaces MODPAGE.md until Miles says so.

Images: thumbnail `art/out/thumbnail-512.png` (blocky Vitruvian character), roadmap banner `art/out/roadmap-1920.png`.

---

> **⚠️ AI-assisted code disclaimer**
> The code, data and documentation in this repository were written with AI assistance (Anthropic's Claude), under human direction and review.
> The human in question is both a Youtuber and _Largely_ Unemployed, so do with this information what you will. The point of this project was to understand how easy it would be for someone with limited modding experience to make significant adjustments to the games they love. Turns out, not all that easy!
> Please keep this in mind when using, auditing or building on this mod, and report anything that looks or plays wrong.
> The issues tab on the github repo will be monitored, but there are also alternative contact points listed below.

**Complex Nutrition** makes what you eat matter a lot more. Every food ingredient in vanilla Vintage Story gets real-world calories, protein, carbohydrate, fat, fiber, vitamins and minerals inspired by the Metabolism system from the game SCUM. Your body keeps reserves of each tracked macro- and micro-nutrient, and the balance of your diet will determine your overall health. Some effects show up and can be addressed quickly, while others take weeks to appear and resolve.

- **Everything in the mod is modular and optional:** each component of the mod has its own switch, so you can keep the elements you like and turn off the rest. With all components switched off, the game will play exactly like vanilla.

Requires Vintage Story 1.22.7. Status: in development (v0.1, food data only).

## What v0.1 does

Not a lot yet, on purpose. v0.1 is the foundation everything else gets built on:

- **Nutrient data for all 341 vanilla foods.** Fruit, vegetables, mushrooms, grains, doughs, breads, meats, fish, dairy, honey, juices, ciders and spirits. The numbers come from the USDA FoodData Central database, per 100 g, and the full table (with a source link for every food) is on GitHub.
- **Nutrition in the item tooltip.** Hover over a food, a bowl of stew or a pie to see calories, protein, carbs, fat, fiber, and the three micronutrients it is richest in, per portion.
- **Nothing changes how you play.** Your hunger bar, food categories and health bonus are all still vanilla. Eat what you like; the mod is only telling you what's in it for now.

A few honest caveats:

- Every food has a real-world weight (an apple is 180 g, a loaf of bread 225 g, a cabbage head 900 g), so its calories are what that much real food would give you. That means they don't always line up with how full vanilla says it makes you: a mushroom is filling but has almost no calories, and flax bread is the opposite.
- USDA has never analysed a beautyberry, a saguaro fruit or a cattail root, so those borrow from their closest real relative (elderberry, prickly pear, arrowhead). Insects aren't in USDA at all and use an average "protein food" profile for now. All of these are flagged in the data table.
- Soups, stews, porridges and pies add up their ingredients, then cooking gives them a boost (20% more calories, easier-to-digest protein and starch) at the cost of some vitamin C and B. Raw meat and raw grain are worth less than cooked, and burnt bread is mostly charcoal.

## Roadmap

- **v0.1:** nutrient data for all vanilla foods, mod feasibility testing, mod page thumbnail, description, and roadmap drafts. (You are here.)
- **v0.2:** nutrient tracking and an in-game handbook guide for all implemented systems (will expand with each update).
- **v0.3:** nutrition UI
- **v0.4:** diet tiers and effects
- **v0.5:** health and effects UI
- **v0.6:** hidden food allergies (optional, off by default)
- **v1.0:** settings menu
- **v1.01:** nutrient uptake modifiers (stretch goal)
- **v1.02:** exercise and fitness (stretch goal)
- **v1.03:** Who knows! (Suggestions welcome)

## For modpack makers and other modders

- Foods from other mods work out of the box: each gets an estimate from its vanilla food category, and the server log lists every one, so you know which ones to fill in.
- Adding real data for them takes one JSON file and no code. Food mods can include it with no dependency (it's ignored without Complex Nutrition), or anyone can ship it as a small compat mod. `/cnutrition template <mod id>` writes a pre-filled file for every food in a mod, and a profile can just say "like a vanilla blueberry". The guide and a template compat mod are on [GitHub](https://github.com/largelyunemployed/VS-Complex-Nutrition/blob/main/docs/COMPATIBILITY.md).
- Server admins can run `/cnutrition coverage` to see the foods still on an estimate, and `/cnutrition info <item code>` to see any food's numbers.

## Installation

1. Download the latest `complexnutrition_<version>.zip` from the [Releases page on GitHub](https://github.com/largelyunemployed/VS-Complex-Nutrition/releases).
2. Don't unzip it. Drop the zip as it is into your Mods folder:
   - **Windows:** `%appdata%\VintagestoryData\Mods`
   - **Linux:** `~/.config/VintagestoryData/Mods`
   - **macOS:** `~/Library/Application Support/VintagestoryData/Mods`
3. Start the game. Complex Nutrition should show up, switched on, in the Mods list on the main menu.

It has only been tested on Vintage Story 1.22.7 so far. Other versions might work, or might not.

- **Multiplayer:** install it on the server. Players need it too; the game normally offers to download a server's missing mods when you join, or they can install it the same way.
- **Settings:** the first launch creates `VintagestoryData/ModConfig/complexnutrition.json`. Each module has a `true`/`false` switch under `Modules`, and `ShowTooltip` turns the nutrition lines in item tooltips on or off. Restart the game (or server) after editing it.
- **Removing it:** v0.1 doesn't save anything into your world, so you can take it out of the Mods folder at any time and carry on as if nothing happened. Later versions will store your body's nutrient reserves, and the mod page will say so when that changes.
- **Building it yourself:** see [DEVELOPING.md](https://github.com/largelyunemployed/VS-Complex-Nutrition/blob/main/docs/DEVELOPING.md) in the GitHub repo.

**Designed by:** [Largely Unemployed](https://www.youtube.com/@LargelyUnemployed)

**All the real work done by:** [Claude](https://claude.ai/)

**Court-mandated discord server:** available on YT channel page (sorry bots, not today!)

**Website:** [largelyunemployed.com](https://largelyunemployed.com/)
