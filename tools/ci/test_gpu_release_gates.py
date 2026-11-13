import copy
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import gpu_release_gates as gates

SHA='a'*40
RUN='sancus-builder-20261030-09e454b-1234abcd'


def cleanup():
    return {'schemaVersion':1,'source':SHA,'runId':RUN,'account':'524329886851','region':'us-east-1',
            'passed':True,'leftovers':[],'errors':[],'protectedInventoryVerified':True,
            'ephemeralRunnersRemoved':True,'gpuVariablesDisarmed':True,
            'checked':{key:{'state':'absent'}for key in gates.CLEANUP_TYPES}}


def receipt(target='windows-x64'):
    device={'adapter':'NVIDIA Tesla T4','deviceType':'DiscreteGpu','driver':'vulkan'}
    comparisons=[{'name':f'run-{i}/{name}',**({'passed':True}if name.startswith('control-')else{'exitCode':0})}
                 for i in (0,1)for name in ('same-draw','fresh-target','control-dof','control-mirror')]
    comparisons.append({'name':'fresh-process','exitCode':0})
    return {'schemaVersion':1,'target':target,'source':SHA,'archiveSha256':'a','downloadSha256':'b','serverSha256':'c',
            'renderFingerprint':'fp','passed':True,'primaryDriver':'vulkan','effectiveSamples':2,'devices':[device],
            'cleanupAudited':True,'cleanupSha256':'0'*64,'campaignRunId':RUN,'diagnosticRepeats':{'failures':[],'cases':{}},
            'primaryHigh':{'passed':True,'revision':SHA,'archiveSha256':'a','downloadSha256':'b',
                           'runs':[{'exitCode':0,'runtimeErrors':[],'validationErrors':[],'meshInputsValidated':True,'device':device}for _ in (0,1)],
                           'comparisons':comparisons}}


