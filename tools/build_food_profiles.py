#!/usr/bin/env python3
"""Builds Complex Nutrition's food profile assets from USDA FoodData Central.

Inputs
  tools/data/vanilla_foods_1.22.7.json  every food item in vanilla 1.22.7, dumped from a running
                                        server with `/cnutrition dump`
  tools/data/food_sources.json          which USDA entry (or mix) each vanilla food uses
  USDA SR Legacy CSV folder             FoodData_Central_sr_legacy_food_csv_2018-04, from
                                        https://fdc.nal.usda.gov/download-datasets (pass with --usda)

Outputs
  ComplexNutrition/assets/complexnutrition/config/foodprofiles/<group>.json
  ComplexNutrition/assets/complexnutrition/config/foodfallbacks.json
  docs/FOOD_DATA.md                     human-readable table of every food

Usage
  python3 tools/build_food_profiles.py --usda /path/to/FoodData_Central_sr_legacy_food_csv_2018-04
"""

import argparse
import csv
import fnmatch
import json
import os
import sys
from collections import OrderedDict, defaultdict

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "ComplexNutrition", "assets", "complexnutrition", "config")
DATA = os.path.join(ROOT, "tools", "data")

# USDA nutrient ids (SR Legacy / FoodData Central)
N_ENERGY, N_PROTEIN, N_FAT, N_CARB, N_WATER, N_FIBER = 1008, 1003, 1004, 1005, 1051, 1079
N_CA, N_FE, N_K = 1087, 1089, 1092
N_VIT_A, N_VIT_E, N_VIT_D, N_VIT_C, N_VIT_K = 1106, 1109, 1114, 1162, 1185
N_B1, N_B2, N_B3, N_B6, N_FOLATE_DFE, N_FOLATE, N_B12 = 1165, 1166, 1167, 1175, 1190, 1177, 1178

# US FDA adult Daily Values (21 CFR 101.9(c)(8)(iv)), as listed by NIH Office of Dietary Supplements.
DV = {
    "vitaminA": (N_VIT_A, 900.0),      # mcg RAE
    "vitaminD": (N_VIT_D, 20.0),       # mcg
    "vitaminE": (N_VIT_E, 15.0),       # mg alpha-tocopherol
    "vitaminK": (N_VIT_K, 120.0),      # mcg
    "vitaminC": (N_VIT_C, 90.0),       # mg
    "calcium": (N_CA, 1300.0),         # mg
    "iron": (N_FE, 18.0),              # mg
    "potassium": (N_K, 4700.0),        # mg
}
B_VITAMINS = [(N_B1, 1.2), (N_B2, 1.3), (N_B3, 16.0), (N_B6, 1.7), (N_FOLATE_DFE, 400.0), (N_B12, 2.4)]
B_NAMES = {N_B1: "thiamin", N_B2: "riboflavin", N_B3: "niacin", N_B6: "B6", N_FOLATE_DFE: "folate", N_B12: "B12"}
B_CAP = 300.0  # one very rich B vitamin (liver-level B12) should not swamp the group average

MACROS = OrderedDict([("calories", N_ENERGY), ("protein", N_PROTEIN), ("carbohydrate", N_CARB), ("fat", N_FAT), ("fiber", N_FIBER)])
MICROS = ["vitaminA", "vitaminD", "vitaminE", "vitaminK", "vitaminC", "vitaminB", "calcium", "iron", "potassium"]
ALL = list(MACROS) + MICROS
LIQUID_KIND = "perlitre"


def load_usda(folder, wanted_ids):
    foods = {}
    with open(os.path.join(folder, "food.csv"), newline="", encoding="utf-8") as f:
        for r in csv.DictReader(f):
            fid = int(r["fdc_id"])
            if fid in wanted_ids:
                foods[fid] = {"description": r["description"], "n": {}}
    with open(os.path.join(folder, "food_nutrient.csv"), newline="", encoding="utf-8") as f:
        for r in csv.DictReader(f):
            fid = int(r["fdc_id"])
            if fid in foods and r["amount"] != "":
                foods[fid]["n"][int(r["nutrient_id"])] = float(r["amount"])
    missing = wanted_ids - set(foods)
    if missing:
        sys.exit("USDA ids not found in SR Legacy: %s" % sorted(missing))
    return foods


