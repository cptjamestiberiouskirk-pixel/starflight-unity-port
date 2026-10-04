"""usage: python changelog_add.py <anchor bullet title> <bullet file>  (inserts the bullet after the anchor bullet's line)"""
import io, sys
title, bullet_path = sys.argv[1:3]
p = 'CHANGELOG.md'
raw = io.open(p, encoding='utf-8', newline='').read()
nl = '\r\n' if '\r\n' in raw else '\n'
lines = raw.replace('\r\n', '\n').split('\n')
bullet = io.open(bullet_path, encoding='utf-8').read().strip()
assert '—' not in bullet and '\n' not in bullet
hits = [i for i, l in enumerate(lines) if l.startswith('- **' + title + '**')]
assert len(hits) == 1, hits
lines.insert(hits[0] + 1, bullet)
io.open(p, 'w', encoding='utf-8', newline='').write(nl.join(lines))
print('inserted after line', hits[0] + 1)
