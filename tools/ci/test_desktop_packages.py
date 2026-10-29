"""Reject incomplete runtimes, tampered inventories, unsafe ZIPs and unrelated smoke evidence."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

import desktop_packages as desktop
import desktop_validation as validation
import download_formats
import promote_release

SHA = 'a' * 40
RELEASE = {'version': '1.2.3', 'build': '42', 'protocolRevision': 6, 'admissionToken': '6/1.2.3/42'}


def add_display_restart(report):
    if 'display-settings' in report['completedScenarios']:
        child = {key: report[key] for key in ('platform', 'release', 'catalogSha256', 'renderingDriver', 'renderingMethod', 'adapter')}
        child.update(schemaVersion=1, passed=True, scenario='display-settings', completedScenarios=['display-settings'],
                     displayRestoration=True, screenshotMode='graphical', assertions=6,
                     checks=[{'passed': True}] * 6, screenshots=['restart/display-restored-next-launch.png'])
        report['displayRestart'] = {'passed': True, 'parentExited': True, 'exitCode': 0, 'timeout': 90,
                                   'runtimeErrors': [], 'preferencesSha256': 'c' * 64, 'summary': child}
    return report


def write_desktop(directory, revision=SHA, release=None):
    release = release or RELEASE
    if not (directory / 'Blast-Circuit-server-dotnet10.zip').exists():
        (directory / 'Blast-Circuit-server-dotnet10.zip').write_bytes(b'fixture server')
    for target, settings in desktop.TARGETS.items():
        item = desktop.names(target)
        files = {name: b'fixture runtime' for name in (
            settings['executable'], settings['runtime'], settings['webrtc'], 'Bomberman64Multiplayer.dll',
            settings.get('pck','BlastCircuit.pck'), 'Notices/Blast-Circuit-MIT.txt', 'Notices/WebRTC-SOURCE.txt', 'Notices/DotNet-LICENSE.TXT')}
        if 'engine_executable'in settings:files[settings['engine_executable']]=b'fixture engine'
        files['release.json'] = json.dumps({'revision': revision, 'release': release, 'target': target}).encode()
        files['data/Bomberman64Multiplayer.runtimeconfig.json'] = b'{"runtimeOptions":{"includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.0"}]}}'
        if target == 'windows-x64':
            files['installed-files.txt'] = ('\n'.join(sorted(set(files) | {'installed-files.txt'})) + '\n').encode()
        with zipfile.ZipFile(directory / item['archive'], 'w') as bundle:
            for name, data in files.items():
                entry = zipfile.ZipInfo(item['root'] + '/' + name)
                entry.external_attr = 0o100755 << 16
                bundle.writestr(entry, data)
        (directory / (item['archive'] + '.sha256')).write_text(desktop.digest(directory / item['archive']) + '  ' + item['archive'])
        (directory / item['metadata']).write_text(json.dumps({
            'schemaVersion': 1, 'target': target, 'revision': revision, 'release': release, 'driver': settings['driver'],
            'automaticUpdates': True, 'files': {name: hashlib.sha256(data).hexdigest() for name, data in files.items()}}))
        cases = []
        for scenario in validation.HEADLESS:
            cases.append({'passed': True, 'platform': settings['os'], 'release': dict(release, revision=revision),
                          'scenario': scenario, 'catalogSha256': validation.catalog_digest(),
                          'completedScenarios': validation.expanded(scenario, validation.catalog()),
                          'assertions': 200, 'checks': [{'passed': True}] * 200, 'runtimeErrors': [], 'screenshotMode': 'disabled-headless'})
        (directory / item['smoke']).write_text(json.dumps({
            'schemaVersion': 2, 'passed': True, 'target': target, 'revision': revision, 'release': release,
            'archiveSha256': desktop.digest(directory / item['archive']), 'os': settings['os'],
            'suite': 'headless-regression', 'cases': cases}))
        graphical = []
        for scenario in validation.SHARDED_GRAPHICAL:
            graphical.append(add_display_restart({'passed': True, 'platform': settings['os'], 'release': dict(release, revision=revision),
                              'scenario': scenario, 'catalogSha256': validation.catalog_digest(),
                              'completedScenarios': validation.expanded(scenario, validation.catalog()),
                              'assertions': 200, 'checks': [{'passed': True}] * 200, 'runtimeErrors': [], 'screenshotMode': 'graphical',
                              'renderingDriver': settings['driver'], 'renderingMethod': 'forward_plus', 'adapter': 'Fixture GPU', 'screenshots': ['capture.png']}))
        (directory / item['graphical']).write_text(json.dumps({'schemaVersion': 2, 'passed': True, 'target': target, 'revision': revision,
            'release': release, 'archiveSha256': desktop.digest(directory / item['archive']), 'suite': 'graphical-regression', 'cases': graphical}))
        (directory / item['startup']).write_text(json.dumps({'schemaVersion': 1, 'passed': True, 'target': target, 'revision': revision,
            'release': release, 'archiveSha256': desktop.digest(directory / item['archive']),
            'cases': [{'id': name, 'passed': True, 'adapter': 'Fixture GPU', 'driver':settings['driver'],'requestedDriver':settings['driver'],'fallbackReason':None,'method':'forward_plus', 'seconds': 4, 'snapshots': 10, 'kind': name.rsplit('-', 1)[1]} for name in validation.STARTUP]}))
        if target=='windows-x64':
            recovered={'driver':'d3d12','requestedDriver':'vulkan','fallbackReason':'previous-vulkan-startup-incomplete','sourceRevision':revision,'method':'forward_plus'}
            guard={'schemaVersion':1,'passed':True,'graphicsQualified':False,'source':revision,'archiveSha256':desktop.digest(directory/item['archive']),'errors':[],
                   'runs':[{'index':0,'exitCode':23,'expectedCrashObserved':True,'pendingMarkers':[{'driver':'vulkan'}]},
                           *[{'index':i,'exitCode':0,'runtimeErrors':[],'renderer':recovered,'shutdown':{'savedStateFlushed':True,'drain':{'completed':True}}}for i in (1,2)]]}
            (directory/item['startup_guard']).write_text(json.dumps(guard))
        (directory / download_formats.FILES[target]).write_bytes(b'MZ fixture installer' if target == 'windows-x64' else b'\x7fELF' + b'\x00'*4 + b'AI\x02 fixture AppImage')
        download_formats.record(directory, target, revision, release, item['archive'])
        for key in ('smoke','graphical'):
            path=directory/item[key];report=json.loads(path.read_text())
            report.update(downloadSha256=desktop.digest(directory/download_formats.FILES[target]),serverSha256=desktop.digest(directory/'Blast-Circuit-server-dotnet10.zip'))
            path.write_text(json.dumps(report))
        parts = release['version'].split('.')
        future = dict(release, version='.'.join([*parts[:2], str(int(parts[2]) + 1)]))
        cases = []
        for name in ('successful-install', 'failed-launch-rollback', 'saved-high-fullscreen'):
            cases.append({'name': name, 'result': ('failed ' + future['version'] + ' launch-rollback') if name == 'failed-launch-rollback' else 'installed ' + future['version'],
                          'fromVersion': release['version'], 'toVersion': future['version'], 'seconds': 5,
                          'oldGameplay': True, 'inventoryMatched': True, 'restoredGameplay': name != 'successful-install',
                          'uninstallVerified': True, 'health': {'version': future['version'], 'driver': settings['driver'], 'method': 'forward_plus', 'adapter': 'Fixture GPU', 'graphics': 'high' if name == 'saved-high-fullscreen' else 'medium', 'window': 'fullscreen' if name == 'saved-high-fullscreen' else 'windowed'}})
        (directory / item['updater_test']).write_text(json.dumps({'schemaVersion': 1, 'passed': True, 'platform': target,
            'candidate': {'release': release, 'revision': revision}, 'fixture': {'release': future, 'revision': revision},
            'candidateArchiveSha256': desktop.digest(directory / item['archive']), 'cases': cases, 'runtimeErrors': []}))



class DesktopPackageTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        write_desktop(self.root)
        self.target = next(iter(desktop.TARGETS))
        self.item = desktop.names(self.target)

    def test_complete_runtime_and_native_exported_smoke_pass(self):
        desktop.inspect(self.root, SHA, RELEASE, targets=list(desktop.TARGETS))

    def test_windows_regression_requires_a_disposable_profile_before_installing(self):
        environment = dict(os.environ, GITHUB_ACTIONS='false')
        result = subprocess.run([sys.executable, str(validation.ROOT / 'tools/packaging/verify-desktop.py'),
                                 '--target', 'windows-x64', '--directory', str(self.root / 'missing')],
                                env=environment, capture_output=True, text=True, timeout=10)
        self.assertNotEqual(0, result.returncode)
        self.assertIn('disposable VM/profile', result.stderr)

    def test_future_fixture_adds_root_and_new_directory_files_to_uninstall_inventory(self):
        spec = importlib.util.spec_from_file_location('update_fixture', validation.ROOT / 'tools/packaging/package-update-fixture.py')
        fixture = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(fixture)
        fixture.add_windows_files(self.root)
        metadata = desktop.inspect_archive(self.root, self.target, SHA, RELEASE)
        self.assertIn('update-added.txt', metadata['files'])
        self.assertIn('extra-runtime/update-added.txt', metadata['files'])

    def test_matching_archive_metadata_cannot_hide_an_incomplete_uninstall_inventory(self):
        archive = self.root / self.item['archive']
        name = self.item['root'] + '/installed-files.txt'
        changed = b'BlastCircuit.exe\ninstalled-files.txt\n'
        with zipfile.ZipFile(archive) as source:
            entries = [(entry, changed if entry.filename == name else source.read(entry)) for entry in source.infolist()]
        with zipfile.ZipFile(archive, 'w') as destination:
            for entry, data in entries: destination.writestr(entry, data)
        metadata_path = self.root / self.item['metadata']
        metadata = json.loads(metadata_path.read_text())
        metadata['files']['installed-files.txt'] = hashlib.sha256(changed).hexdigest()
        metadata_path.write_text(json.dumps(metadata))
        archive.with_suffix('.zip.sha256').write_text(desktop.digest(archive) + '  ' + archive.name)
        with self.assertRaisesRegex(RuntimeError, 'uninstall inventory'):
            desktop.inspect_archive(self.root, self.target, SHA, RELEASE)

    def test_software_renderer_allowance_requires_actual_cpu_devices(self):
        path=self.root/self.item['graphical']
        report=json.loads(path.read_text());report['softwareRendererBudget']=True
        for case in report['cases']:case['adapterType']='DiscreteGpu'
        path.write_text(json.dumps(report))
        with self.assertRaisesRegex(RuntimeError,'CPU-device'):
            desktop.inspect(self.root,SHA,RELEASE,targets=[self.target])
        for case in report['cases']:case['adapterType']='Cpu'
        path.write_text(json.dumps(report))
        desktop.inspect(self.root,SHA,RELEASE,targets=[self.target])
        report['cases'][0]['adapterType']='Other'
        path.write_text(json.dumps(report))
        with self.assertRaisesRegex(RuntimeError,'CPU-device'):
            desktop.inspect(self.root,SHA,RELEASE,targets=[self.target])

    def test_windows_warp_is_software_even_when_reported_as_integrated(self):
        # Retained native 3c180fd: D3D12 Microsoft software rasterizer was
        # reported as IntegratedGpu, so the ordinary GPU budget was selected.
        path = self.root / self.item['graphical']
        report = json.loads(path.read_text())
        report['softwareRendererBudget'] = True
        for case in report['cases']:
            case.update(platform='Windows', renderingDriver='d3d12',requestedDriver='vulkan',fallbackReason='vulkan-initialization-unavailable',
                        adapter='Microsoft Basic Render Driver', adapterType='IntegratedGpu')
            if 'displayRestart'in case:
                case['displayRestart']['summary'].update(renderingDriver='d3d12',requestedDriver='vulkan',fallbackReason='vulkan-initialization-unavailable')
        path.write_text(json.dumps(report))
        desktop.inspect(self.root, SHA, RELEASE, targets=[self.target])
        for changes in ({'adapter': 'Intel Iris Xe'}, {'adapter': 'Microsoft Basic Display Adapter'},
                        {'renderingDriver': 'vulkan'}, {'platform': 'Linux'}):
            with self.subTest(changes=changes):
                broken = json.loads(json.dumps(report))
                broken['cases'][0].update(changes)
                path.write_text(json.dumps(broken))
                with self.assertRaises(RuntimeError):
                    desktop.inspect(self.root, SHA, RELEASE, targets=[self.target])

    def test_complete_graphical_pass_cannot_omit_or_substitute_the_fresh_display_witness(self):
        import copy
        path = self.root / self.item['graphical']
        original = json.loads(path.read_text())
        for field, value in [('passed', False), ('parentExited', False), ('exitCode', 1), ('timeout', 180),
                             ('runtimeErrors', ['ERROR: fixture']), ('summary', {})]:
            broken = copy.deepcopy(original)
            next(case for case in broken['cases'] if 'displayRestart' in case)['displayRestart'][field] = value
            path.write_text(json.dumps(broken))
            with self.subTest(field=field), self.assertRaisesRegex(RuntimeError, 'fresh-display'):
                desktop.inspect(self.root, SHA, RELEASE, targets=[self.target])
        broken = copy.deepcopy(original)
        del next(case for case in broken['cases'] if 'displayRestart' in case)['displayRestart']
        path.write_text(json.dumps(broken))
        with self.assertRaisesRegex(RuntimeError, 'fresh-display'):
            desktop.inspect(self.root, SHA, RELEASE, targets=[self.target])

    def test_complete_tour_cannot_omit_the_controller_popup_preflight(self):
        path=self.root/self.item['graphical']
        report=json.loads(path.read_text())
        report['cases']=[case for case in report['cases'] if case['scenario']!='controller-popups']
        path.write_text(json.dumps(report))
        with self.assertRaisesRegex(RuntimeError,'missing required'):
            desktop.inspect(self.root,SHA,RELEASE,targets=[self.target])

    def test_software_tours_must_cover_the_complete_all_suite_in_order(self):
        # Fresh-process tours replace one serial all-process only when their
        # leaves are exactly the catalog's all-suite, contiguous and ordered.
        catalog = validation.catalog()
        self.assertEqual(validation.expanded('all', catalog),
                         [leaf for tour in validation.TOURS for leaf in validation.expanded(tour, catalog)])
        self.assertEqual(validation.SHARDED_GRAPHICAL[1:-2], validation.TOURS)
        path=self.root/self.item['graphical']
        original=json.loads(path.read_text())
        for mutate in (lambda cases: cases.pop(5), lambda cases: cases.insert(3, cases.pop(4)),
                       lambda cases: cases[2].update(completedScenarios=cases[2]['completedScenarios'][:-1])):
            report=json.loads(json.dumps(original));mutate(report['cases'])
            path.write_text(json.dumps(report))
            with self.subTest(mutate=mutate), self.assertRaises(RuntimeError):
                desktop.inspect(self.root,SHA,RELEASE,targets=[self.target])

    def test_each_software_runner_plan_owns_every_graphical_case_exactly_once(self):
        for target in ('windows-x64', 'linux-x64'):
            plan = validation.software_plan([target])
            owned = [case for part in plan for case in part['cases'].split(',')]
            self.assertEqual(sorted(validation.SHARDED_GRAPHICAL), sorted(owned), target)
            self.assertEqual(len(plan), len({part['part'] for part in plan}))
            self.assertEqual({desktop.TARGETS[target]['os']}, {'Windows' if part['runner'].startswith('windows') else 'Linux' for part in plan})
        self.assertLessEqual(len(validation.software_plan()), 30)

    def test_graphical_parts_merge_only_when_complete_and_bound_to_identical_bytes(self):
        settings=desktop.TARGETS[self.target]
        complete=json.loads((self.root/self.item['graphical']).read_text())
        def parts(groups):
            result=[]
            for index, names in enumerate(groups):
                part={key: value for key, value in complete.items() if key not in ('cases','suite')}
                part.update(suite='graphical-part', part='p'+str(index), cases=[case for case in complete['cases'] if case['scenario'] in names])
                result.append(part)
            return result
        names=[case['scenario'] for case in complete['cases']]
        merged=validation.merge_parts(parts([names[:1],names[1:9],names[9:]]),settings,SHA,RELEASE)
        self.assertEqual(names,[case['scenario'] for case in merged['cases']])
        for broken in (parts([names[:1],names[2:]]), parts([names[:3],names[2:]]), parts([])):
            with self.assertRaises(RuntimeError):
                validation.merge_parts(broken,settings,SHA,RELEASE)
        different=parts([names[:4],names[4:]]);different[1]['downloadSha256']='0'*64
        with self.assertRaisesRegex(RuntimeError,'different bytes'):
            validation.merge_parts(different,settings,SHA,RELEASE)
        unlabeled=parts([names[:4],names[4:]]);del unlabeled[0]['part']
        with self.assertRaises(RuntimeError):
            validation.merge_parts(unlabeled,settings,SHA,RELEASE)

    def test_lengthened_peer_host_lease_requires_a_software_renderer_receipt(self):
        path=self.root/self.item['graphical']
        report=json.loads(path.read_text())
        report['peerHostLeaseSeconds']=validation.SOFTWARE_PEER_HOST_LEASE
        path.write_text(json.dumps(report))
        with self.assertRaisesRegex(RuntimeError,'peer host lease'):
            desktop.inspect(self.root,SHA,RELEASE,targets=[self.target])
        report['softwareRendererBudget']=True
        for case in report['cases']:case['adapterType']='Cpu'
        path.write_text(json.dumps(report))
        desktop.inspect(self.root,SHA,RELEASE,targets=[self.target])
        report['peerHostLeaseSeconds']=30
        path.write_text(json.dumps(report))
        with self.assertRaisesRegex(RuntimeError,'peer host lease'):
            desktop.inspect(self.root,SHA,RELEASE,targets=[self.target])

    def test_full_suite_receipts_must_bind_the_actual_download_and_server(self):
        for key in ('smoke','graphical'):
            path=self.root/self.item[key];original=path.read_text()
            for field in ('downloadSha256','serverSha256'):
                report=json.loads(original);report[field]='0'*64;path.write_text(json.dumps(report))
                with self.subTest(key=key,field=field),self.assertRaisesRegex(RuntimeError,'different download/server'):
                    desktop.inspect(self.root,SHA,RELEASE,targets=[self.target])
            path.write_text(original)

    def test_linux_executable_permission_is_part_of_the_archive_contract(self):
        target = 'linux-x64'
        item = desktop.names(target)
        archive = self.root / item['archive']
        with zipfile.ZipFile(archive) as bundle:
            entries = [(entry, bundle.read(entry)) for entry in bundle.infolist()]
        with zipfile.ZipFile(archive, 'w') as bundle:
            for entry, data in entries:
                if entry.filename.endswith('/' + desktop.TARGETS[target]['executable']):
                    entry.external_attr = 0o100644 << 16
                bundle.writestr(entry, data)
        (self.root / (archive.name + '.sha256')).write_text(desktop.digest(archive) + '  ' + archive.name)
        with self.assertRaisesRegex(RuntimeError, 'executable permission'):
            desktop.inspect_archive(self.root, target, SHA, RELEASE)

    def test_archive_tampering_fails_before_extraction(self):
        with (self.root / self.item['archive']).open('ab') as stream:
            stream.write(b'tampered')
        with self.assertRaisesRegex(RuntimeError, 'checksum mismatch'):
            desktop.inspect(self.root, SHA, RELEASE)

    def test_smoke_from_other_os_archive_or_release_never_qualifies(self):
        path = self.root / self.item['smoke']
        original = path.read_text()
        for changes in ({'passed': False}, {'os': 'Darwin'}, {'archiveSha256': 'b' * 64},
                        {'revision': 'b' * 40}, {'release': {}}, {'schemaVersion': 1}, {'suite': 'unknown'}):
            value = json.loads(original)
            value.update(changes)
            path.write_text(json.dumps(value))
            with self.subTest(changes=changes), self.assertRaises(RuntimeError):
                desktop.inspect(self.root, SHA, RELEASE)

    def test_matching_metadata_cannot_authorize_traversal_or_links(self):
        for name, mode in (('../escape', 0o100644), ('/absolute', 0o100644),
                           (self.item['root'] + '/escape', 0o120777), (self.item['root'] + '/C:drive', 0o100644)):
            write_desktop(self.root)
            archive = self.root / self.item['archive']
            with zipfile.ZipFile(archive, 'a') as bundle:
                entry = zipfile.ZipInfo(name)
                entry.external_attr = mode << 16
                bundle.writestr(entry, b'unsafe')
            (self.root / (archive.name + '.sha256')).write_text(desktop.digest(archive) + '  ' + archive.name)
            with self.subTest(name=name), self.assertRaisesRegex(RuntimeError, 'Unsafe'):
                desktop.inspect_archive(self.root, self.target, SHA, RELEASE)

    def test_inventory_cannot_omit_the_native_runtime(self):
        archive = self.root / self.item['archive']
        with zipfile.ZipFile(archive) as bundle:
            entries = {name: bundle.read(name) for name in bundle.namelist() if not name.endswith(desktop.TARGETS[self.target]['runtime'])}
        with zipfile.ZipFile(archive, 'w') as bundle:
            for name, data in entries.items():
                bundle.writestr(name, data)
        (self.root / (archive.name + '.sha256')).write_text(desktop.digest(archive) + '  ' + archive.name)
        metadata_path = self.root / self.item['metadata']
        metadata = json.loads(metadata_path.read_text())
        del metadata['files'][desktop.TARGETS[self.target]['runtime']]
        metadata_path.write_text(json.dumps(metadata))
        with self.assertRaisesRegex(RuntimeError, 'runtime is incomplete'):
            desktop.inspect_archive(self.root, self.target, SHA, RELEASE)

    def test_successful_main_run_requires_desktop_jobs_and_artifacts(self):
        run = {'head_repository': {'full_name': 'example/game'}, 'head_branch': 'main',
               'status': 'completed', 'conclusion': 'success', 'event': 'push',
               'path': '.github/workflows/ci.yml', 'head_sha': SHA}
        jobs = {'Public contracts and audio', 'Trusted managed and network', 'Trusted Godot headless',
                'Trusted Apple Silicon package', promote_release.release_gates.STARTUP_JOB, promote_release.release_gates.UPDATE_JOB}
        artifacts = [promote_release.ARTIFACT, promote_release.SERVER_ARTIFACT,
                     promote_release.release_gates.STARTUP_ARTIFACT, promote_release.release_gates.UPDATE_ARTIFACT]
        def responses(enabled, complete=True):
            return [json.dumps(run), json.dumps({'jobs': [{'name': name, 'conclusion': 'success'} for name in jobs | (desktop.jobs() if enabled else set())]}),
                    json.dumps({'artifacts': [{'name': name} for name in artifacts + (desktop.artifacts() if complete else [])]})]
        with patch.object(promote_release, 'gh', side_effect=responses(False)), self.assertRaisesRegex(RuntimeError, 'jobs did not all pass'):
            promote_release.inspect_run('example/game', '12')
        with patch.object(promote_release, 'gh', side_effect=responses(True, False)), self.assertRaisesRegex(RuntimeError, 'tested artifact'):
            promote_release.inspect_run('example/game', '12')
        with patch.object(promote_release, 'gh', side_effect=responses(True)):
            self.assertEqual(SHA, promote_release.inspect_run('example/game', '12'))


if __name__ == '__main__':
    unittest.main()
