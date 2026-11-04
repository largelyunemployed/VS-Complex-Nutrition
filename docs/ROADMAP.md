# Roadmap (draft)

A longer version of the roadmap in the README, for anyone who wants to know what each version will actually do. The README list is the official one; this page adds detail and will change as things get built. The banner image is `art/out/roadmap-1920.png` (edit the list at the top of `art/roadmap.html` and run `node art/render.js` to re-export it as versions ship).

Every module has its own switch. With all of them off, the game plays exactly like vanilla.

## v0.1: Food data (released)

- Real-world nutrients for every vanilla food (341 items), from USDA FoodData Central, per 100 g.
- 14 tracked nutrients: calories; fats, protein, carbohydrate, fiber; vitamins A, D, E, K; vitamin C and the B vitamins; calcium, iron and potassium.
- Nutrition shown in item tooltips. No gameplay changes.
- Mod feasibility testing: the mod loads cleanly on a 1.22.7 server and a data table of every food is in [FOOD_DATA.md](FOOD_DATA.md).
- VS Mod DB thumbnail, description and roadmap drafts.

## v0.2: Nutrient tracking and the handbook guide (released)

- Your body gets a reserve for each nutrient. Eating fills it, time drains it toward a daily target.
- Meals, stews and pies count their ingredients, split by servings.
- Eating a mountain of one thing has diminishing returns, so variety beats repetition.
- The vanilla food category bars and their health bonus are replaced (and come back if Tracking is switched off).
- An in-game handbook guide for all implemented systems (will expand with each update).

### v0.2.2: Digestion

- Fullness comes from each food's weight and empties in 6 hours; cooked food fills you less, so cooking lets you eat more.
- Fats, protein and carbohydrate are digested over hours, and only digestion fills the calorie tank (10 active days since v0.2.4). An empty tank starves you.
- Fiber speeds up digestion.

### v0.2.4: Start conditions

- A world setting, on the Complex Nutrition tab when you create a world, for how full the calorie tank is when a character first joins: Full (10 days), Easy (7 days), Normal (5 days), Hard (3 days), Brutal (1 day) or Rock Bottom (0.01 days).

## v0.3: Nutrition UI

- A nutrition window (hotkey N) with tabs: overview, vitamins, minerals, and a 28-day history.
- A shorter tooltip block that expands on a key press.
- An optional little HUD icon that turns amber or red when something is running low.

## v0.4: Diet tiers and effects

- A diet score sets one of five tiers, from Thriving to Malnourished, with buffs and debuffs to healing, max health, walk speed and hunger rate.
- Effects build over in-game weeks; one bad meal does nothing.
- Different nutrients fail on different clocks: vitamin C and B in days, minerals in a week or two, vitamins A, D, E and K over weeks.
- Specific deficiencies have their own effects (scurvy slows healing, low iron costs max health, and so on), each with its own switch.
- Every nutrient gets a gameplay effect of its own, even if a small one.
- A balanced, healthy micronutrient profile raises how much fats, protein, carbohydrate and fiber you can store, and with them the calorie store.

## v0.5: Health and effects UI

- A Health tab showing your tier, active effects and where they came from, and a hint about what to eat next.
- Small status icons for active effects.

## v0.6: Hidden allergies (optional, off by default)

- Each character gets 0 to 3 hidden food allergies (a world setting, 0 to 10), seeded per world and player, kept through death.
- Allergens follow real allergen families mapped to vanilla foods (dairy, egg, fish, wheat-type grains, nightshades, stone fruit, berries and so on). Flax is excluded unless a sub-setting allows it.
- You find them by tasting. Reactions, including from meals and pies, hurt and slow you for a while but never kill.

## v0.7: Body composition

- A simple scale from lean and muscular to heavier and fatter, with different gameplay effects for different builds.
- Driven by calorie surplus or deficit and how active you are: a surplus with activity builds muscle and loses fat, a surplus without activity adds fat, a deficit with activity loses muscle and fat, and a deficit without it loses a little of both.
- Possible effects: more fat insulates against the cold and softens blows a little; more muscle hits harder and works faster but burns far more calories.
- A bodyweight for each character. Needs that depend on size follow it, starting with protein (1.6 g per kg; until then, every character counts as 80 kg).

## v0.8: Exercise and fitness

- Sprinting, swimming, mining and hauling slowly build fitness, but only while you're eating well.

## v0.9: Nutrient uptake modifiers

- Cold, injury and intoxication change how much you absorb and how fast you burn through it.
- A hook for disease mods to plug into.

## v1.0: Settings menu

- Every module and number adjustable in game, with sliders and toggles, as well as in the config file and by server command.

## After 1.0: Who knows!

Suggestions welcome.
