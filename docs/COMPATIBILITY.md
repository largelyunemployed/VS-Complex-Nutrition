# Adding Complex Nutrition support to food mods

Complex Nutrition gives every vanilla food real-world nutrients. Foods from other mods still work without any help: each one gets an estimate from its vanilla food category (Fruit, Vegetable, Protein, Grain, Dairy), sized from its satiety, and the tooltip says it is an estimate. To give them proper numbers, add one JSON file. No code, no Harmony and no dependency is needed.

## How it works

The mod reads every `config/foodprofiles/*.json` file in **every** mod's assets, not just its own. So there are two ways to ship profiles:

1. **You make the food mod:** put the file in your own mod at `assets/<your mod id>/config/foodprofiles/complexnutrition.json`. When Complex Nutrition isn't installed, the game just ignores the file, so your mod doesn't need to depend on it.
2. **You don't own the food mod** (or you're a modpack maker): make a small content mod that holds only that file. A ready-made one is in [`templates/compat-mod/`](../templates/compat-mod/).

## Quick start

1. Install Complex Nutrition and the food mod, start a world (or a server), and run `/cnutrition template <food mod id>`. It writes `ModData/complexnutrition/template-<mod id>.json`, listing every food from that mod with the estimate it gets today. Each entry has a `TODO` note with the food's name.
2. Fix each entry: give it a real weight and real values, or replace `per100g` with `"like": "<a vanilla item code>"` (see below). Delete the notes when you're done.
3. Save it as `assets/<mod id>/config/foodprofiles/<anything>.json` in your mod, or in a copy of `templates/compat-mod/`.
4. Restart, then check with `/cnutrition coverage` (lists foods still on the estimate) and `/cnutrition info <item code>` (shows the tooltip). The server log warns about typos, unknown nutrients and broken `like` codes.

`/cnutrition template` with no mod id covers every food that's still on the estimate, from all mods.

## The file

```json
{
	"profiles": [
		{ "code": "yourfoodmod:fruit-whitecurrant", "like": "game:fruit-redcurrant" }
	]
}
```

Each profile has:

| Field | Required | Meaning |
| --- | --- | --- |
| `code` | yes | Item or block code, with its domain (`yourfoodmod:fruit-gooseberry`). `*` wildcards work: `yourfoodmod:driedfruit-*`. Codes that match nothing are ignored. |
| `portionGrams` | yes, unless `like` gives it | Real-world weight of one item in grams. For liquids, grams per litre (water about 1000, milk 1030, honey 1420). |
| `portionUnit` | no | `"item"` (default) or `"litre"` for liquids kept in containers. |
| `per100g` | yes, unless `like` gives it | Nutrients per 100 g; see the table below. Leaving a nutrient out means 0. |
| `like` | no | Code of a food that already has a profile (usually a vanilla one). Copies its values, weight and unit; anything you set yourself wins. |
| `scale` | no | Multipliers applied last, per nutrient, or `"all"` for every nutrient: `{ "all": 6.0, "vitaminC": 0.1 }`. |
| `priority` | no | Default 0. When two files give the same `code`, the higher number wins. Complex Nutrition's own files use 0, so use 10 to override one of them. |
| `source` | no | `{ "fdcId": 173030, "description": "Gooseberries, raw", "dataset": "SR Legacy" }`. It shows in the tooltip when extended debug info is on (`.edi` in chat). |
| `notes` | no | Anything you like, for other people reading the file. |

Only things the game already treats as food get a profile. Meals, pies and pots work themselves out from their ingredients, so ingredients are all you need to cover.

## Nutrients and units

| Code | Unit | Notes |
| --- | --- | --- |
| `calories` | kcal | |
| `protein`, `carbohydrate`, `fat`, `fiber` | g | `fat` is shown in game as Fats |
| `vitaminA`, `vitaminD`, `vitaminE`, `vitaminK`, `vitaminC` | % of US FDA Daily Value | A 900 mcg RAE, D 20 mcg, E 15 mg, K 120 mcg, C 90 mg |
| `vitaminB` | % DV | Average %DV of thiamin, riboflavin, niacin, B6, folate and B12, each capped at 300% |
| `calcium`, `iron`, `potassium` | % DV | Calcium 1,300 mg, iron 18 mg, potassium 4,700 mg |

The easiest real source is [USDA FoodData Central](https://fdc.nal.usda.gov/) (SR Legacy foods): look the food up, take the per-100 g values, and divide micronutrients by the DV above. If you're comfortable with Python, `tools/build_food_profiles.py` in this repo does that conversion from USDA ids. When nothing real fits, `like` the closest vanilla food and adjust it with `per100g` or `scale`; [FOOD_DATA.md](FOOD_DATA.md) lists every vanilla profile.

## Examples

All of these are in [`templates/compat-mod/`](../templates/compat-mod/assets/yourfoodmodcncompat/config/foodprofiles/yourfoodmod.json):

- A full profile from USDA numbers.
- `like` a vanilla food, nothing else.
- A wildcard for every variant of an item, with `scale` for drying and a lighter portion.
- A liquid, `like` vanilla apple juice, with one value replaced.
- A food with no close match, built from the nearest one and corrected.

## Changing Complex Nutrition's own values

You have two ways to do it:

- **Copy and override:** copy the entry from our file into yours, change what you want, and add `"priority": 10`. This keeps working when we reorder our files.
- **JSON patch:** use a normal Vintage Story patch on `complexnutrition:config/foodprofiles/<file>.json`. This breaks more easily, because the files are generated and entries can move.

## Template compat mod

[`templates/compat-mod/`](../templates/compat-mod/) is a complete content mod:

1. Rename the `yourfoodmodcncompat` folder under `assets/` and set `modid` in `modinfo.json` to the same name. Use lowercase letters and digits only.
2. In `modinfo.json`, set the name, authors and description, and change `yourfoodmod` to the real mod id of the food mod. The dependencies make the game load it only alongside both mods.
3. Replace the example profiles with yours, zip the folder's contents (`modinfo.json` and `assets/` at the top of the zip), and drop the zip into `Mods`.
