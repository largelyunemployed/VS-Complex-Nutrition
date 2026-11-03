# Changelog

## v0.1.0 (unreleased)

- Nutrient data for all 341 vanilla Vintage Story 1.22.7 foods, from USDA FoodData Central (SR Legacy), with a source for every food in [docs/FOOD_DATA.md](docs/FOOD_DATA.md).
- 14 nutrients defined in `config/nutrients.json`: calories, protein, carbohydrate, fat, fiber, vitamins A, D, E, K, C, the B vitamins, calcium, iron and potassium.
- Food tooltips show calories, macros and the top three micronutrients per portion.
- Foods from other mods get an estimate from their vanilla food category; the server log lists each one.
- Admin commands: `/cnutrition coverage`, `/cnutrition info <code>`, `/cnutrition dump`.
- Config file `ModConfig/complexnutrition.json` with a switch per module.
- Mod page thumbnail and roadmap banner (`art/`).
