import { IsNumber, Min } from 'class-validator';

export class SetDefaultRateDto {
  @IsNumber()
  @Min(1)
  hourlyRate!: number;
}
