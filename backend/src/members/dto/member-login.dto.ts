import { IsString, MaxLength, MinLength } from 'class-validator';

export class MemberLoginDto {
  @IsString()
  @MinLength(1)
  @MaxLength(50)
  username!: string;

  @IsString()
  @MinLength(1)
  @MaxLength(100)
  password!: string;
}
