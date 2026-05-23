export type TimeBasedPromotionLite = {
  name?: string;
  daysOfWeek: number[];
  startTime: string;
  endTime: string;
  discountPercent: unknown;
  isActive?: boolean;
};

export type SessionBillingMode = 'PER_MINUTE_STARTED' | 'PER_SECOND';

function parseTimeToMinuteOfDay(raw: string): number | null {
  if (typeof raw !== 'string') {
    return null;
  }

  const value = raw.trim();
  const match = /^(\d{1,2}):(\d{2})$/.exec(value);
  if (!match) {
    return null;
  }

  const hour = Number(match[1]);
  const minute = Number(match[2]);
  if (!Number.isFinite(hour) || !Number.isFinite(minute)) {
    return null;
  }
  if (hour < 0 || hour > 23 || minute < 0 || minute > 59) {
    return null;
  }

  return hour * 60 + minute;
}

function isPromotionActiveAt(
  at: Date,
  promotion: TimeBasedPromotionLite,
): boolean {
  if (promotion.isActive === false) {
    return false;
  }

  if (!Array.isArray(promotion.daysOfWeek)) {
    return false;
  }

  const day = at.getDay();
  if (!promotion.daysOfWeek.includes(day)) {
    return false;
  }

  const start = parseTimeToMinuteOfDay(promotion.startTime);
  const end = parseTimeToMinuteOfDay(promotion.endTime);
  if (start === null || end === null) {
    return false;
  }

  const minuteOfDay = at.getHours() * 60 + at.getMinutes();

  // Same-day range, e.g. 07:00 -> 17:00.
  if (start <= end) {
    return minuteOfDay >= start && minuteOfDay <= end;
  }

  // Overnight range, e.g. 22:00 -> 06:00.
  return minuteOfDay >= start || minuteOfDay <= end;
}

export function getEffectiveHourlyRateAt(
  baseHourlyRate: number,
  at: Date,
  promotions: TimeBasedPromotionLite[],
): number {
  let bestDiscount = 0;
  for (const promotion of promotions) {
    if (!isPromotionActiveAt(at, promotion)) {
      continue;
    }
    const discount = Number(promotion.discountPercent ?? 0);
    if (Number.isFinite(discount) && discount > bestDiscount) {
      bestDiscount = discount;
    }
  }

  if (bestDiscount <= 0) {
    return baseHourlyRate;
  }

  return Math.round(baseHourlyRate * (1 - bestDiscount / 100));
}

export function getActivePromotionAt(
  at: Date,
  promotions: TimeBasedPromotionLite[],
): TimeBasedPromotionLite | null {
  let bestDiscount = 0;
  let activePromo: TimeBasedPromotionLite | null = null;
  for (const promotion of promotions) {
    if (!isPromotionActiveAt(at, promotion)) {
      continue;
    }
    const discount = Number(promotion.discountPercent ?? 0);
    if (Number.isFinite(discount) && discount > bestDiscount) {
      bestDiscount = discount;
      activePromo = promotion;
    }
  }
  return activePromo;
}

function nextMinuteBoundaryMs(currentMs: number): number {
  const minuteMs = 60_000;
  return Math.floor(currentMs / minuteMs) * minuteMs + minuteMs;
}

export function calculateSessionAmountByPromotions(params: {
  startedAt: Date;
  endedAt: Date;
  baseHourlyRate: number;
  promotions: TimeBasedPromotionLite[];
  mode: SessionBillingMode;
}): number {
  const startMs = params.startedAt.getTime();
  const endMs = params.endedAt.getTime();
  if (!Number.isFinite(startMs) || !Number.isFinite(endMs) || endMs <= startMs) {
    return 0;
  }

  let total = 0;
  let cursorMs = startMs;

  while (cursorMs < endMs) {
    const at = new Date(cursorMs);
    const hourlyRate = getEffectiveHourlyRateAt(
      params.baseHourlyRate,
      at,
      params.promotions,
    );

    if (params.mode === 'PER_MINUTE_STARTED') {
      total += hourlyRate / 60;
      cursorMs = Math.min(endMs, nextMinuteBoundaryMs(cursorMs));
      continue;
    }

    const sliceEndMs = Math.min(endMs, nextMinuteBoundaryMs(cursorMs));
    const sliceSeconds = (sliceEndMs - cursorMs) / 1000;
    total += (sliceSeconds / 3600) * hourlyRate;
    cursorMs = sliceEndMs;
  }

  return total;
}
