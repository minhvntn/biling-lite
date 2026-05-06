import { IsIn, IsInt, IsOptional, IsString, Max, MaxLength, Min } from 'class-validator';

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
}
