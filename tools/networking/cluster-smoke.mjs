// Bounded public-cluster admission/cancellation probe. Owns only the rooms it creates.
import fs from 'node:fs/promises';
const [origin, output] = process.argv.slice(2);
if (!origin || !output) throw Error('Usage: cluster-smoke.mjs ORIGIN OUTPUT');
const owned = [];
let queued, failure;
async function api(player, method, path, body) {
  const response = await fetch(new URL('/realtime/v1/' + path, origin), {
    method, headers: { 'Content-Type': 'application/json', ...(player ? { 'x-player-token': player.token } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(10000)
  });
  const value = response.status === 204 ? null : await response.json();
  if (!response.ok) throw Error(`HTTP ${response.status}: ${value?.error?.code}`);
  return value;
}
try {
  for (let index = 0; index < 3; index++) {
    const player = await api(null, 'POST', 'guests', { displayName: 'Peer admission probe' });
    const created = await api(player, 'POST', 'lobbies', { game: 'blast-peer', visibility: 'private', settings: { bots: '0' } });
    owned.push({ player, lobby: created.lobby });
  }
  const servers = owned.map(room => room.lobby.serverId).sort();
  if (servers.join(',') !== 'blast-divemac,blast-pi,blast-pi') throw Error('Unexpected primary/fallback placement');
  const player = await api(null, 'POST', 'guests', { displayName: 'Cancelled queue probe' });
  const ticket = await api(player, 'POST', 'queue', { game: 'blast-lobby' });
  queued = { player, id: ticket.ticketId };
  const waiting = await api(player, 'GET', `queue/${ticket.ticketId}?waitSeconds=3`);
  if (waiting.state !== 'queued') throw Error('Full cluster did not retain a waiting ticket');
  await api(player, 'DELETE', `queue/${ticket.ticketId}`);
  if ((await api(player, 'GET', `queue/${ticket.ticketId}`)).state !== 'cancelled') throw Error('Queue cancellation did not settle');
  if ((await api(player, 'GET', 'me')).lobbyId) throw Error('Cancelled player retained a lobby seat');
} catch (error) { failure = error.message; }
finally {
  if (queued) try { await api(queued.player, 'DELETE', `queue/${queued.id}`); } catch { }
  for (const room of owned) try { await api(room.player, 'POST', `lobbies/${room.lobby.lobbyId}/leave`, {}); }
  catch (error) { failure ??= 'Seat cleanup: ' + error.message; }
  const deadline = Date.now() + 75000;
  const retired = new Set();
  while (retired.size < owned.length && Date.now() < deadline) {
    for (const room of owned) {
      if (retired.has(room.lobby.lobbyId)) continue;
      const response = await fetch(new URL(`/realtime/v1/lobbies/${room.lobby.lobbyId}`, origin), {
        headers: { 'x-player-token': room.player.token }, signal: AbortSignal.timeout(5000)
      });
      if (response.status === 404 || (response.ok && ['ended', 'closed'].includes((await response.json()).state))) retired.add(room.lobby.lobbyId);
    }
    if (retired.size < owned.length) await new Promise(resolve => setTimeout(resolve, 1000));
  }
  if (retired.size !== owned.length) failure ??= 'Owned rooms did not retire';
  await fs.writeFile(output, JSON.stringify({ passed: !failure, publicEndpoint: origin,
    placement: owned.map(room => room.lobby.serverId), cancelledAtCapacity: !failure,
    retired: retired.size, failure }, null, 2));
}
if (failure) { console.error(failure); process.exitCode = 1; }
else console.log('PASS public primary/fallback placement, full-capacity queue cancellation and room retirement');
