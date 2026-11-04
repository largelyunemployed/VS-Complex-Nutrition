# Changelog

## v0.3.3

- The Overview tab tells you more about your calories:
  - how many calories the food in your gut will still give once it is digested, and how many of them are being lost to diminishing returns
  - the calories you burned in the last hour, the last 24 hours and the last 7 days
  - while you are hurt, what each health point costs to heal and about how much getting back to full health will cost
- The History tab has a second chart: the calories digestion added to your reserve each day (green) against the calories you burned (red), with the daily averages for the last week under it. Burned and gained calories start counting when you first play this version.
- Healing costs what a recovering body really needs. A body that is healing needs 33 to 44kcal per kg a day instead of about 25, more the worse the injury. For an 80kg character that makes each health point healed on your own cost about 22kcal when barely hurt and up to about 53kcal when badly hurt, so more damage costs more than its share to heal. This replaces vanilla's satiety cost. Healing items cost nothing. The numbers are in `config/tracking.json`.
- Short food tooltips: hovering over a food shows its calories and how much it fills you on one line, and holding Ctrl opens the full nutrient block. Bowls and pies work the same way. Each player can switch this off under Display in the settings menu.
- New Running Low icon: a small bowl right of the hotbar, dim while all is well, amber when something on the Running Low list is low and red when something is nearly gone or the calorie reserve is under a day. Click it to open the nutrition window. Each player can switch it off under Display in the settings menu.

## v0.3.2

- New History tab in the nutrition window: a chart of the last 28 days. Each bar is the calories you ate that day, coloured by food group (fruit, vegetables, protein foods, grains, dairy), with a dashed line at a day's need. A white line shows one store at the end of each day: the calorie tank, fiber or any vitamin or mineral, picked from the Line list at the bottom of the window. Under the chart, how much you ate a day over the last week, where most of it came from, and the picked store now and a week ago. History starts recording when you first play this version.
- Trend arrows on the Vitamins and Minerals tabs: beside each bar, green up, grey dash or amber down for whether that store went up, held steady or went down over the last day.
- The window's Settings button moved to the bottom left, to make room for the fifth tab.
- New world setting, Respawn calories, with the same choices as Starting calories (default Brutal: 1 day). It sets the calorie tank when you come back after dying (it used to be raised to at least 1 day). Respawning also empties your gut and clears the last 24 hours of digestion, so the new body starts fresh, and fiber empties too. Admins can change it in the settings menu.
- Vitamin and mineral stores follow the start and respawn choice: Full fills them, Easy 70%, Normal half, Hard 30%, Brutal 10%, Rock Bottom next to nothing. Worlds made before v0.2.4 keep the old starting stores.
- Fiber's speed-up is shown in calories: the fiber bar's hover and the window's Overview say how many extra calories an hour fiber is helping you digest right now.
- Food data stores vitamins and minerals in real units (mg and mcg) instead of % of the Daily Value. Nothing changes in play: every food has the same nutrition as before. The B vitamins stay in % (they are a mix of six).
- For modders: food profiles give vitamins and minerals in mg and mcg in `per100g`. A profile can give `per100gDV` (% of the Daily Value) instead, as before; it is turned into amounts on load. The compat template and `docs/COMPATIBILITY.md` use real units. Profiles from earlier versions that gave %DV in `per100g` need moving to `per100gDV`.
- `reviveCalories` is gone from `digestion.json` (the Respawn calories setting replaces it).

## v0.3.1

A clean-up release: nothing changes in play. The code is tidier and safer to build on before the next features.

- Each module keeps to itself. The Nutrient tracking module now does its own switching off (putting everyone back on vanilla values, at once or when they next join), and `/cnutrition info` and `/cnutrition meal` belong to the Complex Nutrition Tooltips module. Both still work as before.
- Every config file loads safely: a missing or broken file is logged and the built-in values are used, and those built-in values now match the shipped files (the stomach drained 50 an hour, not 100, if `digestion.json` was missing).
- Numbers live in one place:
  - The protein numbers (64g needed, 128g where the bar tops out) come from the protein gut pool in `digestion.json`. `proteinNeedGrams` and `proteinMaxGrams` are gone from it, and the server log warns if protein's `dailyTarget` in `nutrients.json` doesn't match.
  - The carbs, fats and protein caps are gone from `nutrients.json`; their size is `maxGrams` in `digestion.json`, as it already was in play.
  - The gut's contents are kept once, as each pool's reserve. The old batch lists in player saves are moved over and removed the first time each player joins.
  - Calories burned per point of vanilla satiety is now `kcalPerSatiety` in `digestion.json`, separate from the one in `foodfallbacks.json` that sizes foods without data. Both are 1.
  - The start options are listed once in the code, and a test checks `worldconfig.json` against them.
  - The version is set only in `modinfo.json`.
