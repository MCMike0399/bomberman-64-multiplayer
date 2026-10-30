#!/usr/bin/env python3
"""DIAGNOSTIC ONLY (branch diag/windows-p2p-stall): repeat native peer-ice-order/peer-capacity on a
free Windows runner and retain interface, WFP-drop and optional verbose libdatachannel/libjuice evidence.
Never a release receipt."""
import argparse
import json
import re
import shutil
import subprocess
import sys
import time
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / 'artifacts'


def ps(script, timeout=120):
    result = subprocess.run(['powershell', '-NoProfile', '-NonInteractive', '-Command', script],
                            capture_output=True, text=True, timeout=timeout)
    return (result.stdout + result.stderr).strip()


NETWORK = r'''
"=== Get-NetAdapter"; Get-NetAdapter -IncludeHidden | Format-Table -AutoSize Name,InterfaceDescription,ifIndex,Status,MacAddress,LinkSpeed | Out-String -Width 300
"=== Get-NetIPAddress"; Get-NetIPAddress | Format-Table -AutoSize InterfaceAlias,ifIndex,AddressFamily,IPAddress,PrefixLength,PrefixOrigin,AddressState | Out-String -Width 300
"=== Get-NetIPInterface"; Get-NetIPInterface | Format-Table -AutoSize InterfaceAlias,ifIndex,AddressFamily,InterfaceMetric,ConnectionState,Forwarding,WeakHostSend,WeakHostReceive | Out-String -Width 300
"=== Get-NetRoute IPv4"; Get-NetRoute -AddressFamily IPv4 | Format-Table -AutoSize ifIndex,DestinationPrefix,NextHop,RouteMetric,InterfaceAlias | Out-String -Width 300
"=== Get-NetFirewallProfile"; Get-NetFirewallProfile | Format-Table -AutoSize Name,Enabled,DefaultInboundAction,DefaultOutboundAction | Out-String -Width 300
"=== Get-NetConnectionProfile"; Get-NetConnectionProfile | Format-Table -AutoSize InterfaceAlias,NetworkCategory,IPv4Connectivity | Out-String -Width 300
"=== Get-NetNat"; try { Get-NetNat | Format-List | Out-String -Width 300 } catch { "n/a: $_" }
"=== Get-VMSwitch"; try { Get-VMSwitch | Format-Table -AutoSize Name,SwitchType | Out-String -Width 300 } catch { "n/a: $_" }
"=== HNS networks"; try { Get-HnsNetwork | Select-Object Name,Type,Id | Format-Table -AutoSize | Out-String -Width 300 } catch { "n/a: $_" }
"=== UDP endpoints (excluding svchost)"; Get-NetUDPEndpoint | Where-Object { $_.LocalPort -ge 49152 } | Measure-Object | Select-Object Count | Out-String
'''


def wfp_events(since_epoch, process_hint='BlastCircuit'):
    # 5152 = WFP dropped a packet, 5157 = WFP blocked a connection. Requires auditpol enabled below.
    script = (f"$t=[DateTimeOffset]::FromUnixTimeMilliseconds({int(since_epoch * 1000)}).LocalDateTime;"
              "try { Get-WinEvent -FilterHashtable @{LogName='Security';Id=5152,5157;StartTime=$t} -ErrorAction Stop | "
              "ForEach-Object { $x=[xml]$_.ToXml(); $d=@{}; $x.Event.EventData.Data | ForEach-Object { $d[$_.Name]=$_.'#text' }; "
              "[pscustomobject]@{Id=$_.Id;Time=$_.TimeCreated.ToString('o');App=$d.Application;Dir=$d.Direction;Src=$d.SourceAddress;"
              "SrcPort=$d.SourcePort;Dst=$d.DestAddress;DstPort=$d.DestPort;Proto=$d.Protocol;Layer=$d.LayerName;Filter=$d.FilterRTID} } | "
              "ConvertTo-Json -Depth 3 } catch { '[]' }")
    text = ps(script, timeout=180)
    try:
        events = json.loads(text) if text.strip() else []
    except json.JSONDecodeError:
        return {'raw': text[:4000]}
    if isinstance(events, dict): events = [events]
    return events


def candidates(observation):
    rows = []
    for c in observation.get('candidates', []):
        rows.append({k: c.get(k) for k in ('action', 'address', 'port', 'type', 'result', 'afterCreationMs')})
    return rows


