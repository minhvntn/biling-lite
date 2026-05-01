export type MemberItem = {
  id: string;
  username: string;
  fullName: string;
  phone: string | null;
  identityNumber: string | null;
  balance: number;
  playSeconds: number;
  playHours: number;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
};

export type MembersResponse = {
  items: MemberItem[];
  total: number;
  serverTime: string;
};

export type MemberTransactionItem = {
  id: string;
  memberId: string;
  type: 'TOPUP' | 'BUY_PLAYTIME' | 'ADJUSTMENT';
  amountDelta: number;
  playSecondsDelta: number;
  createdBy: string;
  note: string | null;
  createdAt: string;
};

export type MemberTransactionsResponse = {
  member: MemberItem;
  items: MemberTransactionItem[];
  total: number;
  serverTime: string;
};

export type CreateMemberPayload = {
  username: string;
  fullName: string;
  password: string;
  phone?: string;
  identityNumber?: string;
};

export type TopupPayload = {
  amount: number;
  note?: string;
  createdBy?: string;
};

export type BuyHoursPayload = {
  hours: number;
  ratePerHour?: number;
  note?: string;
  createdBy?: string;
};
