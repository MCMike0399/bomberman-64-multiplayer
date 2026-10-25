// Read the checked-out canonical identity, never a server-provided compatibility bypass.
import {execFileSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
const reader = fileURLToPath(new URL('../ci/release_identity.py', import.meta.url));
export const releaseIdentity = JSON.parse(execFileSync('python3', [reader, '--json'], {encoding: 'utf8'}));
