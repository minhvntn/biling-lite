import { io } from 'socket.io-client';

const serverUrl = process.env.SERVER_URL ?? 'http://localhost:9000';
const agentId = process.env.AGENT_ID ?? 'PC-001';
const hostname = process.env.AGENT_HOSTNAME ?? `${agentId}-MOCK`;
const heartbeatSeconds = Number(process.env.HEARTBEAT_SECONDS ?? '10');
const ackDelayMs = Number(process.env.ACK_DELAY_MS ?? '250');
const forceFail = process.env.FORCE_FAIL === '1';

let machineState = 'LOCKED';

const socket = io(`${serverUrl}/billing`, {
  transports: ['websocket'],
  reconnection: true,
});

function nowIso() {
  return new Date().toISOString();
}

function log(message, payload) {
  if (payload === undefined) {
    console.log(`[${new Date().toLocaleTimeString()}] ${message}`);
    return;
  }

  console.log(`[${new Date().toLocaleTimeString()}] ${message}`, payload);
}

function emitHello() {
  socket.emit('agent.hello', {
    agentId,
    hostname,
    ip: '127.0.0.1',
    version: 'mock-1.0.0',
    at: nowIso(),
  });
}

function emitHeartbeat() {
  socket.emit('agent.heartbeat', {
    agentId,
    at: nowIso(),
  });
}

socket.on('connect', () => {
  log(`Connected to ${serverUrl} as ${agentId}`);
  emitHello();
});

socket.on('disconnect', (reason) => {
  log(`Disconnected: ${reason}`);
});

socket.on('agent.hello.ack', (data) => {
  log('agent.hello.ack', data);
});

socket.on('agent.heartbeat.ack', (data) => {
  log('agent.heartbeat.ack', data);
});

socket.on('command.execute', async (payload) => {
  log('command.execute', payload);
  const type = payload?.type;
  const commandId = payload?.commandId;

  if (!commandId || !type) {
    return;
  }

  await new Promise((resolve) => setTimeout(resolve, ackDelayMs));

  if (!forceFail) {
    machineState = type === 'OPEN' ? 'IN_USE' : 'LOCKED';
  }

  socket.emit('command.ack', {
    commandId,
    agentId,
    result: forceFail ? 'FAILED' : 'SUCCESS',
    message: forceFail
      ? `Mock forced failure for ${type}`
      : `Mock executed ${type}; state=${machineState}`,
  });
});

setInterval(() => {
  if (socket.connected) {
    emitHeartbeat();
  }
}, Math.max(5, heartbeatSeconds) * 1000);

log('Mock agent started', {
  serverUrl,
  agentId,
  heartbeatSeconds,
  ackDelayMs,
  forceFail,
});
