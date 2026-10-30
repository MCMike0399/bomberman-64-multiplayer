import copy
import unittest
import hashlib
import json
from pathlib import Path
import tempfile

import builder_finalize as finalize


class CleanupBinding(unittest.TestCase):
    def setUp(self):
        self.plan={'account':'524329886851','region':'us-east-1','runId':'sancus-builder-20261030-120b573-1234567890abcdef','payloadSource':'a'*40}
        self.raw={**self.plan,'allCreatedResourcesGone':True,'leftovers':[],'cleanupStageErrors':[],
                  'cost':{'complete':True,'estimateUsd':.25,'invoice':False},
                  'retainedEvidenceValidated':True,'evidenceComplete':True,'testVerdict':'pass',
                  'protectedComparisonComplete':True,'protectedObservedChanges':[],
                  'protectedRequestedInstances':9,'protectedComparedInstances':9,'protectedFieldsChecked':['state','securityGroups','blockDeviceMappings'],
                  'standingIdentity':{'role':'sancus-builder-ci'},'nonResourceRecords':['SSM command history'],
                  'typeAudit':{name:{'state':'absent'}for names in finalize.GROUPS.values()for name in names}}
        stack=f"arn:aws:cloudformation:us-east-1:524329886851:stack/{self.plan['runId']}/owned"
        self.created=[{'event':'CREATED','type':kind,'id':identity}for kind,identity in (
            ('AWS::CloudFormation::Stack',stack),('AWS::EC2::Instance','i-one'),('AWS::EC2::Instance','i-two'))]
        self.raw['resources']=[{'type':row['type'],'id':row['id'],'finalState':'DELETE_COMPLETE'if row['type']=='AWS::CloudFormation::Stack'else'terminated'}for row in self.created]

    def test_every_required_type_is_independently_bound(self):
        result=finalize.cleanup_receipt(self.raw,self.plan,{},self.created)
        self.assertTrue(result['passed']);self.assertEqual(set(result['checked']),finalize.gates.CLEANUP_TYPES)
        self.assertEqual(result['nonResourceRecords'],['SSM command history'])

    def test_no_qualification_for_missing_type_protected_change_or_incomplete_evidence(self):
        for key,value in [('typeAudit',{}),('protectedObservedChanges',[{'id':'protected','state':'changed'}]),
                          ('evidenceComplete',False),('testVerdict','incomplete'),('leftovers',[{'id':'owned'}])]:
            with self.subTest(key=key),self.assertRaises(ValueError):finalize.cleanup_receipt({**self.raw,key:value},self.plan,{},[])

    def test_retained_variables_and_unexpected_registration_are_not_ignored(self):
        with self.assertRaises(ValueError):finalize.cleanup_receipt(self.raw,self.plan,{'BLAST_GPU_RUN_ID':''},[])
        with self.assertRaises(ValueError):finalize.cleanup_receipt(self.raw,self.plan,{},[{'event':'CREATED','type':'GitHub::Runner'}])

    def test_stale_source_never_binds_to_current_bytes(self):
        with self.assertRaises(ValueError):finalize.cleanup_receipt({**self.raw,'payloadSource':'b'*40},self.plan,{},[])

    def test_empty_audit_is_never_paid_game_qualification(self):
        with self.assertRaisesRegex(ValueError,'real campaign'):finalize.cleanup_receipt(self.raw,self.plan,{},[])

    def test_type_sweep_cannot_hide_a_missing_exact_created_id(self):
        with self.assertRaisesRegex(ValueError,'physical ID'):
            finalize.cleanup_receipt({**self.raw,'resources':self.raw['resources'][:2]},self.plan,{},self.created)

    def test_downloaded_state_binds_complete_contract_fixtures_and_cleanup(self):
        # Synthetic schema fixtures only; this does not execute a native game.
        import builder_journal,desktop_packages,download_formats,release_identity
        from test_desktop_packages import write_desktop
        from test_gpu_release_gates import receipt
        with tempfile.TemporaryDirectory()as temporary:
            root=Path(temporary);state=root/'state';state.mkdir();packages=root/'packages';packages.mkdir()
            release=release_identity.check_exports();write_desktop(packages,revision=self.plan['payloadSource'],release=release)
            plan={**self.plan,'verificationRunId':'123456','renderFingerprint':'fp','release':release,'artifacts':{}}
            status={'targets':{}}
            for target in ('windows-x64','linux-x64'):
                expected={'archiveSha256':desktop_packages.digest(packages/desktop_packages.names(target)['archive']),
                          'downloadSha256':desktop_packages.digest(packages/download_formats.FILES[target]),
                          'serverSha256':desktop_packages.digest(packages/'Blast-Circuit-server-dotnet10.zip')}
                plan['artifacts'][target]=expected
                graph=json.loads((packages/('Blast-Circuit-'+target+'-graphical.json')).read_text())
                for case in graph['cases']:case.update(adapter='NVIDIA Tesla T4',adapterType='DiscreteGpu')
                graph_bytes=json.dumps(graph).encode();data=receipt(target)
                data.update(expected,campaignRunId=plan['runId'],verificationRunId=plan['verificationRunId'],
                            cleanupAudited=False,graphicalSha256=hashlib.sha256(graph_bytes).hexdigest())
                data['primaryHigh'].update(archiveSha256=expected['archiveSha256'],downloadSha256=expected['downloadSha256'])
                files=[]
                for relative,content in [('gpu-release-'+target+'/'+finalize.gates.receipt_name(target),json.dumps(data).encode()),
                                         ('Blast-Circuit-'+target+'-graphical.json',graph_bytes)]:
                    path=state/'evidence'/target/'finished'/'finished'/relative;path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(content)
                    files.append({'path':'finished/'+relative,'localPath':'/old/host/no-longer-present/'+relative,
                                  'sha256':hashlib.sha256(content).hexdigest()})
                status['targets'][target]={'state':'finished','retention':{'files':files}}
            (state/'plan.json').write_text(json.dumps(plan));(state/'controller.json').write_text(json.dumps(status))
            (state/'cleanup-receipt.json').write_text(json.dumps(self.raw))
            for row in self.created:builder_journal.append(state/'creation-manifest.jsonl',plan,'CREATED',row['type'],row['id'])
            output=finalize.bind(state,{},root/'bound')
            self.assertTrue((output/finalize.gates.CLEANUP_RECEIPT).is_file())
            for target in plan['artifacts']:
                result=json.loads((output/finalize.gates.receipt_name(target)).read_text())
                self.assertTrue(result['cleanupAudited'])


if __name__=='__main__':unittest.main()
