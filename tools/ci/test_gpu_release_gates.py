import copy
import hashlib
import json
from pathlib import Path
import tempfile
import unittest

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
