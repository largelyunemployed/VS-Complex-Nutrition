# Developing Complex Nutrition

## Layout

| Path | What it is |
| --- | --- |
| `ComplexNutrition/` | The mod: C# project, `modinfo.json`, `modicon.png` and `assets/` |
| `ComplexNutrition/Core/` | Config, nutrient registry, food database, module loader (`ComplexNutritionModSystem`) |
| `ComplexNutrition/Modules/FoodData/` | v0.1 module: tooltip behaviour, cooked-dish sums, pie tooltip patch, fallback logging |
| `ComplexNutrition/assets/complexnutrition/config/cooking.json` | Multipliers for cooked dishes and pie bake states |
| `ComplexNutrition/assets/complexnutrition/config/nutrients.json` | The 14 nutrients, their units and daily targets |
| `ComplexNutrition/assets/complexnutrition/config/foodprofiles/*.json` | Per-100 g food profiles (generated) |
| `ComplexNutrition/assets/complexnutrition/config/foodfallbacks.json` | Category templates for foods with no profile (generated) |
| `tools/build_food_profiles.py` | Builds the profiles from USDA data |
| `tools/data/food_sources.json` | Which USDA food each vanilla food uses (edit this, not the generated JSON) |
| `tools/data/vanilla_foods_1.22.7.json` | Every vanilla food with its satiety and category, dumped from a 1.22.7 server |
| `docs/FOOD_DATA.md` | Generated table of every food |
| `art/` | Thumbnail and roadmap banner sources (`node art/render.js` exports PNGs to `art/out/`) |

## Building

Needs the .NET 10 SDK (Vintage Story 1.22 runs on .NET 10) and a copy of the game or the dedicated server for `VintagestoryAPI.dll`.

```sh
export VINTAGE_STORY=/path/to/vintagestory      # folder that holds VintagestoryAPI.dll
dotnet build -c Release ComplexNutrition/ComplexNutrition.csproj
```

The release build writes `release/complexnutrition_<version>.zip`; drop it into the game's `Mods` folder.

## Testing on a headless server

```sh
# server: https://cdn.vintagestory.at/gamefiles/stable/vs_server_linux-x64_1.22.7.tar.gz
mkdir -p data/Mods && cp release/complexnutrition_0.1.0.zip data/Mods/
dotnet VintagestoryServer.dll --dataPath ./data
```

The log should show `Loaded 14 nutrients and 164 food profiles`, `Nutrient info added to 341 foods`, and a coverage line listing the three insect foods on the fallback. Then, in the server console:

- `/cnutrition coverage` lists foods without their own profile.
- `/cnutrition info game:fruit-blueberry` prints that item's full tooltip, including our block, through the game's own tooltip code.
- `/cnutrition dump` writes every food with its vanilla values to `ModData/complexnutrition/foods.json` (this is how `tools/data/vanilla_foods_1.22.7.json` was made).

## Changing food data

1. Edit `tools/data/food_sources.json`. Each entry maps a vanilla item code (wildcards allowed) to one or more USDA FoodData Central ids, with optional `water` (rescale to that water content), `zero` (nutrients lost, for example in baking), `juice` (drop fiber), `factor` (gameplay multiplier on top of USDA, one number or a per-nutrient map), `grams` (weight of one item), `stateFactors` (multiplier on every nutrient for a cooking or baking state, keyed by a word in the item code), `fillFrom` (ids to borrow missing values from) and `notes`.
2. Download the SR Legacy CSV set from https://fdc.nal.usda.gov/download-datasets (FoodData_Central_sr_legacy_food_csv_2018-04).
3. Run `python3 tools/build_food_profiles.py --usda /path/to/FoodData_Central_sr_legacy_food_csv_2018-04`.

The script refuses to run if any vanilla food is left without an entry. After a game update, re-dump `vanilla_foods_*.json` with `/cnutrition dump` first.

## Rules the data follows

- Values are per 100 g. Macros in grams, calories in kcal, micronutrients in % of the US FDA Daily Value. The B-vitamin tracker is the mean %DV of thiamin, riboflavin, niacin, B6, folate and B12, each capped at 300%.
- Each entry gives a real-world weight for one in-game item (`grams`, or grams per litre for liquids; `gramsPerUnit` for fish chunks, `slicesOf` for cheese wheels). Calories follow from the weight and are not tied to vanilla satiety; `docs/FOOD_DATA.md` shows both side by side.
- Only foods with no profile (the insects, other mods' foods) are sized from satiety, at `fallbackKcalPerSatiety` (1.0) kcal per point. Vanilla burns about 700 to 2,800 satiety per in-game day (0.96 per second active, a quarter of that idle, at default speed), close to a real adult's daily kcal, so 1:1 is a sensible guess when nothing else is known.
- Only things vanilla treats as food get a profile. Cooked dishes are never profiled: a bowl, pot or crock holds one serving's ingredients (the cooking pot divides them by the serving count), so its tooltip sums those ingredients and, if the dish came from a cooking recipe, applies `cookedMultipliers` from `config/cooking.json`. A crock with no recipe (pickles) is summed as is. Pies store the whole pie; they are scaled by slices left / 4 and by `pieStateFactors`, and raw pies get no cooking multipliers. BlockPie does not run collectible behaviours for its tooltip, so pies use a Harmony postfix (`PieTooltipPatch`).
- `/cnutrition meal game:bowl-blue-meal 1 game:redmeat-raw game:vegetable-carrot` builds a dish and prints its tooltip; use `code*N` for several of one ingredient, and for pies pass the slice count as servings.

## For other mods

Add a JSON file under `assets/<yourdomain>/config/foodprofiles/` in the same format, or patch ours. Any food without a profile gets its vanilla category's template, sized by its satiety, and is listed in the server log.
