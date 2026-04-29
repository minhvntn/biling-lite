import { API_BASE_URL } from '../lib/config';

type CommandResponse = {
  id: string;
  pcId: string;
  type: 'OPEN' | 'LOCK';
  status: 'PENDING' | 'SENT' | 'ACK_SUCCESS' | 'ACK_FAILED' | 'TIMEOUT';
  errorMessage: string | null;
};

async function requestCommand(
  pcId: string,
  action: 'open' | 'lock',
): Promise<CommandResponse> {
  const response = await fetch(`${API_BASE_URL}/pcs/${pcId}/${action}`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({
      requestedBy: 'admin.web',
    }),
  });

  if (!response.ok) {
    throw new Error(`Failed to ${action} PC: ${response.status}`);
  }

  return (await response.json()) as CommandResponse;
}

export function openPc(pcId: string): Promise<CommandResponse> {
  return requestCommand(pcId, 'open');
}

export function lockPc(pcId: string): Promise<CommandResponse> {
  return requestCommand(pcId, 'lock');
}
