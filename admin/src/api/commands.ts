import { API_BASE_URL } from '../lib/config';

type CommandResponse = {
  id: string;
  pcId: string;
  type: 'OPEN' | 'LOCK' | 'SHUTDOWN' | 'RESTART';
  status: 'PENDING' | 'SENT' | 'ACK_SUCCESS' | 'ACK_FAILED' | 'TIMEOUT';
  errorMessage: string | null;
};

async function requestCommand(
  pcId: string,
  action: 'open' | 'lock' | 'shutdown' | 'restart',
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

export async function guestOpenPc(pcId: string, amount: number): Promise<any> {
  const response = await fetch(`${API_BASE_URL}/pcs/${pcId}/guest-open`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({
      amount,
      requestedBy: 'admin.web',
    }),
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `Failed to open guest PC: ${response.status}`);
  }

  return response.json();
}

export async function shutdownPc(pcId: string): Promise<CommandResponse> {
  return requestCommand(pcId, 'shutdown');
}

export async function restartPc(pcId: string): Promise<CommandResponse> {
  return requestCommand(pcId, 'restart');
}

export async function wakePc(pcId: string, macAddress: string): Promise<any> {
  const response = await fetch(`${API_BASE_URL}/pcs/${pcId}/wake`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({
      macAddress,
      requestedBy: 'admin.web',
    }),
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `Failed to wake PC: ${response.status}`);
  }

  return response.json();
}

