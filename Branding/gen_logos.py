# Generates the UltimateKTV logo SVGs:  python gen_logos.py <out_dir>
#
# PNG/ICO are not kept in the repo. To regenerate, render each SVG with Edge:
#   msedge --headless=new --hide-scrollbars --default-background-color=00000000
#          --force-device-scale-factor=1 --window-size=<w>,<h> --screenshot=<name>.png file:///<name>.svg
# then build the .ico with Pillow (sizes 16-256) and copy into the project:
#   ultimatektv-splash      -> logo.jpg (JPEG, quality 92)
#   ultimatektv-icon        -> icon.ico
#   ultimatektv-updater-icon        -> Updater/UpdaterIcon.ico
#   ultimatektv-stylesimulator-icon -> StyleSimulator/StyleSimulator.ico
import os, sys, math

OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)

DEFS = """
<linearGradient id="gold" x1="0" y1="0" x2="0" y2="1">
  <stop offset="0" stop-color="#fff6c8"/><stop offset=".35" stop-color="#f3c548"/>
  <stop offset=".62" stop-color="#b9821a"/><stop offset=".8" stop-color="#e9b43a"/>
  <stop offset="1" stop-color="#fff0a8"/>
</linearGradient>
<linearGradient id="goldH" x1="0" y1="0" x2="1" y2="0">
  <stop offset="0" stop-color="#9c6a12"/><stop offset=".5" stop-color="#ffe9a0"/><stop offset="1" stop-color="#9c6a12"/>
</linearGradient>
<linearGradient id="silver" x1="0" y1="0" x2="0" y2="1">
  <stop offset="0" stop-color="#ffffff"/><stop offset=".45" stop-color="#d6dbe2"/>
  <stop offset=".6" stop-color="#8b939e"/><stop offset="1" stop-color="#f2f4f7"/>
</linearGradient>
<linearGradient id="emerald" x1="0" y1="0" x2="0" y2="1">
  <stop offset="0" stop-color="#5cf2a8"/><stop offset=".5" stop-color="#14a862"/><stop offset="1" stop-color="#0a5c36"/>
</linearGradient>
<radialGradient id="night" cx=".5" cy=".38" r=".75">
  <stop offset="0" stop-color="#3a1a5e"/><stop offset=".55" stop-color="#170c33"/><stop offset="1" stop-color="#07081a"/>
</radialGradient>
<radialGradient id="spot" cx=".5" cy="0" r=".9">
  <stop offset="0" stop-color="#ffe7a0" stop-opacity=".55"/><stop offset=".5" stop-color="#ffd36b" stop-opacity=".08"/>
  <stop offset="1" stop-color="#ffd36b" stop-opacity="0"/>
</radialGradient>
<filter id="glow" x="-30%" y="-30%" width="160%" height="160%">
  <feGaussianBlur in="SourceAlpha" stdDeviation="6" result="b"/>
  <feFlood flood-color="#ffcf55" flood-opacity=".55"/><feComposite in2="b" operator="in" result="g"/>
  <feMerge><feMergeNode in="g"/><feMergeNode in="SourceGraphic"/></feMerge>
</filter>
<filter id="greenGlow" x="-30%" y="-30%" width="160%" height="160%">
  <feGaussianBlur in="SourceAlpha" stdDeviation="5" result="b"/>
  <feFlood flood-color="#2ee08a" flood-opacity=".7"/><feComposite in2="b" operator="in" result="g"/>
  <feMerge><feMergeNode in="g"/><feMergeNode in="SourceGraphic"/></feMerge>
</filter>
<filter id="shadow" x="-20%" y="-20%" width="140%" height="150%">
  <feDropShadow dx="0" dy="6" stdDeviation="6" flood-color="#000" flood-opacity=".6"/>
</filter>
<clipPath id="micHead"><circle cx="256" cy="196" r="64"/></clipPath>
<!-- light green (mint) palette, used by the splash screen -->
<linearGradient id="mint" x1="0" y1="0" x2="0" y2="1">
  <stop offset="0" stop-color="#9ff0c4"/><stop offset=".4" stop-color="#3cc283"/>
  <stop offset=".65" stop-color="#17804f"/><stop offset=".85" stop-color="#2fae6f"/>
  <stop offset="1" stop-color="#8fe8b8"/>
</linearGradient>
<linearGradient id="mintH" x1="0" y1="0" x2="1" y2="0">
  <stop offset="0" stop-color="#2fae6f" stop-opacity="0"/><stop offset=".5" stop-color="#17804f"/>
  <stop offset="1" stop-color="#2fae6f" stop-opacity="0"/>
</linearGradient>
<radialGradient id="meadow" cx=".5" cy=".38" r=".8">
  <stop offset="0" stop-color="#f7fff9"/><stop offset=".5" stop-color="#dcf5e5"/><stop offset="1" stop-color="#a9e2c0"/>
</radialGradient>
<radialGradient id="sun" cx=".5" cy="0" r=".9">
  <stop offset="0" stop-color="#ffffff" stop-opacity=".85"/><stop offset=".5" stop-color="#ffffff" stop-opacity=".15"/>
  <stop offset="1" stop-color="#ffffff" stop-opacity="0"/>
</radialGradient>
<filter id="softShadow" x="-20%" y="-20%" width="140%" height="150%">
  <feDropShadow dx="0" dy="6" stdDeviation="7" flood-color="#0b4a2c" flood-opacity=".35"/>
</filter>
"""


