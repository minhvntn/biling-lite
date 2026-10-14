import { fetchWithAuth } from './fetchWithAuth';
import { API_BASE_URL } from '../lib/config';

export type PushSubscriptionKeys = {
  p256dh: string;
  auth: string;
};

export type PushSubscriptionPayload = {
  endpoint: string;
  keys: PushSubscriptionKeys;
  userAgent?: string;
};

async function handleResponse<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || `Request failed: ${response.status}`);
  }

  return (await response.json()) as T;
}

export async function fetchVapidPublicKey(): Promise<{ publicKey: string }> {
  const response = await fetchWithAuth(`${API_BASE_URL}/notifications/vapid-public-key`);
  return handleResponse<{ publicKey: string }>(response);
}

export async function subscribePushNotification(payload: PushSubscriptionPayload): Promise<{ success: boolean; total: number }> {
  const response = await fetchWithAuth(`${API_BASE_URL}/notifications/subscribe`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });
  return handleResponse<{ success: boolean; total: number }>(response);
}

export async function unsubscribePushNotification(endpoint: string): Promise<{ success: boolean; total: number }> {
  const response = await fetchWithAuth(`${API_BASE_URL}/notifications/unsubscribe`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ endpoint }),
  });
  return handleResponse<{ success: boolean; total: number }>(response);
}

export async function sendTestPushNotification(): Promise<{ sent: number; failed: number }> {
  const response = await fetchWithAuth(`${API_BASE_URL}/notifications/test`, {
    method: 'POST',
  });
  return handleResponse<{ sent: number; failed: number }>(response);
}

// Convert VAPID key base64 to Uint8Array for PushManager subscribe
export function urlBase64ToUint8Array(base64String: string): Uint8Array {
  const padding = '='.repeat((4 - (base64String.length % 4)) % 4);
  const base64 = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/');
  const rawData = window.atob(base64);
  const outputArray = new Uint8Array(rawData.length);
  for (let i = 0; i < rawData.length; ++i) {
    outputArray[i] = rawData.charCodeAt(i);
  }
  return outputArray;
}
