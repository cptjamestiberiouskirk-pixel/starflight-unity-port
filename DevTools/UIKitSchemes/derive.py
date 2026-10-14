# Works out every one of UIKitTheme's 24 colour roles for a scheme's dark and light mode from a few hand-picked key colours (palettes_spec.py),
# in the OKLCH colour space, with WCAG contrast checks that push the text colours until they pass. Used by make_themes.py.
import math

# ---------------------------------------------------------------- colour math (sRGB <-> OKLab/OKLCH)

def s2l(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4

def l2s(c):
    c = min(max(c, 0.0), 1.0)
    return 12.92 * c if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055

def cbrt(x):
    return math.copysign(abs(x) ** (1 / 3), x)

def hex2rgb(h):
    h = h.lstrip('#')
    return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))

def rgb2hex(rgb):
    return '#' + ''.join('%02X' % round(min(max(c, 0), 1) * 255) for c in rgb)

def lin2oklab(r, g, b):
    l = 0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b
    m = 0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b
    s = 0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b
    l, m, s = cbrt(l), cbrt(m), cbrt(s)
    return (0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
            0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s)

def oklab2lin(L, a, b):
    l = (L + 0.3963377774 * a + 0.2158037573 * b) ** 3
    m = (L - 0.1055613458 * a - 0.0638541728 * b) ** 3
    s = (L - 0.0894841775 * a - 1.2914855480 * b) ** 3
    return (4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
            -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
            -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s)

def hex2lch(h):
    r, g, b = (s2l(c) for c in hex2rgb(h))
    L, a, bb = lin2oklab(r, g, b)
    C = math.hypot(a, bb)
    H = math.degrees(math.atan2(bb, a)) % 360
    return L, C, H

def in_gamut(lin):
    return all(-0.0005 <= c <= 1.0005 for c in lin)

def lch2hex(L, C, H):
    L = min(max(L, 0.0), 1.0)
    hr = math.radians(H)
    lo, hi = 0.0, C
    lin = oklab2lin(L, C * math.cos(hr), C * math.sin(hr))
    if not in_gamut(lin):
        # reduce chroma until the colour fits in sRGB
        for _ in range(30):
            mid = (lo + hi) / 2
            if in_gamut(oklab2lin(L, mid * math.cos(hr), mid * math.sin(hr))):
                lo = mid
            else:
                hi = mid
        lin = oklab2lin(L, lo * math.cos(hr), lo * math.sin(hr))
    return rgb2hex(tuple(l2s(c) for c in lin))

def lum(h):
    r, g, b = (s2l(c) for c in hex2rgb(h))
    return 0.2126 * r + 0.7152 * g + 0.0722 * b

def contrast(a, b):
    la, lb = lum(a), lum(b)
    if la < lb:
        la, lb = lb, la
    return (la + 0.05) / (lb + 0.05)

def fit(hexcol, bgs, target, direction):
    """moves a colour's lightness (direction +1 lighter, -1 darker) until it has the target contrast with every background"""
    L, C, H = hex2lch(hexcol)
    col = hexcol
    for _ in range(200):
        if min(contrast(col, bg) for bg in bgs) >= target:
            return col
        L += 0.005 * direction
        if L <= 0.0 or L >= 1.0:
            break
        col = lch2hex(L, C, H)
    return col

def shade(hexcol, dL=0.0, cMul=1.0, L=None, C=None):
    l0, c0, h0 = hex2lch(hexcol)
    return lch2hex(L if L is not None else l0 + dL, C if C is not None else c0 * cMul, h0)

ROLES = ['backgroundApp', 'backgroundPanel', 'backgroundRaised', 'backgroundHover',
         'borderSubtle', 'borderDefault', 'borderAccent', 'borderAlert',
         'textPrimary', 'textSecondary', 'textMuted', 'textAccent', 'textOnAccent', 'textAlert', 'textWarning',
         'accentPrimary', 'accentPrimaryStrong', 'accentPrimaryDim', 'accentSecondary',
         'statusShield', 'statusArmor', 'statusFuel', 'statusDanger', 'sensorWindow']

# ---------------------------------------------------------------- deriving a mode

