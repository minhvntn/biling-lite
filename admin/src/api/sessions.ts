import { API_BASE_URL } from '../lib/config';
import { SessionListResponse } from '../types/session';

export async function fetchSessions(params?: {
  status?: 'ACTIVE' | 'CLOSED';
  from?: string;
  to?: string;
  pcId?: string;
}): Promise<SessionListResponse> {
  const query = new URLSearchParams();
  if (params?.status) {
    query.set('status', params.status);
  }
  if (params?.from) {
    query.set('from', params.from);
  }
  if (params?.to) {
    query.set('to', params.to);
  }
  if (params?.pcId) {
    query.set('pcId', params.pcId);
  }

  const url = `${API_BASE_URL}/sessions${
    query.toString() ? `?${query.toString()}` : ''
  }`;
  const response = await fetch(url);

  if (!response.ok) {
    throw new Error(`Failed to fetch sessions: ${response.status}`);
  }

  return (await response.json()) as SessionListResponse;
}
