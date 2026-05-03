import { io } from 'socket.io-client';

function toInt(value, fallback) {
  const parsed = Number(value);
  if (!Number.isFinite(parsed)) {
    return fallback;
  }

  return Math.trunc(parsed);
}

function readArg(name, fallback) {
  const index = process.argv.indexOf(`--${name}`);
  if (index === -1 || index + 1 >= process.argv.length) {
    return fallback;
  }

  return process.argv[index + 1];
}

const serverUrl = readArg('server', process.env.SERVER_URL ?? 'http://localhost:9000');
const count = Math.max(1, toInt(readArg('count', process.env.MOCK_COUNT ?? '10'), 10));
const start = Math.max(1, toInt(readArg('start', process.env.MOCK_START ?? '1'), 1));
const digits = Math.max(1, toInt(readArg('digits', process.env.AGENT_DIGITS ?? '3'), 3));
const prefix = readArg('prefix', process.env.AGENT_PREFIX ?? 'PC-');
const suffix = readArg('suffix', process.env.AGENT_SUFFIX ?? '-MOCK');
const heartbeatSeconds = Math.max(
  5,
  toInt(readArg('heartbeat', process.env.HEARTBEAT_SECONDS ?? '10'), 10),
);
const ackDelayMs = Math.max(
  50,
  toInt(readArg('ackDelay', process.env.ACK_DELAY_MS ?? '250'), 250),
);
const ackJitterMs = Math.max(
  0,
  toInt(readArg('ackJitter', process.env.ACK_JITTER_MS ?? '200'), 200),
);
const ipBase = readArg('ipBase', process.env.MOCK_IP_BASE ?? '192.168.1.');
const ipStart = Math.max(2, toInt(readArg('ipStart', process.env.MOCK_IP_START ?? '101'), 101));
const forceFail = readArg('forceFail', process.env.FORCE_FAIL ?? '0') === '1';
const runSeconds = Math.max(0, toInt(readArg('runSeconds', process.env.RUN_SECONDS ?? '0'), 0));

const agents = [];
const stats = {
  connected: 0,
  commandAckSuccess: 0,
  commandAckFailed: 0,
};

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

function padNumber(value, size) {
  return String(value).padStart(size, '0');
}

function createAgentConfig(order) {
  const number = start + order;
  const agentId = `${prefix}${padNumber(number, digits)}${suffix}`;
  const hostname = `${agentId}-HOST`;
  const ip = `${ipBase}${ipStart + order}`;
  return { number, agentId, hostname, ip };
}

function createAgent(order) {
  const cfg = createAgentConfig(order);
  const socket = io(`${serverUrl}/billing`, {
    transports: ['websocket'],
    reconnection: true,
    reconnectionAttempts: Infinity,
  });

  const state = {
    machineState: 'LOCKED',
    socket,
    ...cfg,
  };

  socket.on('connect', () => {
    stats.connected += 1;
    log(`CONNECTED ${cfg.agentId} (${stats.connected}/${count})`);
    socket.emit('agent.hello', {
      agentId: cfg.agentId,
      hostname: cfg.hostname,
      ip: cfg.ip,
      version: 'mock-bulk-1.0.0',
      at: nowIso(),
    });
  });

  socket.on('disconnect', (reason) => {
    stats.connected = Math.max(0, stats.connected - 1);
    log(`DISCONNECTED ${cfg.agentId}: ${reason}`);
  });

  socket.on('command.execute', async (payload) => {
    const type = payload?.type;
    const commandId = payload?.commandId;
    if (!commandId || !type) {
      return;
    }

    const jitter = ackJitterMs > 0 ? Math.floor(Math.random() * ackJitterMs) : 0;
    await new Promise((resolve) => setTimeout(resolve, ackDelayMs + jitter));

    if (!forceFail) {
      if (type === 'OPEN' || type === 'RESUME') {
        state.machineState = 'IN_USE';
      } else if (type === 'LOCK' || type === 'PAUSE') {
        state.machineState = 'LOCKED';
      }
    }

    const success = !forceFail;
    if (success) {
      stats.commandAckSuccess += 1;
    } else {
      stats.commandAckFailed += 1;
    }

    socket.emit('command.ack', {
      commandId,
      agentId: cfg.agentId,
      result: success ? 'SUCCESS' : 'FAILED',
      message: success
        ? `Mock bulk executed ${type}; state=${state.machineState}`
        : `Mock bulk forced failure for ${type}`,
    });

    log(`ACK ${cfg.agentId} ${type} -> ${success ? 'SUCCESS' : 'FAILED'}`);
  });

  return state;
}

function emitHeartbeatAll() {
  for (const agent of agents) {
    if (!agent.socket.connected) {
      continue;
    }

    agent.socket.emit('agent.heartbeat', {
      agentId: agent.agentId,
      at: nowIso(),
    });
  }
}

function stopAll(exitCode = 0) {
  try {
    for (const agent of agents) {
      agent.socket.disconnect();
    }
  } finally {
    log('STOPPED mock agents', {
      count,
      connected: stats.connected,
      commandAckSuccess: stats.commandAckSuccess,
      commandAckFailed: stats.commandAckFailed,
    });
    process.exit(exitCode);
  }
}

for (let i = 0; i < count; i += 1) {
  agents.push(createAgent(i));
}

const heartbeatTimer = setInterval(() => {
  emitHeartbeatAll();
}, heartbeatSeconds * 1000);

process.on('SIGINT', () => {
  clearInterval(heartbeatTimer);
  stopAll(0);
});

process.on('SIGTERM', () => {
  clearInterval(heartbeatTimer);
  stopAll(0);
});

if (runSeconds > 0) {
  setTimeout(() => {
    clearInterval(heartbeatTimer);
    stopAll(0);
  }, runSeconds * 1000);
}

log('MOCK BULK STARTED', {
  serverUrl,
  count,
  start,
  digits,
  prefix,
  suffix,
  heartbeatSeconds,
  ackDelayMs,
  ackJitterMs,
  ipBase,
  ipStart,
  forceFail,
  runSeconds,
});
