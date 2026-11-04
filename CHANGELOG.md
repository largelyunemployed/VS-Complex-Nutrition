# Changelog

## v0.2.4

- New world setting, Starting calories, on a new Complex Nutrition tab when you create a world. It sets how much is in a character's calorie tank the first time they join: Full (10 days), Easy (7 days), Normal (5 days, the default and the amount before this version), Hard (3 days), Brutal (1 day) or Rock Bottom (0.01 days, good luck!). Respawning is not affected. Server owners can change it later with `/worldconfig complexnutritionStart <1000|700|500|300|100|1>`.
- The calorie tank holds 10 active days (28,000 kcal), up from 7 (19,600 kcal). The HUD bar still flashes below 10% of a full tank, which is now 1 day.
- The handbook's Nutrient Reserves page mentions the setting.

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
