import { IsInt, Max, Min } from 'class-validator';

export class SetClientRuntimeSettingsDto {
  @IsInt()
  @Min(1)
  @Max(240)
  readyAutoShutdownMinutes!: number;
}

