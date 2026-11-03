# Mod page text (draft)

Text for the Vintage Story mod database and Workshop listing. Keep the disclaimer at the top of the published description.

---

> **⚠️ AI-assisted code disclaimer**
> The code, data and documentation in this repository were written with AI assistance (Anthropic's Claude), under human direction and review.
> The human in question is both a Youtuber and _Largely_ Unemployed, so do with this information what you will. The point of this project was to understand how easy it would be for someone with limited modding experience to make significant adjustments to the games they love. Turns out, not all that easy! 
> Please keep this in mind when using, auditing or building on this mod, and report anything that looks or plays wrong.
> The issues tab on the github repo will be monitored, but there are also alternative contact points listed below.

**Complex Nutrition** makes what you eat matter a lot more. Every food ingredient in vanilla Vintage Story gets real-world calories, protein, carbohydrate, fat, fiber, vitamins and minerals inspired by the Metabolism system from the game SCUM. Your body keeps reserves of each tracked macro- and micro-nutrient, and the balance of your diet will determine your overall health. Some effects show up and can be addressed quickly, while others take weeks to appear and resolve. 

- **Everything in the mod is modular and optional:** each component of the mod has its own switch, so you can keep the elements you like and turn off the rest. With all components switched off, the game will play exactly like vanilla.

Requires Vintage Story 1.22.7. Status: in development.

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
