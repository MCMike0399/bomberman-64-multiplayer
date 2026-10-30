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
            'cleanupAudited':True,'cleanupSha256':'0'*64,'campaignRunId':RUN,
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


if __name__=='__main__':unittest.main()
