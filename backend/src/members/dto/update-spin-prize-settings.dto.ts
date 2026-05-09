import { Type } from 'class-transformer';
import {
  ArrayMaxSize,
  ArrayMinSize,
  IsArray,
  IsNumber,
  IsOptional,
  IsString,
  Max,
  MaxLength,
  Min,
  ValidateNested,
} from 'class-validator';

export class SpinPrizeConfigItemDto {
  @Type(() => Number)
  @IsNumber({ maxDecimalPlaces: 0 })
  @Min(0)
  @Max(1000)
  minutes!: number;

  @Type(() => Number)
  @IsNumber({ maxDecimalPlaces: 4 })
  @Min(0)
  @Max(100)
  chance!: number;
}

export class UpdateSpinPrizeSettingsDto {
  @IsArray()
  @ArrayMinSize(10)
  @ArrayMaxSize(10)
  @ValidateNested({ each: true })
  @Type(() => SpinPrizeConfigItemDto)
  items!: SpinPrizeConfigItemDto[];

  @IsOptional()
  @IsString()
  @MaxLength(100)
  updatedBy?: string;
}
