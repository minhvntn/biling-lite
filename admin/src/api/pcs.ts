import { API_BASE_URL } from '../lib/config';
import { PcListResponse } from '../types/pc';

export async function fetchPcs(signal?: AbortSignal): Promise<PcListResponse> {
  const response = await fetch(`${API_BASE_URL}/pcs`, { signal });
  if (!response.ok) {
    throw new Error(`Failed to fetch PCs: ${response.status}`);
  }

  return (await response.json()) as PcListResponse;
}
