export type PcStatus = 'OFFLINE' | 'ONLINE' | 'IN_USE' | 'LOCKED' | 'BOOTING';

export type PcListItem = {
  id: string;
  agentId: string;
  name: string;
  groupName?: string;
  macAddress?: string | null;
  hourlyRate?: number;
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
  activeMember: {
    id: string;
    username: string;
    fullName: string;
    balance: number;
  } | null;
  activeGuest: {
    displayName: string;
    prepaidAmount: number;
  } | null;
  activeAdmin: {
    username: string;
    fullName: string;
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
