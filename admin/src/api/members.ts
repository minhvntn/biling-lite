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
    const message = await response.text();
    throw new Error(message || `Request failed: ${response.status}`);
  }

  return (await response.json()) as T;
}

export async function fetchMembers(search?: string): Promise<MembersResponse> {
  const response = await fetch(`${API_BASE_URL}/members${toQuery(search)}`);
  return handleResponse<MembersResponse>(response);
}

export async function createMember(payload: CreateMemberPayload): Promise<MemberItem> {
  const response = await fetch(`${API_BASE_URL}/members`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });

  return handleResponse<MemberItem>(response);
}

export async function topupMember(memberId: string, payload: TopupPayload) {
  const response = await fetch(`${API_BASE_URL}/members/${memberId}/topups`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });

  return handleResponse<{ member: MemberItem }>(response);
}

export async function buyHours(memberId: string, payload: BuyHoursPayload) {
  const response = await fetch(`${API_BASE_URL}/members/${memberId}/buy-hours`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });

  return handleResponse<{ member: MemberItem }>(response);
}

export async function fetchMemberTransactions(
  memberId: string,
): Promise<MemberTransactionsResponse> {
  const response = await fetch(`${API_BASE_URL}/members/${memberId}/transactions`);
  return handleResponse<MemberTransactionsResponse>(response);
}
