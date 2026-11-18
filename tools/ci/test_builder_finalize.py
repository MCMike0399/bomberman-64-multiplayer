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
        # 2026-11-17: two parallel Windows lanes, one journaled instance per lane slot.
        self.created=[{'event':'CREATED','type':'AWS::CloudFormation::Stack','id':stack,'details':{}},
                      {'event':'CREATED','type':'AWS::EC2::Instance','id':'i-0000000000000c0e0','details':{'logicalId':'WindowsInstance'}},
                      {'event':'CREATED','type':'AWS::EC2::Instance','id':'i-0000000000000aea0','details':{'logicalId':'WindowsRemainingInstance'}}]
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
            finalize.cleanup_receipt({**self.raw,'resources':self.raw['resources'][:1]},self.plan,{},self.created)

    def test_a_missing_third_or_unreviewed_lane_instance_is_not_the_reviewed_campaign(self):
        created=self.created+[{'event':'CREATED','type':'AWS::EC2::Instance','id':'i-0000000000000ccc0','details':{'logicalId':'WindowsLane3Instance'}}]
        raw={**self.raw,'resources':self.raw['resources']+[{'type':'AWS::EC2::Instance','id':'i-0000000000000ccc0','finalState':'terminated'}]}
        with self.assertRaisesRegex(ValueError,'one journaled Windows instance per lane'):finalize.cleanup_receipt(raw,self.plan,{},created)
        with self.assertRaisesRegex(ValueError,'one journaled Windows instance per lane'):
            finalize.cleanup_receipt(self.raw,self.plan,{},self.created[:2])
        swapped=[self.created[0],self.created[1],{**self.created[2],'details':{'logicalId':'WindowsInstance'}}]
        with self.assertRaisesRegex(ValueError,'reviewed lane slots'):finalize.cleanup_receipt(self.raw,self.plan,{},swapped)
        # Cleanup must observe both lane instances terminated.
        raw={**self.raw,'resources':self.raw['resources'][:2]}
        with self.assertRaisesRegex(ValueError,'physical ID'):finalize.cleanup_receipt(raw,self.plan,{},self.created)

    def two_lane_state(self,root,mutate=None):
        """Synthetic schema fixtures for both lanes; this does not execute a native game."""
        import builder_journal,builder_namespace as names,desktop_packages,download_formats,release_identity
        from test_desktop_packages import write_desktop
        from test_gpu_release_gates import receipt
        state=root/'state';state.mkdir();packages=root/'packages';packages.mkdir()
        release=release_identity.check_exports();write_desktop(packages,revision=self.plan['payloadSource'],release=release)
        plan={**self.plan,'verificationRunId':'123456','renderFingerprint':'fp','release':release,'artifacts':{}}
        target='windows-x64'
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
        ids={names.CORE_SLOT:'i-0000000000000c0e0',names.REMAINING_SLOT:'i-0000000000000aea0'}
        core_result=json.dumps({'collectionComplete':True,'phase':'core','source':plan['payloadSource'],'target':target,'capabilityPassed':True}).encode()
        high=json.dumps(data['primaryHigh']).encode()
        handoff_bytes=dict(zip(names.HANDOFF_FILES,(core_result,high)))
        def steps(*names_):return [{'name':name,'exitCode':0}for name in names_]
        lanes={names.CORE_SLOT:{'guest-execution.json':{'schemaVersion':1,'source':plan['payloadSource'],'slot':names.CORE_SLOT,'lane':'core',
                                                         'steps':steps('desktop','engine','core'),'complete':True,'errors':[]},
                                **handoff_bytes},
               names.REMAINING_SLOT:{'guest-execution.json':{'schemaVersion':1,'source':plan['payloadSource'],'slot':names.REMAINING_SLOT,'lane':'remaining',
                                                              'steps':steps('desktop','engine','installed-launcher','remaining','preliminary-qualification'),
                                                              'complete':True,'errors':[],'coreHandoff':{}},
                                     **handoff_bytes,
                                     'gpu-release-'+target+'/'+finalize.gates.receipt_name(target):data,
                                     'Blast-Circuit-'+target+'-graphical.json':graph_bytes}}
        raw_manifest='d'*64
        handoff={'schemaVersion':1,'runId':plan['runId'],'account':plan['account'],'region':plan['region'],'source':plan['payloadSource'],
                 'target':target,'coreSlot':names.CORE_SLOT,'coreInstanceId':ids[names.CORE_SLOT],'coreManifestSha256':raw_manifest,
                 'remainingSlot':names.REMAINING_SLOT,'remainingInstanceId':ids[names.REMAINING_SLOT],
                 'files':[{'path':path,'objectKey':names.handoff_object(index),'size':len(handoff_bytes[path]),
                           'sha256':hashlib.sha256(handoff_bytes[path]).hexdigest()}for index,path in enumerate(names.HANDOFF_FILES)]}
        context={'plan':plan,'lanes':lanes,'handoff':handoff,'ids':ids,'rawManifest':raw_manifest}
        if mutate:mutate(context)
        handoff_path=state/'core-handoff.json';handoff_path.write_text(json.dumps(context['handoff']))
        handoff_sha=hashlib.sha256(handoff_path.read_bytes()).hexdigest()
        lanes[names.REMAINING_SLOT]['guest-execution.json']['coreHandoff']={
            'receiptSha256':handoff_sha,'coreInstanceId':context['handoff']['coreInstanceId'],
            'coreManifestSha256':context['handoff']['coreManifestSha256'],**lanes[names.REMAINING_SLOT]['guest-execution.json']['coreHandoff']}
        status={'targets':{},'coreHandoffSha256':handoff_sha}
        for slot,files in lanes.items():
            rows=[]
            for relative,content in files.items():
                content=content if isinstance(content,bytes)else json.dumps(content).encode()
                path=state/'evidence'/slot/'finished'/'finished'/relative;path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(content)
                rows.append({'path':'finished/'+relative,'localPath':'/old/host/no-longer-present/'+relative,'sha256':hashlib.sha256(content).hexdigest()})
            status['targets'][slot]={'state':'finished','instanceId':ids[slot],'retention':{
                'runId':plan['runId'],'source':plan['payloadSource'],'target':target,'slot':slot,'lane':names.SLOTS[slot]['lane'],
                'instanceId':ids[slot],'retained':True,'rawManifestSha256':raw_manifest if slot==names.CORE_SLOT else 'f'*64,'files':rows}}
        if context.get('status'):context['status'](status)
        (state/'plan.json').write_text(json.dumps(plan));(state/'controller.json').write_text(json.dumps(status))
        (state/'cleanup-receipt.json').write_text(json.dumps(self.raw))
        for row in self.created:builder_journal.append(state/'creation-manifest.jsonl',plan,'CREATED',row['type'],row['id'],row['details'])
        return state,plan

    def test_downloaded_state_binds_complete_contract_fixtures_and_cleanup(self):
        with tempfile.TemporaryDirectory()as temporary:
            root=Path(temporary);state,plan=self.two_lane_state(root)
            output=finalize.bind(state,{},root/'bound')
            self.assertTrue((output/finalize.gates.CLEANUP_RECEIPT).is_file())
            for target in plan['artifacts']:
                result=json.loads((output/finalize.gates.receipt_name(target)).read_text())
                self.assertTrue(result['cleanupAudited'])
            # Windows only: the bound set never carries a Linux receipt that could collide with the workstation's.
            self.assertEqual(sorted(path.name for path in output.iterdir()),
                             sorted([finalize.gates.CLEANUP_RECEIPT,'Blast-Circuit-windows-x64-gpu.json','Blast-Circuit-windows-x64-graphical.json']))

    def test_two_lane_binding_refuses_every_missing_or_mismatched_lane_proof(self):
        import builder_namespace as names
        core,lane=names.CORE_SLOT,names.REMAINING_SLOT
        def lanes(change):return lambda context:change(context['lanes'])
        def status(change):return lambda context:context.__setitem__('status',change)
        cases={
            'core lane missing':(status(lambda value:value['targets'].pop(core)),'exactly the reviewed lane slots'),
            'core lane not finished':(status(lambda value:value['targets'][core].__setitem__('state','stopped')),'did not complete'),
            'core step failed':(lanes(lambda value:value[core]['guest-execution.json']['steps'][2].__setitem__('exitCode',1)),'required step'),
            'core execution incomplete':(lanes(lambda value:value[core]['guest-execution.json'].__setitem__('complete',False)),'incomplete'),
            'core collection incomplete':(lanes(lambda value:value[core].__setitem__(names.HANDOFF_FILES[0],b'{"collectionComplete": false}')),'not byte-identical|incomplete'),
            'launcher missing':(lanes(lambda value:value[lane]['guest-execution.json']['steps'].pop(2)),'required step'),
            'remaining step failed':(lanes(lambda value:value[lane]['guest-execution.json']['steps'][3].__setitem__('exitCode',2)),'required step'),
            'gate ran on other core bytes':(lanes(lambda value:value[lane].__setitem__(names.HANDOFF_FILES[1],b'{"other": 1}')),'byte-identical'),
            'handoff names another core instance':(lambda context:context['handoff'].__setitem__('coreInstanceId','i-0000000000000fff0'),'core instance'),
            'handoff names another core manifest':(lambda context:context['handoff'].__setitem__('coreManifestSha256','e'*64),'core instance and manifest'),
            'handoff hash differs from core':(lambda context:context['handoff']['files'][0].__setitem__('sha256','0'*64),'byte-identical'),
            'guest gated another receipt':(lanes(lambda value:value[lane]['guest-execution.json'].__setitem__('coreHandoff',{'receiptSha256':'0'*64})),'exact core handoff'),
            'controller handoff record differs':(status(lambda value:value.__setitem__('coreHandoffSha256','0'*64)),'controller record'),
            'shared instance':(status(lambda value:(value['targets'][lane].__setitem__('instanceId','i-0000000000000c0e0'),
                                                     value['targets'][lane]['retention'].__setitem__('instanceId','i-0000000000000c0e0'))),'journaled stack member|distinct'),
            'retention from another slot':(status(lambda value:value['targets'][lane]['retention'].__setitem__('slot',core)),'another slot'),
        }
        for name,(mutate,message) in cases.items():
            with self.subTest(case=name),tempfile.TemporaryDirectory()as temporary:
                root=Path(temporary);state,_=self.two_lane_state(root,mutate)
                with self.assertRaisesRegex(ValueError,message):finalize.bind(state,{},root/'bound')
                self.assertFalse((root/'bound'/'Blast-Circuit-windows-x64-gpu.json').exists())

    def test_missing_handoff_receipt_never_binds(self):
        with tempfile.TemporaryDirectory()as temporary:
            root=Path(temporary);state,_=self.two_lane_state(root);(state/'core-handoff.json').unlink()
            with self.assertRaisesRegex(ValueError,'handoff receipt is required'):finalize.bind(state,{},root/'bound')


if __name__=='__main__':unittest.main()