class GpuPromotionBoundary(unittest.TestCase):
    def inspect(self,data):return gates.inspect_receipt(data,'windows-x64',SHA,'a','b','c','fp')

    def test_no_gpu_receipt_fails_even_if_other_files_exist(self):
        with tempfile.TemporaryDirectory()as temporary:
            root=Path(temporary);(root/'Blast-Circuit-windows-x64-graphical.json').write_text('{}')
            with self.assertRaisesRegex(RuntimeError,'Missing mandatory GPU'):
                gates.inspect(root,'windows-x64',SHA,'a','b','c','fp')

    def test_exact_hardware_evidence_is_accepted(self):
        self.assertTrue(self.inspect(receipt())['passed'])

    def test_preliminary_or_missing_cleanup_cannot_qualify(self):
        data=receipt();data['cleanupAudited']=False
        with self.assertRaisesRegex(RuntimeError,'cleanup audit'):self.inspect(data)

    def test_different_main_sha_or_any_payload_hash_fails(self):
        for field in ['source','archiveSha256','downloadSha256','serverSha256','renderFingerprint']:
            data=receipt();data[field]='different'
            with self.subTest(field=field),self.assertRaisesRegex(RuntimeError,'mismatch'):self.inspect(data)

    def test_software_and_paravirtual_are_never_physical_qualification(self):
        for adapter in ['Microsoft WARP','Microsoft Basic Render Driver','llvmpipe','lavapipe','Apple Paravirtual device','SwiftShader']:
            data=copy.deepcopy(receipt());data['devices'][0]['adapter']=adapter
            with self.subTest(adapter=adapter),self.assertRaisesRegex(RuntimeError,'physical'):self.inspect(data)
        data=copy.deepcopy(receipt());data['primaryHigh']['runs'][0]['device']['deviceType']='Cpu'
        with self.assertRaises(RuntimeError):self.inspect(data)

    def test_missing_repeat_effect_or_fresh_process_never_passes(self):
        for change in ['control','repeat','process']:
            data=copy.deepcopy(receipt())
            if change=='control':data['primaryHigh']['comparisons'][2]['passed']=False
            elif change=='repeat':data['primaryHigh']['comparisons'][0]['exitCode']=1
            else:data['primaryHigh']['runs'].pop()
            with self.subTest(change=change),self.assertRaises(RuntimeError):self.inspect(data)

    def test_graphical_receipt_is_bound_by_its_own_hash(self):
        with tempfile.TemporaryDirectory()as temporary:
            root=Path(temporary);data=receipt();data['graphicalSha256']=hashlib.sha256(b'original').hexdigest()
            raw=json.dumps(cleanup()).encode();data['cleanupSha256']=hashlib.sha256(raw).hexdigest()
            (root/gates.CLEANUP_RECEIPT).write_bytes(raw)
            (root/gates.receipt_name('windows-x64')).write_text(json.dumps(data))
            (root/'Blast-Circuit-windows-x64-graphical.json').write_bytes(b'tampered')
            with self.assertRaisesRegex(RuntimeError,'differs'):gates.inspect(root,'windows-x64',SHA,'a','b','c','fp')

    def test_successful_gpu_workflow_for_other_main_sha_is_rejected(self):
        run={'head_repository':{'full_name':'o/r'},'head_sha':'b'*40,'path':'.github/workflows/gpu-qualification.yml',
             'head_branch':'main','event':'workflow_dispatch','status':'completed','conclusion':'success'}
        with self.assertRaisesRegex(RuntimeError,'main SHA'):gates.inspect_run(run,'o/r',SHA)

    def test_only_final_main_controller_not_raw_collector_can_supply_receipts(self):
        run={'head_repository':{'full_name':'o/r'},'head_sha':SHA,'path':'.github/workflows/gpu-qualification.yml',
             'head_branch':'main','event':'workflow_dispatch','status':'completed','conclusion':'success'}
        self.assertEqual(gates.inspect_run(run,'o/r',SHA),SHA)
        for changed in ({'path':'.github/workflows/gpu-once.yml'},{'head_branch':'qa/three-platform-release'},
                        {'head_repository':{'full_name':'fork/repo'}}):
            with self.subTest(changed=changed),self.assertRaises(RuntimeError):gates.inspect_run({**run,**changed},'o/r',SHA)

    def test_download_merges_real_two_artifact_shape_in_either_order(self):
        self.download_shared_cleanup(mismatch=False)

    def test_download_rejects_different_shared_cleanup_in_either_order(self):
        self.download_shared_cleanup(mismatch=True)

    def download_shared_cleanup(self,mismatch):
        for order in (('windows-x64','linux-x64'),('linux-x64','windows-x64')):
            with self.subTest(order=order),tempfile.TemporaryDirectory() as temporary:
                root=Path(temporary);destinations=[]
                run={'head_repository':{'full_name':'o/r'},'head_sha':SHA,'path':'.github/workflows/gpu-qualification.yml',
                     'head_branch':'main','event':'workflow_dispatch','status':'completed','conclusion':'success'}
                def gh(*args):
                    if args[0]=='api':return json.dumps(run)
                    destination=Path(args[args.index('--dir')+1]);destination.mkdir(exist_ok=True)
                    destinations.append(destination)
                    target=order[len(destinations)-1]
                    files={gates.receipt_name(target):target.encode(),
                           'Blast-Circuit-'+target+'-graphical.json':b'graphical',
                           gates.CLEANUP_RECEIPT:(target.encode() if mismatch else b'identical cleanup')}
                    for name,data in files.items():
                        path=destination/name
                        # gh's actual extractor refuses existing names, even for identical bytes.
                        with path.open('xb') as stream:stream.write(data)
                    return ''
                with patch.object(gates,'artifact_name',side_effect=[gates.artifact_name(t) for t in order]):
                    if mismatch:
                        with self.assertRaisesRegex(RuntimeError,'conflicting GPU artifact file'):
                            gates.download('o/r','123',SHA,root,gh)
                        self.assertEqual((root/gates.CLEANUP_RECEIPT).read_bytes(),order[0].encode())
                    else:
                        gates.download('o/r','123',SHA,root,gh)
                        self.assertEqual((root/gates.CLEANUP_RECEIPT).read_bytes(),b'identical cleanup')
                        for target in order:self.assertEqual((root/gates.receipt_name(target)).read_bytes(),target.encode())
                self.assertEqual(len(set(destinations)),2)



def workstation_receipt(target):
    data=receipt(target)
    for key in ('cleanupAudited','cleanupSha256','campaignRunId'):data.pop(key)
    data['devices'][0]['adapter']='NVIDIA GeForce RTX 2080 SUPER'
    for row in data['primaryHigh']['runs']:row['device']['adapter']='NVIDIA GeForce RTX 2080 SUPER'
    data.update(qualificationHost='workstation',hostVerified=False,qualificationRunId='777',
                graphicalSha256=hashlib.sha256(b'graphical-'+target.encode()).hexdigest())
    return data


