export type CommandUpdatedEvent = {
  commandId: string;
  pcId: string;
  status: 'PENDING' | 'SENT' | 'ACK_SUCCESS' | 'ACK_FAILED' | 'TIMEOUT';
  type: 'OPEN' | 'LOCK';
  errorMessage: string | null;
  at: string;
};
