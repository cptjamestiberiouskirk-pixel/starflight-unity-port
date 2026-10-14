"""Writes the UI kit's colour schemes as theme assets: Assets/Resources/UI Kit Themes/<Scheme> Dark.asset and <Scheme> Light.asset.

Each scheme of palettes_spec.py (the owner's palette options of 2026-10-13, all twelve kept) gets every one of UIKitTheme's 24 colour roles
for each mode from derive.py; the original games' schemes (ORIGINALS) give theirs as they are. The assets are copies of the classic dark
theme (Resources/UI Kit Theme.asset: the fonts, text styles and sprites are the kit's) with the colours and the name replaced, and their
.meta files get GUIDs made from the asset names, so running the script again gives the same GUIDs. The schemes' names must match
UIKitTheme.c_schemes. A table of every colour and its contrast ratios is written to schemes.json.

    python make_themes.py <project root>

Needs Python 3 only.
"""
import hashlib
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from derive import ROLES, build, contrast
from palettes_spec import ORIGINALS, PALETTES

FOLDER = 'Assets/Resources/UI Kit Themes'


def guid(name):
    return hashlib.md5(('starflight ui kit scheme ' + name).encode('utf-8')).hexdigest()


def colour_line(role, hexcolour):
    h = hexcolour.lstrip('#')
    r, g, b = (int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))
    return '  m_%s: {r: %s, g: %s, b: %s, a: 1}' % (role, fmt(r), fmt(g), fmt(b))


def fmt(value):
    text = ('%.7f' % value).rstrip('0').rstrip('.')
    return text if text else '0'


def theme_roles(spec, mode):
    """the 24 colours of a mode: given as they are for an original game's scheme, worked out by derive.py for the others"""
    if 'roles' not in spec:
        return build(spec, mode)
    roles = {role: spec['roles'][role].upper() for role in ROLES}
    report = {
        'textPrimary/panel': contrast(roles['textPrimary'], roles['backgroundPanel']),
        'textSecondary/raised': contrast(roles['textSecondary'], roles['backgroundRaised']),
        'label/selected': contrast(roles['textPrimary'], roles['accentPrimaryDim']),
        'onAccent/accent': contrast(roles['textOnAccent'], roles['accentPrimary']),
        'sensor/black': contrast(roles['sensorWindow'], '#000000'),
    }
    return roles, {k: round(v, 2) for k, v in report.items()}


def main():
    root = sys.argv[1]
    template = open(os.path.join(root, 'Assets/Resources/UI Kit Theme.asset'), encoding='utf-8', newline='').read()
    newline = '\r\n' if '\r\n' in template else '\n'
    folder = os.path.join(root, FOLDER)
    os.makedirs(folder, exist_ok=True)

    folder_meta = folder + '.meta'
    if not os.path.exists(folder_meta):
        open(folder_meta, 'w', encoding='utf-8', newline='').write(newline.join([
            'fileFormatVersion: 2', 'guid: ' + guid('folder'), 'folderAsset: yes', 'DefaultImporter:', '  externalObjects: {}',
            '  userData: ', '  assetBundleName: ', '  assetBundleVariant: ', '']))

    table = []
    for palette in ORIGINALS + PALETTES:
        entry = {'name': palette['name'], 'tagline': palette['tagline']}
        for mode in ('dark', 'light'):
            roles, report = theme_roles(palette[mode], mode)
            name = '%s %s' % (palette['name'], mode.capitalize())
            text = re.sub(r'^  m_Name: [^\r\n]*', '  m_Name: ' + name, template, count=1, flags=re.M)
            for role in ROLES:
                text, count = re.subn(r'^  m_%s: \{r: [^}]*\}' % role, colour_line(role, roles[role]), text, count=1, flags=re.M)
                if count != 1:
                    sys.exit('the template has no colour %s' % role)
            open(os.path.join(folder, name + '.asset'), 'w', encoding='utf-8', newline='').write(text)
            open(os.path.join(folder, name + '.asset.meta'), 'w', encoding='utf-8', newline='').write(newline.join([
                'fileFormatVersion: 2', 'guid: ' + guid(name), 'NativeFormatImporter:', '  externalObjects: {}', '  mainObjectFileID: 11400000',
                '  userData: ', '  assetBundleName: ', '  assetBundleVariant: ', '']))
            entry[mode] = {'name': palette[mode].get('name', ''), 'roles': roles, 'contrast': report}
        table.append(entry)
        print('%-16s dark %s on %s, light %s on %s' % (palette['name'], entry['dark']['roles']['accentPrimary'], entry['dark']['roles']['backgroundPanel'],
                                                       entry['light']['roles']['accentPrimary'], entry['light']['roles']['backgroundPanel']))

    with open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'schemes.json'), 'w', encoding='utf-8', newline='\n') as f:
        json.dump(table, f, indent=1)


if __name__ == '__main__':
    main()