def jobs(**override):
    rows=[{'target':'linux-x64','jobId':2,'runnerName':'blast-gpu-linux-2','labels':['self-hosted','blast-gpu','Linux','X64'],
           'conclusion':'success'}]
    for row in rows:row.update(override.get(row['target'],{}))
    return rows


def aws_windows(root):
    """The Windows half of a hybrid run: the Sancus AWS receipt bound to its cleanup audit."""
    raw=json.dumps(cleanup()).encode();graphical=b'graphical-windows-x64'
    data=receipt('windows-x64');data.update(cleanupSha256=hashlib.sha256(raw).hexdigest(),graphicalSha256=hashlib.sha256(graphical).hexdigest())
    files={gates.CLEANUP_RECEIPT:raw,gates.receipt_name('windows-x64'):json.dumps(data).encode(),
           'Blast-Circuit-windows-x64-graphical.json':graphical}
    for name,content in files.items():(Path(root)/name).write_bytes(content)
    return files


def stage_linux(root):
    """The staged workstation Linux receipt, as the native job uploads it for finalization."""
    folder=root/'gpu-release-linux-x64';folder.mkdir()
    (folder/gates.receipt_name('linux-x64')).write_text(json.dumps(workstation_receipt('linux-x64')))
    (folder/'Blast-Circuit-linux-x64-graphical.json').write_bytes(b'graphical-linux-x64')


