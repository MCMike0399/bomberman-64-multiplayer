import unittest

import builder_namespace as names
import builder_staging as staging

RUN='sancus-builder-20261030-8762d11-1234abcd'


class OwnedStaging(unittest.TestCase):
    def test_private_encrypted_tls_only_bucket_with_expiration(self):
        resources=staging.resources(RUN,'2026-10-30T12:00:00Z')
        bucket=resources['StagingBucket']['Properties']
        self.assertEqual(bucket['BucketName'],RUN+'-'+names.ACCOUNT)
        self.assertTrue(all(bucket['PublicAccessBlockConfiguration'].values()))
        self.assertEqual(bucket['BucketEncryption']['ServerSideEncryptionConfiguration'][0]['ServerSideEncryptionByDefault']['SSEAlgorithm'],'AES256')
        rule=bucket['LifecycleConfiguration']['Rules'][0]
        self.assertEqual(rule['ExpirationInDays'],1)
        self.assertEqual(rule['AbortIncompleteMultipartUpload']['DaysAfterInitiation'],1)
        self.assertEqual({row['Key']for row in bucket['Tags']},{names.RUN_TAG,names.EXPIRY_TAG})
        tls=resources['StagingBucketPolicy']['Properties']['PolicyDocument']['Statement'][0]
        self.assertEqual((tls['Effect'],tls['Principal']),('Deny','*'))
        self.assertEqual(tls['Condition'],{'Bool':{'aws:SecureTransport':'false'}})

    def test_controller_only_uses_exact_dated_namespace(self):
        for row in staging.controller_statements():
            self.assertTrue(row['Resource'].startswith('arn:aws:s3:::sancus-builder-20??????-*'))
            self.assertNotIn('gpu',row['Resource']);self.assertNotIn('blast',row['Resource'])
        self.assertIn('s3:AbortMultipartUpload',staging.OBJECT_ACTIONS)
        self.assertIn('s3:ListBucketVersions',staging.BUCKET_ACTIONS)

    def test_guests_can_read_payload_and_write_results_only_in_this_bucket(self):
        rows=staging.guest_statements(RUN)
        self.assertEqual([row['Action']for row in rows],['s3:GetObject','s3:PutObject'])
        self.assertEqual(rows[0]['Resource'],'arn:aws:s3:::'+staging.bucket_name(RUN)+'/payload/*')
        self.assertEqual(rows[1]['Resource'],'arn:aws:s3:::'+staging.bucket_name(RUN)+'/results/*')

    def test_unrelated_names_or_naive_expiry_cannot_generate_resources(self):
        for value in ('sancus-runner','sancus-builder-ci','blast-gpu-old'):
            with self.subTest(value=value),self.assertRaises(ValueError):staging.bucket_name(value)
        with self.assertRaises(ValueError):staging.resources(RUN,'2026-10-30T12:00:00')


if __name__=='__main__':unittest.main()
