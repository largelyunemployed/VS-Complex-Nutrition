# Developing Complex Nutrition

## Layout

| Path | What it is |
| --- | --- |
| `ComplexNutrition/` | The mod: C# project, `modinfo.json`, `worldconfig.json`, `modicon.png` and `assets/` |
| `ComplexNutrition/Core/` | Config and settings (`ConfigAsset` loads every config file safely, `WorldSettings` reads the world settings and lists the start options, `DigestionSettings`, `TrackingSettings`), nutrient registry, food database, cooked-dish sums, eating hooks, player nutrition state and diet ledger, module loader and events (`ComplexNutritionModSystem`), the shared number format (`Fmt`) |
| `ComplexNutrition/UI/` | v0.3 nutrition window and settings menu: page blocks and their Cairo renderer (`NutritionPage.cs`, also usable outside the game for previews), the tab builders (`NutritionPages.cs`), the window and the settings dialog, and the display pieces shared with the modules (`StatsHover`, `NutrientDisplay`: group colours, days and gut-time text) |
| `ComplexNutrition/Modules/FoodData/` | v0.1 module: tooltip behaviour, pie tooltip patch (client only), fallback logging, `/cnutrition info` and `meal` |
| `ComplexNutrition/Modules/Tracking/` | v0.2 module: reserves, draining, absorption, hunger patches, Stats panel bars, its `/cnutrition` commands, and putting players back on vanilla values when it is switched off |
| `tests/ComplexNutrition.Tests/` | Tests: the digestion maths, the shipped config agreeing with the code's defaults, lang keys, JSON parsing |
| `tools/preview/` | Draws the nutrition window's tabs to PNG outside the game |
| `tools/release.sh` | Builds a release folder: mod zip, compat mod template zip, release notes from the changelog |
| `.github/workflows/check.yml` | GitHub check on every push and pull request: build, tests |
| `ComplexNutrition/assets/complexnutrition/config/cooking.json` | Multipliers for cooked dishes and pie bake states |
| `ComplexNutrition/assets/complexnutrition/config/nutrients.json` | The 14 nutrients: units, daily targets, and tracking caps, start reserves, drain and sitting limits |
| `ComplexNutrition/assets/complexnutrition/config/tracking.json` | Tracking rules: sitting window, diminishing returns, ledger length, update rate |
| `ComplexNutrition/assets/complexnutrition/config/digestion.json` | Digestion rules: fullness per gram and drain, gut pools (size, absorption time, kcal per g, diminishing returns), fiber speed-up, starvation |
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

The release build writes `release/complexnutrition_<version>.zip`; drop it into the game's `Mods` folder. The version is read from `modinfo.json`, the only place it is set.

### Modules

Each module (`INutritionModule`) keeps everything it does to itself and talks only to the core, never to another module. The core calls `Start` on every module once assets are loaded, whether it runs or not (for its commands, and anything that must happen while it is off), then `Enable` on those switched on. `Disable` stops a module, and `OnSwitchedOff` follows it when an admin switches the module off while the game runs. Module ids are in `ModuleIds`.

### Tests and checks

```sh
dotnet run -c Release --project tests/ComplexNutrition.Tests   # exit code 0 = all passed
dotnet run -c Release --project tools/preview /tmp/preview     # window tabs as PNG
tools/release.sh                                               # release/v<version>/
```

