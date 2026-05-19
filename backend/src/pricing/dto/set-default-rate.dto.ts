import { IsNumber, IsOptional, Min } from 'class-validator';

export class SetDefaultRateDto {
  @IsNumber()
  @Min(1)
  hourlyRate!: number;

  @IsOptional()
  @IsNumber()
  @Min(1)
  memberHourlyRate?: number;
}
