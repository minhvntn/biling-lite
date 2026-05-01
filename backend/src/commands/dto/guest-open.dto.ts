import { Type } from 'class-transformer';
import { IsNumber, IsOptional, IsString, Max, MaxLength, Min } from 'class-validator';

export class GuestOpenDto {
  @Type(() => Number)
  @IsNumber({ maxDecimalPlaces: 2 })
  @Min(1000)
  @Max(100000000)
  amount!: number;

  @IsOptional()
  @IsString()
  @MaxLength(100)
  requestedBy?: string;
}
