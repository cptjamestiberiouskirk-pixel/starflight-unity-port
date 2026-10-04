"""Adds a scenario to ClaudeProbe.cs.

usage: python add_scenario.py <path to ClaudeProbe.cs> <scenario name> <method name> <snippet file>

The case goes in front of the default case of the scenario switch and the snippet in front of the
"batch 1 (starport side)" section. Running it again for the same scenario replaces nothing and fails.
"""
import io
import sys

probe, name, method, snippet_path = sys.argv[1:5]

s = io.open(probe, encoding='utf-8', newline='').read()
nl = '\r\n' if '\r\n' in s else '\n'
s = s.replace('\r\n', '\n')
snippet = io.open(snippet_path, encoding='utf-8').read().replace('\r\n', '\n')

assert ('case "' + name + '":') not in s, 'scenario already present'

default_anchor = '''			default:
				Finish( "abort: unknown scenario " + scenario, 2 );'''
assert s.count(default_anchor) == 1
s = s.replace(default_anchor, '''			case "''' + name + '''":
				yield return ''' + method + '''();
				break;

''' + default_anchor, 1)

section_anchor = '	// ---------------------------------------------------------------- batch 1 (starport side)'
assert s.count(section_anchor) == 1
if not snippet.endswith('\n\n'):
    snippet = snippet.rstrip('\n') + '\n\n'
s = s.replace(section_anchor, snippet + section_anchor, 1)

assert '—' not in s

io.open(probe, 'w', encoding='utf-8', newline='').write(s.replace('\n', nl))
print('added scenario', name, '- lines now', len(s.splitlines()))