def mix_nutrients(usda_foods):
    """Equal-weight mean per USDA nutrient, over the components that report it."""
    ids = {nid for f in usda_foods for nid in f["n"]}
    out = {}
    for nid in ids:
        vals = [f["n"][nid] for f in usda_foods if nid in f["n"]]
        out[nid] = sum(vals) / len(vals)
    return out


def to_profile(n):
    """Per-100 g values in our nutrient set, plus a list of nutrients USDA did not report."""
    out, gaps = {}, []
    for key, nid in MACROS.items():
        if nid not in n:
            gaps.append(key)
        out[key] = n.get(nid, 0.0)
    for key, (nid, dv) in DV.items():
        if nid not in n:
            gaps.append(key)
        out[key] = n.get(nid, 0.0) / dv * 100.0
    parts = []
    for nid, dv in B_VITAMINS:
        val = n.get(nid)
        if val is None and nid == N_FOLATE_DFE:
            val = n.get(N_FOLATE)
        if val is None:
            gaps.append(B_NAMES[nid])
            val = 0.0
        parts.append(min(val / dv * 100.0, B_CAP))
    out["vitaminB"] = sum(parts) / len(parts)
    return out, gaps


def build_entry_profile(entry, usda):
    n = mix_nutrients([usda[i] for i in entry["fdc"]])
    if "fillFrom" in entry:
        fill = mix_nutrients([usda[i] for i in entry["fillFrom"]])
        for nid, val in fill.items():
            n.setdefault(nid, val)
    prof, gaps = to_profile(n)
    if "water" in entry:
        if N_WATER not in n:
            sys.exit("%s: water rescaling needs the USDA water value" % entry["code"])
        factor = (100.0 - entry["water"]) / (100.0 - n[N_WATER])
        prof = {k: v * factor for k, v in prof.items()}
    for k in entry.get("zero", []):
        prof[k] = 0.0
    if entry.get("juice"):
        prof["fiber"] = 0.0
        gaps = [g for g in gaps if g != "fiber"]
    return prof, gaps


