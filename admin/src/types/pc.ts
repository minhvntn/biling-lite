export type PcStatus = 'OFFLINE' | 'ONLINE' | 'IN_USE' | 'LOCKED';

export type PcListItem = {
  id: string;
  agentId: string;
  name: string;
  hostname: string | null;
  ipAddress: string | null;
  status: PcStatus;
  lastSeenAt: string | null;
  activeSession: {
    id: string;
    startedAt: string;
    elapsedSeconds: number;
    billableMinutes: number;
    estimatedAmount: number;
  } | null;
};

export type PcListResponse = {
  items: PcListItem[];
  total: number;
  serverTime: string;
};

export type PcStatusChangedEvent = {
  pcId: string;
  agentId: string;
  previousStatus: PcStatus;
  status: PcStatus;
  at: string;
  sourceEvent: string;
};
