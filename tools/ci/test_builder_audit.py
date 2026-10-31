import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest import mock
import uuid
import zipfile

import builder_audit
import builder_namespace as names
import builder_retained_journals as retained


def empty(service,operation,*arguments):
    if service=='sts':return {'Account':names.ACCOUNT,'Arn':f'arn:aws:sts::{names.ACCOUNT}:assumed-role/{names.CI_ROLE}/test'}
    return {key:[]for key in ('StackSummaries','Reservations','Volumes','NetworkInterfaces','SecurityGroups','Addresses',
            'Snapshots','Images','Roles','InstanceProfiles','Schedules','ScheduleGroups','logGroups','Buckets',
            'Parameters','DocumentIdentifiers','ResourceTagMappingList','KeyPairs')}


class EmptyAudit(unittest.TestCase):
    def test_empty_role_scoped_audit_checks_every_type_without_mutating(self):
        calls=[]
        def aws(*args):calls.append(args);return empty(*args)
        result=builder_audit.audit(aws)
        self.assertTrue(result['passed'])
        self.assertTrue({'CFN','EC2','EBS','ENI','SG','EIP','IAMRoles','IAMProfiles','SchedulerSchedules',
                         'Logs','S3','SSMParameters','SSMDocuments','TaggedResources'}<=set(result['checked']))
        self.assertEqual(result['resourcesCreated'],[])
        self.assertTrue(all(args[1].startswith(('get-','list-','describe-'))for args in calls))

    def test_missing_permissions_do_not_mean_empty_and_other_reads_continue(self):
        calls=[]
        def aws(*args):
            calls.append(args)
            if args[:2]==('logs','describe-log-groups'):raise RuntimeError('AccessDenied')
            return empty(*args)
        result=builder_audit.audit(aws)
        self.assertFalse(result['passed']);self.assertEqual(result['checked']['Logs']['state'],'unverified')
        self.assertIn(('s3api','list-buckets'),calls)

    def test_unexpected_builder_resource_is_retained_not_deleted(self):
        def aws(*args):
            result=empty(*args)
            if args[:2]==('iam','list-roles'):result['Roles']=[{'RoleName':names.CI_ROLE},{'RoleName':'sancus-builder-20261030-other'}]
            return result
        result=builder_audit.audit(aws)
        self.assertFalse(result['passed'])
        self.assertEqual(result['leftovers'],[{'type':'IAMRoles','id':'sancus-builder-20261030-other'}])
        self.assertEqual(result['resourcesDeleted'],[])

    def test_wrong_account_or_operator_credentials_are_not_oidc_proof(self):
        for arn in ('arn:aws:iam::524329886851:user/operator','arn:aws:sts::111111111111:assumed-role/sancus-builder-ci/a'):
            def aws(*args):return {'Account':names.ACCOUNT,'Arn':arn}
            with self.subTest(arn=arn),self.assertRaises(RuntimeError):builder_audit.audit(aws)

    def test_only_exact_standing_identity_is_excluded_from_tag_sweep(self):
        self.assertTrue(builder_audit.expected_standing(f'arn:aws:iam::{names.ACCOUNT}:role/sancus-builder-ci'))
        self.assertFalse(builder_audit.expected_standing(f'arn:aws:iam::{names.ACCOUNT}:role/sancus-builder-ci-other'))



ARN=f'arn:aws:{{}}:{names.REGION}:{names.ACCOUNT}:{{}}'


def missing(code):
    return RuntimeError(f'ec2/describe: An error occurred ({code}) when calling the operation: not found')


