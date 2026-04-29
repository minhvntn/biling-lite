export type CommandAckPayload = {
  commandId: string;
  agentId: string;
  result: 'SUCCESS' | 'FAILED';
  message?: string;
};