def rnd(key, v):
    if key == "calories":
        return round(v, 1)
    return round(v, 2)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--usda", required=True, help="Folder of the SR Legacy CSV download")
    args = ap.parse_args()

    vanilla = json.load(open(os.path.join(DATA, "vanilla_foods_1.22.7.json")))
    by_code = {v["code"]: v for v in vanilla}
    src = json.load(open(os.path.join(DATA, "food_sources.json")), object_pairs_hook=OrderedDict)
    ratio = float(src["kcalPerSatiety"])

    wanted = {i for g in src["groups"].values() for e in g for i in e.get("fdc", []) + e.get("fillFrom", [])}
    usda = load_usda(args.usda, wanted)

    claimed = {}
    out_files = OrderedDict()
    rows = []
    template_inputs = defaultdict(list)

    for group, entries in src["groups"].items():
        profiles = []
        for e in entries:
            pattern = e["code"]
            matches = [c for c in by_code if fnmatch.fnmatchcase(c, pattern) and c not in claimed]
            if not matches:
                sys.exit("%s matches no unclaimed vanilla food" % pattern)
            for c in matches:
                claimed[c] = pattern

            if e.get("fallback"):
                for c in matches:
                    rows.append({"code": c, "v": by_code[c], "fallback": True, "notes": e.get("notes", "")})
                continue

            per100, gaps = build_entry_profile(e, usda)
            kcal = per100["calories"]
            if kcal <= 0:
                sys.exit("%s: zero calories" % pattern)
            source = {"fdcId": e["fdc"][0], "description": usda[e["fdc"][0]]["description"], "dataset": "SR Legacy"}
            if len(e["fdc"]) > 1:
                source["description"] = " + ".join(usda[i]["description"] for i in e["fdc"]) + " (equal mix)"
            is_liquid = all(by_code[c]["kind"] == LIQUID_KIND for c in matches)

            def portion_for(code):
                if "slicesOf" in e:
                    s = by_code[e["slicesOf"]]["satiety"]
                    return s * ratio / kcal * 100.0 * e["slices"]
                return by_code[code]["satiety"] * ratio / kcal * 100.0

            portions = {c: portion_for(c) for c in matches}
            uniform = len({round(p, 3) for p in portions.values()}) == 1
            codes_out = [pattern] if ("*" in pattern and uniform) else sorted(matches)
            for code in codes_out:
                grams = portions[code] if code in portions else next(iter(portions.values()))
                prof = OrderedDict()
                prof["code"] = code
                prof["portionGrams"] = round(grams, 1)
                if is_liquid:
                    prof["portionUnit"] = "litre"
                prof["per100g"] = OrderedDict((k, rnd(k, per100[k])) for k in ALL)
                prof["source"] = source
                notes = e.get("notes", "")
                if gaps:
                    notes = (notes + " " if notes else "") + "USDA reports no value for: %s (treated as 0)." % ", ".join(gaps)
                if notes:
                    prof["notes"] = notes
                profiles.append(prof)

            for c in matches:
                rows.append({"code": c, "v": by_code[c], "fallback": False, "grams": portions[c], "per100": per100,
                             "source": source, "notes": e.get("notes", ""), "liquid": is_liquid, "group": group})
            if not is_liquid:
                template_inputs[by_code[matches[0]]["category"]].append(per100)
        out_files[group] = profiles

    unclaimed = sorted(set(by_code) - set(claimed))
    if unclaimed:
        sys.exit("Vanilla foods with no entry in food_sources.json: %s" % unclaimed)

    # Category templates for foods without a profile (other mods' foods, insects): the plain mean of
    # this mod's own solid-food profiles in that vanilla category.
    templates = OrderedDict()
    everything = [p for lst in template_inputs.values() for p in lst]
    for cat in ["Fruit", "Vegetable", "Protein", "Grain", "Dairy"]:
        lst = template_inputs.get(cat, [])
        if not lst:
            continue
        templates[cat] = mean_template(cat, lst)
    templates["Unknown"] = mean_template("Unknown", everything)
    templates["NoNutrition"] = mean_template("NoNutrition", everything)

    os.makedirs(os.path.join(ASSETS, "foodprofiles"), exist_ok=True)
    for name in os.listdir(os.path.join(ASSETS, "foodprofiles")):
        if name.endswith(".json"):
            os.remove(os.path.join(ASSETS, "foodprofiles", name))
    for group, profiles in out_files.items():
        with open(os.path.join(ASSETS, "foodprofiles", group + ".json"), "w") as f:
            f.write(dump_profiles(profiles, "Generated by tools/build_food_profiles.py from USDA FoodData Central (SR Legacy). Values per 100 g; micronutrients in % of the FDA Daily Value. Edit tools/data/food_sources.json and re-run rather than editing by hand."))
    with open(os.path.join(ASSETS, "foodfallbacks.json"), "w") as f:
        json.dump(OrderedDict([
            ("_comment", "Templates for foods with no profile of their own, by vanilla food category: the mean of this mod's profiles in that category. Portions are sized so kcal = satiety x kcalPerSatiety. Generated by tools/build_food_profiles.py."),
            ("kcalPerSatiety", ratio),
            ("byCategory", templates)]), f, indent="\t")
        f.write("\n")

    write_report(rows, ratio)
    n_fb = sum(1 for r in rows if r["fallback"])
    print("Profiled %d vanilla foods (%d on category fallback) in %d files." % (len(rows), n_fb, len(out_files)))


