import { Transform } from 'class-transformer';
import { IsOptional, IsString, MaxLength } from 'class-validator';

export class RejectTopupRequestDto {
  @IsOptional()
  @IsString()
  @MaxLength(100)
  @Transform(({ value }: { value?: string }) => value?.trim() || undefined)
  rejectedBy?: string;

  @IsOptional()
  @IsString()
  @MaxLength(300)
  @Transform(({ value }: { value?: string }) => value?.trim() || undefined)
  note?: string;
}
