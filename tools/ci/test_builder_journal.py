import copy, unittest
import builder_journal as safety

class SafetyChecks(unittest.TestCase):
    def setUp(self):
        run='sancus-builder-20261030-aaaaaaaa-1234567890abcdef'
        self.plan={'account':'123456789012','region':'us-east-1','runId':run}
        self.arn='arn:aws:cloudformation:us-east-1:123456789012:stack/'+run+'/uuid'
        self.template={'Resources':{'OnlyOwn':{}}};self.params={'RunId':run}
        self.rows=[{'event':'CREATED','type':'AWS::CloudFormation::Stack','id':self.arn,'details':{'templateSha256':safety.canonical_hash(self.template),'parameters':copy.deepcopy(self.params)}},
                   {'event':'CREATED','type':'AWS::EC2::Instance','id':'i-own','parent':self.arn,'details':{}},
                   {'event':'CREATED','type':'AWS::IAM::Role','id':'role-own','parent':self.arn,'details':{'uniqueId':'unique-role-id'}}]
        self.args=[self.plan,self.rows,{'Account':'123456789012'},{'StackId':self.arn,'Tags':{'sancus-builder:run':run}},self.template,self.params,[('AWS::EC2::Instance','i-own'),('AWS::IAM::Role','role-own')],{'i-protected'},{('AWS::IAM::Role','role-own'):'unique-role-id'}]
    def test_exact_owned_stack(self):self.assertEqual(safety.authorize_stack_deletion(*self.args),self.arn)
    def test_wrong_account_region_or_nonce(self):
        for index,key,value in [(2,'Account','wrong'),(0,'region','wrong'),(0,'runId','wrong')]:
            args=copy.deepcopy(self.args);args[index][key]=value
            with self.assertRaises(ValueError):safety.authorize_stack_deletion(*args)
    def test_existing_reference_never_authorizes_deletion(self):
        self.args[6].append(('AWS::EC2::Instance','i-protected'))
        with self.assertRaisesRegex(ValueError,'PROTECTED'):safety.authorize_stack_deletion(*self.args)
    def test_matching_name_or_tag_does_not_replace_created_id(self):
        self.args[6].append(('AWS::EC2::Instance','i-unique-lookalike'))
        with self.assertRaisesRegex(ValueError,'Unjournaled'):safety.authorize_stack_deletion(*self.args)
    def test_replaced_iam_unique_id(self):
        self.args[8][('AWS::IAM::Role','role-own')]='replacement'
        with self.assertRaisesRegex(ValueError,'IAM identity'):safety.authorize_stack_deletion(*self.args)
    def test_changed_template_or_parameters(self):
        for index in (4,5):
            args=copy.deepcopy(self.args);args[index]['unexpected']='change'
            with self.assertRaises(ValueError):safety.authorize_stack_deletion(*args)
    def test_partial_creation_requires_recording_returned_members(self):
        self.args[1].pop()
        with self.assertRaisesRegex(ValueError,'Unjournaled'):safety.authorize_stack_deletion(*self.args)
    def test_already_missing_member_does_not_widen_delete_scope(self):
        self.args[6]=[];self.args[8]={}
        self.assertEqual(safety.authorize_stack_deletion(*self.args),self.arn)

    def test_bucket_and_policy_with_same_physical_name_are_distinct(self):
        rows=[{'event':'CREATED','type':kind,'id':'same-bucket','details':{}}for kind in ('AWS::S3::Bucket','AWS::S3::BucketPolicy')]
        self.assertEqual(len(safety.effective_created(rows)),2)

    def test_wrong_parent_cannot_authorize_cleanup(self):
        self.rows[1]['parent']='another-stack'
        with self.assertRaisesRegex(ValueError,'parent'):safety.authorize_stack_deletion(*self.args)

if __name__=='__main__':unittest.main()
