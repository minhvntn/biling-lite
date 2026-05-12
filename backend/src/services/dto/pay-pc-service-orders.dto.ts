import { Transform } from 'class-transformer';
import { ArrayMaxSize, IsArray, IsOptional, IsString, IsUUID, MaxLength } from 'class-validator';

export class PayPcServiceOrdersDto {
  @IsOptional()
  @IsString()
  @MaxLength(100)
  requestedBy?: string;

  @IsOptional()
  @IsString()
  @MaxLength(255)
  note?: string;

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
