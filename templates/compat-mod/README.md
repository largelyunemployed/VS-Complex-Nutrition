# Complex Nutrition compatibility mod template

A content-only mod that adds Complex Nutrition food data for another mod's foods. See [docs/COMPATIBILITY.md](../../docs/COMPATIBILITY.md) for the field reference and the `/cnutrition template` command that fills most of it in for you.

1. Rename `assets/yourfoodmodcncompat/` and set `modid` in `modinfo.json` to the same name (lowercase letters and digits).
2. In `modinfo.json`, set the name, authors and description, and change `yourfoodmod` in `dependencies` to the food mod's id.
3. Replace the example profiles in `config/foodprofiles/yourfoodmod.json` with yours.
4. Zip the contents of this folder (so `modinfo.json` is at the top of the zip) and put it in your `Mods` folder.

If you make the food mod yourself, skip all this: put the JSON file in your own mod's `assets/<your mod id>/config/foodprofiles/` instead.
