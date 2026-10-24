"""Apple Metal HUD command-buffer timings, separate from Godot viewport timers."""
import datetime
import json
import math
import re
import subprocess
from pathlib import Path


class MetalHudCapture:
    """Capture only the owned game's HUD messages; never change global defaults."""
    def __init__(self, folder, pid):
        self.folder, self.pid = Path(folder), pid

    def __enter__(self):
        self.output = (self.folder / 'metal-hud.ndjson').open('w')
        self.errors = (self.folder / 'metal-hud-logger.log').open('w')
        predicate = f'processIdentifier == {int(self.pid)} AND eventMessage CONTAINS "metal-HUD"'
        try:
            self.process = subprocess.Popen(['/usr/bin/log', 'stream', '--style', 'ndjson', '--level', 'debug',
                                             '--predicate', predicate], stdout=self.output, stderr=self.errors,
                                            start_new_session=True)
        except Exception:
            self.output.close(); self.errors.close()
            raise
        return self

    def __exit__(self, kind, value, traceback):
        early_exit = self.process.poll()
        try:
            if early_exit is None:
                self.process.terminate()
                try:
                    self.process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    self.process.kill(); self.process.wait(timeout=5)
        finally:
            self.output.close(); self.errors.close()
        if kind is None and early_exit not in (None, 0):
            raise RuntimeError('Metal HUD log capture failed; inspect metal-hud-logger.log')


def utc_time(value):
    value = datetime.datetime.fromisoformat(value.replace('Z', '+00:00'))
    if value.tzinfo is None:
        raise ValueError('GPU timing alignment requires timezone-aware timestamps')
    return value.timestamp()


def read_batches(path, pid, console_path=None):
    console = {}
    if console_path is not None:
        pattern = re.compile(r'\[' + str(int(pid)) + r':\d+\]\s+metal-HUD:\s*(\d+,.*)', re.I)
        for line in Path(console_path).read_text(errors='replace').splitlines():
            match = pattern.search(line)
            if match:
                payload = match.group(1)
                frame = int(payload.split(',', 1)[0])
                if frame in console and console[frame] != payload:
                    raise ValueError('Conflicting console Metal HUD batch IDs')
                console[frame] = payload
    batches = []
    seen = {}
    for line in Path(path).read_text(errors='replace').splitlines():
        try:
            event = json.loads(line)
        except ValueError:
            continue  # log stream prints a non-JSON filter/status preamble.
        if event.get('processID', event.get('processIdentifier')) != pid:
            continue
        match = re.search(r'metal-HUD:\s*(.*)', event.get('eventMessage', ''), re.I)
        if not match:
            continue
        payload = match.group(1)
        if '<private>' in payload:
            # macOS may redact only the dynamic array in unified logs while the
            # owned app's NSLog console has it in full. Join rather than changing
            # any system/app privacy setting. The public header must also match.
            frame = int(payload.split(',', 1)[0])
            header = payload.removesuffix('<private>')
            if frame not in console or not console[frame].startswith(header + ','):
                raise ValueError('Redacted HUD batch lacks matching owned-process console data')
            payload = console[frame]
        values = [float(part.strip()) for part in payload.split(',')]
        if len(values) < 5 or (len(values) - 3) % 2 or not all(math.isfinite(v) for v in values):
            raise ValueError('Malformed Metal HUD timing batch')
        intervals, gpu = values[3::2], values[4::2]
        if any(v <= 0 for v in intervals) or any(v < 0 for v in gpu):
            raise ValueError('Invalid Metal HUD frame interval/GPU duration')
        end = utc_time(event['timestamp'])
        frame = int(values[0])
        if frame in seen:
            if seen[frame] != values:
                raise ValueError('Conflicting unified-log Metal HUD batch IDs')
            continue  # NSLog can deliver the same batch through two log records.
        seen[frame] = values
        batches.append(dict(firstFrame=int(values[0]), end=end,
                            start=end - sum(intervals) / 1000, gpuMs=gpu))
    return batches


def summarize(path, pid, benchmark, console_path=None):
    batches = read_batches(path, pid, console_path)
    if not batches or not any(v > 0 for b in batches for v in b['gpuMs']):
        raise ValueError('Apple Metal HUD produced no positive GPU timings; inspect metal-hud.ndjson')
    measurements = []
    for window in benchmark['measurements']:
        # HUD timestamps identify a once-per-second batch. Infer its start from
        # the included present intervals, then exclude both boundary guard bands.
        # HUD drawable-present IDs are NOT Godot process/frame IDs.
        start, end = utc_time(window['startUtc']) + .25, utc_time(window['endUtc']) - .25
        selected = [b for b in batches if b['start'] >= start and b['end'] <= end]
        samples = sorted(v for b in selected for v in b['gpuMs'] if v > 0)
        if len(samples) < 30:
            raise ValueError('Insufficient aligned Metal GPU timings: ' + window['scene'] + ' ' + window['preset'])
        percentile = lambda fraction: samples[max(0, math.ceil(len(samples) * fraction) - 1)]
        measurements.append(dict(scene=window['scene'], preset=window['preset'], samples=len(samples),
                                 batches=len(selected), gpuMedianMs=percentile(.5), gpuP95Ms=percentile(.95),
                                 gpuMeanMs=sum(samples) / len(samples)))
    return dict(schemaVersion=1, source='Apple Metal Performance HUD', units='milliseconds',
                scope='Frame command-buffer duration, including possible gaps; not per-viewport or encoder-active time',
                alignment='Whole HUD batches inside UTC sample windows, with 250 ms boundary guards; inferred batch start',
                pid=pid, totalTimingBatches=len(batches), measurements=measurements)
