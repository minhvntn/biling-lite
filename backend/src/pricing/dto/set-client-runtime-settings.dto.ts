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

  @IsOptional()
  @IsString()
  @MaxLength(50)
  vietQrBankId?: string;

  @IsOptional()
  @IsString()
  @MaxLength(100)
  vietQrAccountNo?: string;

  @IsOptional()
  @IsString()
  @MaxLength(150)
  vietQrAccountName?: string;

  @IsOptional()
  @Type(() => Number)
  @IsInt()
  @Min(1000)
  @Max(100000000)
  maxMemberTopupAmount?: number;

  @IsOptional()
  @Type(() => Boolean)
  @IsBoolean()
  vpnEnabled?: boolean;

  @IsOptional()
  @IsString()
  @MaxLength(2048)
  vpnLocalInstallerPath?: string;

  @IsOptional()
  @IsString()
  @MaxLength(2048)
  vpnDownloadUrl?: string;

  @IsOptional()
  @Type(() => Boolean)
  @IsBoolean()
  desktopWallpaperEnabled?: boolean;

  @IsOptional()
  @IsString()
  @MaxLength(2048)
  desktopWallpaperUrl?: string;

  @IsOptional()
  @IsIn(['fill', 'fit', 'stretch'])
  desktopWallpaperStyle?: 'fill' | 'fit' | 'stretch';
}