class WorkstationHostBinding(unittest.TestCase):
    def staged(self,root):stage_linux(root)

    def test_preliminary_workstation_receipt_cannot_promote(self):
        with self.assertRaisesRegex(RuntimeError,'workstation host receipt'):
            gates.inspect_receipt(workstation_receipt('linux-x64'),'linux-x64',SHA,'a','b','c','fp')

    def test_bound_linux_receipt_passes_promotion_inspection(self):
        with tempfile.TemporaryDirectory()as temporary:
            root=Path(temporary);self.staged(root)
            output=gates.bind_host(root,root/'qualified',SHA,'777',jobs())
            self.assertEqual(gates.bound_receipts(output),[gates.HOST_RECEIPT])
            self.assertEqual(sorted(path.name for path in output.iterdir()),
                             sorted([gates.HOST_RECEIPT,'Blast-Circuit-linux-x64-gpu.json','Blast-Circuit-linux-x64-graphical.json']))
            data=gates.inspect(output,'linux-x64',SHA,'a','b','c','fp')
            self.assertIs(data['hostVerified'],True)
            host=json.loads((output/gates.HOST_RECEIPT).read_text())
            self.assertEqual([row['target']for row in host['jobs']],['linux-x64'])
            self.assertEqual(host['devices'],['NVIDIA GeForce RTX 2080 SUPER'])
            self.assertEqual(host['runnerMode'],'jit-single-use')
            with self.assertRaises(FileExistsError):gates.bind_host(root,output,SHA,'777',jobs())

    def test_the_abandoned_workstation_windows_vm_can_never_qualify(self):
        # 2026-11-13: GPU passthrough hung the host kernel; Windows qualifies only on the Sancus builder.
        self.assertEqual(gates.WORKSTATION_TARGETS,('linux-x64',))
        with tempfile.TemporaryDirectory()as temporary:
            root=Path(temporary);self.staged(root);output=gates.bind_host(root,root/'q',SHA,'777',jobs())
            data=workstation_receipt('windows-x64');data.update(hostVerified=True,
                hostReceiptSha256=hashlib.sha256((output/gates.HOST_RECEIPT).read_bytes()).hexdigest())
            (output/gates.receipt_name('windows-x64')).write_text(json.dumps(data))
            (output/'Blast-Circuit-windows-x64-graphical.json').write_bytes(b'graphical-windows-x64')
            with self.assertRaisesRegex(RuntimeError,'host receipt'):gates.inspect(output,'windows-x64',SHA,'a','b','c','fp')
            # A host receipt claiming a Windows job too (the retired two-job shape) proves nothing.
            path=output/gates.HOST_RECEIPT;host=json.loads(path.read_text())
            host['jobs'].append({**host['jobs'][0],'target':'windows-x64','runnerName':'blast-gpu-windows-1'});path.write_text(json.dumps(host))
            linux=json.loads((output/gates.receipt_name('linux-x64')).read_text())
            linux['hostReceiptSha256']=hashlib.sha256(path.read_bytes()).hexdigest()
            (output/gates.receipt_name('linux-x64')).write_text(json.dumps(linux))
            with self.assertRaisesRegex(RuntimeError,'host receipt'):gates.inspect(output,'linux-x64',SHA,'a','b','c','fp')

    def test_foreign_failed_or_other_run_jobs_are_never_bound(self):
        for changed in ({'linux-x64':{'runnerName':'sancus-builder-x'}},{'linux-x64':{'runnerName':'blast-gpu-windows-9'}},
                        {'linux-x64':{'conclusion':'failure'}}):
            with self.subTest(changed=changed),tempfile.TemporaryDirectory()as temporary:
                root=Path(temporary);self.staged(root)
                with self.assertRaisesRegex(RuntimeError,'workstation runner'):gates.bind_host(root,root/'q',SHA,'777',jobs(**changed))
        with tempfile.TemporaryDirectory()as temporary:
            root=Path(temporary);self.staged(root)
            with self.assertRaisesRegex(RuntimeError,'workstation runner'):gates.bind_host(root,root/'q',SHA,'777',[])
            with self.assertRaisesRegex(RuntimeError,'not from this workstation run'):gates.bind_host(root,root/'q',SHA,'778',jobs())

    def test_tampered_or_mismatched_host_receipt_fails(self):
        with tempfile.TemporaryDirectory()as temporary:
            root=Path(temporary);self.staged(root);output=gates.bind_host(root,root/'q',SHA,'777',jobs())
            path=output/gates.HOST_RECEIPT;host=json.loads(path.read_text())
            path.write_text(json.dumps({**host,'runId':'778'}))
            with self.assertRaisesRegex(RuntimeError,'host receipt'):gates.inspect(output,'linux-x64',SHA,'a','b','c','fp')
            path.unlink()
            with self.assertRaisesRegex(RuntimeError,'host receipt'):gates.inspect(output,'linux-x64',SHA,'a','b','c','fp')

    def test_run_jobs_reads_only_the_named_workstation_linux_job(self):
        page={'jobs':[{'id':1,'name':'Workstation GPU linux-x64','runner_name':'blast-gpu-linux-1','labels':['blast-gpu'],
                       'conclusion':'success','started_at':'s','completed_at':'c'},
                      {'id':3,'name':'Sancus builder windows-x64 / PAID up to $3.67 - source x','runner_name':'GitHub Actions 2',
                       'labels':['ubuntu-24.04'],'conclusion':'success'},
                      {'id':4,'name':'Workstation GPU windows-x64','runner_name':'blast-gpu-windows-1','labels':['blast-gpu'],'conclusion':'success'},
                      {'id':2,'name':'select','runner_name':'GitHub Actions 1','labels':['ubuntu-24.04'],'conclusion':'success'}]}
        rows=gates.run_jobs('o/r','5',lambda *args:json.dumps([page]))
        self.assertEqual([(row['target'],row['runnerName'])for row in rows],[('linux-x64','blast-gpu-linux-1')])


