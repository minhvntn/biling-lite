import { IsNumber, Max, Min } from 'class-validator';

export class UpdateHorseRaceSettingsDto {
  @IsNumber()
  @Min(1)
  @Max(100)
  top1Multiplier!: number;

  @IsNumber()
  @Min(1)
  @Max(100)
  top2Multiplier!: number;

  @IsNumber()
  @Min(1)
  @Max(100)
  top3Multiplier!: number;
}
