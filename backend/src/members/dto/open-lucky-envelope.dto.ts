import { Transform } from 'class-transformer';
import { IsOptional, IsString, MaxLength } from 'class-validator';

export class OpenLuckyEnvelopeDto {
  @IsOptional()
  @IsString()
  @MaxLength(100)
  @Transform(({ value }: { value?: string }) => value?.trim() || undefined)
  createdBy?: string;
}
