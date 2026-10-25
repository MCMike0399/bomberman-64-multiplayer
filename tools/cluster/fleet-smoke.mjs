// Real public HTTP/socket admission checks. All rooms are private and owned by this run.
import fs from 'node:fs/promises';
import path from 'node:path';
const [origin, output, primaryCountText = '2', fallbackCountText = '2'] = process.argv.slice(2);
if (!origin || !output) throw Error('Usage: node fleet-smoke.mjs ORIGIN OUTPUT PRIMARY_CAP FALLBACK_CAP');
const primaryCount = Number(primaryCountText), fallbackCount = Number(fallbackCountText);
if (![primaryCount, fallbackCount].every(n => Number.isInteger(n) && n > 0 && n <= 32)) throw Error('Invalid capacity');
const rooms = [], checks = [];
let failure;
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
function check(value, label) { if (!value) throw Error(label); checks.push(label); console.log('PASS', label); }
async function api(path, body, token, method = 'POST') {
  const response = await fetch(new URL('/realtime/v1/' + path, origin), {
    method, headers: { 'X-Blast-Protocol': '5', 'Content-Type': 'application/json', ...(token ? { 'x-player-token': token } : {}) },
    body: method === 'GET' ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(10000)
  });
  const data = response.status === 204 ? null : await response.json();
  if (!response.ok) throw Object.assign(Error(`HTTP ${response.status}: ${data?.error?.code}`), { code: data?.error?.code, status: response.status });
  return data;
}
async function connect(reservation) {
  const url = new URL(reservation.connectUrl, origin);
  url.protocol = ['https:', 'wss:'].includes(url.protocol) ? 'wss:' : 'ws:';
  if (origin.startsWith('https:') && url.protocol !== 'wss:') throw Error('Insecure reservation');
  const socket = new WebSocket(url, {headers: {'X-Blast-Protocol': '5'}});
  socket.addEventListener('open', () => socket.send(JSON.stringify({t:'peer.hello',d:{version:5}})));
  await new Promise((resolve, reject) => {
    const timer = setTimeout(() => { socket.close(); reject(Error('Socket welcome timeout')); }, 10000);
    socket.addEventListener('message', event => {
      const frame = JSON.parse(event.data);
      if (frame.t === 'sys.welcome') { clearTimeout(timer); resolve(); }
    });
    socket.addEventListener('error', () => { clearTimeout(timer); reject(Error('Socket failed')); });
  });
  return socket;
}
async function create() {
  const guest = await api('guests', { displayName: 'Fleet check' });
  const created = await api('lobbies', { game: 'blast-room', visibility: 'private', settings: { hosting: 'server', bots: '0' } }, guest.token);
  const room = { id: created.lobby.lobbyId, node: created.lobby.serverId, code: created.lobby.joinCode, peers: [{ token: guest.token }] };
  rooms.push(room); // Own the reservation even if the socket fails.
  room.peers[0].socket = await connect(created.reservation);
  return room;
}
async function leave(room) {
  for (const peer of room.peers) {
    peer.socket?.close();
    await api(`lobbies/${room.id}/leave`, {}, peer.token);
  }
  room.left = true;
}
async function waitRetired(room) {
  const deadline = Date.now() + 75000;
  while (Date.now() < deadline) {
    try {
      const lobby = await api(`lobbies/${room.id}`, undefined, room.peers[0].token, 'GET');
      if (['closed', 'ended'].includes(lobby.state)) { room.retired = true; return; }
    } catch (error) { if (error.status === 404) { room.retired = true; return; } throw error; }
    await delay(250);
  }
  throw Error('Owned room did not retire');
}
try {
  for (let i = 0; i < primaryCount; i++) check((await create()).node === 'blast-pi', `primary room ${i + 1} placed on Pi`);
  for (let i = 0; i < fallbackCount; i++) check((await create()).node === 'blast-divemac', `overflow room ${i + 1} placed on DiveMac`);
  try { await create(); throw Error('Fleet accepted a room above both limits'); }
  catch (error) { check(error.code === 'no_capacity', 'both full nodes refuse excess admission'); }
  const target = rooms.find(r => r.node === 'blast-divemac');
  const guest = await api('guests', { displayName: 'Cross-node guest' });
  const peer = { token: guest.token }; target.peers.push(peer);
  const reservation = await api('lobbies/join', { code: target.code }, guest.token);
  check(new URL(reservation.connectUrl, origin).pathname === '/nodes/divemac/realtime/v1/connect', 'public join returns owning fallback socket route');
  peer.socket = await connect(reservation);
  check(true, 'guest joined fallback room through primary HTTP API');
  const released = rooms[0];
  await leave(released);
  // Wait for the explicit leave to close the running kernel room, not merely a public list to empty.
  let closed = false;
  for (let attempt = 0; attempt < 300; attempt++) {
    try {
      const lobby = await api(`lobbies/${released.id}`, undefined, released.peers[0].token, 'GET');
      if (['closed', 'ended'].includes(lobby.state)) { closed = true; break; }
    } catch (error) { if (error.status === 404) { closed = true; break; } throw error; }
    await delay(250);
  }
  check(closed, 'released primary slot closed');
  check((await create()).node === 'blast-pi', 'available primary wins while fallback still hosts rooms');
} catch (error) { failure = error.message; }
finally {
  for (const room of rooms.filter(r => !r.left)) {
    try { await leave(room); } catch (error) { failure ??= `Cleanup: ${error.message}`; }
  }
  await Promise.all(rooms.map(async room => {
    try { await waitRetired(room); } catch (error) { failure ??= `Retirement: ${error.message}`; }
  }));
  await fs.mkdir(path.dirname(path.resolve(output)), { recursive: true });
  await fs.writeFile(output, JSON.stringify({ passed: !failure, checks, failure, rooms: rooms.map(({ id, node, left, retired }) => ({ id, node, left, retired })) }, null, 2));
}
if (failure) { console.error(failure); process.exitCode = 1; }