class TaggingIndexResolution(unittest.TestCase):
    """A tagging-API hit is cleared only by an authoritative gone answer."""
    def sweep(self,arn,answer,journals=None):
        calls=[]
        def aws(*args):
            calls.append(args)
            if args[:2]==('resourcegroupstaggingapi','get-resources'):
                return {'ResourceTagMappingList':[{'ResourceARN':arn}]}
            if args[0]in ('sts',)or args[1]in ('list-stacks','list-roles','list-instance-profiles','list-schedules',
                    'list-schedule-groups','describe-log-groups','list-buckets','describe-parameters','list-documents',
                    'describe-snapshots','describe-images','describe-addresses','describe-key-pairs')or '--filters'in args:
                return empty(*args)
            if isinstance(answer,Exception):raise answer
            return answer
        result=builder_audit.audit(aws,journals=journals)
        self.assertTrue(all(args[1].startswith(('get-','list-','describe-','head-'))for args in calls))
        self.assertEqual(len(result['taggedResources']),1)
        row=result['taggedResources'][0]
        self.assertEqual((row['id'],row['taggingIndex']),(arn,'present'))
        return result,row

    def resolved(self,arn,answer,journals=None):
        result,row=self.sweep(arn,answer,journals)
        self.assertTrue(row['resolved'],row);self.assertTrue(result['passed']);self.assertEqual(result['leftovers'],[])
        return row

    def leftover(self,arn,answer,journals=None):
        result,row=self.sweep(arn,answer,journals)
        self.assertFalse(row['resolved'],row);self.assertFalse(result['passed'])
        self.assertEqual(result['leftovers'],[{'type':'TaggedResources','id':arn}])
        return row

    def instance(self,state):return {'Reservations':[{'Instances':[{'InstanceId':'i-0aa4cc1ac8162d4e5','State':{'Name':state}}]}]}

    def test_terminated_instance_resolves(self):
        row=self.resolved(ARN.format('ec2','instance/i-0aa4cc1ac8162d4e5'),self.instance('terminated'))
        self.assertEqual(row['authoritative'],'terminated')

    def test_not_found_resources_resolve(self):
        for resource,code in (('instance/i-0aa4cc1ac8162d4e5','InvalidInstanceID.NotFound'),('volume/vol-041eca3f76f58e996','InvalidVolume.NotFound'),
                              ('network-interface/eni-01','InvalidNetworkInterfaceID.NotFound'),('security-group/sg-01','InvalidGroup.NotFound')):
            with self.subTest(resource=resource):
                self.assertEqual(self.resolved(ARN.format('ec2',resource),missing(code))['authoritative'],code)
        run='sancus-builder-20261030-454da3d-0123456789abcdef'
        for arn,code in ((f'arn:aws:iam::{names.ACCOUNT}:role/{run}-agent','NoSuchEntity'),
                         (f'arn:aws:iam::{names.ACCOUNT}:instance-profile/{run}-profile','NoSuchEntity'),
                         (ARN.format('scheduler',f'schedule/{run}-expiry-group/{run}-linux-expiry'),'ResourceNotFoundException'),
                         (ARN.format('scheduler',f'schedule-group/{run}-expiry-group'),'ResourceNotFoundException')):
            with self.subTest(arn=arn):self.resolved(arn,missing(code))
        from builder_s3 import ApiError
        for code in ('404','NoSuchBucket'):
            with self.subTest(code=code):self.resolved('arn:aws:s3:::'+run,ApiError(code))
        stack=ARN.format('cloudformation',f'stack/{run}/0123')
        self.resolved(stack,{'Stacks':[{'StackStatus':'DELETE_COMPLETE'}]})
        self.resolved(stack,RuntimeError('An error occurred (ValidationError) when calling the DescribeStacks operation: Stack with id x does not exist'))

    def test_shutting_down_or_existing_resources_stay_leftovers(self):
        self.assertEqual(self.leftover(ARN.format('ec2','instance/i-0aa4cc1ac8162d4e5'),self.instance('shutting-down'))['authoritative'],'shutting-down')
        self.leftover(ARN.format('ec2','volume/vol-041eca3f76f58e996'),{'Volumes':[{'VolumeId':'vol-041eca3f76f58e996','State':'available'}]})
        self.leftover(ARN.format('cloudformation','stack/sancus-builder-20261030-x/1'),{'Stacks':[{'StackStatus':'DELETE_FAILED'}]})
        self.leftover('arn:aws:s3:::sancus-builder-20261030-x',{})

    def test_access_denied_or_other_errors_stay_leftovers(self):
        from builder_s3 import ApiError
        for arn,error in ((ARN.format('ec2','instance/i-1'),missing('UnauthorizedOperation')),
                          (ARN.format('ec2','volume/vol-1'),RuntimeError('timeout')),
                          ('arn:aws:s3:::sancus-builder-20261030-x',ApiError('403')),
                          ('arn:aws:s3:::sancus-builder-20261030-x',ApiError('AccessDenied')),
                          (f'arn:aws:iam::{names.ACCOUNT}:role/sancus-builder-20261030-x',missing('AccessDenied')),
                          (ARN.format('cloudformation','stack/sancus-builder-20261030-x/1'),missing('ValidationError'))):
            with self.subTest(arn=arn,error=str(error)):
                row=self.leftover(arn,error);self.assertEqual(row['authoritative'],'describe-error')

    def test_unknown_type_or_foreign_scope_stays_leftover(self):
        for arn in (ARN.format('logs','log-group:sancus-builder-20261030-x'),ARN.format('ec2','snapshot/snap-1'),
                    'arn:aws:ec2:eu-west-1:'+names.ACCOUNT+':instance/i-1','arn:aws:ec2:us-east-1:111111111111:instance/i-1','not-an-arn'):
            with self.subTest(arn=arn):self.leftover(arn,self.instance('terminated'))


