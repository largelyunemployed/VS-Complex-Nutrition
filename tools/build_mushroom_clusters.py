#!/usr/bin/env python3
"""Builds the mushroom cluster patch from the game's own models.

Each mushroom block is drawn as a cluster, while the item you hold is one mushroom. For every species:
  grams per mushroom = held item model volume (voxels) x 0.6, at least 3 g  -> 'grams' in tools/data/food_sources.json
  drops per patch    = block model volume / item model volume, as a range of +-30% (1 below a ratio of 1.6)
  satiety per mushroom = vanilla satiety / average drops, so one patch fills you as much as in vanilla

Usage: python3 tools/build_mushroom_clusters.py <VintageStory folder>
Writes ComplexNutrition/assets/complexnutrition/patches/mushroom-clusters.json and prints the table.
"""
import glob, json, os, re, sys

GRAMS_PER_VOXEL, MIN_GRAMS, CLUSTER_RATIO = 0.6, 3, 1.6
OUT = os.path.join(os.path.dirname(__file__), '..', 'ComplexNutrition', 'assets', 'complexnutrition', 'patches', 'mushroom-clusters.json')


def load(path):
    text = open(path, encoding='utf-8-sig').read()
    try:
        return json.loads(text)
    except ValueError:  # the game's JSON5: comments, bare keys, trailing commas
        text = re.sub(r'//.*', '', text)
        text = re.sub(r'([{,]\s*)([A-Za-z_][A-Za-z0-9_]*)\s*:', r'\1"\2":', text)
        text = re.sub(r',(\s*[}\]])', r'\1', text)
        return json.loads(text)


def volume(elements):
    v = 0
    for e in elements or []:
        f, t = e['from'], e['to']
        v += abs((t[0] - f[0]) * (t[1] - f[1]) * (t[2] - f[2])) + volume(e.get('children'))
    return v


def matches(pattern, code):
    return re.fullmatch(pattern.replace('*', '.*'), code) is not None


def main(game):
    assets = os.path.join(game, 'assets', 'survival')
    shapes, blocktypes = os.path.join(assets, 'shapes'), os.path.join(assets, 'blocktypes', 'plant')
    species = [v['code'] for v in load(os.path.join(assets, 'worldproperties', 'block', 'mushroom.json'))['variants']]
    table = {}
    for s in species:
        item = volume(load(os.path.join(shapes, 'item', 'food', 'mushroom', s + '.json'))['elements'])
        blocks = glob.glob(os.path.join(shapes, 'block', 'plant', 'mushroom', s, 'normal*.json'))
        block = sum(volume(load(p)['elements']) for p in blocks) / len(blocks)
        ratio = block / item
        lo, hi = (1, 1) if ratio < CLUSTER_RATIO else (max(2, round(ratio * 0.7)), round(ratio * 1.3))
        table[s] = {'grams': max(MIN_GRAMS, round(item * GRAMS_PER_VOXEL)), 'lo': lo, 'hi': hi}

    ops = []
    for fn, suffix in (('mushroom.json', ''), ('mushroom-side.json', '-north')):
        d = load(os.path.join(blocktypes, fn))
        if 'allowedVariants' in d:
            here = [s for s in species if any(matches(x, 'mushroom-%s-x' % s) for x in d['allowedVariants'])]
        else:
            here = [s for s in species if not any(matches(x, 'mushroom-%s-x' % s) for x in d.get('skipVariants', []))]
        drops, nutrition = {}, {}
        for s in here:
            t = table[s]
            avg = (t['lo'] + t['hi']) / 2
            if t['hi'] > 1:  # only the grown patch; a harvested stump still drops one
                key = '*-%s-normal' % s + ('-*' if suffix else '')
                drops[key] = [{'type': 'block', 'code': 'mushroom-{mushroom}-normal' + suffix, 'quantity': {'avg': avg, 'var': (t['hi'] - t['lo']) / 2}}]
            props = next((dict(v) for k, v in d['nutritionPropsByType'].items() if k != '*' and matches(k, 'mushroom-%s-normal' % s)), dict(d['nutritionPropsByType']['*']))
            props['satiety'] = round(props['satiety'] / avg)
            nutrition['*-%s-*' % s] = props
        drops['*'] = [{'type': 'block', 'code': 'mushroom-{mushroom}-normal' + suffix, 'quantity': {'avg': 1, 'var': 0}}]
        f = 'game:blocktypes/plant/' + fn
        ops += [{'op': 'remove', 'path': '/drops', 'file': f, 'side': 'Server'},
                {'op': 'add', 'path': '/dropsByType', 'value': drops, 'file': f, 'side': 'Server'},
                {'op': 'replace', 'path': '/nutritionPropsByType', 'value': nutrition, 'file': f, 'side': 'Server'}]
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, 'w') as fh:
        json.dump(ops, fh, indent=1)
        fh.write('\n')
    for s, t in sorted(table.items()):
        print('%-20s %3d g  drops %d-%d' % (s, t['grams'], t['lo'], t['hi']))


if __name__ == '__main__':
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    main(sys.argv[1])