The tests are a plain console runner (no test framework to download): every `[Test]` method in `Program.cs` runs. They check the gut pools' maths, that `digestion.json` and `tracking.json` hold the same values as the code's defaults (the code's are used when a file is missing or broken), that `worldconfig.json` lists the same start options as `WorldSettings.StartOptions`, that every lang key the code uses is in `en.json`, that every JSON file parses, and that `CHANGELOG.md` has a section for the version in `modinfo.json`. The GitHub check downloads the game server for its libraries, builds and runs them.

## Testing on a headless server

```sh
# server: https://cdn.vintagestory.at/gamefiles/stable/vs_server_linux-x64_1.22.7.tar.gz
mkdir -p data/Mods && cp release/complexnutrition_<version>.zip data/Mods/
dotnet VintagestoryServer.dll --dataPath ./data
```

The log should show `Loaded 14 nutrients and 203 food profiles`, a coverage line listing the three insect foods on the fallback, and `Module fooddata enabled` and `Module tracking enabled`. (`Nutrient info added to ... foods` is logged by clients, which draw the tooltips.) Then, in the server console:

- `/cnutrition coverage` lists foods without their own profile.
- `/cnutrition info game:fruit-blueberry` prints the nutrient block that item's tooltip shows.
- `/cnutrition template [domain]` writes a ready-to-edit profile file for every food still on the category estimate (one mod's, or all) to `ModData/complexnutrition/template-<domain>.json`.
- `/cnutrition dump` writes every food with its vanilla values to `ModData/complexnutrition/foods.json` (this is how `tools/data/vanilla_foods_1.22.7.json` was made).

Tracking needs a player in the world. With a client connected:

- `/cnutrition stats` (any player) lists your reserves, `/cnutrition display` sets your advanced display choice and `/cnutrition settings` opens the settings menu. The other `/cnutrition` subcommands are for admins.
- `/cnutrition reserves <player>` adds today's intake and each cap.
- `/cnutrition setreserve <player> <nutrient|all> <percent>` sets reserves, for testing.
- `/cnutrition ledger <player>` shows the diet ledger (calories per food category per day).

## How tracking works

- **State.** Each player's reserves live in `WatchedAttributes["complexnutrition"]` (`NutritionState`): a reserve per nutrient in % of its daily target (for the gut pools, the grams in the gut), today's absorbed amounts, the current sitting, and the game time of the last drain. It saves with the player and syncs to clients. The `tracking` flag in that tree (`NutritionState.TrackingOn`) tells the client whether the server is tracking this player; the Stats panel only swaps in our bars when it is set. The 28-day diet ledger (`DietLedger`, calories per vanilla food category per day) and the nutrient history (`NutrientHistory`, v0.3.2: every reserve at the end of each in-game day, kept for `ledgerDays`, and once an hour in 25 slots for the trend arrows) are kept in the server-only entity `Attributes`.
- **Eating.** `EatingHooks` (core) patches the three vanilla eating paths: `CollectibleObject.tryEatStop` (single foods), `BlockLiquidContainerBase.tryEatStop` (drinks) and `BlockMeal.Consume` (bowls, pots and pie slices). A prefix on `EntityBehaviorHunger.OnEntityReceiveSaturation` turns the satiety vanilla hands out into a share of the food's profile, so spoilage scales nutrients the same way it scales satiety. Meals count the servings the dish actually lost. Satiety from any other code path is counted from its category template. The core publishes `FoodEaten` for each.
- **Fullness (v0.2.2).** While tracking runs, a player's `maxsaturation` is set to `fullnessMax` (600 since v0.2.4, draining 100 an hour; a new character starts empty; current satiety scaled to match, flag `fullnessScaled` in our tree) and restored when tracking is switched off. Switching off is applied by the tracking module, at once to everyone online and to anyone who later joins while it is off (`Digestion.ConvertToVanilla`): vanilla satiety comes from the calorie reserve (so an empty reserve cannot be dodged), the five food group bars from vitamin C, vitamin K, the B vitamins, protein over 24 hours and calcium, and our fullness is saved as `savedFullness`. Our state is not touched while off, and `Digestion.UseOurScale` puts the saved fullness back when tracking returns, so the player is restored exactly, whatever they ate on vanilla. `EatingHooks` replaces the satiety vanilla hands out with our fullness: for single foods and drinks, the share eaten (from vanilla satiety, so spoilage counts) times `Digestion.FoodFullness` (grams × `fullnessPerGram`, × `liquidFullness`, × `cookedFullness` when the code contains a `cookedWords` entry). For meals and pies, a postfix on the static `BlockMeal.GetContentNutritionProperties` rescales the contents' satiety to `Digestion.MealFullness`, so vanilla's room check in `Consume` serves what fits. Single foods and sips that don't fit are refused in the `tryEatStop` prefixes, with an in-game error.
- **Absorption.** The tracking module converts each nutrient to % of its daily target and applies any registered absorption multipliers (`RegisterAbsorptionModifier`, none yet). Calories are never added from eating. Fats, protein and net carbohydrate go into gut pools, capped at `maxGrams`: the pool's reserve is what it holds, and the `gut` subtree keeps each pool's hours to empty. (Saves from v0.2.2 to v0.3.0 also kept batch lists there, `<pool>-g` and `<pool>-r`; they are moved into the reserve and removed when the player next joins.) Every other nutrient gets diminishing returns per sitting: within `sittingHours` of the last food, intake above the nutrient's `sittingLimit` adds `scale * ln(1 + extra / scale)`. Reserves are capped at `cap`.
- **Digestion and draining.** Every `updateSeconds` (once at least `minDrainHours` of game time have passed), each pool absorbs `maxRateGrams × g / (halfRateGrams + g)` grams per hour while it holds g grams (since v0.2.4; integrated in 0.05 h midpoint steps, and under 0.5 g counts as empty), with `maxRateGrams` × the fiber speed-up; absorbed grams go into an hourly 24-slot log per pool (`absorbed` subtree), and the calories they yield are reduced above `diminishingAboveGrams` within `windowHours`, by the same log curve or, for protein since v0.3.0, the two-tier taper below. `protein24` sums the protein log, capped at the protein pool's `taperEndGrams`, and fullness drains by `fullnessDrainPerHour`. Fiber, vitamins and minerals drain by `drainPerDay`. Calories burn with vanilla's satiety burn (prefix on `EntityBehaviorHunger.ReduceSaturation`, at `kcalPerSatiety` from `digestion.json`), and the prefix skips vanilla's own step, so fullness and the five category bars no longer move with activity. Nothing drains in creative or spectator mode, while dead, or while offline.
- **Starvation.** A prefix on the private `EntityBehaviorHunger.SlowTick` keeps fullness just above 0, so vanilla's starvation damage never fires for tracked players; the tracking tick deals the same damage (`starvationDamage` every `starvationSeconds`) while the calorie tank is empty. On respawn the world's `complexnutritionRespawn` condition (v0.3.2) sets the tank and every vitamin and mineral store (see Start conditions), empties the gut pools, clears the 24-hour absorbed logs, `protein24` and the sitting, and restarts the hourly history; fiber is kept.
- **Start conditions.** `worldconfig.json` at the mod root adds the `complexnutritionStart` world setting (calorie tank in % of a day: 1000, 700, 500, 300, 100 or 1). It is applied once, when a player's nutrition state is first created; without it (worlds made before v0.2.4) the calories `startReserve` from `nutrients.json` applies. Since v0.3.2 it also sets each vitamin and mineral to `cap × value / 1000` (`WorldSettings.StoreShare`: Full 100%, Easy 70%, ...), and `complexnutritionRespawn` (same values, default 100) does the same on every respawn. Its labels are in `assets/game/lang/worldconfig-en.json`, the only lang file the main menu reads from a mod. It also adds `complexnutritionAdvancedDisplay` (bool), the default for the advanced gut hover text; a player's own choice from `/cnutrition display` is the `display` int in our tree (0 world, 1 advanced, 2 simple), read by `Digestion.AdvancedDisplay`. The file must keep its empty `playStyles` list: the game's singleplayer world list loops over every mod's play styles without a null check and crashes without it.
- **Hover boxes.** Our Stats panel bars and the HUD calorie bar set `GuiElementStatbar.ShowValueOnHover = false`, and `StatsHover` (an `IRenderer` at `EnumRenderStage.Ortho`, order 1.01, so after the GUI) draws their hover instead: VTML through `GuiElementRichtext` into one texture, redrawn only when the text changes. Vanilla's hover is plain text in one font, so this is what allows the coloured title and section headings. The bar under the mouse is checked every frame and its text every 100 ms. Restore `ShowValueOnHover` when handing a bar back to vanilla.
- **HUD.** On the client, a prefix on `HudStatbar.UpdateSaturation` (an internal class, found by name) shows the calorie tank on the satiety bar, one line per `dailyTarget`, with tank and fullness in its hover text; a postfix on `OnFlashStatbars` flashes it below `flashBelow` of a full tank. Without tracking, the bar is handed back to vanilla. `FullnessDisplayPatch` makes vanilla's own client-side numbers agree: postfixes on `CollectibleObject.GetNutritionProperties`, `BlockLiquidContainerBase.GetNutritionPropertiesPerLitre` and `BlockMeal.GetContentNutritionProperties` return our fullness on the client only, so "When eaten: N fullness" and meal facts show it.
- **Vanilla bars.** While tracking runs for a player, a prefix on `EntityBehaviorHunger.UpdateNutrientHealthBoost` keeps the `nutrientHealthMod` max-health bonus at 0, and on the client a prefix on `CharacterExtraDialogs.ComposeStatsGui` lays out the Stats panel with our bars (calories; carbs and fats in the gut; protein, whose bar shows the last 24 hours and whose hover adds what is in the gut; and the stored nutrients) in place of the five category bars. Switching the module off removes both patches and vanilla recomputes its bonus.
- **Display units.** `DisplayUnits` in `ModConfig/complexnutrition.json` is `amount` (default) or `percent`. Since v0.3.2 every nutrient's `unit` in `nutrients.json` is the unit its food values are stored in (kcal, g, mg or mcg), and `dailyTarget` is a day's need in that unit; `NutrientDefinition.Format` shows a reserve as `reserve × dailyTarget / 100` in that unit. The B vitamins' unit is `%DV` (target 100), so they are always shown as a percentage.
- **Mushroom clusters.** `patches/mushroom-clusters.json` makes each mushroom patch drop as many mushrooms as its block model holds and shares vanilla's satiety across them. It is generated from the game's models by `tools/build_mushroom_clusters.py <VintageStory folder>`, which also prints each mushroom's weight (held model volume × 0.6 g) for the `grams` in `tools/data/food_sources.json`.
- **Fullness names.** Vanilla satiety is shown as Fullness. `assets/game/lang/en.json` overrides the vanilla lang keys for the Stats panel line, food and drink tooltips and the handbook's meal facts.
- **Nutrition window (v0.3.0).** `ComplexNutritionModSystem.Settings.cs` registers the hotkey `complexnutrition-window` (Left Alt + N) and opens `GuiDialogNutrition`, laid out like the handbook (500 by 500 content, top centre at y 70). Each tab is a list of `PageBlock`s built by `NutritionPages` from `NutritionPageData` and drawn by `PageRenderer` into one texture, redrawn only when the blocks' text changes (checked every second). The server records foods each player has held in `WatchedAttributes["complexnutritionHeld"]` (`NutritionState.HeldKey`) (a string array, outside our tree so it only syncs when it grows), scanned every `heldScanSeconds`. Running-low lines come from `lowCaloriesDays`, `lowDays` and `lowGroups` in `tracking.json`.
- **History (v0.3.2).** The window asks for `HistoryRequestPacket` on opening, on switching to the History tab and every 30 seconds while open; the server (at most once a second per player) answers with a `HistoryPacket` holding `HistoryData` as JSON, built by the tracking module (`ComplexNutritionModSystem.HistoryProvider`): 28 days of end-of-day reserves (today's are the current ones) with the ledger's calories by category, and the oldest hourly reading from the last 24 hours (`DayAgo`, at least 6 hours old). Trend arrows compare that reading with the current reserve, scaled to a day: more than 15% of a day's need up or down is rising or falling (`NutrientHistory.Trend`). The chart is a `ChartBlock` drawn by `PageRenderer`.
- **Settings (v0.3.0).** One network channel, `complexnutrition`: the server sends each client a `SettingsSnapshot` (JSON) on join and after every change, and the client sends `SettingsChangePacket`s. The server checks `controlserver` for everything but the player's own display choice, applies the change (config file, world config or player data), saves it and sends everyone the new snapshot. Clients follow the server's module switches (`SetModuleRunning`), so a module switched in the menu starts or stops on both sides; switching tracking off converts every online player with `Digestion.ConvertToVanilla` (from the tracking module's `OnSwitchedOff`). Amounts or percent is `ComplexNutritionModSystem.ShowPercent` (the player's choice on the client, the config on the server), passed to the tooltip and text code. The client's own file, `ModConfig/complexnutrition-client.json`, holds `DisplayUnits` (`server`, `amount` or `percent`).
- **Cooking pots.** `patches/cooking-pot-slots.json` raises `maxContainerSlotStackSize` on clay pots to 10. `patches/meat-in-pots.json` adds poultry and bushmeat (raw, cooked, cured) to the meat slots of meaty stew and soup, conditional on the world setting `complexnutritionMeatInPots`, so it applies when the world loads. `/cnutrition meatinpots <on|off>` sets it on any world, including ones made before v0.3.0.
- **Protein curve (v0.3.0).** A pool with `taperEndGrams` gives full calories up to `diminishingAboveGrams`, sliding in a straight line to `taperEndValue` per gram at `taperEndGrams`, then `beyondValue` per gram past it (protein: 64g, 128g, 0.8, 0.5). The protein pool is the one source for the 64g need and the 128g bar top; `nutrients.json`'s protein `dailyTarget` must match (a warning is logged, and a test fails, if it doesn't).
- **Events** (on `ComplexNutritionModSystem`, server side): `FoodEaten`, `FoodAbsorbed`, `DailyRollover` (with the ledger day that ended) and `ReserveChanged`.

## Changing food data

1. Edit `tools/data/food_sources.json`. Each entry maps a vanilla item code (wildcards allowed) to one or more USDA FoodData Central ids, with optional `water` (rescale to that water content), `zero` (nutrients lost, for example in baking), `juice` (drop fiber), `factor` (gameplay multiplier on top of USDA, one number or a per-nutrient map), `grams` (weight of one item), `stateFactors` (multiplier on every nutrient for a cooking or baking state, keyed by a word in the item code), `fillFrom` (ids to borrow missing values from) and `notes`.
2. Download the SR Legacy CSV set from https://fdc.nal.usda.gov/download-datasets (FoodData_Central_sr_legacy_food_csv_2018-04).
3. Run `python3 tools/build_food_profiles.py --usda /path/to/FoodData_Central_sr_legacy_food_csv_2018-04`.

The script refuses to run if any vanilla food is left without an entry. After a game update, re-dump `vanilla_foods_*.json` with `/cnutrition dump` first.

## Rules the data follows

- Values are per 100 g. Macros in grams, calories in kcal, vitamins and minerals in real units (mg or mcg, as USDA gives them; since v0.3.2). The B-vitamin tracker stays in %DV: the mean %DV of thiamin, riboflavin, niacin, B6, folate and B12, each capped at 300%. Hand-set values in `food_sources.json` use the same units. Profiles may give `per100gDV` (% of the Daily Value) instead; it is turned into amounts on load.
- Each entry gives a real-world weight for one in-game item (`grams`, or grams per litre for liquids; `gramsPerUnit` for fish chunks, `slicesOf` for cheese wheels). Calories follow from the weight and are not tied to vanilla satiety; `docs/FOOD_DATA.md` shows both side by side.
- Only foods with no profile (the insects, other mods' foods) are sized from satiety, at `fallbackKcalPerSatiety` (1.0) kcal per point. Vanilla burns about 700 to 2,800 satiety per in-game day (0.96 per second active, a quarter of that idle, at default speed), close to a real adult's daily kcal, so 1:1 is a sensible guess when nothing else is known.
- Only things vanilla treats as food get a profile. Cooked dishes are never profiled: a bowl, pot or crock holds one serving's ingredients (the cooking pot divides them by the serving count), so its tooltip sums those ingredients and, if the dish came from a cooking recipe, applies `cookedMultipliers` from `config/cooking.json`. A crock with no recipe (pickles) is summed as is. Pies store the whole pie; they are scaled by slices left / 4 and by `pieStateFactors`, and raw pies get no cooking multipliers. BlockPie does not run collectible behaviours for its tooltip, so pies use a Harmony postfix (`PieTooltipPatch`).
- `/cnutrition meal game:bowl-blue-meal 1 game:redmeat-raw game:vegetable-carrot` builds a dish and prints its tooltip; use `code*N` for several of one ingredient, and for pies pass the slice count as servings.

## For other mods

See [COMPATIBILITY.md](COMPATIBILITY.md). In short: any mod can add a JSON file under `assets/<yourdomain>/config/foodprofiles/` (profiles can copy a vanilla food with `like`, adjust it with `scale`, and override ours with `priority`), and `/cnutrition template <domain>` writes a pre-filled file for a mod's foods. A ready-made content mod is in `templates/compat-mod/`. Any food without a profile gets its vanilla category's template, sized by its satiety, and is listed in the server log.