# Minimal rows copied verbatim from GitHub artifact builder-state-41151908001-1-final
# (creation-manifest.jsonl lines 11, 15, 93-96) and its cleanup-receipt.json.
RUN_41151908001_ROWS=r'''{"schemaVersion": 1, "eventId": "ef19323e-20f6-4f4b-a702-b818c46f5172", "timestampUtc": "2026-10-31T00:52:52.221132+00:00", "event": "CREATED", "account": "524329886851", "region": "us-east-1", "runId": "sancus-builder-20261031-454da3d-8830291184ff347f", "type": "AWS::EC2::Instance", "id": "i-099e34e90f667045f", "parent": "arn:aws:cloudformation:us-east-1:524329886851:stack/sancus-builder-20261031-454da3d-8830291184ff347f/f25d37f0-d4c4-11f1-ac1f-0e9bacc4e46d", "dependencies": [], "details": {"logicalId": "LinuxInstance", "cfnStackId": "arn:aws:cloudformation:us-east-1:524329886851:stack/sancus-builder-20261031-454da3d-8830291184ff347f/f25d37f0-d4c4-11f1-ac1f-0e9bacc4e46d"}}
{"schemaVersion": 1, "eventId": "c90a9224-6c9e-4730-8aa4-94887302d1c5", "timestampUtc": "2026-10-31T00:53:00.000794+00:00", "event": "CREATED", "account": "524329886851", "region": "us-east-1", "runId": "sancus-builder-20261031-454da3d-8830291184ff347f", "type": "AWS::EC2::Instance", "id": "i-0aa4cc1ac8162d4e5", "parent": "arn:aws:cloudformation:us-east-1:524329886851:stack/sancus-builder-20261031-454da3d-8830291184ff347f/f25d37f0-d4c4-11f1-ac1f-0e9bacc4e46d", "dependencies": [], "details": {"logicalId": "WindowsInstance", "cfnStackId": "arn:aws:cloudformation:us-east-1:524329886851:stack/sancus-builder-20261031-454da3d-8830291184ff347f/f25d37f0-d4c4-11f1-ac1f-0e9bacc4e46d"}}
{"schemaVersion": 1, "eventId": "4e433adb-3ad9-4eb2-a55d-b131d8acc17a", "timestampUtc": "2026-10-31T01:13:07.556876+00:00", "event": "STATUS", "account": "524329886851", "region": "us-east-1", "runId": "sancus-builder-20261031-454da3d-8830291184ff347f", "type": "AWS::EC2::Instance", "id": "i-099e34e90f667045f", "parent": null, "dependencies": [], "details": {"terminatedObservedAtUtc": "2026-10-31T01:13:07.556839+00:00"}}
{"schemaVersion": 1, "eventId": "85fe83a0-b58c-4fc0-a89b-91dd92b934de", "timestampUtc": "2026-10-31T01:16:03.098071+00:00", "event": "STATUS", "account": "524329886851", "region": "us-east-1", "runId": "sancus-builder-20261031-454da3d-8830291184ff347f", "type": "AWS::EC2::Instance", "id": "i-0aa4cc1ac8162d4e5", "parent": null, "dependencies": [], "details": {"terminatedObservedAtUtc": "2026-10-31T01:16:03.098035+00:00"}}
{"schemaVersion": 1, "eventId": "ce602e37-c6ac-4bce-a3d2-1d5e942b1932", "timestampUtc": "2026-10-31T01:16:18.612295+00:00", "event": "STATUS", "account": "524329886851", "region": "us-east-1", "runId": "sancus-builder-20261031-454da3d-8830291184ff347f", "type": "AWS::EC2::Instance", "id": "i-099e34e90f667045f", "parent": null, "dependencies": [], "details": {"terminatedObservedAtUtc": "2026-10-31T01:16:18.612259+00:00"}}
{"schemaVersion": 1, "eventId": "a5079319-1648-4a1e-8367-b1a3573e66df", "timestampUtc": "2026-10-31T01:16:19.633384+00:00", "event": "STATUS", "account": "524329886851", "region": "us-east-1", "runId": "sancus-builder-20261031-454da3d-8830291184ff347f", "type": "AWS::EC2::Instance", "id": "i-0aa4cc1ac8162d4e5", "parent": null, "dependencies": [], "details": {"terminatedObservedAtUtc": "2026-10-31T01:16:19.633347+00:00"}}'''
RUN_41151908001_RECEIPT=json.dumps({'account':names.ACCOUNT,'region':names.REGION,'runId':'sancus-builder-20261031-454da3d-8830291184ff347f',
    'observedAtUtc':'2026-10-31T01:20:14.595470+00:00','resources':[{'type':'AWS::EC2::Instance','id':'i-099e34e90f667045f','finalState':'terminated'},
    {'type':'AWS::EC2::Instance','id':'i-0aa4cc1ac8162d4e5','finalState':'terminated'}]})
