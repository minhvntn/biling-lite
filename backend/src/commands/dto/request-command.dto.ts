import { IsOptional, IsString, MaxLength } from 'class-validator';

export class RequestCommandDto {
  @IsOptional()
  @IsString()
  @MaxLength(100)
  requestedBy?: string;
}
