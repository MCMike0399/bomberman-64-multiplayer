from pathlib import Path
import subprocess
import tempfile
import unittest
import zipfile

import builder_payload as payload


class FrozenPayload(unittest.TestCase):
    def test_public_snapshot_preserves_exact_commit_without_local_config_or_history(self):
        with tempfile.TemporaryDirectory()as temporary:
            root=Path(temporary);repo=root/'repo';repo.mkdir()
            def git(*args):return subprocess.check_output(['git',*args],cwd=repo)
            git('init','--quiet');git('config','user.email','fixture@example.invalid');git('config','user.name','Fixture')
            (repo/'public.txt').write_text('old');git('add','.');git('commit','--quiet','-m','old')
            (repo/'public.txt').write_text('current');git('commit','--quiet','-am','current')
            revision=git('rev-parse','HEAD').decode().strip()
            (repo/'ignored-private-note.txt').write_text('MUST NOT TRANSFER')
            git('config','http.extraheader','Secret local header')
            bundle=root/'source.zip';payload.source_bundle(repo,revision,bundle)
            out=root/'guest';payload.unpack_source(bundle,out,revision)
            self.assertEqual((out/'public.txt').read_text(),'current')
            self.assertFalse((out/'ignored-private-note.txt').exists())
            self.assertNotIn('Secret local header',(out/'.git/config').read_text())
            self.assertEqual(subprocess.check_output(['git','rev-list','--count','HEAD'],cwd=out,text=True).strip(),'1')
            self.assertEqual(subprocess.check_output(['git','status','--porcelain'],cwd=out,text=True),'')

    def test_credential_in_decoded_input_is_rejected_without_printing_it(self):
        secret='fake-sensitive-token-1234567890'
        with self.assertRaisesRegex(ValueError,'Credential detected')as failure:payload.reject_credentials(secret.encode(),[secret])
        self.assertNotIn(secret,str(failure.exception))

    def test_controller_aws_credentials_are_also_excluded(self):
        self.assertTrue({'AWS_ACCESS_KEY_ID','AWS_SECRET_ACCESS_KEY','AWS_SESSION_TOKEN'}<=set(payload.TOKEN_KEYS))


if __name__=='__main__':unittest.main()