def sunburst(cx, cy, r, n, color, op, width=2):
    lines = []
    for i in range(n + 1):
        a = math.pi + math.pi * i / n
        lines.append(f'<line x1="{cx}" y1="{cy}" x2="{cx + r*math.cos(a):.1f}" y2="{cy + r*math.sin(a):.1f}"/>')
    return f'<g stroke="{color}" stroke-opacity="{op}" stroke-width="{width}">{"".join(lines)}</g>'


def deco_fans(cx, cy, radii, color, op):
    arcs = "".join(
        f'<path d="M{cx-r} {cy} A{r} {r} 0 0 1 {cx+r} {cy}" fill="none"/>' for r in radii)
    return f'<g stroke="{color}" stroke-opacity="{op}" stroke-width="2">{arcs}</g>'


# The mark is drawn in a 512x512 box: microphone inside an art-deco medallion,
# emerald collar and grip, music note accent.
def mark(mono=None, ring='url(#gold)', note='url(#silver)'):
    g = f'url(#gold)' if not mono else mono
    s = note if not mono else mono
    e = f'url(#emerald)' if not mono else mono
    r = ring if not mono else mono
    grid = "" if mono else (
        '<g clip-path="url(#micHead)" stroke="#6b4a0c" stroke-opacity=".55" stroke-width="3">'
        + "".join(f'<line x1="{x}" y1="120" x2="{x}" y2="270"/>' for x in range(196, 320, 12))
        + "".join(f'<line x1="180" y1="{y}" x2="330" y2="{y}"/>' for y in range(136, 262, 12))
        + '</g>'
        '<ellipse cx="236" cy="168" rx="26" ry="16" fill="#fff" fill-opacity=".45" transform="rotate(-30 236 168)"/>')
    glow = '' if mono else ' filter="url(#glow)"'
    return f"""
<g>
  <!-- medallion -->
  <circle cx="256" cy="256" r="232" fill="none" stroke="{r}" stroke-width="10"/>
  <circle cx="256" cy="256" r="212" fill="none" stroke="{r}" stroke-width="3" stroke-opacity=".8"/>
  <g fill="{r}">
    <path d="M256 6 l10 18 -10 18 -10 -18z"/><path d="M256 470 l10 18 -10 18 -10 -18z"/>
    <path d="M6 256 l18 -10 18 10 -18 10z"/><path d="M470 256 l18 -10 18 10 -18 10z"/>
  </g>
  <!-- microphone -->
  <g{glow} transform="translate(0 -30)">
    <circle cx="256" cy="196" r="64" fill="{g}"/>
    {grid}
    <rect x="212" y="256" width="88" height="22" rx="6" fill="{e}"/>
    <path d="M220 280 H292 L276 430 Q256 448 236 430 Z" fill="{g}"/>
    <rect x="246" y="312" width="20" height="40" rx="10" fill="{e}"/>
  </g>
  <!-- music note -->
  <g fill="{s}"{glow}>
    <path d="M356 104 l52 -16 v74 a18 14 0 1 1 -10 -12 v-38 l-32 10 v52 a18 14 0 1 1 -10 -12z"/>
  </g>
</g>"""


