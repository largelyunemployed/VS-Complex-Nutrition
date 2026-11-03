# Changelog

## v0.1.0 (unreleased)

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
