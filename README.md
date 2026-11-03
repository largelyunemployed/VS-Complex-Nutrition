# Complex Nutrition (Vintage Story mod)

> **⚠️ AI-assisted code disclaimer**
> The code, data and documentation in this repository were written with AI assistance (Anthropic's Claude), under human direction and review.

> The human in question is both a Youtuber and _Largely_ Unemployed, so do with this information what you will. The point of this project was to understand how easy it would be for someone with limited modding experience to make significant adjustments to the games they love. Turns out, not all that easy! 

> Please keep this in mind when using, auditing or building on this mod, and report anything that looks or plays wrong.
> The issues tab on the github repo will be monitored, but there are also alternative contact points listed below.

Complex Nutrition expands Vintage Story's nutrition system. Every food gets real-world calories, macronutrients (protein, carbohydrate, fat, fiber), vitamins and minerals. Your body tracks reserves of each, and a balanced diet over in-game weeks earns buffs while a narrow one slowly costs you.

**Target game version:** 1.22.7 (latest stable)
**Status:** design stage, no playable release yet.
**VS Mod DB page:** [placeholder]

**Designed by:** Largely Unemployed - https://www.youtube.com/@LargelyUnemployed
**All the real work done by:** Claude
**Court-mandated discord server:** available on YT channel page (sorry bots, not today!)
**Website:** largelyunemployed.com

## Installation

1. Download the latest `complexnutrition_<version>.zip` from the VS Mod DB page or the [Releases page on GitHub](https://github.com/largelyunemployed/VS-Complex-Nutrition/releases).
2. Don't unzip it. Drop the zip as it is into your Mods folder:
   - **Windows:** `%appdata%\VintagestoryData\Mods`
   - **Linux:** `~/.config/VintagestoryData/Mods`
   - **macOS:** `~/Library/Application Support/VintagestoryData/Mods`
3. Start the game. Complex Nutrition should show up, switched on, in the Mods list on the main menu.

It has only been tested on Vintage Story 1.22.7 so far. Other versions might work, or might not.

- **Multiplayer:** install it on the server. Players need it too; the game normally offers to download a server's missing mods when you join, or they can install it the same way.
- **Settings:** the first launch creates `VintagestoryData/ModConfig/complexnutrition.json`. Each module has a `true`/`false` switch under `Modules`, and `ShowTooltip` turns the nutrition lines in item tooltips on or off. Restart the game (or server) after editing it.
- **Removing it:** v0.1 doesn't save anything into your world, so you can take it out of the Mods folder at any time and carry on as if nothing happened. Later versions will store your body's nutrient reserves, and the mod page will say so when that changes.
- **Building it yourself:** see [docs/DEVELOPING.md](docs/DEVELOPING.md).

## Roadmap

- **v0.1:** nutrient data for all vanilla foods, mod feasibility testing, VS Mod DB thumbnail, description, and roadmap drafts.
- **v0.2:** nutrient tracking and an in-game handbook guide for all implemented systems (will expand with each update).
- **v0.3:** nutrition UI
- **v0.4:** diet tiers and effects
- **v0.5:** health and effects UI
- **v0.6:** hidden food allergies (optional, off by default)
- **v1.0:** settings menu
- **v1.01:** nutrient uptake modifiers (stretch goal)
- **v1.02:** exercise and fitness (stretch goal)
- **v1.03:** Who knows! (Suggestions welcome)

## Modular by design

Every feature is a separate module with its own on/off switch. 
Turning one module off never breaks the others, and with all modules off the game simply plays like vanilla.

| Module | Version | What it does |
| --- | --- | --- |
| Food data | v0.1 | Real-world nutrient profiles for every vanilla food |
| Tracking | v0.2 | Per-nutrient body reserves; replaces the vanilla category bars |
| Nutrition UI | v0.3 | Nutrition window, tooltips and optional HUD indicator |
| Effects | v0.4 | Five diet tiers (Thriving to Malnourished) with buffs and debuffs |
| Health UI | v0.5 | Shows your diet tier, active effects and what to eat next |
| Allergies | v0.6 | Optional, off by default: 0 to 3 hidden food allergies per character, found by tasting; reactions hurt and slow you but never kill |
| Settings menu | v1.0 | In-game sliders and toggles for every module |
| Modifiers | v1.01 | Body temperature, injury and illness change nutrient uptake |
| Exercise | v1.02 | Activity plus good diet gives slow, permanent fitness gains |

## Tracked nutrients (default set)

- **Energy:** calories
- **Macros:** protein, carbohydrate, fat, fiber
- **Fat-soluble vitamins:** A, D, E, K
- **Water-soluble vitamins:** C, B-complex
- **Minerals:** calcium, iron, potassium

## Compatibility with other food mods

Foods from other mods work straight away, using an estimate based on their vanilla food category. To give them proper numbers, a mod (or a modpack maker) only needs to add one JSON file, with no code and no hard dependency. The guide is in [docs/COMPATIBILITY.md](docs/COMPATIBILITY.md), and a ready-to-fill compat mod is in [templates/compat-mod](templates/compat-mod/).

## Inspiration

The per-food nutrient profiles and diet-driven buffs are inspired by the metabolism system in SCUM, adapted to suit Vintage Story.

## License

MIT, see [LICENSE](LICENSE).