class BoundReceiptShapes(unittest.TestCase):
    """Promotion/verification name exactly the bound receipts the target receipts reference."""

    def hybrid(self,root):
        staged=root/'staged';staged.mkdir()
        stage_linux(staged)
        output=gates.bind_host(staged,root/'linux',SHA,'777',jobs())
        merged=root/'merged';merged.mkdir()
        for path in output.iterdir():(merged/path.name).write_bytes(path.read_bytes())
        windows=aws_windows(merged)
        return merged,set(windows),{path.name for path in output.iterdir()}

    def test_hybrid_binds_windows_to_cleanup_and_linux_to_the_host_receipt(self):
        with tempfile.TemporaryDirectory()as temporary:
            merged,windows,linux=self.hybrid(Path(temporary))
            self.assertFalse(windows&linux,'the two promotion artifacts must never share a file name')
            self.assertEqual(gates.bound_receipts(merged),sorted([gates.CLEANUP_RECEIPT,gates.HOST_RECEIPT]))
            self.assertEqual(gates.bound_receipts(merged,['windows-x64']),[gates.CLEANUP_RECEIPT])
            self.assertEqual(gates.bound_receipts(merged,['linux-x64']),[gates.HOST_RECEIPT])
            self.assertIs(gates.inspect(merged,'windows-x64',SHA,'a','b','c','fp')['cleanupAudited'],True)
            self.assertIs(gates.inspect(merged,'linux-x64',SHA,'a','b','c','fp')['hostVerified'],True)

    def test_hybrid_missing_either_bound_receipt_fails(self):
        for missing in (gates.CLEANUP_RECEIPT,gates.HOST_RECEIPT):
            with self.subTest(missing=missing),tempfile.TemporaryDirectory()as temporary:
                merged,_,_=self.hybrid(Path(temporary));(merged/missing).unlink()
                with self.assertRaisesRegex(RuntimeError,'missing its bound'):gates.bound_receipts(merged)

    def test_legacy_aws_release_binds_both_targets_to_the_one_cleanup_receipt(self):
        with tempfile.TemporaryDirectory()as temporary:
            root=Path(temporary);aws_windows(root)
            data=receipt('linux-x64');data['cleanupSha256']=hashlib.sha256((root/gates.CLEANUP_RECEIPT).read_bytes()).hexdigest()
            data['graphicalSha256']=hashlib.sha256(b'graphical-linux').hexdigest()
            (root/gates.receipt_name('linux-x64')).write_text(json.dumps(data))
            (root/'Blast-Circuit-linux-x64-graphical.json').write_bytes(b'graphical-linux')
            self.assertEqual(gates.bound_receipts(root),[gates.CLEANUP_RECEIPT])
            for target in ('windows-x64','linux-x64'):
                self.assertIs(gates.inspect(root,target,SHA,'a','b','c','fp')['cleanupAudited'],True)

    def test_no_receipt_or_an_unreferenced_bound_receipt_is_refused(self):
        with tempfile.TemporaryDirectory()as temporary:
            root=Path(temporary)
            with self.assertRaisesRegex(RuntimeError,'no target receipt'):gates.bound_receipts(root)
            aws_windows(root);self.assertEqual(gates.bound_receipts(root),[gates.CLEANUP_RECEIPT])
            (root/gates.HOST_RECEIPT).write_text('{}')
            with self.assertRaisesRegex(RuntimeError,'unreferenced'):gates.bound_receipts(root)

    def test_download_merges_the_hybrid_artifacts_without_conflict_in_either_order(self):
        with tempfile.TemporaryDirectory()as temporary:
            merged,windows,linux=self.hybrid(Path(temporary))
            contents={'windows-x64':{name:(merged/name).read_bytes()for name in windows},
                      'linux-x64':{name:(merged/name).read_bytes()for name in linux}}
            run={'head_repository':{'full_name':'o/r'},'head_sha':SHA,'path':'.github/workflows/gpu-qualification.yml',
                 'head_branch':'main','event':'workflow_dispatch','status':'completed','conclusion':'success'}
            for order in (('windows-x64','linux-x64'),('linux-x64','windows-x64')):
                with self.subTest(order=order),tempfile.TemporaryDirectory()as destination_root:
                    destination_root=Path(destination_root);calls=[]
                    def gh(*args):
                        if args[0]=='api':return json.dumps(run)
                        destination=Path(args[args.index('--dir')+1]);destination.mkdir(exist_ok=True)
                        calls.append(args[args.index('--name')+1])
                        for name,data in contents[order[len(calls)-1]].items():
                            with (destination/name).open('xb')as stream:stream.write(data)
                        return ''
                    with patch.object(gates,'artifact_name',side_effect=[gates.artifact_name(t)for t in order]):
                        gates.download('o/r','123',SHA,destination_root,gh)
                    self.assertEqual({path.name for path in destination_root.iterdir()},windows|linux)
                    self.assertEqual(gates.bound_receipts(destination_root),sorted([gates.CLEANUP_RECEIPT,gates.HOST_RECEIPT]))


