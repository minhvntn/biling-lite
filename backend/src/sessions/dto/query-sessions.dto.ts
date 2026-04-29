import { IsDateString, IsIn, IsOptional, IsUUID } from 'class-validator';

export class QuerySessionsDto {
  @IsOptional()
  @IsUUID()
  pcId?: string;

  @IsOptional()
  @IsDateString()
  from?: string;

  @IsOptional()
  @IsDateString()
  to?: string;

  @IsOptional()
  @IsIn(['ACTIVE', 'CLOSED'])
  status?: 'ACTIVE' | 'CLOSED';
}
