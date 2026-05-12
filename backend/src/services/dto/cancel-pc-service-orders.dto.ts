import { Transform, Type } from 'class-transformer';
import {
  ArrayMaxSize,
  IsArray,
  IsInt,
  IsOptional,
  IsString,
  IsUUID,
  Max,
  MaxLength,
  Min,
} from 'class-validator';

export class CancelPcServiceOrdersDto {
  @IsOptional()
  @IsString()
  @MaxLength(100)
  requestedBy?: string;

  @IsOptional()
  @IsString()
  @MaxLength(255)
  note?: string;

  @IsOptional()
  @IsUUID('4')
  sessionId?: string;

  @IsOptional()
  @IsUUID('4')
  serviceItemId?: string;

  @IsOptional()
  @Type(() => Number)
  @IsInt()
  @Min(1)
  @Max(999)
  quantity?: number;

  @IsOptional()
  @IsArray()
  @ArrayMaxSize(200)
  @IsUUID('4', { each: true })
  @Transform(({ value }: { value?: unknown }) => {
    if (!Array.isArray(value)) {
      return undefined;
    }

    const normalized = value
      .map((item) => (typeof item === 'string' ? item.trim() : ''))
      .filter((item) => !!item);

    return Array.from(new Set(normalized));
  })
  orderIds?: string[];
}
