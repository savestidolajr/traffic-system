import re, base64, pathlib

# The deck carries its own framework: brand tokens, the fixed 16:9 stage, the arc
# device and the slide runtime. It donates them back on each rebuild, so this
# script no longer depends on the deep-dive deck the framework was lifted from -
# that file was internal working material and has left the tree.
OUT = 'docs/deliverables/arn-handover-2026-09-04.html'
s = open(OUT).read()

# Framework: everything before the first slide, everything after the last.
first = s.index('<section class="slide')
last = s.rindex('</section>') + len('</section>')
head, tail = s[:first], s[last:]

# The Arinco marks, taken from the deck's own footer and title lockup.
logo = re.search(r'<span class="foot-meta">[^<]*</span><img src="(data:[^"]+)"', s).group(1)
title_logo = re.search(r'<div class="logo-lockup reveal d1"><img src="(data:[^"]+)"', s).group(1)
# Export fixes. The donor deck already carries them, so apply only on a first
# build from a framework that lacks them:
#  - no @page size, so print-to-PDF fell back to letter
#  - the annotation canvas follows the slides, so `.slide:last-child` never
#    matched and the final slide forced a trailing blank page
#  - `.support li` is hard-coded navy, so a support list on a navy slide read
#    navy on navy and rendered as bullets with no words
if '@page' not in head:
    head = head.replace('@media print {', """@page { size: 1920px 1080px; margin: 0; }

@media print {
    .slide:last-of-type { break-after: auto; page-break-after: auto; }
}

.slide--navy .support li { color: rgba(255,255,255,0.88); }
.slide--navy .support li b { color: #fff; }

@media print {""", 1)

head = re.sub(r'<title>.*?</title>', '<title>Handover to ARN - 4 September 2026</title>', head, flags=re.S)

def b64(path):
    return 'data:image/png;base64,' + base64.b64encode(pathlib.Path(path).read_bytes()).decode()

ARCS = ('<div class="arcs" aria-hidden="true"><div class="rings">'
        + '<span></span>' * 6 + '</div></div>')
FOOT = ('<div class="slide-foot"><span class="foot-meta">Handover to ARN &middot; '
        f'4 September 2026</span><img src="{logo}" alt="Arinco"></div>')

def slide(eyebrow, title, body, cls=''):
    return f'''<section class="slide{(' ' + cls) if cls else ''}">
    {ARCS}
    <div class="slide-head">
      <div class="eyebrow reveal d1">{eyebrow}</div>
      <h2 class="reveal d1">{title}</h2>
      <div class="triple-rule reveal d2"><i></i><i></i><i></i></div>
    </div>
    <div class="slide-body">{body}</div>
    {FOOT}
  </section>'''

def shot(eyebrow, title, img, cap):
    return f'''<section class="slide slide--shot">
    {ARCS}
    <div class="sh-head">
      <div class="eyebrow reveal d1">{eyebrow}</div>
      <h2 class="reveal d1">{title}</h2>
    </div>
    <div class="sh-body">
      <div class="sh-fig"><img class="reveal d2" src="{img}" alt="{title}"></div>
      <div class="sh-cap reveal d3">{cap}</div>
    </div>
    {FOOT}
  </section>'''

def diagram(eyebrow, title, img, cap):
    return f'''<section class="slide slide--diagram">
    {ARCS}
    <div class="dgm-head">
      <div class="eyebrow reveal d1">{eyebrow}</div>
      <h2 class="reveal d1">{title}</h2>
    </div>
    <div class="dgm-body">
      <div class="dgm-figure"><img class="reveal d2" src="{img}" alt="{title}"></div>
      <div class="dgm-cap reveal d3">{cap}</div>
    </div>
    {FOOT}
  </section>'''

def li(d, text):
    return f'<li class="reveal d{d}"><i></i><span>{text}</span></li>'

slides = []

# 1 - Title
slides.append(f'''<section class="slide slide--navy title-slide active visible">
  {ARCS}
  <div class="logo-lockup reveal d1"><img src="{title_logo}" alt="Arinco"></div>
  <div style="position:relative">
    <div class="eyebrow reveal d2">AI.N &middot; ARN Information Bulletin Platform</div>
    <div class="hero-line reveal d3">It's yours from today.</div>
    <div class="triple-rule reveal d4"><i></i><i></i><i></i></div>
    <div class="standfirst reveal d5">
      Built, deployed, documented and operating. Three environments live, the pilot
      on air, and everything needed to run it, change it and re-check it published
      for ARN to read without us.
    </div>
  </div>
  <div class="credit reveal d5">Friday 4 September 2026 &middot; Arinco delivery team</div>
</section>''')

# 2 - Where things stand
slides.append(slide('Status', 'Where things stand', f'''
  <div class="lead reveal d2" style="margin-bottom:26px">
    Nothing we hold is needed to run it, and nothing we take away stops it.
  </div>
  <ul class="support" style="gap:20px;max-width:1560px">
    {li(3, "<b>Three environments, all healthy</b> - checked again this week.")}
    {li(3, "<b>The pilot is on air from test</b> - KIIS 90s Sydney and Star 102.7 Cairns.")}
    {li(4, "<b>Production is deliberately disarmed</b> - healthy, running the accepted release, schedules off until ARN arms it.")}
    {li(5, "<b>All of it sits in ARN's own subscription and DevOps</b> - no Arinco account has to keep existing.")}
  </ul>'''))