def mean_template(cat, lst):
    t = OrderedDict()
    t["code"] = "complexnutrition:template-" + cat.lower()
    t["portionGrams"] = 100
    t["per100g"] = OrderedDict((k, rnd(k, sum(p[k] for p in lst) / len(lst))) for k in ALL)
    t["notes"] = "Mean of %d profiles in this category." % len(lst)
    return t


def dump_profiles(profiles, comment):
    lines = ["{", '\t"_comment": %s,' % json.dumps(comment), '\t"profiles": [']
    for i, p in enumerate(profiles):
        lines.append("\t\t" + json.dumps(p, ensure_ascii=False) + ("," if i < len(profiles) - 1 else ""))
    lines += ["\t]", "}", ""]
    return "\n".join(lines)


def write_report(rows, ratio):
    names = {"vitaminA": "A", "vitaminD": "D", "vitaminE": "E", "vitaminK": "K", "vitaminC": "C", "vitaminB": "B",
             "calcium": "Ca", "iron": "Fe", "potassium": "K+"}
    L = []
    L.append("# Food data (v0.1)")
    L.append("")
    L.append("Every vanilla Vintage Story 1.22.7 food, its vanilla satiety, and the real-world nutrients Complex Nutrition gives it. Generated by `tools/build_food_profiles.py`; do not edit by hand.")
    L.append("")
    L.append("**How portions are sized.** One in-game portion is set so its calories equal its vanilla satiety (%.1f kcal per satiety point). Vanilla drains about 0.96 satiety per real second while active and a quarter of that while idle, which at default speed is roughly 700 to 2,800 satiety per in-game day, close to a real adult's 2,000 to 2,800 kcal. Keeping kcal equal to satiety leaves vanilla's food balance untouched; the nutrients then follow from the real food. Low-calorie foods therefore get large portions (a whole cabbage head is about 1.2 kg).", )
    L[-1] = L[-1] % ratio
    L.append("")
    L.append("**Sources.** USDA FoodData Central, SR Legacy (April 2018). Micronutrients are %% of the US FDA Daily Value; \"B\" is the mean %%DV of thiamin, riboflavin, niacin, B6, folate and B12 (each capped at 300%%). Liquids follow the same rule per in-game litre, so \"1 L = 139 g real\" means an in-game litre of milk carries the nutrients of 139 g of real milk. Where USDA has no matching food, the nearest real food is used and the note says so.".replace("%%", "%"))
    L.append("")
    L.append("| Item | Name | Satiety | Portion | kcal | Protein g | Carbs g | Fat g | Fiber g | Top micronutrients (%DV per portion) | USDA source | Notes |")
    L.append("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- | --- |")
    for r in sorted(rows, key=lambda r: r["code"]):
        v = r["v"]
        code = r["code"].replace("game:", "")
        name = v["name"] if ":" not in v["name"] else ""
        if r["fallback"]:
            L.append("| `%s` | %s | %g | | | | | | | | none | %s |" % (code, name, v["satiety"], r["notes"]))
            continue
        g = r["grams"] / 100.0
        p = r["per100"]
        micros = sorted(((p[k] * g, k) for k in MICROS), reverse=True)[:3]
        micro_s = ", ".join("%s %d" % (names[k], round(val)) for val, k in micros if val >= 1)
        portion = ("1 L = %d g real" % round(r["grams"])) if r["liquid"] else "%d g" % round(r["grams"])
        sat = v["satiety"] if not v["satiety"] == 0 else "(wheel)"
        L.append("| `%s` | %s | %s | %s | %d | %.1f | %.1f | %.1f | %.1f | %s | [%s](https://fdc.nal.usda.gov/food-details/%d/nutrients) | %s |" % (
            code, name, sat, portion, round(p["calories"] * g), p["protein"] * g, p["carbohydrate"] * g, p["fat"] * g,
            p["fiber"] * g, micro_s, r["source"]["description"].replace("|", "/"), r["source"]["fdcId"], r["notes"]))
    L.append("")
    with open(os.path.join(ROOT, "docs", "FOOD_DATA.md"), "w") as f:
        f.write("\n".join(L))


if __name__ == "__main__":
    main()
