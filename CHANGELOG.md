# Changelog

## v0.2.1

- Vanilla satiety is now called Fullness: how much your stomach holds. The Stats panel, food and drink tooltips and the handbook use the new name, and hovering over the bar next to the hotbar shows "Fullness: 1070 / 1500".
- The nutrient bars on the Stats panel sit under a "Nutritional Reserves" heading, and every bar is split into one block per day of storage.
- Hovering over a nutrient bar also says what the nutrient does, and that its effect in game is planned for v0.4. The handbook's nutrient page marks each one the same way.
- Protein's daily need rises from 55 g to 128 g (1.6 g per kg of bodyweight for an 80 kg character), so one cooked bushmeat now fills about half a day instead of topping up the reserve.
- Amounts are now shown in real units by default: reserves on the Stats panel and in /nutrition in kcal, grams, mg or mcg (the B vitamins, a mix of six, stay in % of a day), and food tooltips list vitamins and minerals in mg or mcg as well. Set `DisplayUnits` to `percent` in `ModConfig/complexnutrition.json` to see everything as % of a day's need instead.
- Vitamin C and the B vitamins are listed above vitamins A, D, E and K, so the fastest-draining reserves sit at the top.
- The handbook's reserves page lists the daily values and store sizes in real units.
- Every mushroom species now has its own nutrient profile (45 in all). Chanterelle, morel, shiitake, oyster, field, almond and paddy straw mushrooms use real USDA data; the rest are estimates built on the nearest studied mushroom. All stay low in protein, calcium and iron and modest in vitamins D and B; oyster mushrooms, shiitake, field and almond mushrooms and Tapir's liver carry some vitamin C. Portions follow each mushroom's size, from a 5 g liberty cap to a 200 g puffball. The handbook explains it.
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