def connect_ms(observation):
    applied = observation.get('firstRemoteDescriptionAppliedMs')
    connected = next((s['observedAfterCreationMs'] for s in observation.get('states', []) if s.get('connection') == 'Connected'), None)
    return None if applied is None or connected is None else round(connected - applied, 1)


JUICE = re.compile(r'(juice|ICE|Ice|DTLS|Dtls|pair|Selected|selected|candidate|STUN|stun|Connectivity|connectivity|SCTP|Sctp|state)', re.I)


def launch(executable, scenario, folder, origin, verbose, timeout):
    folder.mkdir(parents=True)
    command = [str(executable), '--max-fps', '30', '--fixed-fps', '30', '--disable-vsync', '--windowed', '--headless']
    if verbose: command.append('--verbose')
    command += ['--', '--playtest=' + scenario, '--playtest-output=' + str(folder), '--playtest-server=' + origin, '--display-windowed']
    started = time.time()
    with (folder / 'runtime.log').open('w') as log:
        process = subprocess.Popen(command, cwd=folder, stdout=log, stderr=subprocess.STDOUT)
        try:
            code = process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            process.kill(); code = 'timeout'
    summary_path = folder / 'summary.json'
    summary = json.loads(summary_path.read_text()) if summary_path.exists() else {}
    passed = code == 0 and bool(summary.get('passed'))
    return started, time.time() - started, code, passed


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--variant', choices=('baseline', 'verbose', 'novswitch'), required=True)
    parser.add_argument('--shard', default='0')
    parser.add_argument('--ice', type=int, default=40)
    parser.add_argument('--capacity', type=int, default=4)
    parser.add_argument('--keep-passes', type=int, default=3)
    args = parser.parse_args()
    out = ART / 'p2p-stall' / f'{args.variant}-{args.shard}'; out.mkdir(parents=True)
    meta = {'mode': 'diagnostic-only', 'variant': args.variant, 'shard': args.shard}

    setup = next(ART.rglob('Blast-Circuit-windows-x64-Setup.exe'))
    server_zip = next(ART.rglob('Blast-Circuit-server-dotnet10.zip'))
    work = Path(r'C:\p2pdiag'); work.mkdir()
    installed = work / 'Installed game with spaces'
    subprocess.run([str(setup), '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', '/DIR=' + str(installed)], check=True, timeout=300)
    executable = installed / 'BlastCircuit.exe'
    release_dll = installed / 'libwebrtc_native.windows.template_release.x86_64.dll'
    meta['releaseDllPresent'] = release_dll.exists()
    with zipfile.ZipFile(server_zip) as bundle: bundle.extractall(work)

    if args.variant == 'verbose':
        # Same hash-pinned webrtc-native 1.2.2-stable archive; its template_debug build compiles DEBUG_ENABLED,
        # which raises rtc::InitLogger to Debug (incl. libjuice) and routes it through print_verbose.
        subprocess.run([sys.executable, str(ROOT / 'tools/networking/bootstrap_webrtc.py')], check=True, timeout=180)
        debug = sorted((ROOT / 'addons/webrtc_native').rglob('*windows.template_debug.x86_64.dll'))
        meta['debugDllCandidates'] = [str(p) for p in debug]
        shutil.copy2(debug[0], release_dll)
        meta['swappedDebugDll'] = str(debug[0])

    (out / 'network-before.txt').write_text(ps(NETWORK))
    if args.variant == 'novswitch':
        meta['disable'] = ps(r"Get-NetAdapter -IncludeHidden | Where-Object { $_.Name -like 'vEthernet*' -or $_.InterfaceDescription -like 'Hyper-V Virtual Ethernet*' } | "
                             r"ForEach-Object { $_.Name; Disable-NetAdapter -InputObject $_ -Confirm:$false }; Start-Sleep 3")
        (out / 'network-after-disable.txt').write_text(ps(NETWORK))
    meta['auditpol'] = ps('auditpol /set /subcategory:"Filtering Platform Packet Drop" /success:enable /failure:enable; '
                          'auditpol /set /subcategory:"Filtering Platform Connection" /success:disable /failure:enable')
    meta['pktmonStart'] = ps(r'pktmon filter remove; pktmon filter add udp -t UDP; pktmon start --capture --comp all --pkt-size 256 --file-size 256 --log-mode circular --file-name C:\p2pdiag\pktmon.etl')

    server_log = (out / 'server.log').open('w')
    server = subprocess.Popen(['dotnet', str(work / 'Blast-Circuit-server-dotnet10' / 'Bomber.Server.dll'), '--urls', 'http://127.0.0.1:0'],
                              stdout=server_log, stderr=subprocess.STDOUT)
    origin = None; deadline = time.time() + 60
    while origin is None and time.time() < deadline:
        match = re.search(r'Now listening on:\s+(http://127\.0\.0\.1:\d+)', (out / 'server.log').read_text(errors='replace'))
        origin = match.group(1) if match else None; time.sleep(.2)
    meta['origin'] = origin or 'http://127.0.0.1:9'

    plan = ['peer-ice-order'] * args.ice
    for position in range(args.capacity):  # interleave capacity cases evenly
        plan.insert(min(len(plan), (position + 1) * max(1, len(plan) // (args.capacity + 1)) + position), 'peer-capacity')
    results = []; kept = {'peer-ice-order': 0, 'peer-capacity': 0}
    for index, scenario in enumerate(plan):
        folder = out / 'runs' / f'{index:03d}-{scenario}'
        started, seconds, code, passed = launch(executable, scenario, folder, meta['origin'], args.variant == 'verbose',
                                                60 if scenario == 'peer-ice-order' else 180)
        row = {'index': index, 'scenario': scenario, 'passed': passed, 'exit': code, 'seconds': round(seconds, 1)}
        diag = folder / ('peer-ice-order-diagnostics.json' if scenario == 'peer-ice-order' else 'peer-capacity-diagnostics.json')
        if diag.exists() and scenario == 'peer-ice-order':
            d = json.loads(diag.read_text())
            row.update(stage=d.get('stage'), left=candidates(d['leftObservation']), right=candidates(d['rightObservation']),
                       leftConnectMs=connect_ms(d['leftObservation']), rightConnectMs=connect_ms(d['rightObservation']),
                       leftStates=d['leftObservation'].get('states'), rightStates=d['rightObservation'].get('states'))
        log_text = (folder / 'runtime.log').read_text(errors='replace')
        if args.variant == 'verbose':
            row['nativeLog'] = [line for line in log_text.splitlines() if JUICE.search(line)][:600]
        row['warnings'] = [line for line in log_text.splitlines() if 'WARNING' in line or 'ERROR' in line][:40]
        if not passed:
            row['wfpDrops'] = wfp_events(started - 2)
            subprocess.run(['pktmon', 'stop'], capture_output=True, timeout=120)
            etl = Path(r'C:\p2pdiag\pktmon.etl')
            if etl.exists():
                subprocess.run(['pktmon', 'etl2txt', str(etl), '--out', str(folder / 'pktmon.txt'), '--verbose', '2'], capture_output=True, timeout=300)
            ps(r'pktmon start --capture --comp all --pkt-size 256 --file-size 256 --log-mode circular --file-name C:\p2pdiag\pktmon.etl')
            (folder / 'network-at-failure.txt').write_text(ps(NETWORK))
        elif kept[scenario] < args.keep_passes:
            kept[scenario] += 1
        else:
            shutil.rmtree(folder)
        results.append(row)
        print(json.dumps({k: row.get(k) for k in ('index', 'scenario', 'passed', 'exit', 'seconds', 'stage', 'leftConnectMs', 'rightConnectMs')}), flush=True)

    subprocess.run(['pktmon', 'stop'], capture_output=True, timeout=120)
    drops = wfp_events(time.time() - 7200)
    drops = drops if isinstance(drops, list) else []
    meta['wfpDropsWholeRun'] = len(drops)
    meta['wfpUdpDropsSample'] = [e for e in drops if str(e.get('Proto')) == '17'][:50]
    server.terminate()
    summary = {name: {'runs': sum(r['scenario'] == name for r in results),
                      'failures': sum(r['scenario'] == name and not r['passed'] for r in results)} for name in ('peer-ice-order', 'peer-capacity')}
    meta['summary'] = summary
    (out / 'results.json').write_text(json.dumps({'meta': meta, 'results': results}, indent=2) + '\n')
    print(json.dumps(meta, indent=2))


if __name__ == '__main__':
    main()