- `zeroAtGrams` (a pool option not used since v0.3.0) is removed.
- The pie tooltip patch runs on the client only, like the rest of the tooltips.
- Fixed: with Percent of a day's need chosen, the Stats panel's gut hovers (the advanced display, and protein's 24-hour line) and the nutrition window's carbs, fats and protein (Overview and Digestion tabs) stayed in grams and kcal. They now switch to percent like everything else. The handbook stays in grams.
- The settings menu's Amounts choice reads Amounts (Recommended).
- Switching nutrient tracking back on now says, in red at the bottom of the confirmation box, to close and reopen the character window to see the nutrition bars again.
- For developers: tests (`tests/ComplexNutrition.Tests`, 24 checks of the digestion maths and the config files), a GitHub check that builds and tests every push, the window preview tool (`tools/preview`) and a release script (`tools/release.sh`). See `docs/DEVELOPING.md`.

## v0.3.0

- New nutrition window. Press Left Alt + N (rebindable in the controls menu) or the new Details button on the Stats panel. It is the size of the handbook, scales the same way and refreshes every second. Four tabs:
  - Overview: calorie reserve and fullness, carbs, fats and protein digested lately against the point where they give fewer calories, a Running Low list with the best foods you know for each, and a one-line diet summary.
  - Digestion: fullness and when the stomach will be empty, each gut pool with the calories still to come, time left and absorption rate, the rolling totals against their limits, and the fiber speed-up.
  - Vitamins and Minerals: each one's store, days left, what it does and the best foods you know for it.
- Running Low lists the calorie reserve under 1 day, and vitamins A, D, E and K and the minerals under 2 days (amber, red under half a day). Carbs, fats, protein, fiber, vitamin C and the B vitamins refresh too quickly to need a warning. The thresholds are in `config/tracking.json`.
- Food suggestions only use foods you have held. The server remembers every food each player has had in their hotbar, backpack or hand.
- New settings menu: the Settings button in the nutrition window, or `/cnutrition settings`. Display (every player): Advanced nutrition display (follow the world, on or off) and Amounts or percent. Food Info, World and Modules (admins, and you in single player): food tooltips, the server's default for amounts or percent, logging foods without data, Starting calories, the Advanced display default, Poultry and bushmeat in pots, and the Complex Nutrition Tooltips and Nutrient tracking modules. Hover over a setting's name to see what it does.
- Amounts or percent is now each player's own choice, saved on their computer in `ModConfig/complexnutrition-client.json`, with the server's setting as the default.
- Nutrient tracking can be switched off and on while the game runs, after a confirmation. Switching off saves everyone's reserves and sets their vanilla food groups from them; switching on puts everyone back exactly where they were. Admins can also use `/cnutrition module <fooddata|tracking> <on|off>`.
- Cooking pots hold up to 10 of an ingredient per slot (vanilla 6), and cook meals of up to 10 servings.
- New world setting, Poultry and bushmeat in pots (off by default): raw, cooked and cured poultry and bushmeat count as meat in meat stews and soups. It takes effect the next time the world loads. Admins can also switch it on for any world, including ones made before v0.3.0, with `/cnutrition meatinpots on` (or `off`).
- Protein no longer stops giving calories at 128g. Up to 64g digested in 24 hours, each gram gives its full 4kcal; from 64g to 128g each gram gives a little less, down to 80%; past 128g each gram gives 50%.
- Healing works from the calorie tank: while tracking runs you heal at full speed whenever there are any calories in reserve, however full your stomach is (vanilla heals at full speed only above 75% satiety). Healing costs the same as in vanilla, taken as calories. With an empty tank you don't heal.
- All commands now start with `/cnutrition`: `/cnutrition stats` lists your reserves (was `/nutrition`), `/cnutrition display` sets your advanced display (was `/nutritiondisplay`) and `/cnutrition settings` opens the settings menu. The admin subcommands are unchanged.
- The Food data module is now called Complex Nutrition Tooltips in the settings menu.
- Fixed a crash when switching nutrient tracking off with the character screen open: the Stats panel now switches back to vanilla's.
- Fixed a stutter every few seconds while digesting with the character screen open: the Stats bars are redrawn a couple at a time, and only when they move by a pixel.
- The Protein bar on the Stats panel shows the protein in your gut, like Carbs and Fats. The amount taken in over the last 24 hours is in its hover and on the window's Overview.
- New handbook page, The Nutrition Window. The other pages mention the window, the settings, the bigger pots and the new protein curve.

## v0.2.4

- New world setting, Starting calories, on a new Complex Nutrition tab when you create a world. It sets how much is in a character's calorie tank the first time they join: Full (10 days), Easy (7 days), Normal (5 days, the default and the amount before this version), Hard (3 days), Brutal (1 day) or Rock Bottom (0.01 days, good luck!). Respawning is not affected. Server owners can change it later with `/worldconfig complexnutritionStart <1000|700|500|300|100|1>`.
- The calorie tank holds 10 active days (28,000 kcal), up from 7 (19,600 kcal). The HUD bar still flashes below 10% of a full tank, which is now 1 day.
- The stomach holds 600 fullness (up from 300, about 2 kg of food) and still empties in 6 hours, draining an even 100 an hour. A new character starts with an empty stomach, whatever the start condition.
- The gut absorbs each macro faster the more of it there is, up to a limit, instead of absorbing each meal at its own steady rate. Carbs absorb at up to 90 g an hour (half speed at 30 g), protein at up to 15 g an hour (half speed at 10 g) and fats at up to 6 g an hour (half speed at 15 g). So 100 g of carbs takes about 3 hours and 300 g about 5 and a half, 40 g of protein about 5 and a half hours, and 70 g of fats about a day. Fiber still raises the top speeds.
- New world setting, Advanced nutrition display (off by default). When it is on, hovering over the Carbs, Fats and Protein bars shows the grams being digested, the calories they will give after diminishing returns, how long they will take, and how fast they are being absorbed, in grams and kcal an hour. Each player can override it for themselves with `/nutritiondisplay advanced`, `/nutritiondisplay simple` or `/nutritiondisplay world`.
- Units now sit right against their numbers everywhere in game ("94g", "80kcal", "7.0mg", "4mcg"), and "h" is written as "hours". Hover boxes wrap at 550px.
- The hover boxes on the Stats panel and the HUD calorie bar are easier to read. Each one starts with the bar's name in its colour, then splits into sections with gold headings: Status (amounts and time left), About (what the nutrient does) and, where it applies, In Game. The "no effect yet" note is shown in grey. Amounts are coloured by unit everywhere the mod shows them (hovers, food tooltips and the handbook): grams green, kcal orange, mg, mcg and % DV (the B vitamins) yellow. The absorption rate segment ("at 6.4g (26kcal) an hour") is red, with its amounts still green and orange.
- The handbook's Nutrient Reserves page mentions the setting. The Digestion page explains that absorption times are for one serving on its own and gives examples of when each diminishing-returns limit is reached. Every amount in the handbook and tooltips carries its unit, with "fullness" as the unit for the stomach.

## v0.2.3

- Protein has one bar on the Stats panel. It shows protein taken in over the last 24 hours against the 64 g a day you need, and its hover text adds what is still being digested. The daily need falls from 128 g to 64 g (0.8 g per kg of bodyweight, enough to keep muscle), and 128 g is now the most the body uses in 24 hours.
- Protein's diminishing returns start at 65 g digested in 24 hours and fall to nothing at 128 g. Protein past that gives no calories and no benefit.
- The gut holds up to 240 g of fats (was 100 g).
- The Stats panel lists Calories, Carbs, Fats, Protein, then fiber, vitamins and minerals. The bars are labelled without "(gut)".
- New descriptions for calories, carbs, fats, protein and fiber on hover. Hover text on the Stats panel and the HUD calorie bar now wraps to a fixed width instead of running off screen. The Stats panel no longer stutters the game when your reserves change, such as after eating: a bar only draws its hover text while the mouse is over it, and only bars that visibly moved are redrawn.
- Porridge no longer counts cooking water toward fullness.
- Switching tracking off now sets your vanilla values from your Complex Nutrition ones: satiety from your calorie reserve, and the five food group bars from the nearest reserves. Your Complex Nutrition state is kept and comes back exactly as it was when you switch tracking on again, even if you ate in between.
- The handbook pages are rewritten, with title case headings, a clearer explanation of diminishing returns, and new text for Pies and Bread, Drinks, Large Meals, Mushrooms and Foods from Other Mods.

## v0.2.2

- Digestion. Eating no longer fills the calorie reserve directly: fats, protein and carbohydrate (net of fiber) go into the gut and are digested over time, and only what is digested becomes calories (4 kcal per g of carbohydrate and protein, 9 per g of fats). Carbs take about 3 hours, protein 6 and fats 24; the gut holds up to 500 g of carbs, 200 g of protein and 100 g of fats, and anything past that passes through unused. Above 300 g of carbs in 12 hours, 130 g of protein or 70 g of fats in a day, each extra gram gives fewer calories. Rules are in `config/digestion.json`.
- Fullness now comes from each food's weight: 0.3 per gram, out of a stomach that holds 300 (about 1 kg). Meals, pies and anything cooked, baked, smoked or cured fill 20% less; drinks fill half; porridge counts its cooking water. Fullness drains a fixed 50 per in-game hour and does nothing at 0. A food that doesn't fit has to wait until there is room. Food tooltips show how much each food fills.
- The calorie reserve is now a tank of 7 active days (19,600 kcal at 2,800 a day); a new character starts with 5 days. Activity burns it as it burned satiety. Starvation comes from an empty tank instead of an empty stomach, and after dying the tank holds at least a day's worth.
- The bar above the hotbar shows the calorie tank, one block per day, and flashes below 10%. Its hover text shows the tank and fullness.
- With a fiber reserve at 70% of a day's need or more, digestion runs faster (fats 30%, protein 20%, carbs 10%), less so as the reserve runs down.
- The Stats panel shows the gut (fats, protein and carbs being digested, with how long until they are absorbed), protein absorbed over the last 24 hours against the 128 g a day needed, and fullness out of 300. Tooltip calories are worked out from the macros, as the body gets them.
- Switching tracking off puts fullness back on vanilla's 1,500 scale.
- Fat is now called Fats everywhere: the Stats panel, tooltips, `/nutrition` and the handbook. It sits at the top of the reserves, just below calories, and food tooltips list it first among the macros.
- Larger stores: vitamin C, the B vitamins and fiber now hold up to 2 days (was a day and a half).
- New handbook page on digestion; the other pages are updated to match.

## v0.2.1

- Vanilla satiety is now called Fullness: how much your stomach holds. The Stats panel, food and drink tooltips and the handbook use the new name, and hovering over the bar next to the hotbar shows "Fullness: 1070 / 1500".
- The nutrient bars on the Stats panel sit under a "Nutritional Reserves" heading, and every bar is split into one block per day of storage.
- Hovering over a nutrient bar also says what the nutrient does, and that its effect in game is planned for v0.4. The handbook's nutrient page marks each one the same way.
- Protein's daily need rises from 55 g to 128 g (1.6 g per kg of bodyweight for an 80 kg character), so one cooked bushmeat now fills about half a day instead of topping up the reserve.
- Amounts are now shown in real units by default: reserves on the Stats panel and in /nutrition in kcal, grams, mg or mcg (the B vitamins, a mix of six, stay in % of a day), and food tooltips list vitamins and minerals in mg or mcg as well. Set `DisplayUnits` to `percent` in `ModConfig/complexnutrition.json` to see everything as % of a day's need instead.
- Vitamin C and the B vitamins are listed above vitamins A, D, E and K, so the fastest-draining reserves sit at the top.
- The handbook's reserves page lists the daily values and store sizes in real units.
- Every mushroom species now has its own nutrient profile (45 in all). Chanterelle, morel, shiitake, oyster, field, almond and paddy straw mushrooms use real USDA data; the rest are estimates built on the nearest studied mushroom. The real-data species keep their real values, so some stand out: morels are rich in iron, and chanterelles and morels in vitamin D. The estimates stay low in protein, calcium and iron and modest in vitamins D and B, with some vitamin C in a few, such as Tapir's liver. Each mushroom weighs what its held model suggests, from a 3 g pink bonnet to an 86 g chicken of the woods. Mushroom patches drawn as clusters now drop several mushrooms (a morel patch drops 4 to 7 and a liberty cap patch 4 to 8, a chanterelle or shiitake patch 2 to 4), and the forage stat still multiplies that. Fullness is shared across a patch, so one patch fills you about as much as before. The handbook explains it.
- Roadmap: new v0.7 body composition; exercise and fitness move to v0.8, nutrient uptake modifiers to v0.9, and "Who knows!" to after 1.0. v0.4 adds an effect for every nutrient, and larger macronutrient and calorie stores for a balanced diet.

## v0.2.0

- New Tracking module (on by default, `tracking` in `ModConfig/complexnutrition.json`): every player has a reserve of each of the 14 nutrients, measured in days of supply (100% = one day's need).
- Eating fills the reserves: single foods by portion, drinks by the litre, cooked dishes by the servings eaten, pie slices as a quarter of the pie. Spoiled food gives fewer nutrients, in step with its lower satiety. Satiety from other mods' eating code is counted from its food category.
- Diminishing returns: within one sitting (an in-game hour), intake above one and a half days' worth of a nutrient is absorbed at a falling rate.
- Reserves drain by one day's worth per in-game day. Calories burn with vanilla satiety instead, so moving, sprinting, cold and hunger-rate changes all count.
- Storage caps and starting reserves per nutrient group: vitamins A, D, E and K up to 25 days (start with 14), calcium, iron and potassium up to 10 days (start with 7), calories up to 5 days (start with 2), everything else a day and a half (start with 1). All set in `config/nutrients.json`.
- While tracking is on, the five vanilla food group bars on the Stats panel are replaced by 14 nutrient bars (hover for days left), and vanilla's +12.5 HP nutrition bonus is off. Switching tracking off restores both.
- 28-day diet ledger of calories per food category, kept on the server for later versions.
- `/nutrition` lists your reserves. Admin commands `/cnutrition reserves`, `/cnutrition setreserve` and `/cnutrition ledger`.
- Handbook guide pages: Eating well, Nutrient reserves, How food is counted, and The fourteen nutrients.
- Tracking rules in `config/tracking.json`. Events for other modules: `FoodEaten`, `FoodAbsorbed`, `DailyRollover` and `ReserveChanged`.

## v0.1.1 (2026-11-03)

First release, on GitHub.

- Fixed: the game crashed when joining a world with the mod installed.

## v0.1.0 (not released)

- Nutrient data for all 341 vanilla Vintage Story 1.22.7 foods, from USDA FoodData Central (SR Legacy), with a source for every food in [docs/FOOD_DATA.md](docs/FOOD_DATA.md).
- 14 nutrients defined in `config/nutrients.json`: calories, protein, carbohydrate, fat, fiber, vitamins A, D, E, K, C, the B vitamins, calcium, iron and potassium.
- Each food has a real-world weight per item (grams per litre for liquids), independent of vanilla satiety.
- Baking state matters: part-baked bread keeps 50% of a perfect loaf's nutrients, charred bread 20%.
- Gameplay multipliers for food states: raw meat, poultry, fish and eggs x0.8; cured and smoked meat and fish lose 40% of vitamin C and B; vintage red meat x1.1; raw grain x0.6, dough x0.8; raw cassava x0.3, soaked x0.8; pickled vegetables lose 40% of vitamin C; ciders x0.9.
- Cooked dishes (soups, stews, porridges in a bowl, pot or crock) add up their ingredients per serving, then apply cooking multipliers from `config/cooking.json` (calories +20%, among others). Pies do the same for their remaining slices, scaled by bake state.
- Admin command `/cnutrition meal <container> <servings> <ingredients...>` to work out any dish.
- Support for other mods' foods: any mod can add `config/foodprofiles/*.json` files; profiles can copy another food (`like`), adjust it (`scale`) and override ours (`priority`). `/cnutrition template <domain>` writes a pre-filled file for a mod's foods. Guide in [docs/COMPATIBILITY.md](docs/COMPATIBILITY.md), template content mod in `templates/compat-mod/`.
- Food tooltips show calories, macros and the top three micronutrients per portion.
- Foods from other mods get an estimate from their vanilla food category; the server log lists each one.
- Admin commands: `/cnutrition coverage`, `/cnutrition info <code>`, `/cnutrition meal ...`, `/cnutrition dump`.
- Config file `ModConfig/complexnutrition.json` with a switch per module.
- Mod page thumbnail and roadmap banner (`art/`), drawn in the game's own UI style with a blocky Vintage Story character.
- Installation steps in the README and mod page.
