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
- Fats, protein and carbohydrate are digested over hours, and only digestion fills the calorie reserve (10 active days since v0.2.4). An empty reserve starves you.
- Fiber speeds up digestion.

### v0.2.4: Start conditions

- A world setting, on the Complex Nutrition tab when you create a world, for how full the calorie reserve is when a character first joins: Full (10 days), Easy (7 days), Normal (5 days), Hard (3 days), Brutal (1 day) or Rock Bottom (0.01 days).
- The calorie reserve holds 10 active days, the stomach holds 600 fullness (100 an hour drained), and the gut absorbs faster the more it holds, up to a limit.
- An Advanced nutrition display setting (world default, per-player override with /cnutrition display) that shows calories, time and absorption rate for the food being digested.

## v0.3: Nutrition UI

- v0.3.0: a nutrition window (Left Alt + N), the size of the handbook, with Overview, Digestion, Vitamins and Minerals tabs, and a settings menu holding every toggle so far. Healing runs at full speed while the calorie reserve has anything in it.
- v0.3.1: a clean-up release with no new features: the code tidied so each module keeps to itself, settings read from one place, safer config loading, and automatic tests and build checks.
- v0.3.2: a respawn calories world setting (same choices as the start setting, default Brutal; respawning clears the 24-hour log); vitamin and mineral reserves that follow the start and respawn choice; fiber's speed-up shown as calories per hour in its tooltip and the window; food data in real units (mg and mcg); a 28-day history tab and trend arrows.
- v0.3.3: the Overview tab shows the calories your gut will still give and those lost to diminishing returns, calories burned over the last hour, day and week, and the cost of healing; the History tab charts calories gained and burned; healing costs calories per health point at a realistic rate (a recovering body needs 33 to 44 kcal per kg a day, for an 80 kg character), with healing items free; a shorter tooltip block that expands while you hold Ctrl, and an optional little HUD icon right of the hotbar that turns amber or red when something is running low.
- v0.3.4: a calorie rebalance, since reaching a day's calories takes far too much food. Diminishing returns become a setting that can be switched off, portion sizes are checked against real ones, and wild plants drop a range of portions when harvested (as mushrooms do), with farmed crops checked for the same. Also Discover Nutrition, a setting that hides a food's values ("???") until you have eaten it.
- v0.3.5: hover the History charts to see each day's makeup and totals and each point's calories; a pause button at the top of the nutrition window, like the handbook's; each chart's top value in yellow in its corner instead of a line of text; the dashed line becomes your average daily burn over the last 28 days (or as long as you have played), with an info symbol explaining it; less text on the History tab; and a max fullness slider (0 to 1,500, default 300; 0 means no limit), with fullness always draining 100 an hour and kept apart from every other calculation.
- v0.3.6: a simpler nutrition window and better food tooltips, and the last v0.3 release. Every food tooltip shows all its nutrients on four short lines ("Carb · Fat · Prot · Fib", "Vit A · B · C · D · E · K", "Ca · Fe · K"), in amounts or % as you choose, with pots, bowls and pies showing their whole contents. The window pages are redesigned: a Last 24 Hours table on the Overview, one coloured bar per macro on the Digestion tab, and one row per vitamin and mineral, with hovers for the details. Cooked grain and dough give their full value, and activities burn 5% fewer calories.

## v0.4: Diet tiers and effects

- v0.4.0: nutrient effects. Every vitamin and mineral gets an effect on a vanilla stat. The slow stores (vitamins A, D, E and K and the minerals) hold 20 days, give a 1% bonus for each day stored (up to 20%) and a 10% penalty when empty: A ranged accuracy, D physical damage resistance, E mining speed, K healing, calcium fall damage, iron max health, potassium walk speed, melee and ranged damage and butchering speed. Vitamin C (walk speed) and the B vitamins (hunger rate) only give a penalty, after 3 days without a full day's worth stored, growing until day 5. Sunlight makes vitamin D. A world switch for all effects and one per nutrient. Carbs, fats, protein and fiber have no effect yet.
- v0.4.1: healing uses up vitamin K (a tenth of a day's worth per health point; an empty store only slows it). Porridge and other pot meals count grains (x3) and dry soybeans (x2.5) at their cooked weight, so they fill you like the real thing. Every mushroom gets a signature vitamin or mineral by its colour (vitamin A, calcium, vitamin D or iron), and a Hide Poison Effects setting under Discover Nutrition hides a food's health loss until you have eaten it. The calorie reserve holds 4 days for now, with the groundwork for it to grow with your diet tier.
- v0.4.2: a diet score that builds over in-game weeks, so one bad meal does nothing. Gentler respawning: a switch to keep vitamin and mineral stores on death (on by default), and a Respawn with calories choice (lose calories on death, the default: 10% of what you had, at least 1,000kcal; keep calories; or reset calorie progress, for masochists only). The gut always empties. New start settings: starting diet tier, starting calories and starting vitamin and mineral stores, each as a share of the store.
- v0.4.3: five diet tiers, from Thriving to Malnourished, from the diet score, with buffs and debuffs to healing, max health, walk speed and hunger rate. A tier change in either direction only counts after 3 days in a row at the new tier.
- v0.4.4: the calorie reserve's size follows your diet tier: 2 days on the first tier and 2 more for each tier above it, up to 10 days. Dying doesn't shrink it.

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

## v1.0: Graphical UI overhaul

- A new look for the nutrition window, the settings menu and the HUD. (The settings menu itself arrived in v0.3.0.)
- A Complex Nutrition Guide (PDF) for new players, written once the core features are tested and settled: an overview, how nutrition is gained, each nutrient, the calorie system with tips, reading the UI, a food nutrient table and a modder's annex. Rebuilt with each release after that.

## After 1.0: Who knows!

Suggestions welcome.
