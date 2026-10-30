"""Keep short-lived OIDC profile files outside all campaign/artifact directories."""
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import signal


def run(command):
    if not command:raise ValueError('A hosted phase command is required')
    keys={'aws_access_key_id':'AWS_ACCESS_KEY_ID','aws_secret_access_key':'AWS_SECRET_ACCESS_KEY','aws_session_token':'AWS_SESSION_TOKEN'}
    if any(not os.environ.get(value)for value in keys.values()):raise ValueError('Short-lived OIDC credentials are missing')
    with tempfile.TemporaryDirectory(prefix='builder-credentials-',dir=os.environ.get('RUNNER_TEMP'))as temporary:
        root=Path(temporary);root.chmod(0o700)
        credentials=root/'credentials';config=root/'config'
        credentials.write_text('[sancus]\n'+''.join(key+'='+os.environ[value]+'\n'for key,value in keys.items()))
        config.write_text('[profile sancus]\nregion=us-east-1\n')
        credentials.chmod(0o600);config.chmod(0o600)
        environment={**os.environ,'AWS_SHARED_CREDENTIALS_FILE':str(credentials),'AWS_CONFIG_FILE':str(config)}
        process=subprocess.Popen(command,env=environment)
        previous={number:signal.getsignal(number)for number in (signal.SIGINT,signal.SIGTERM)}
        def forward(number,frame):
            if process.poll()is None:
                try:process.send_signal(number)
                except ProcessLookupError:pass
        try:
            for number in previous:signal.signal(number,forward)
            return process.wait()
        finally:
            for number,handler in previous.items():signal.signal(number,handler)


if __name__=='__main__':
    command=sys.argv[1:]
    if command[:1]==['--']:command=command[1:]
    raise SystemExit(run(command))
