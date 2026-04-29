export type SessionItem = {
  id: string;
  pcId: string;
  pcName: string;
  agentId: string;
  status: 'ACTIVE' | 'CLOSED';
  startedAt: string;
  endedAt: string | null;
  durationSeconds: number | null;
  billableMinutes: number | null;
  pricePerMinute: number | null;
  amount: number | null;
  closedReason: string | null;
};

export type SessionListResponse = {
  items: SessionItem[];
  total: number;
  serverTime: string;
};

export type DailyRevenueResponse = {
  date: string;
  closedSessions: number;
  totalAmount: number;
  serverTime: string;
};