def wordmark(x, y, size, mono=None, anchor="start", first='url(#gold)', second='url(#silver)',
             first_stroke="#4a2c05", second_stroke="#4a2c05", shadow="shadow"):
    g = first if not mono else mono
    s = second if not mono else mono
    stroke = lambda c: '' if mono else f' stroke="{c}" stroke-width="2" paint-order="stroke"'
    flt = '' if mono else f' filter="url(#{shadow})"'
    return (f'<text x="{x}" y="{y}" text-anchor="{anchor}" font-family="Bahnschrift, \'Segoe UI\', sans-serif" '
            f'font-weight="700" font-stretch="semi-condensed" font-size="{size}" letter-spacing="2"{flt}>'
            f'<tspan fill="{g}"{stroke(first_stroke)}>Ultimate</tspan><tspan fill="{s}"{stroke(second_stroke)}>KTV</tspan></text>')


def svg(w, h, body, bg=None):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}">'
            f'<defs>{DEFS}</defs>{body}</svg>')


files = {}

# 1. App icon (square, rounded, dark background) -------------------------------
files["ultimatektv-icon"] = (512, 512, f"""
<rect width="512" height="512" rx="96" fill="url(#night)"/>
{sunburst(256, 470, 420, 24, "#f3c548", .14)}
<rect width="512" height="512" rx="96" fill="url(#spot)"/>
<g transform="translate(36 30) scale(.86)">{mark()}</g>
<rect x="5" y="5" width="502" height="502" rx="92" fill="none" stroke="url(#gold)" stroke-width="6" stroke-opacity=".7"/>
""")

# 2. Badge: icon + KTV text, for small surfaces like the web remote ------------
files["ultimatektv-badge"] = (512, 512, f"""
<rect width="512" height="512" rx="96" fill="url(#night)"/>
{deco_fans(256, 512, range(80, 520, 44), "#f3c548", .16)}
<rect width="512" height="512" rx="96" fill="url(#spot)"/>
<g transform="translate(96 22) scale(.625)">{mark()}</g>
<text x="256" y="468" text-anchor="middle" font-family="Bahnschrift, 'Segoe UI', sans-serif" font-weight="700"
      font-size="128" letter-spacing="10" fill="url(#gold)" stroke="#4a2c05" stroke-width="3" paint-order="stroke"
      filter="url(#shadow)">KTV</text>
<rect x="5" y="5" width="502" height="502" rx="92" fill="none" stroke="url(#gold)" stroke-width="6" stroke-opacity=".7"/>
""")

# 3. Horizontal lockup, transparent background ---------------------------------
files["ultimatektv-horizontal"] = (1400, 360, f"""
<g transform="translate(20 20) scale(.625)">{mark()}</g>
{wordmark(370, 228, 168)}
<g fill="url(#goldH)">
  <rect x="374" y="262" width="980" height="4" rx="2"/>
  <path d="M864 252 l12 12 -12 12 -12 -12z"/>
</g>
<text x="864" y="318" text-anchor="middle" font-family="Bahnschrift, 'Segoe UI', sans-serif" font-size="34"
      letter-spacing="18" fill="#2ee08a">KARAOKE · 點歌系統</text>
""")

# 4. Splash screen (logo.jpg): light green (mint) as the main colour, gold mic as accent
curtain = lambda x, w, c1, c2, flip: (
    f'<linearGradient id="cur{x}" x1="{1 if flip else 0}" x2="{0 if flip else 1}"><stop offset="0" stop-color="{c1}"/>'
    f'<stop offset="1" stop-color="{c2}" stop-opacity="0"/></linearGradient>')
files["ultimatektv-splash"] = (1920, 1080, f"""
<defs>{curtain(0, 0, "#8fdcb2", "#8fdcb2", False)}{curtain(1, 0, "#8fdcb2", "#8fdcb2", True)}</defs>
<rect width="1920" height="1080" fill="url(#meadow)"/>
<rect width="480" height="1080" fill="url(#cur0)"/>
<rect x="1440" width="480" height="1080" fill="url(#cur1)"/>
<rect width="1920" height="1080" fill="url(#sun)"/>
<g transform="translate(750 80) scale(.82)" filter="url(#softShadow)">{mark(ring='url(#mint)', note='url(#mint)')}</g>
{wordmark(960, 745, 240, anchor="middle", first='url(#mint)', second='url(#gold)',
          first_stroke="#0b4a2c", second_stroke="#6b4a0c", shadow="softShadow")}
<g fill="url(#mintH)">
  <rect x="440" y="796" width="1040" height="5" rx="2.5"/>
</g>
<path d="M960 780 l18 18 -18 18 -18 -18z" fill="url(#gold)"/>
<text x="960" y="895" text-anchor="middle" font-family="Bahnschrift, 'Segoe UI', sans-serif" font-size="48"
      letter-spacing="26" fill="#17804f">KARAOKE · 點歌系統</text>
""")

