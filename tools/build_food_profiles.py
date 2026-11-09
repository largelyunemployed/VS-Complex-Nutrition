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
    # Vitamins and minerals in real units (mg or mcg, as USDA gives them and the game shows them), since v0.3.2.
    for key, (nid, dv) in DV.items():
        if nid not in n:
            gaps.append(key)
        out[key] = n.get(nid, 0.0)
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
    # 'set': values per 100 g chosen by design, replacing the USDA ones (for species with no lab data,
    # or to keep a nutrient in a range the game wants). A set value is not a gap.
    unknown = set(entry.get("set", {})) - set(ALL)
    if unknown:
        sys.exit("%s: unknown nutrient in set: %s" % (entry["code"], sorted(unknown)))
    for k, v in entry.get("set", {}).items():
        prof[k] = float(v)
    gaps = [g for g in gaps if g not in entry.get("set", {})]
    gaps = [g for g in gaps if g not in entry.get("gameplayBonus", {})]
    return prof, gaps


def with_gameplay_bonus(entry, per100):
    """
    The profile per 100 g with the entry's 'gameplayBonus' (since v0.4.1) added: extra nutrient given by design as a share
    of a day's need per 100 g, e.g. {"calcium": 45} adds 45% of the calcium Daily Value for every 100 g. The category
    templates for other mods' foods use the values without it, so a bonus never spreads to foods it wasn't given to.
    """
    boosted = dict(per100)
    for nutrient, percent_of_day_per_100g in entry.get("gameplayBonus", {}).items():
        boosted[nutrient] = boosted.get(nutrient, 0.0) + daily_value_amount(nutrient) * float(percent_of_day_per_100g) / 100.0
    return boosted


def daily_value_amount(nutrient):
    """A day's need of a vitamin or mineral in the profiles' unit (mg or mcg); the B vitamins are already in %DV."""
    if nutrient == "vitaminB":
        return 100.0
    if nutrient not in DV:
        sys.exit("gameplayBonus: %s has no Daily Value" % nutrient)
    return DV[nutrient][1]


def describe_bonus(entry):
    """'Gameplay bonus (per 100 g, by design): calcium +45% of a day's need', or '' for none."""
    bonus = entry.get("gameplayBonus")
    if not bonus:
        return ""
    return "Gameplay bonus (per 100 g, by design): " + ", ".join("%s +%g%% of a day's need" % (k, bonus[k]) for k in ALL if k in bonus)


def describe_set(entry):
    if not entry.get("set"):
        return ""
    return "Set by design (per 100 g): " + ", ".join("%s %g" % (k, entry["set"][k]) for k in ALL if k in entry["set"])


def as_factor(f):
    if isinstance(f, (int, float)):
        return {k: float(f) for k in ALL}
    unknown = set(f) - set(ALL)
    if unknown:
        sys.exit("unknown nutrient in factor: %s" % sorted(unknown))
    return {k: float(f.get(k, 1.0)) for k in ALL}


def describe_factor(f):
    """'Gameplay multiplier x0.8' or 'Gameplay multiplier: vitaminC x0.6, vitaminB x0.6', or '' for none."""
    vals = {round(v, 3) for v in f.values()}
    if vals == {1.0}:
        return ""
    if len(vals) == 1:
        return "Gameplay multiplier x%g on all nutrients" % vals.pop()
    return "Gameplay multiplier: " + ", ".join("%s x%g" % (k, f[k]) for k in ALL if round(f[k], 3) != 1.0)


def rnd(key, v):
    if key == "calories":
        return round(v, 1)
    if key in DV:
        return round(v, 3)
    return round(v, 2)


