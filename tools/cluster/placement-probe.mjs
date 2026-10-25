import {releaseIdentity} from '../networking/release-identity.mjs';
// Real public admission + owning-node socket. Creates and removes one private room.
import fs from 'node:fs/promises';
const [origin, expected, output, region, game = 'blast-room'] = process.argv.slice(2);
if (!origin || !expected || !output) throw Error('Usage: placement-probe.mjs ORIGIN EXPECTED_SERVER OUTPUT [REGION] [GAME]');
let token, playerId, room, socket, failure, cleaned = false;
async function api(route, body) {
  const response = await fetch(new URL('/realtime/v1/' + route, origin), {
    method: 'POST', headers: { 'X-Blast-Protocol': releaseIdentity.admissionToken, 'Content-Type': 'application/json', ...(token ? { 'x-player-token': token } : {}) },
    body: JSON.stringify(body), signal: AbortSignal.timeout(10000)
  });
  const value = response.status === 204 ? null : await response.json();
  if (!response.ok) throw Error(`HTTP ${response.status}: ${value?.error?.code}`);
  return value;
}
try {
  const guest = await api('guests', { displayName: 'Placement verification' });
  token = guest.token; playerId = guest.playerId;
  const created = await api('lobbies', { game, visibility: 'private', ...(region ? { region } : {}), settings: { hosting: 'server', bots: '0' } });
  room = created.lobby;
  if (room.serverId !== expected) throw Error(`Expected ${expected}, got ${room.serverId}`);
  const url = new URL(created.reservation.connectUrl, origin);
  url.protocol = ['https:', 'wss:'].includes(url.protocol) ? 'wss:' : 'ws:';
  if (origin.startsWith('https:') && url.protocol !== 'wss:') throw Error('Insecure socket route');
  socket = new WebSocket(url, {headers: {'X-Blast-Protocol': releaseIdentity.admissionToken}});
  socket.addEventListener('open', () => socket.send(JSON.stringify({ t: 'peer.hello', d: { version: releaseIdentity.protocolRevision } })));
  await new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(Error('No owning-node welcome')), 10000);
    socket.addEventListener('message', e => { if ((frame => frame.t === (game === 'blast-peer' ? 'party.status' : 'sys.welcome') && (game !== 'blast-peer' || frame.d.requiredOnlineProtocol === releaseIdentity.protocolRevision && frame.d.notice === '' && frame.d.players.some(player => player.connected && player.playerId === playerId)))(JSON.parse(e.data))) { clearTimeout(timer); resolve(); } });
    socket.addEventListener('error', () => { clearTimeout(timer); reject(Error('Socket failed')); });
  });
} catch (error) { failure = error.message; }
finally {
  socket?.close();
  if (room) try {
    await api(`lobbies/${room.lobbyId}/leave`, {});
    // An empty private room still owns capacity until the kernel's empty timeout.
    // Wait for actual retirement so another probe does not consume its last slot.
    const until = Date.now() + 75000;
    while (Date.now() < until) {
      const response = await fetch(new URL(`/realtime/v1/lobbies/${room.lobbyId}`, origin),
        { headers: { 'x-player-token': token }, signal: AbortSignal.timeout(5000) });
      if (response.status === 404) { cleaned = true; break; }
      if (!response.ok) throw Error(`HTTP ${response.status}`);
      if (['closed','ended'].includes((await response.json()).state)) { cleaned = true; break; }
      await new Promise(resolve => setTimeout(resolve, 1000));
    }
    if (!cleaned) throw Error('Room did not retire within its expected empty timeout');
  } catch (error) { failure ??= 'Cleanup: '+error.message; }
  await fs.writeFile(output, JSON.stringify({ passed: !failure, game, server: room?.serverId, roomId: room?.lobbyId,
    cleaned, failure, publicEndpoint: origin }, null, 2));
}
if (failure) { console.error(failure); process.exitCode = 1; }
else console.log('PASS public placement and owning-node socket: '+expected);
