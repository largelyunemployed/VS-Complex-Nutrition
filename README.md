# Complex Nutrition (Vintage Story mod)

> **⚠️ AI-assisted code disclaimer**
> The code, data and documentation in this repository were written with AI assistance (Anthropic's Claude), under human direction and review. Please keep this in mind when using, auditing or building on it, and report anything that looks wrong.

Complex Nutrition expands Vintage Story's nutrition system. Every food gets real-world calories, macronutrients (protein, carbohydrate, fat, fiber), vitamins and minerals. Your body tracks reserves of each, and a balanced diet over in-game weeks earns buffs while a narrow one slowly costs you.

**Target game version:** 1.22.7 (latest stable)
**Status:** design stage, no playable release yet.

## Modular by design

Every feature is a separate module with its own on/off switch. Turning one off never breaks the others, and with all modules off the game plays like vanilla.

| Module | Version | What it does |
| --- | --- | --- |
| Food data | v0.1 | Real-world nutrient profiles for every vanilla food |
| Tracking | v0.2 | Per-nutrient body reserves; replaces the vanilla category bars |
| Nutrition UI | v0.3 | Nutrition window, tooltips and optional HUD indicator |
| Effects | v0.4 | Five diet tiers (Thriving to Malnourished) with buffs and debuffs |
| Health UI | v0.5 | Shows your diet tier, active effects and what to eat next |
| Settings menu | v1.0 | In-game sliders and toggles for every module |
| Modifiers | v1.01 | Body temperature, injury and illness change nutrient uptake |
| Exercise | v1.02 | Activity plus good diet gives slow, permanent fitness gains |

## Tracked nutrients (default set)

- **Energy:** calories
- **Macros:** protein, carbohydrate, fat, fiber
- **Fat-soluble vitamins:** A, D, E, K
- **Water-soluble vitamins:** C, B-complex
- **Minerals:** calcium, iron, potassium

## Roadmap

- **v0.1:** nutrient data for all vanilla foods, mod write-up, Workshop thumbnail and roadmap image
- **v0.2:** nutrient tracking and an in-game handbook guide
- **v0.3:** nutrition UI
- **v0.4:** diet tiers and effects
- **v0.5:** health and effects UI
- **v1.0:** settings menu
- **v1.01:** nutrient uptake modifiers (stretch goal)
- **v1.02:** exercise and fitness (stretch goal)

## Inspiration

The per-food nutrient profiles and diet-driven buffs are inspired by the metabolism system in SCUM, adapted to suit Vintage Story.

## License

MIT, see [LICENSE](LICENSE).