def dv_percent(key, v):
    """A value in our profile units as % of the daily value (vitamin B is already a %DV)."""
    return v / DV[key][1] * 100.0 if key in DV else v


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--usda", required=True, help="Folder of the SR Legacy CSV download")
    args = ap.parse_args()

    vanilla = json.load(open(os.path.join(DATA, "vanilla_foods_1.22.7.json")))
    by_code = {v["code"]: v for v in vanilla}
    src = json.load(open(os.path.join(DATA, "food_sources.json")), object_pairs_hook=OrderedDict)
    ratio = float(src["fallbackKcalPerSatiety"])

    wanted = {i for g in src["groups"].values() for e in g for i in e.get("fdc", []) + e.get("fillFrom", [])}
    usda = load_usda(args.usda, wanted)

    claimed = {}
    slice_grams = {}
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

            real_per100, gaps = build_entry_profile(e, usda)
            per100 = with_gameplay_bonus(e, real_per100)
            kcal = per100["calories"]
            if kcal <= 0:
                sys.exit("%s: zero calories" % pattern)
            source = {"fdcId": e["fdc"][0], "description": usda[e["fdc"][0]]["description"], "dataset": "SR Legacy"}
            if len(e["fdc"]) > 1:
                source["description"] = " + ".join(usda[i]["description"] for i in e["fdc"]) + " (equal mix)"
            if e.get("estimated"):
                # A species with no lab data of its own: say which real food it was built on.
                source["description"] = "estimate built on " + source["description"]
            is_liquid = all(by_code[c]["kind"] == LIQUID_KIND for c in matches)

            def portion_for(code):
                # Real weight of one in-game item (grams per litre for liquids), independent of satiety.
                if "slicesOf" in e:
                    return slice_grams[e["slicesOf"]] * e["slices"]
                if "gramsPerUnit" in e:
                    units = [int(part) for part in code.split("-") if part.isdigit()]
                    if len(units) != 1:
                        sys.exit("%s: cannot read a piece count from %s" % (pattern, code))
                    return e["gramsPerUnit"] * units[0]
                if "grams" not in e:
                    sys.exit("%s: no grams given" % pattern)
                return float(e["grams"])

            def factor_for(code):
                # Gameplay multipliers on top of the USDA values: 'factor' for the whole entry, and
                # 'stateFactors' for a cooking or baking state named in the item code. Each is either
                # one number for every nutrient or a {nutrient: multiplier} map.
                parts = code.split(":")[-1].split("-")
                f = as_factor(e.get("factor", 1.0))
                for state, sf in e.get("stateFactors", {}).items():
                    if state in parts:
                        sf = as_factor(sf)
                        f = {k: f[k] * sf[k] for k in ALL}
                return f

            portions = {c: portion_for(c) for c in matches}
            factors = {c: factor_for(c) for c in matches}
            for c in matches:
                slice_grams[c] = portions[c]
            uniform = len({(round(portions[c], 3), tuple(factors[c][k] for k in ALL)) for c in matches}) == 1
            codes_out = [pattern] if ("*" in pattern and uniform) else sorted(matches)
            for code in codes_out:
                grams = portions[code] if code in portions else next(iter(portions.values()))
                factor = factors[code] if code in factors else next(iter(factors.values()))
                prof = OrderedDict()
                prof["code"] = code
                prof["portionGrams"] = round(grams, 1)
                if is_liquid:
                    prof["portionUnit"] = "litre"
                prof["per100g"] = OrderedDict((k, rnd(k, per100[k] * factor[k])) for k in ALL)
                prof["source"] = source
                notes = e.get("notes", "")
                if describe_set(e):
                    notes = (notes + " " if notes else "") + describe_set(e) + "."
                if describe_factor(factor):
                    notes = (notes + " " if notes else "") + describe_factor(factor) + "."
                if describe_bonus(e):
                    notes = (notes + " " if notes else "") + describe_bonus(e) + "."
                if gaps:
                    notes = (notes + " " if notes else "") + "USDA reports no value for: %s (treated as 0)." % ", ".join(gaps)
                if notes:
                    prof["notes"] = notes
                profiles.append(prof)

            for c in matches:
                rows.append({"code": c, "v": by_code[c], "fallback": False, "grams": portions[c],
                             "per100": {k: per100[k] * factors[c][k] for k in ALL},
                             "source": source, "liquid": is_liquid, "group": group,
                             "notes": (e.get("notes", "") + (" " + describe_factor(factors[c]) + "." if describe_factor(factors[c]) else "")).strip()})
            # 'noTemplate': leave this entry out of the category templates, so a family with many
            # entries (the mushrooms) does not outweigh the rest of its category.
            if not is_liquid and not e.get("noTemplate"):
                template_inputs[by_code[matches[0]]["category"]].append(real_per100)
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
            f.write(dump_profiles(profiles, "Generated by tools/build_food_profiles.py from USDA FoodData Central (SR Legacy). Values per 100 g: calories in kcal, macros and fiber in g, vitamins and minerals in mg or mcg (see nutrients.json), the B vitamins in % of the FDA Daily Value. Edit tools/data/food_sources.json and re-run rather than editing by hand."))
    with open(os.path.join(ASSETS, "foodfallbacks.json"), "w") as f:
        json.dump(OrderedDict([
            ("_comment", "Templates for foods with no profile of their own, by vanilla food category: the mean of this mod's profiles in that category. Units as in the profiles (vitamins and minerals in mg or mcg). Foods with no profile get their portion from vanilla satiety: kcal = satiety x kcalPerSatiety. Generated by tools/build_food_profiles.py."),
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
    L.append("**How portions are sized.** Each item has a real-world weight chosen for what it looks like in game (an apple is 180 g, a cabbage head 900 g, a loaf 225 g, a cut of raw red meat 300 g). Calories and nutrients follow from that weight, so they are no longer tied to vanilla satiety; the Satiety and kcal columns show how the two compare. Liquids are given per litre. Only foods with no profile of their own (the insects below, and foods from other mods) are sized from satiety, at %.1f kcal per satiety point, because nothing else is known about them." % ratio)
    L.append("")
    L.append("**Sources.** USDA FoodData Central, SR Legacy (April 2018). Vitamins and minerals are stored in mg or mcg as USDA gives them, and shown here as %% of the US FDA Daily Value; \"B\" is the mean %%DV of thiamin, riboflavin, niacin, B6, folate and B12 (each capped at 300%%). Where USDA has no matching food, the nearest real food is used and the note says so.".replace("%%", "%"))
    L.append("")
    L.append("| Item | Name | Satiety | Portion | kcal | Protein g | Carbs g | Fats g | Fiber g | Top micronutrients (%DV per portion) | USDA source | Notes |")
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
        micros = sorted(((dv_percent(k, p[k]) * g, k) for k in MICROS), reverse=True)[:3]
        micro_s = ", ".join("%s %d" % (names[k], round(val)) for val, k in micros if val >= 1)
        portion = "1 L" if r["liquid"] else "%d g" % round(r["grams"])
        sat = v["satiety"] if not v["satiety"] == 0 else "(wheel)"
        L.append("| `%s` | %s | %s | %s | %d | %.1f | %.1f | %.1f | %.1f | %s | [%s](https://fdc.nal.usda.gov/food-details/%d/nutrients) | %s |" % (
            code, name, sat, portion, round(p["calories"] * g), p["protein"] * g, p["carbohydrate"] * g, p["fat"] * g,
            p["fiber"] * g, micro_s, r["source"]["description"].replace("|", "/"), r["source"]["fdcId"], r["notes"]))
    L.append("")
    with open(os.path.join(ROOT, "docs", "FOOD_DATA.md"), "w") as f:
        f.write("\n".join(L))


if __name__ == "__main__":
    main()
