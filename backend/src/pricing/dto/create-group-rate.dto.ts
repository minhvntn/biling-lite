import { IsNumber, IsString, MaxLength, Min, MinLength } from 'class-validator';

export class CreateGroupRateDto {
  @IsString()
  @MinLength(1)
  @MaxLength(80)
  name!: string;

  @IsNumber()
  @Min(1)
  hourlyRate!: number;
}