RUN='sancus-builder-20261030-454da3d-0123456789abcdef'
EMPTY={'Reservations':[]}


def row(event,identity,details,minute,kind='AWS::EC2::Instance'):
    return json.dumps({'schemaVersion':1,'eventId':str(uuid.uuid4()),'timestampUtc':f'2026-10-30T01:{minute:02d}:00+00:00','event':event,
                       'account':names.ACCOUNT,'region':names.REGION,'runId':RUN,'type':kind,'id':identity,'parent':None,
                       'dependencies':[],'details':details})


class Source:
    name='fake'
    def __init__(self,*records,error=None):self.items=list(records);self.error=error;self.errors=[]
    def records(self):
        if self.error:raise self.error
        return self.items


def journal(*rows,receipt=None,reference=None):
    return retained.Journals(Source({'reference':reference or {'workflowRunId':1,'artifactName':'builder-state-1-1-final'},
                                     'journal':'\n'.join(rows),'receipt':receipt}))


def artifact_zip(files):
    data=io.BytesIO()
    with zipfile.ZipFile(data,'w')as archive:
        for name,text in files.items():archive.writestr(name,text)
    return data.getvalue()


def github(artifacts,downloads,fail=None):
    """Injected read-only GitHub API: run list, artifact lists and zip downloads."""
    calls=[]
    def api(path,binary=False):
        calls.append(path)
        if fail and fail in path:raise RuntimeError('HTTP 502 from GitHub')
        if path.endswith('/runs?per_page=30'):return {'workflow_runs':[{'id':run}for run in artifacts]}
        if '/artifacts?'in path:return {'artifacts':artifacts[int(path.split('/runs/')[1].split('/')[0])]}
        return downloads[int(path.split('/artifacts/')[1].split('/')[0])]
    return retained.GitHubJournals(api=api),calls


