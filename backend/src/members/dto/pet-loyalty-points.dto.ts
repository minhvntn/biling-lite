import { Transform } from 'class-transformer';
import { IsIn, IsInt, IsOptional, IsString, Max, MaxLength, Min } from 'class-validator';

export class PetLoyaltyPointsDto {
  @IsString()
  @IsIn(['SPEND', 'REWARD'])
  @Transform(({ value }: { value?: string }) => value?.trim().toUpperCase())
  action!: 'SPEND' | 'REWARD';

  @IsInt()
  @Min(1)
  @Max(50)
  @Transform(({ value }: { value: unknown }) => Number(value))
  points!: number;

  @IsOptional()
  @IsString()
  @MaxLength(160)
  @Transform(({ value }: { value?: string }) => value?.trim() || undefined)
  note?: string;

  @IsOptional()
  @IsString()
  @MaxLength(100)
  @Transform(({ value }: { value?: string }) => value?.trim() || undefined)
  createdBy?: string;
}
