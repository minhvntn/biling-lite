import { API_BASE_URL } from '../lib/config';

async function handleResponse<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || `Request failed: ${response.status}`);
  }
  return (await response.json()) as T;
}

export type ServiceItem = {
  id: string;
  name: string;
  category: string | null;
  unitPrice: number;
  isActive: boolean;
  imageDataUrl: string | null;
};

export type ServiceItemsResponse = {
  items: ServiceItem[];
  total: number;
  serverTime: string;
};

export type PcServiceOrder = {
  id: string;
  pcId: string;
  sessionId: string | null;
  serviceItemId: string;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
  note: string | null;
  createdBy: string;
  createdAt: string;
  isPaid: boolean;
  serviceItem: ServiceItem;
};

export type PcServiceOrdersResponse = {
  pcId: string;
  items: PcServiceOrder[];
  total: number;
  serverTime: string;
};

export async function fetchServiceItems(): Promise<ServiceItemsResponse> {
  const response = await fetch(`${API_BASE_URL}/services/items`);
  return handleResponse<ServiceItemsResponse>(response);
}

export async function fetchPcServiceOrders(pcId: string): Promise<PcServiceOrdersResponse> {
  const response = await fetch(`${API_BASE_URL}/services/pcs/${pcId}/orders`);
  return handleResponse<PcServiceOrdersResponse>(response);
}

export async function createPcServiceOrder(
  pcId: string,
  payload: {
    serviceItemId: string;
    quantity: number;
    note?: string;
    requestedBy?: string;
  },
): Promise<PcServiceOrder> {
  const response = await fetch(`${API_BASE_URL}/services/pcs/${pcId}/orders`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      ...payload,
      requestedBy: payload.requestedBy || 'admin.web',
    }),
  });
  return handleResponse<PcServiceOrder>(response);
}

export async function payPcServiceOrders(
  pcId: string,
  payload: {
    orderIds?: string[];
    note?: string;
    requestedBy?: string;
  },
): Promise<any> {
  const response = await fetch(`${API_BASE_URL}/services/pcs/${pcId}/orders/pay`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      ...payload,
      requestedBy: payload.requestedBy || 'admin.web',
    }),
  });
  return handleResponse<any>(response);
}