class AgedOutInstances(unittest.TestCase):
    """Empty exact-ID Reservations resolve only with retained journal proof."""
    sweep,resolved,leftover,instance=(TaggingIndexResolution.sweep,TaggingIndexResolution.resolved,
                                      TaggingIndexResolution.leftover,TaggingIndexResolution.instance)
    INSTANCE='i-0aa4cc1ac8162d4e5'
    ARN=ARN.format('ec2','instance/i-0aa4cc1ac8162d4e5')
    CREATED=row('CREATED',INSTANCE,{'logicalId':'WindowsInstance'},1)
    TERMINATED=row('STATUS',INSTANCE,{'terminatedObservedAtUtc':'2026-10-30T01:20:00+00:00'},20)

    def test_a_empty_reservations_created_and_observed_terminated_is_aged_out(self):
        row=self.resolved(self.ARN,EMPTY,journal(self.CREATED,self.TERMINATED))
        self.assertEqual((row['authoritative'],row['describeShape']),('AGED_OUT','describe-instances: empty Reservations, no error'))
        evidence=row['retainedJournal']
        self.assertEqual(evidence['journal'],{'workflowRunId':1,'artifactName':'builder-state-1-1-final'})
        self.assertEqual(evidence['builderRunId'],RUN)
        self.assertEqual(evidence['created']['eventId'],json.loads(self.CREATED)['eventId'])
        self.assertEqual(evidence['observedTermination'][0]['eventId'],json.loads(self.TERMINATED)['eventId'])

    def test_a_terminate_request_then_observed_shutting_down_or_receipt_is_aged_out(self):
        request=row('STATUS',self.INSTANCE,{'terminationRequestedAtUtc':'2026-10-30T01:10:00+00:00'},10)
        down=row('STATUS',self.INSTANCE,{'state':'shutting-down'},11)
        self.assertEqual(self.resolved(self.ARN,EMPTY,journal(self.CREATED,request,down))['authoritative'],'AGED_OUT')
        receipt=json.dumps({'account':names.ACCOUNT,'region':names.REGION,'runId':RUN,'resources':[{'type':'AWS::EC2::Instance','id':self.INSTANCE,'finalState':'absent'}]})
        self.assertEqual(self.resolved(self.ARN,EMPTY,journal(self.CREATED,receipt=receipt))['authoritative'],'AGED_OUT')

    def test_b_empty_reservations_without_journal_entry_stay_leftover(self):
        other=row('CREATED','i-0000000000000000a',{},1)
        for journals in (journal(other,row('STATUS','i-0000000000000000a',{'state':'terminated'},2)),retained.Journals()):
            with self.subTest():
                row_=self.leftover(self.ARN,EMPTY,journals)
                self.assertEqual(row_['authoritative'],'AMBIGUOUS:');self.assertNotIn('retainedJournal',row_)
        # The STATUS alone, without a CREATED row from that run, is not ownership.
        self.leftover(self.ARN,EMPTY,journal(self.TERMINATED))

    def test_c_created_without_observed_termination_stays_leftover(self):
        running=row('STATUS',self.INSTANCE,{'state':'running'},2)
        request=row('STATUS',self.INSTANCE,{'terminationRequestedAtUtc':'2026-10-30T01:10:00+00:00'},10)
        early=row('STATUS',self.INSTANCE,{'state':'shutting-down'},5)
        wrong_run=json.dumps({'account':names.ACCOUNT,'region':names.REGION,'runId':'sancus-builder-20261030-454da3d-ffffffffffffffff',
                              'resources':[{'type':'AWS::EC2::Instance','id':self.INSTANCE,'finalState':'terminated'}]})
        unclean=json.dumps({'account':names.ACCOUNT,'region':names.REGION,'runId':RUN,'resources':[{'type':'AWS::EC2::Instance','id':self.INSTANCE,'finalState':'running'}]})
        for journals in (journal(self.CREATED),journal(self.CREATED,running),journal(self.CREATED,request),
                         journal(self.CREATED,early,request),journal(self.CREATED,receipt=wrong_run),journal(self.CREATED,receipt=unclean)):
            with self.subTest():self.assertEqual(self.leftover(self.ARN,EMPTY,journals)['authoritative'],'AMBIGUOUS:')

    def test_d_journal_unavailable_unreadable_or_fetch_error_stays_leftover(self):
        good=artifact_zip({'creation-manifest.jsonl':'\n'.join((self.CREATED,self.TERMINATED))})
        listing={7:[{'id':70,'name':'builder-state-7-1-final','size_in_bytes':len(good),'expired':False}]}
        for fail in ('/runs?per_page','/runs/7/artifacts','/artifacts/70/zip'):
            with self.subTest(fail=fail):
                source,_=github(listing,{70:good},fail)
                row_=self.leftover(self.ARN,EMPTY,retained.Journals(source));self.assertTrue(row_['journalLookup']['errors'])
        expired={7:[{**listing[7][0],'expired':True}]}
        oversized={7:[{**listing[7][0],'size_in_bytes':retained.MAX_ARTIFACT_BYTES+1}]}
        for artifacts,downloads in ((expired,{70:good}),(oversized,{70:good}),(listing,{70:b'not a zip'}),
                                    (listing,{70:artifact_zip({'creation-manifest.jsonl':self.CREATED+'\n{bad json'})}),
                                    (listing,{70:artifact_zip({'other.json':'{}'})})):
            with self.subTest():self.leftover(self.ARN,EMPTY,retained.Journals(github(artifacts,downloads)[0]))
        self.leftover(self.ARN,EMPTY,retained.Journals(Source(error=RuntimeError('journal store down'))))
        foreign=self.CREATED.replace(names.ACCOUNT,'111111111111')
        self.leftover(self.ARN,EMPTY,journal(foreign,self.TERMINATED.replace(names.ACCOUNT,'111111111111')))

    def test_d_default_without_token_is_unavailable_not_resolved(self):
        with mock.patch.dict(os.environ,{},clear=True),mock.patch.object(retained,'_DEFAULT',None):
            row_=self.leftover(self.ARN,EMPTY)
        self.assertIn('GH_TOKEN unavailable',json.dumps(row_['journalLookup']))

    def test_e_describe_errors_other_than_not_found_stay_leftover_even_with_journal(self):
        proof=journal(self.CREATED,self.TERMINATED)
        for error in (missing('UnauthorizedOperation'),missing('InvalidInstanceID.Malformed'),RuntimeError('timeout')):
            with self.subTest(error=str(error)):self.assertEqual(self.leftover(self.ARN,error,proof)['authoritative'],'describe-error')

    def test_f_terminated_still_resolves_without_journal(self):
        self.assertEqual(self.resolved(self.ARN,self.instance('terminated'),retained.Journals())['authoritative'],'terminated')

    def test_g_shutting_down_or_other_reservations_stay_leftover_even_with_journal(self):
        proof=journal(self.CREATED,self.TERMINATED)
        self.assertEqual(self.leftover(self.ARN,self.instance('shutting-down'),proof)['authoritative'],'shutting-down')
        two={'Reservations':self.instance('terminated')['Reservations']*2}
        for answer in (two,{'Reservations':[{'Instances':[]}]},{}):
            with self.subTest(answer=answer):self.assertTrue(self.leftover(self.ARN,answer,proof)['authoritative'].startswith('AMBIGUOUS'))

    def test_h_not_found_still_resolves(self):
        self.assertEqual(self.resolved(self.ARN,missing('InvalidInstanceID.NotFound'),retained.Journals())['authoritative'],'InvalidInstanceID.NotFound')

    def test_i_real_run_41151908001_journal_resolves_both_instances_via_github(self):
        final=artifact_zip({'creation-manifest.jsonl':RUN_41151908001_ROWS,'cleanup-receipt.json':RUN_41151908001_RECEIPT})
        stopped=artifact_zip({'creation-manifest.jsonl':RUN_41151908001_ROWS.splitlines()[0]})
        listing={41151908001:[{'id':5001,'name':'builder-stopped-41151908001-1','size_in_bytes':len(stopped),'expired':False},
                              {'id':5002,'name':'builder-state-41151908001-1-final','size_in_bytes':len(final),'expired':False,'digest':'sha256:x'},
                              {'id':5003,'name':'builder-evidence-41151908001-1','size_in_bytes':10,'expired':False}]}
        source,calls=github(listing,{5001:stopped,5002:final})
        journals=retained.Journals(source)
        for instance,created,observed in (('i-0aa4cc1ac8162d4e5','c90a9224-6c9e-4730-8aa4-94887302d1c5','85fe83a0-b58c-4fc0-a89b-91dd92b934de'),
                                          ('i-099e34e90f667045f','ef19323e-20f6-4f4b-a702-b818c46f5172','4e433adb-3ad9-4eb2-a55d-b131d8acc17a')):
            with self.subTest(instance=instance):
                row_=self.resolved(ARN.format('ec2','instance/'+instance),EMPTY,journals)
                evidence=row_['retainedJournal']
                self.assertEqual((row_['authoritative'],evidence['journal']['workflowRunId'],evidence['journal']['artifactName'],evidence['journal']['artifactId']),
                                 ('AGED_OUT',41151908001,'builder-state-41151908001-1-final',5002))
                self.assertEqual(evidence['created']['eventId'],created)
                self.assertEqual(evidence['observedTermination'][0]['eventId'],observed)
                self.assertIn({'source':'cleanup-receipt resources','finalState':'terminated','receiptObservedAtUtc':'2026-10-31T01:20:14.595470+00:00'},
                              evidence['observedTermination'])
        # Only listed builder journal artifacts are downloaded, once per process.
        self.assertEqual(sorted(path for path in calls if path.endswith('/zip')),
                         ['repos/MCMike0399/bomberman-64-multiplayer/actions/artifacts/5001/zip','repos/MCMike0399/bomberman-64-multiplayer/actions/artifacts/5002/zip'])

    def test_own_run_local_journal_serves_its_final_sweep(self):
        with tempfile.TemporaryDirectory()as root:
            Path(root,'creation-manifest.jsonl').write_text(self.CREATED+'\n'+self.TERMINATED+'\n')
            journals=retained.Journals(retained.LocalJournal(root,{'workflowRunId':'9','artifactName':'builder-state-9-1-final (pending)'}))
            row_=self.resolved(self.ARN,EMPTY,journals)
        self.assertEqual(row_['retainedJournal']['journal']['workflowRunId'],'9')
        with tempfile.TemporaryDirectory()as root:
            self.leftover(self.ARN,EMPTY,retained.Journals(retained.LocalJournal(root,{})))

    def test_cleanup_final_sweep_reads_this_runs_state_directory_first(self):
        import builder_cleanup
        with (tempfile.TemporaryDirectory()as root,mock.patch.object(builder_cleanup,'ROOT',Path(root)),
              mock.patch.dict(os.environ,{'GITHUB_RUN_ID':'41','GITHUB_RUN_ATTEMPT':'2'},clear=True),mock.patch.object(retained,'_DEFAULT',None)):
            Path(root,'creation-manifest.jsonl').write_text(self.CREATED+'\n'+self.TERMINATED+'\n')
            row_=self.resolved(self.ARN,EMPTY,builder_cleanup.own_journals())
        self.assertEqual((row_['retainedJournal']['journal']['workflowRunId'],row_['retainedJournal']['journal']['artifactName']),
                         ('41','builder-state-41-2-final (uploaded after this sweep)'))


if __name__=='__main__':unittest.main()