# 3 - What ARN receives: the three contracted deliverables
slides.append(slide('Deliverables', 'What ARN receives', f'''
  <div class="lead reveal d2" style="margin-bottom:34px">
    Three deliverables, exactly as the work order defines them.
  </div>
  <ul class="support" style="gap:26px;max-width:1560px">
    {li(2, "<b>Application code and CI/CD pipelines, including infrastructure as code</b> - all in ARN's own Azure DevOps.")}
    {li(3, "<b>This handover session</b> - to the people who will operate and support it.")}
    {li(4, "<b>As-built documentation for operations</b> - a website, a signed cover, and an offline copy of the site that needs no internet and no login.")}
  </ul>'''))

# 4 - Documentation site
slides.append(shot('Documentation', 'Written for whoever has the problem', b64('docs/deliverables/assets-handover/docsite-home.png'),
    'Grouped by who is reading, not by how the system is built. A content operator, a support engineer and a developer each have their own path in. It needs no Azure access and no login, so it still works after our identities lapse.'))

# 5 - Console
slides.append(shot('The console', 'A person still approves every bulletin', b64('docs-site/assets/screenshots/01-onair.desktop.png'),
    'The on air board: what is queued, what is waiting for a decision, and what went to air. Sixteen screenshots of the real console are published, so anyone who missed the session can follow the same path alone.'))

# 6 - The writer's prompt
slides.append(shot('Day to day', "The writer's prompt is ARN's to change", b64('docs/deliverables/assets-handover/writer-prompt.png'),
    "This is how a station reads: three desks, and the rules each one writes to. ARN edits these in the console and the next bulletin obeys them - the single biggest lever over what goes to air, bigger than any wording list. Facts, values, running order and reply format stay protected by the platform."))

# 7 - Where it runs
slides.append(diagram('How it works', 'Where it runs', b64('docs-site/assets/diagrams/platform-architecture.png'),
    'One of seven published diagrams. The point of it: everything runs in ARN\'s own subscription, and the same templates build all three environments.'))

# 8 - Support
slides.append(slide('Support', 'When something breaks', f'''
  <div class="lead reveal d2" style="margin-bottom:26px">
    Every alert maps to a written action.
  </div>
  <ul class="support" style="gap:20px;max-width:1560px">
    {li(3, "<b>The runbook starts where support starts</b> - an alert fired, what do I do, page by page.")}
    {li(3, "<b>Alerts arrive in a Microsoft Teams channel</b>, with email as a backstop, and each one maps to an action.")}
    {li(4, "<b>The support model says which vendor owns which failure.</b>")}
    {li(5, "<b>The escalation matrix is a template</b> - the roles are laid out; ARN fills in its own names and hours.")}
  </ul>'''))

# 10 - Where it depends on ARN
slides.append(slide('Dependencies', 'Where it depends on ARN', f'''
  <div class="lead reveal d2" style="margin-bottom:30px">
    Six state road-authority feeds are wired. Two are switched on, and the rest
    need one thing each from ARN.
  </div>
  <ul class="support" style="gap:20px;max-width:1580px">
    {li(2, "<b>New South Wales</b> answers reliably. <b>Queensland</b> answers on a shared demonstration key that gets rate-limited, and is refused in development. It needs <b>a dedicated authority key</b>.")}
    {li(3, "<b>Tasmania and Western Australia</b> answer and parse, but sit switched off. They need <b>a setting in Bulletin rules</b>, nothing else.")}
    {li(4, "<b>South Australia</b> refuses the platform's address. It needs <b>one allowlist entry</b> from their road-operations team.")}
    {li(5, "<b>Playout</b> - the audio reaches the share. The station clock decides whether it airs.")}
  </ul>'''))

# 11 - Read-only by design
slides.append(slide('Boundaries', 'What is read-only on purpose', f'''
  <ul class="support" style="gap:26px;max-width:1560px">
    {li(2, "<b>Emergency services.</b> The console shows emergency records once ARN holds a DataQuoll key and the licensing is settled. It is display-only by design: those records never reach a script and cannot go to air, so on-air behaviour is the same with or without the key.")}
    {li(3, "<b>Every bulletin still waits for a person.</b> Nothing reaches air on its own, in any environment.")}
    {li(4, "<b>Alerts</b> land in a Microsoft Teams channel. That path runs inside the same subscription as the platform, so it cannot report the subscription being down - an ARN-owned email or SMS address is the only route that survives that.")}
  </ul>'''))

# 12 - Close
slides.append(f'''<section class="slide slide--navy">
  {ARCS}
  <div class="slide-head">
    <div class="eyebrow reveal d1">Close</div>
    <h2 class="reveal d1">That's the handover</h2>
    <div class="triple-rule reveal d2"><i></i><i></i><i></i></div>
  </div>
  <div class="slide-body">
    <ul class="support" style="gap:26px;max-width:1580px">
      {li(2, "<b>The code, the pipelines and the infrastructure</b> are in ARN's Azure DevOps.")}
      {li(3, "<b>The documentation</b> is published, offline-bundled, and needs no account to read.")}
      {li(4, "<b>The pack is reissued once after today</b>, with the session recorded, and signed.")}
      {li(5, "Then it is ARN's platform, run ARN's way.")}
    </ul>
  </div>
  {FOOT}
</section>''')

open(OUT, 'w').write(head + '\n'.join(slides) + tail)
print('wrote', OUT, len(head + ''.join(slides) + tail), 'chars,', len(slides), 'slides')
