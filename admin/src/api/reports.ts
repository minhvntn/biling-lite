import { API_BASE_URL } from '../lib/config';
import { DailyRevenueResponse } from '../types/session';

export async function fetchDailyRevenue(date: string): Promise<DailyRevenueResponse> {
  const response = await fetch(
    `${API_BASE_URL}/reports/revenue/daily?date=${encodeURIComponent(date)}`,
  );

  if (!response.ok) {
    throw new Error(`Failed to fetch daily revenue: ${response.status}`);
  }

  return (await response.json()) as DailyRevenueResponse;
}