def run(index,crashed=False):
    """One repeat row as verify-desktop --repeat writes it (shape from run 41304300722)."""
    if crashed:
        return {'index':index,'runnerExitCode':1,'folder':'20261101T133641.306719Z-tour-09','passed':False,'exitCode':127,
                'assertions':None,'errors':['Godot did not write summary.json; inspect runtime.log for compile/launch errors'],
                'summaryPresent':False,'seconds':58.136,'crashed':True,
                'inferredSignal':{'signal':'SIGSEGV','source':'kernel segfault report','pid':34357,'comm':'BlastCircuit.x8'}}
    return {'index':index,'runnerExitCode':0,'passed':True,'exitCode':0,'assertions':923,'errors':[],'summaryPresent':True,
            'seconds':61.4,'crashed':False,'inferredSignal':None}


def repeats(case,count,crash_at=None):
    rows=[run(i,i==crash_at)for i in range(count)]
    passed=sum(r['passed']for r in rows);crashed=sum(r['crashed']for r in rows)
    return {'schemaVersion':1,'scenario':case,'requested':count,'runs':rows,'completed':count,'passed':passed,'crashed':crashed,
            'failed':count-passed-crashed,'allPassed':passed==count}


class DiagnosticRepeatGate(unittest.TestCase):
    """A failed diagnostic repeat inside an otherwise finished remaining phase can never qualify."""

    def linux_report(self,**override):
        import gpu_campaign
        report={'phase':'remaining','collectionComplete':True,'passed':False,
                'diagnosticRepeats':{case:repeats(case,count)for case,count,_ in gpu_campaign.LINUX_REPEATS}}
        report['diagnosticRepeats'].update(override);return report

    def test_clean_repeats_pass(self):
        self.assertEqual(gates.diagnostic_repeat_failures(self.linux_report(),'linux-x64'),[])

    def test_run_41304300722_shape_fails(self):
        import gpu_campaign
        case,count,_=next(row for row in gpu_campaign.LINUX_REPEATS if row[0]=='tour-09')
        failures=gates.diagnostic_repeat_failures(self.linux_report(**{case:repeats(case,count,crash_at=min(4,count-1))}),'linux-x64')
        self.assertEqual(len(failures),1);self.assertIn('1 crashed',failures[0])

    def test_missing_incomplete_or_errored_repeats_fail(self):
        import gpu_campaign
        case,count,_=gpu_campaign.LINUX_REPEATS[0]
        missing=self.linux_report();del missing['diagnosticRepeats'][case]
        short=self.linux_report(**{case:{**repeats(case,count),'runs':repeats(case,count)['runs'][:-1]}})
        errored=self.linux_report(**{case:{'error':'repeat receipt missing'}})
        for report in (missing,short,errored,{}):
            self.assertTrue(gates.diagnostic_repeat_failures(report,'linux-x64'),report.keys())

    def test_failed_repeats_write_a_failed_receipt_and_stop(self):
        import gpu_campaign
        case,count,_=next(row for row in gpu_campaign.LINUX_REPEATS if row[0]=='tour-09')
        with tempfile.TemporaryDirectory()as temporary:
            directory=Path(temporary);(directory/'gpu-campaign-linux-x64-remaining').mkdir()
            (directory/'gpu-campaign-linux-x64-remaining/campaign-result.json').write_text(
                json.dumps(self.linux_report(**{case:repeats(case,count,crash_at=min(4,count-1))})))
            graphical=directory/'Blast-Circuit-linux-x64-graphical.json';graphical.write_text('{}')
            output=directory/'gpu-release-linux-x64';output.mkdir()
            data=receipt('linux-x64');data.pop('diagnosticRepeats')
            with self.assertRaisesRegex(RuntimeError,'Diagnostic repeats failed'):
                gates.block_failed_repeats(directory,'linux-x64',data,graphical,output)
            written=json.loads((output/gates.receipt_name('linux-x64')).read_text())
        self.assertIs(written['passed'],False);self.assertTrue(written['diagnosticRepeats']['failures'])

    def test_receipts_without_a_clean_repeat_verdict_cannot_promote(self):
        for verdict in (None,{'failures':['tour-09: 5/6 passed, 1 crashed, 6 ran']}):
            data=receipt('linux-x64')
            if verdict is None:data.pop('diagnosticRepeats')
            else:data['diagnosticRepeats']=verdict
            with self.assertRaisesRegex(RuntimeError,'diagnostic-repeat verdict'):
                gates.inspect_receipt(data,'linux-x64',SHA,'a','b','c','fp',require_cleanup=False)


if __name__=='__main__':
    unittest.main()
