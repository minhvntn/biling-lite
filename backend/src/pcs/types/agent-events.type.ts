export type AgentHelloPayload = {
  agentId: string;
  hostname?: string;
  ip?: string;
  macAddress?: string;
  version?: string;
  at?: string;
};

export type AgentHeartbeatPayload = {
  agentId: string;
  at?: string;
  ip?: string;
  hostname?: string;
  macAddress?: string;
};
