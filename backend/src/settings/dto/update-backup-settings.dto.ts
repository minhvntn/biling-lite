import { Transform } from 'class-transformer';
import {
  IsBoolean,
  IsIn,
  IsInt,
  IsOptional,
  IsString,
  Matches,
  Max,
  MaxLength,
  Min,
} from 'class-validator';

function toBool(value: unknown): unknown {
  if (typeof value === 'boolean') {
    return value;
  }

  if (typeof value === 'string') {
    const normalized = value.trim().toLowerCase();
    if (normalized === 'true') {
      return true;
    }
    if (normalized === 'false') {
      return false;
    }
  }

  return value;
}

function toInt(value: unknown): unknown {
  if (typeof value === 'number') {
    return value;
  }

  if (typeof value === 'string') {
    const trimmed = value.trim();
    if (!trimmed) {
      return value;
    }

    const parsed = Number(trimmed);
    if (Number.isFinite(parsed)) {
      return Math.trunc(parsed);
    }
  }

  return value;
}

export class UpdateBackupSettingsDto {
  @IsOptional()
  @Transform(({ value }) => toBool(value))
  @IsBoolean()
  enabled?: boolean;

  @IsOptional()
  @IsString()
  @IsIn(['daily', 'weekly'])
  scheduleType?: 'daily' | 'weekly';

  @IsOptional()
  @IsString()
  @Matches(/^([01]\d|2[0-3]):([0-5]\d)$/)
  time?: string;

  @IsOptional()
  @Transform(({ value }) => toInt(value))
  @IsInt()
  @Min(1)
  @Max(7)
  weeklyDay?: number;

  @IsOptional()
  @IsString()
  @MaxLength(500)
  directory?: string;

  @IsOptional()
  @Transform(({ value }) => toInt(value))
  @IsInt()
  @Min(1)
  @Max(3650)
  retentionDays?: number;
}