def build(spec, mode):
    s = dict(spec)
    dark = (mode == 'dark')
    bh, bc = s['bg_h'], s['bg_c']
    bl = s['bg_L']
    r = {}
    r['backgroundApp'] = lch2hex(bl[0], bc, bh)
    r['backgroundPanel'] = lch2hex(bl[1], bc, bh)
    r['backgroundRaised'] = lch2hex(bl[2], bc * (1.15 if dark else 0.8), bh)
    r['backgroundHover'] = lch2hex(bl[3], bc * (1.25 if dark else 1.1), bh)
    brl = s.get('border_L', [bl[3] + 0.02, bl[3] + 0.10] if dark else [bl[1] - 0.09, bl[1] - 0.22])
    r['borderSubtle'] = lch2hex(brl[0], bc * 1.4, bh)
    r['borderDefault'] = lch2hex(brl[1], bc * 1.7, bh)

    acc = s['accent']
    acc2 = s['accent2']
    danger = s['danger']
    warning = s.get('warning', acc2)
    fuel = s['fuel']
    shield = s.get('shield', acc)
    armor = s.get('armor', acc2)

    th = s.get('text_h', bh)
    tc = s.get('text_c', 0.015)
    if dark:
        r['textPrimary'] = lch2hex(s.get('tp_L', 0.955), tc, th)
        r['textSecondary'] = lch2hex(s.get('ts_L', 0.80), s.get('ts_c', tc * 2.5), th)
        r['textMuted'] = lch2hex(s.get('tm_L', 0.60), s.get('tm_c', tc * 3.0), th)
    else:
        r['textPrimary'] = lch2hex(s.get('tp_L', 0.21), tc, th)
        r['textSecondary'] = lch2hex(s.get('ts_L', 0.36), s.get('ts_c', tc * 1.8), th)
        r['textMuted'] = lch2hex(s.get('tm_L', 0.49), s.get('tm_c', tc * 2.0), th)

    r['accentPrimary'] = acc
    r['accentSecondary'] = acc2
    r['statusShield'] = shield
    r['statusArmor'] = armor
    r['statusFuel'] = fuel
    r['statusDanger'] = danger
    r['textAlert'] = s.get('text_alert', danger)
    r['textWarning'] = warning
    r['borderAccent'] = s.get('border_accent', acc)
    r['borderAlert'] = danger

    if dark:
        r['textAccent'] = s.get('text_accent', shade(acc, dL=+0.05, cMul=0.85))
        r['accentPrimaryStrong'] = s.get('accent_strong', shade(acc, dL=-0.08, cMul=1.05))
        r['accentPrimaryDim'] = s.get('accent_dim', shade(acc, L=s.get('dim_L', 0.47), cMul=0.75))
    else:
        r['textAccent'] = s.get('text_accent', shade(acc, dL=-0.06))
        r['accentPrimaryStrong'] = s.get('accent_strong', shade(acc, dL=-0.07, cMul=1.05))
        r['accentPrimaryDim'] = s.get('accent_dim', shade(acc, L=s.get('dim_L', 0.86), cMul=0.35))

    l0, c0, h0 = hex2lch(r['backgroundPanel'])
    sw = s.get('sensor', None)
    r['sensorWindow'] = sw if sw else lch2hex(0.43 if dark else 0.56, max(bc * 2.5, 0.055), s.get('sensor_h', bh))

    # ---- contrast fixes
    bgs = [r['backgroundApp'], r['backgroundPanel'], r['backgroundRaised']]
    d = +1 if dark else -1
    r['textPrimary'] = fit(r['textPrimary'], bgs + [r['backgroundHover']], 12.0 if dark else 11.0, d)
    r['textSecondary'] = fit(r['textSecondary'], bgs, 7.0 if dark else 6.5, d)
    r['textMuted'] = fit(r['textMuted'], [r['backgroundPanel']], 3.2 if dark else 3.6, d)
    coloured_text = ['textAccent', 'textAlert', 'textWarning', 'statusFuel', 'statusShield', 'statusArmor', 'statusDanger', 'accentSecondary']
    if not dark:
        coloured_text.append('accentPrimary')
    for role in coloured_text:
        r[role] = fit(r[role], bgs, 4.6, d)
    if dark:
        r['accentPrimary'] = fit(r['accentPrimary'], bgs, 4.6, d)
    # the selected button: the label (text primary) on accentPrimaryDim
    if dark:
        r['accentPrimaryDim'] = fit(r['accentPrimaryDim'], [r['textPrimary']], 4.6, -1)
    else:
        r['accentPrimaryDim'] = fit(r['accentPrimaryDim'], [r['textPrimary']], 8.0, +1)
    # text on the accent fill
    if 'text_on_accent' in s:
        r['textOnAccent'] = s['text_on_accent']
    else:
        cand = [r['backgroundApp'], '#FFFFFF', lch2hex(0.15, 0.02, bh)]
        r['textOnAccent'] = max(cand, key=lambda c: contrast(c, r['accentPrimary']))
    # the sensor window must stand well clear of the black silhouettes drawn on it
    r['sensorWindow'] = fit(r['sensorWindow'], ['#000000'], 3.0 if dark else 4.0, +1)
    r['borderSubtle'] = fit(r['borderSubtle'], [r['backgroundPanel']], 1.35, d)
    r['borderDefault'] = fit(r['borderDefault'], [r['backgroundPanel']], 1.9, d)

    # ---- report
    rep = {
        'textPrimary/panel': contrast(r['textPrimary'], r['backgroundPanel']),
        'textSecondary/raised': contrast(r['textSecondary'], r['backgroundRaised']),
        'textMuted/panel': contrast(r['textMuted'], r['backgroundPanel']),
        'textAccent/panel': contrast(r['textAccent'], r['backgroundPanel']),
        'textAlert/panel': contrast(r['textAlert'], r['backgroundPanel']),
        'textWarning/panel': contrast(r['textWarning'], r['backgroundPanel']),
        'fuel/panel': contrast(r['statusFuel'], r['backgroundPanel']),
        'label/selected': contrast(r['textPrimary'], r['accentPrimaryDim']),
        'onAccent/accent': contrast(r['textOnAccent'], r['accentPrimary']),
        'accent/panel': contrast(r['accentPrimary'], r['backgroundPanel']),
        'sensor/black': contrast(r['sensorWindow'], '#000000'),
    }
    return {k: r[k] for k in ROLES}, {k: round(v, 2) for k, v in rep.items()}
