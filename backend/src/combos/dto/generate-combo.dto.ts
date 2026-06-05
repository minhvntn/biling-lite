import { IsString, IsNumber, IsNotEmpty, Min, Max } from 'class-validator';

export class GenerateComboDto {
  @IsString()
  @IsNotEmpty()
  comboId: string;

  @IsNumber()
  @Min(1)
  @Max(100)
  quantity: number;
}
