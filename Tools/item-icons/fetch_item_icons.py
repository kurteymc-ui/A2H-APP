"""Fills the item icons the other sets (Client/assets/aion2/items/icons, Client/assets/aion2/craft/icons) leave out: souls, soulstones, Odyle,
materials, quest items and the gear pieces they miss, so every item the loot tool or the crafting calculator lists has its picture.

Source: NCSOFT's public item data (the Taiwan site's `gameconst/item` endpoint, the same the website's item pages use), which names each item's icon
on NCSOFT's own image host (`assets.playnccdn.com/static-aion2-gamedata/resources/<name>.png`). Item ids are the same as on Global. Pictures are
fetched once per icon name and stored at 64 px like the others.

Usage: python3 fetch_item_icons.py <Client/assets/aion2 dir>
Out:   items/more/<icon name>.png and items/more_icons.json {"<item id>": "<icon name>"} (only items without an icon in the other two sets).
Run by .github/workflows/item-icons.yml (it needs the internet); results go to the item-icons branch.
"""
import io
import json
import os
import sys
import time
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor

from PIL import Image

API = 'https://tw.ncsoft.com/aion2/api/gameconst/item?lang=en&enchantLevel=0&id={}'
HOST = 'https://assets.playnccdn.com/static-aion2-gamedata/resources/'
SIZE = 64
HEADERS = {'User-Agent': 'Atreia App icon fetch (github.com/kurteymc-ui/A2H-APP)', 'Accept': 'application/json, image/png'}


def get(url, tries=4):
    for attempt in range(tries):
        try:
            with urllib.request.urlopen(urllib.request.Request(url, headers=HEADERS), timeout=20) as r:
                return r.read()
        except urllib.error.HTTPError as e:
            if e.code == 404:
                return None
            time.sleep(1.5 * (attempt + 1))
        except (urllib.error.URLError, TimeoutError, ConnectionError):
            time.sleep(1.5 * (attempt + 1))
    return None


def icon_name(item_id):
    raw = get(API.format(item_id))
    if not raw:
        return item_id, None
    try:
        url = json.loads(raw).get('icon') or ''
    except ValueError:
        return item_id, None
    if not url.startswith(HOST) or not url.endswith('.png'):
        return item_id, None
    return item_id, url[len(HOST):-4]


def main(root):
    names = json.load(open(os.path.join(root, 'items', 'item_names.json'), encoding='utf-8'))
    have = set()
    for sub in ('items', 'craft'):
        m = json.load(open(os.path.join(root, sub, 'item_icons.json'), encoding='utf-8'))
        have |= {k for k, v in m.items() if os.path.exists(os.path.join(root, sub, 'icons', v + '.png'))}
    todo = sorted(k for k in names if k not in have)
    print(len(names), 'items,', len(todo), 'without an icon', flush=True)

    out_dir = os.path.join(root, 'items', 'more')
    os.makedirs(out_dir, exist_ok=True)
    mapping = {}
    with ThreadPoolExecutor(8) as pool:
        for n, (item_id, name) in enumerate(pool.map(icon_name, todo), 1):
            if name and all(c.isalnum() or c in '_-' for c in name):
                mapping[item_id] = name
            if n % 500 == 0:
                print(n, 'asked,', len(mapping), 'with an icon', flush=True)
    print(len(mapping), 'of', len(todo), 'named an icon', flush=True)

    def picture(name):
        path = os.path.join(out_dir, name + '.png')
        if os.path.exists(path):
            return name, True
        raw = get(HOST + name + '.png')
        if not raw:
            return name, False
        try:
            im = Image.open(io.BytesIO(raw)).convert('RGBA')
        except OSError:
            return name, False
        im.thumbnail((SIZE, SIZE), Image.LANCZOS)
        # a 256-colour palette (with its transparency): a third of the size, no visible difference at 64 px
        im.quantize(colors=256, method=Image.Quantize.FASTOCTREE, dither=Image.Dither.NONE).save(path, optimize=True)
        return name, True

    names_needed = sorted(set(mapping.values()))
    with ThreadPoolExecutor(8) as pool:
        ok = dict(pool.map(picture, names_needed))
    mapping = {k: v for k, v in mapping.items() if ok.get(v)}
    json.dump(dict(sorted(mapping.items())), open(os.path.join(root, 'items', 'more_icons.json'), 'w'), separators=(',', ':'))
    print(len(mapping), 'items,', sum(ok.values()), 'pictures', flush=True)
    if not mapping:
        sys.exit('nothing fetched: the item data or the image host did not answer')


if __name__ == '__main__':
    main(sys.argv[1])
