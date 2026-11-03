# Developing Complex Nutrition

## Layout

| Path | What it is |
| --- | --- |
| `ComplexNutrition/` | The mod: C# project, `modinfo.json`, `modicon.png` and `assets/` |
| `ComplexNutrition/Core/` | Config, nutrient registry, food database, cooked-dish sums, eating hooks, player nutrition state and diet ledger, module loader and events (`ComplexNutritionModSystem`) |
| `ComplexNutrition/Modules/FoodData/` | v0.1 module: tooltip behaviour, pie tooltip patch, fallback logging |
| `ComplexNutrition/Modules/Tracking/` | v0.2 module: reserves, draining, absorption, hunger patches, Stats panel bars, `/nutrition` and admin commands |
| `ComplexNutrition/assets/complexnutrition/config/cooking.json` | Multipliers for cooked dishes and pie bake states |
| `ComplexNutrition/assets/complexnutrition/config/nutrients.json` | The 14 nutrients: units, daily targets, and tracking caps, start reserves, drain and sitting limits |
| `ComplexNutrition/assets/complexnutrition/config/tracking.json` | Tracking rules: sitting window, diminishing returns, ledger length, update rate |
| `ComplexNutrition/assets/complexnutrition/config/handbook/*.json` | Handbook guide pages (text in `lang/en.json`, keys `guide-*`) |
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
mkdir -p data/Mods && cp release/complexnutrition_0.2.1.zip data/Mods/
dotnet VintagestoryServer.dll --dataPath ./data
```

The log should show `Loaded 14 nutrients and 164 food profiles`, a coverage line listing the three insect foods on the fallback, and `Module fooddata enabled` and `Module tracking enabled`. (`Nutrient info added to ... foods` is logged by clients, which draw the tooltips.) Then, in the server console:

- `/cnutrition coverage` lists foods without their own profile.
- `/cnutrition info game:fruit-blueberry` prints the nutrient block that item's tooltip shows.
- `/cnutrition template [domain]` writes a ready-to-edit profile file for every food still on the category estimate (one mod's, or all) to `ModData/complexnutrition/template-<domain>.json`.
- `/cnutrition dump` writes every food with its vanilla values to `ModData/complexnutrition/foods.json` (this is how `tools/data/vanilla_foods_1.22.7.json` was made).

Tracking needs a player in the world. With a client connected:

- `/nutrition` (any player) lists your reserves.
- `/cnutrition reserves <player>` adds today's intake and each cap.
- `/cnutrition setreserve <player> <nutrient|all> <percent>` sets reserves, for testing.
- `/cnutrition ledger <player>` shows the diet ledger (calories per food category per day).

## How tracking works

- **State.** Each player's reserves live in `WatchedAttributes["complexnutrition"]` (`NutritionState`): a reserve per nutrient in % of its daily target, today's absorbed amounts, the current sitting, and the game time of the last drain. It saves with the player and syncs to clients. The `tracking` flag in that tree tells the client whether the server is tracking this player; the Stats panel only swaps in our bars when it is set. The 28-day diet ledger (`DietLedger`, calories per vanilla food category per day) is kept in the server-only entity `Attributes`.
- **Eating.** `EatingHooks` (core) patches the three vanilla eating paths: `CollectibleObject.tryEatStop` (single foods), `BlockLiquidContainerBase.tryEatStop` (drinks) and `BlockMeal.Consume` (bowls, pots and pie slices). A prefix on `EntityBehaviorHunger.OnEntityReceiveSaturation` turns the satiety vanilla hands out into a share of the food's profile, so spoilage scales nutrients the same way it scales satiety. Meals count the servings the dish actually lost. Satiety from any other code path is counted from its category template. The core publishes `FoodEaten` for each.
- **Absorption.** The tracking module converts each nutrient to % of its daily target, applies any registered absorption multipliers (`RegisterAbsorptionModifier`, none in v0.2), and applies diminishing returns per sitting: within `sittingHours` of the last food, intake above the nutrient's `sittingLimit` adds `scale * ln(1 + extra / scale)`. Reserves are capped at `cap`.
- **Draining.** Every `updateSeconds`, reserves drain by `drainPerDay` scaled to the in-game hours passed, so sleeping and game speed count. Calories instead burn with vanilla satiety (prefix on `EntityBehaviorHunger.ReduceSaturation`), at `kcalPerSatiety` from `foodfallbacks.json`. Nothing drains in creative or spectator mode, while dead, or while offline.
- **Vanilla bars.** While tracking runs for a player, a prefix on `EntityBehaviorHunger.UpdateNutrientHealthBoost` keeps the `nutrientHealthMod` max-health bonus at 0, and on the client a prefix on `CharacterExtraDialogs.ComposeStatsGui` lays out the Stats panel with our 14 bars in place of the five category bars. Switching the module off removes both patches and vanilla recomputes its bonus.
- **Display units.** `DisplayUnits` in `ModConfig/complexnutrition.json` is `amount` (default) or `percent`. Each nutrient's `displayUnit` and `dvAmount` in `nutrients.json` turn a reserve or food amount into kcal, g, mg or mcg (`NutrientDefinition.Format`); the B vitamins have no `displayUnit` and are always shown in %.
- **Mushroom clusters.** `patches/mushroom-clusters.json` makes each mushroom patch drop as many mushrooms as its block model holds and shares vanilla's satiety across them. It is generated from the game's models by `tools/build_mushroom_clusters.py <VintageStory folder>`, which also prints each mushroom's weight (held model volume × 0.6 g) for the `grams` in `tools/data/food_sources.json`.
- **Fullness.** Vanilla satiety is shown as Fullness. `assets/game/lang/en.json` overrides the vanilla lang keys for the Stats panel line, food and drink tooltips and the handbook's meal facts. The HUD bar next to the hotbar gets its hover text from `CharacterStatsPatch.LabelFullnessBar`, which finds vanilla's `HudStatbar` once a second and sets the bar's `onGetStatbarValue`, since the HUD makes a new bar whenever it recomposes.
- **Events** (on `ComplexNutritionModSystem`, server side): `FoodEaten`, `FoodAbsorbed`, `DailyRollover` (with the ledger day that ended) and `ReserveChanged`.

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

See [COMPATIBILITY.md](COMPATIBILITY.md). In short: any mod can add a JSON file under `assets/<yourdomain>/config/foodprofiles/` (profiles can copy a vanilla food with `like`, adjust it with `scale`, and override ours with `priority`), and `/cnutrition template <domain>` writes a pre-filled file for a mod's foods. A ready-made content mod is in `templates/compat-mod/`. Any food without a profile gets its vanilla category's template, sized by its satiety, and is listed in the server log.