# 5. Single-colour gold mark (watermark / overlays) ----------------------------
files["ultimatektv-mark-gold"] = (512, 512, mark("#e8b93a"))

# 6. Single-colour white lockup (for dark video overlays) ----------------------
files["ultimatektv-horizontal-white"] = (1400, 360, f"""
<g transform="translate(20 20) scale(.625)">{mark("#ffffff")}</g>
{wordmark(370, 228, 168, mono="#ffffff")}
<rect x="374" y="262" width="980" height="4" rx="2" fill="#ffffff"/>
""")

# Companion app icons use their own silhouettes so they are not mistaken for
# the main app: the updater is a frameless refresh arrow, the style simulator
# a gold hexagon with mixer sliders.
def updater_icon():
    cx, cy, r = 256, 256, 150
    a0, a1 = math.radians(-50), math.radians(222)
    p = lambda a: (cx + r * math.cos(a), cy + r * math.sin(a))
    x0, y0 = p(a0); x1, y1 = p(a1)
    ae = math.radians(228)  # arrowhead sits at the end of the arc
    ex, ey = p(ae)
    tx, ty = -math.sin(ae), math.cos(ae)
    nx, ny = math.cos(ae), math.sin(ae)
    head = (f"M{ex + 48*tx:.1f} {ey + 48*ty:.1f} L{ex + 38*nx:.1f} {ey + 38*ny:.1f} "
            f"L{ex - 38*nx:.1f} {ey - 38*ny:.1f}Z")
    return f"""
<g transform="translate(256 256) scale(1.45) translate(-256 -256)">
  <g filter="url(#greenGlow)">
    <path d="M{x0:.1f} {y0:.1f} A{r} {r} 0 1 1 {x1:.1f} {y1:.1f}" fill="none" stroke="url(#emerald)" stroke-width="34" stroke-linecap="round"/>
    <path d="{head}" fill="url(#emerald)"/>
  </g>
  <g filter="url(#shadow)">
    <rect x="228" y="170" width="56" height="104" rx="12" fill="url(#gold)"/>
    <path d="M182 264 H330 L256 350Z" fill="url(#gold)"/>
  </g>
</g>"""


def stylesimulator_icon():
    hexpts = " ".join(f"{256 + 246*math.cos(math.radians(90 + 60*i)):.1f},{256 + 246*math.sin(math.radians(90 + 60*i)):.1f}"
                      for i in range(6))
    knobs = [(176, 290, "#14a862"), (256, 190, "#7b2fbf"), (336, 250, "#e04060")]
    tracks = "".join(f'<rect x="{x-10}" y="136" width="20" height="240" rx="10" fill="#2a1a3e"/>' for x, _, _ in knobs)
    caps = "".join(f'<rect x="{x-40}" y="{y-24}" width="80" height="48" rx="12" fill="{c}" stroke="#2a1600" stroke-width="4"/>'
                   for x, y, c in knobs)
    return f"""
<defs><linearGradient id="hexGold" x1="0" y1="0" x2="1" y2="1">
  <stop offset="0" stop-color="#ffe9a0"/><stop offset=".5" stop-color="#e0a52a"/><stop offset="1" stop-color="#8a5a0c"/>
</linearGradient></defs>
<polygon points="{hexpts}" fill="url(#hexGold)" stroke="#5a3a06" stroke-width="8" stroke-linejoin="round"/>
<g transform="translate(256 262) scale(1.05) translate(-256 -256)">{tracks}{caps}</g>"""


files["ultimatektv-updater-icon"] = (512, 512, updater_icon())
files["ultimatektv-stylesimulator-icon"] = (512, 512, stylesimulator_icon())

for name, (w, h, body) in files.items():
    with open(os.path.join(OUT, name + ".svg"), "w", encoding="utf-8") as f:
        f.write(svg(w, h, body))
    print(name, w, h)
