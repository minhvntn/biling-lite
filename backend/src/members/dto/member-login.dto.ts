import { Transform } from 'class-transformer';
import { IsOptional, IsString, MaxLength, MinLength } from 'class-validator';

export class MemberLoginDto {
  @IsString()
  @MinLength(1)
  @MaxLength(50)
  @Transform(({ value }: { value: string }) => value?.trim().toUpperCase())
  username!: string;

  @IsString()
  @MinLength(1)
  @MaxLength(100)
  password!: string;

  @IsOptional()
  @IsString()
  agentId?: string;
}
