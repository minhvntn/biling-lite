import { Transform } from 'class-transformer';
import {
  IsArray,
  IsInt,
  IsNumber,
  IsOptional,
  IsString,
  Max,
  MaxLength,
  Min,
} from 'class-validator';

export class RemoteInputDto {
  @IsString()
  @MaxLength(40)
  @Transform(({ value }: { value?: string }) => value?.trim().toLowerCase() ?? '')
  type!: string;

  @IsOptional()
  @IsNumber()
  @Min(0)
  @Max(1)
  x?: number;

  @IsOptional()
  @IsNumber()
  @Min(0)
  @Max(1)
  y?: number;

  @IsOptional()
  @IsString()
  @MaxLength(20)
  @Transform(({ value }: { value?: string }) => value?.trim().toLowerCase() ?? undefined)
  button?: string;

  @IsOptional()
  @IsInt()
  @Min(-12000)
  @Max(12000)
  delta?: number;

  @IsOptional()
  @IsString()
  @MaxLength(80)
  @Transform(({ value }: { value?: string }) => value?.trim() ?? undefined)
  key?: string;

  @IsOptional()
  @IsString()
  @MaxLength(200)
  text?: string;

  @IsOptional()
  @IsArray()
  @IsString({ each: true })
  @MaxLength(20, { each: true })
  modifiers?: string[];
}
