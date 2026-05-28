import { IsInt, IsNotEmpty, IsString, Max, Min } from 'class-validator';

export class HorseRaceDto {
  @IsInt()
  @Min(1)
  betPoints: number;

  @IsInt()
  @Min(0)
  @Max(9)
  selectedHorse: number; // 0 to 9

  @IsString()
  @IsNotEmpty()
  createdBy: string;
}
