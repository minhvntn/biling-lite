import { Type } from 'class-transformer';
import {
  IsBoolean,
  IsIn,
  IsInt,
  IsOptional,
  IsString,
  Max,
  MaxLength,
  Min,
} from 'class-validator';

export class SetClientRuntimeSettingsDto {
  @IsOptional()
  @IsInt()
  @Min(1)
  @Max(240)
  readyAutoShutdownMinutes?: number;

  @IsOptional()
  @IsString()
  @IsIn(['none', 'image', 'video'])
  lockScreenBackgroundMode?: string;

  @IsOptional()
  @IsString()
  @MaxLength(2048)
  lockScreenBackgroundUrl?: string;

  @IsOptional()
  @Type(() => Boolean)
  @IsBoolean()
  allowMemberWithdraw?: boolean;

  @IsOptional()
  @Type(() => Boolean)
  @IsBoolean()
  allowMemberTopupRequest?: boolean;

  @IsOptional()
  @Type(() => Number)
  @IsInt()
  @Min(1)
  @Max(3600)
  lockScreenIntervalSeconds?: number;

  @IsOptional()
  @Type(() => Number)
  @IsInt()
  @Min(0)
  @Max(3600)
  autoCollapseIntervalSeconds?: number;

  @IsOptional()
  @IsString()
  @MaxLength(2048)
  gameLauncherPath?: string;
}
