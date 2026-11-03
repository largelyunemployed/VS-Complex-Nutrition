# Roadmap (draft)

A longer version of the roadmap in the README, for anyone who wants to know what each version will actually do. The README list is the official one; this page adds detail and will change as things get built. The banner image is `art/out/roadmap-1920.png` (edit the list at the top of `art/roadmap.html` and run `node art/render.js` to re-export it as versions ship).

Every module has its own switch. With all of them off, the game plays exactly like vanilla.

## v0.1: Food data (this release)

- Real-world nutrients for every vanilla food (341 items), from USDA FoodData Central, per 100 g.
- 14 tracked nutrients: calories; protein, carbohydrate, fat, fiber; vitamins A, D, E, K; vitamin C and the B vitamins; calcium, iron and potassium.
- Nutrition shown in item tooltips. No gameplay changes.
- Mod feasibility testing: the mod loads cleanly on a 1.22.7 server and a data table of every food is in [FOOD_DATA.md](FOOD_DATA.md).
- VS Mod DB thumbnail, description and roadmap drafts.

## v0.2: Nutrient tracking and the handbook guide

- Your body gets a reserve for each nutrient. Eating fills it, time drains it toward a daily target.
- Meals, stews and pies count their ingredients, split by servings.
- Eating a mountain of one thing has diminishing returns, so variety beats repetition.
- The vanilla food category bars and their health bonus are replaced (and come back if Tracking is switched off).
- An in-game handbook guide for all implemented systems (will expand with each update).

## v0.3: Nutrition UI

- A nutrition window (hotkey N) with tabs: overview, vitamins, minerals, and a 28-day history.
- A shorter tooltip block that expands on a key press.
- An optional little HUD icon that turns amber or red when something is running low.

## v0.4: Diet tiers and effects

- A diet score sets one of five tiers, from Thriving to Malnourished, with buffs and debuffs to healing, max health, walk speed and hunger rate.
- Effects build over in-game weeks; one bad meal does nothing.
- Different nutrients fail on different clocks: vitamin C and B in days, minerals in a week or two, vitamins A, D, E and K over weeks.
- Specific deficiencies have their own effects (scurvy slows healing, low iron costs max health, and so on), each with its own switch.

## v0.5: Health and effects UI

- A Health tab showing your tier, active effects and where they came from, and a hint about what to eat next.
- Small status icons for active effects.

## v1.0: Settings menu

- Every module and number adjustable in game, with sliders and toggles, as well as in the config file and by server command.

## v1.01: Nutrient uptake modifiers (stretch goal)

- Cold, injury and intoxication change how much you absorb and how fast you burn through it.
- A hook for disease mods to plug into.

## v1.02: Exercise and fitness (stretch goal)

- Sprinting, swimming, mining and hauling slowly build fitness, but only while you're eating well.

## v1.03: Who knows!

Suggestions welcome.
