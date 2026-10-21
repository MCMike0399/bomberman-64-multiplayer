"""Small shared interface for scenario discovery and trustworthy engine receipts."""
import json
import hashlib
import binascii
import zlib
import math
from pathlib import Path
import struct
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
CATALOG = ROOT / 'assets/testing/scenarios.json'


def catalog(path=CATALOG):
    data = json.loads(Path(path).read_text())
    if data.get('schemaVersion') != 1:
        raise ValueError('Unsupported scenario catalog schema')
    scenarios = data['scenarios']
    result = {s['id']: s for s in scenarios}
    if len(result) != len(scenarios):
        raise ValueError('Duplicate scenario id')
    def visit(name, parents=()):
        if name in parents:
            raise ValueError('Scenario suite cycle: ' + name)
        if name not in result:
            raise ValueError('Unknown suite child: ' + name)
        s = result[name]
        if not s.get('domains') or not isinstance(s.get('headless'), bool) or s.get('timeout', 0) <= 0:
            raise ValueError('Incomplete scenario metadata: ' + name)
        if sum(k in s for k in ('steps', 'method', 'script')) != 1:
            raise ValueError('Scenario must have one execution kind: ' + name)
        for child in s.get('steps', []):
            visit(child, (*parents, name))
            if s['headless'] and not result[child]['headless']:
                raise ValueError('Headless suite contains a graphical-only scenario')
    for name in result:
        visit(name)
    return result


def expanded(name, scenarios):
    entry = scenarios[name]
    return [leaf for child in entry['steps'] for leaf in expanded(child, scenarios)] if 'steps' in entry else [name]


def validate_summary(summary, folder, scenario, headless):
    """Reject false passes, missing captures and artifact paths outside this run."""
    errors = []
    checks = summary.get('checks', [])
    if summary.get("catalogSha256") != hashlib.sha256(CATALOG.read_bytes()).hexdigest():
        errors.append("Bundled scenario catalog differs from this runner; rebuild the app")
    if summary.get("completedScenarios") != expanded(scenario, catalog()):
        errors.append("Engine did not complete the requested scenario coverage")
    if summary.get('schemaVersion') != 1 or summary.get('scenario') != scenario:
        errors.append('Engine receipt schema/scenario does not match this invocation')
    if not isinstance(checks, list) or not checks or summary.get('assertions') != len(checks):
        errors.append('Missing checks or inconsistent assertion count')
    elif any(not isinstance(c, dict) or c.get('passed') is not True for c in checks):
        errors.append('One or more engine checks failed')
    if summary.get('passed') is not True:
        errors.append('Engine scenario did not pass')
    seconds = summary.get('seconds')
    if not isinstance(seconds, (int, float)) or not math.isfinite(seconds) or seconds < 0:
        errors.append('Invalid engine elapsed time')
    expected = 'disabled-headless' if headless else 'graphical'
    if summary.get('screenshotMode') != expected:
        errors.append('Engine screenshot mode does not match requested rendering mode')
    screenshots = summary.get('screenshots', [])
    if not headless and not screenshots:
        errors.append('Graphical run produced no captures')
    for value in screenshots:
        path = Path(value)
        try:
            path.resolve().relative_to(Path(folder).resolve())
            raw = path.read_bytes()
            if len(raw) < 33 or raw[:8] != b'\x89PNG\r\n\x1a\n' or raw[12:16] != b'IHDR':
                raise ValueError('Not a complete PNG header')
            width, height = struct.unpack('>II', raw[16:24])
            if min(width, height) < 32:
                raise ValueError('Capture dimensions are too small')
            position=8; compressed=bytearray(); ended=False
            while position < len(raw):
                length=struct.unpack('>I',raw[position:position+4])[0]
                kind=raw[position+4:position+8];data=raw[position+8:position+8+length]
                crc=raw[position+8+length:position+12+length]
                if len(data)!=length or len(crc)!=4 or binascii.crc32(kind+data)!=struct.unpack('>I',crc)[0]:
                    raise ValueError('Truncated or corrupt PNG chunk')
                if kind==b'IDAT':compressed.extend(data)
                if kind==b'IEND':ended=True;break
                position+=length+12
            if not ended or not compressed or not zlib.decompress(compressed):
                raise ValueError('Missing PNG pixel data/end marker')
        except (OSError, ValueError, TypeError, struct.error, zlib.error) as error:
            errors.append('Invalid capture ' + path.name + ': ' + str(error))
    return errors


def junit(path, result, summary=None):
    checks = (summary or {}).get('checks', [])
    suite = ET.Element('testsuite', name=result['scenario'], tests=str(len(checks) + 1), failures=str(sum(c.get('passed') is not True for c in checks) + (not result['passed'])), time=str(result.get('seconds', 0)))
    for check in checks:
        case = ET.SubElement(suite, 'testcase', name=check.get('description', 'unnamed engine check'), classname=result['scenario'])
        if check.get('passed') is not True:
            ET.SubElement(case, 'failure', message='Engine assertion failed').text = str(check)
    case = ET.SubElement(suite, 'testcase', name='Runner, rendering and artifact integrity', classname=result['scenario'])
    if not result['passed']:
        ET.SubElement(case, 'failure', message='Runner or evidence validation failed').text = '\n'.join(result.get('errors', []))
    ET.ElementTree(suite).write(path, encoding='utf-8', xml_declaration=True)
