import { Type } from 'class-transformer';
import {
  ArrayMaxSize,
  IsArray,
  IsDateString,
  IsOptional,
  IsString,
  MaxLength,
  ValidateNested,
} from 'class-validator';

export class WebsiteLogEntryDto {
  @IsOptional()
  @IsString()
  @MaxLength(120)
  domain?: string;

  @IsOptional()
  @IsString()
  @MaxLength(400)
  url?: string;

  @IsOptional()
  @IsString()
  @MaxLength(200)
  title?: string;

  @IsOptional()
  @IsString()
  @MaxLength(80)
  browser?: string;

  @IsOptional()
  @IsDateString()
  visitedAt?: string;
}

export class IngestWebsiteLogsDto {
  @IsString()
  @MaxLength(120)
  agentId!: string;

  @IsArray()
  @ArrayMaxSize(300)
  @ValidateNested({ each: true })
  @Type(() => WebsiteLogEntryDto)
  entries!: WebsiteLogEntryDto[];
}

