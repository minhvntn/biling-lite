import { fetchWithAuth } from './fetchWithAuth';
import { API_BASE_URL } from '../lib/config';
import {
  BuyHoursPayload,
  CreateMemberPayload,
  MemberItem,
  MembersResponse,
  MemberTransactionsResponse,
  TopupPayload,
} from '../types/member';

function toQuery(search?: string): string {
  if (!search?.trim()) {
    return '';
  }

  const params = new URLSearchParams({ search: search.trim() });
  return `?${params.toString()}`;
}

async function handleResponse<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const raw = await response.text();
    let message = raw;
    try {
      const parsed = JSON.parse(raw);
      if (parsed.message) {
        message = Array.isArray(parsed.message) ? parsed.message.join(', ') : parsed.message;
      }
    } catch {
      // Not JSON
    }
    throw new Error(message || `Request failed: ${response.status}`);
  }

  return (await response.json()) as T;
}

export async function fetchMembers(search?: string): Promise<MembersResponse> {
  const response = await fetchWithAuth(`${API_BASE_URL}/members${toQuery(search)}`);
  return handleResponse<MembersResponse>(response);
}

export async function createMember(payload: CreateMemberPayload): Promise<MemberItem> {
  const response = await fetchWithAuth(`${API_BASE_URL}/members`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });

  return handleResponse<MemberItem>(response);
}

export async function topupMember(memberId: string, payload: TopupPayload) {
  const response = await fetchWithAuth(`${API_BASE_URL}/members/${memberId}/topups`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });

  return handleResponse<{ member: MemberItem }>(response);
}

export async function buyHours(memberId: string, payload: BuyHoursPayload) {
  const response = await fetchWithAuth(`${API_BASE_URL}/members/${memberId}/buy-hours`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });

  return handleResponse<{ member: MemberItem }>(response);
}

export async function fetchMemberTransactions(
  memberId: string,
): Promise<MemberTransactionsResponse> {
  const response = await fetchWithAuth(`${API_BASE_URL}/members/${memberId}/transactions`);
  return handleResponse<MemberTransactionsResponse>(response);
}

export async function setMemberPresence(
  memberId: string,
  agentId: string,
  isActive: boolean,
): Promise<any> {
  const response = await fetchWithAuth(`${API_BASE_URL}/members/presence`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ memberId, agentId, isActive }),
  });

  return handleResponse<any>(response);
}

export async function withdrawMemberBalance(memberId: string, payload: { amount: number; note?: string }) {
  const response = await fetchWithAuth(`${API_BASE_URL}/members/${memberId}/adjust`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      amountDelta: -payload.amount,
      note: payload.note,
      createdBy: 'admin.web',
    }),
  });

  return handleResponse<{ member: MemberItem }>(response);
}

export async function fetchPendingTopupRequests(): Promise<{ items: any[]; total: number }> {
  const response = await fetchWithAuth(`${API_BASE_URL}/members/topup-requests/pending`);
  return handleResponse<{ items: any[]; total: number }>(response);
}

export async function approveTopupRequest(requestId: string, payload?: { approvedBy?: string }) {
  const response = await fetchWithAuth(`${API_BASE_URL}/members/topup-requests/${requestId}/approve`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload ?? { approvedBy: 'admin.web' }),
  });
  return handleResponse<any>(response);
}

export async function rejectTopupRequest(requestId: string, payload?: { rejectedBy?: string; note?: string }) {
  const response = await fetchWithAuth(`${API_BASE_URL}/members/topup-requests/${requestId}/reject`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload ?? { rejectedBy: 'admin.web', note: 'Từ chối bởi admin web' }),
  });
  return handleResponse<any>(response);
}

export interface ImportGcafeResult {
  message: string;
  processed: number;
  imported: number;
  updated: number;
  failed: number;
  totalMoney: number;
  totalSpent: number;
  totalUsedMinutes: number;
}

export async function importGcafeMembers(file: File): Promise<ImportGcafeResult> {
  const formData = new FormData();
  formData.append('file', file);

  const response = await fetchWithAuth(`${API_BASE_URL}/members/import-gcafe`, {
    method: 'POST',
    body: formData,
  });

  return handleResponse<ImportGcafeResult>(response);
}

