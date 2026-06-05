import { Controller, Get, Post, Body, Put, Param, Delete, UseGuards } from '@nestjs/common';
import { CombosService } from './combos.service';
import { CreateComboDto } from './dto/create-combo.dto';
import { GenerateComboDto } from './dto/generate-combo.dto';

@Controller('combos')
export class CombosController {
  constructor(private readonly combosService: CombosService) {}

  @Get()
  findAll() {
    return this.combosService.findAll();
  }

  @Post()
  create(@Body() createComboDto: CreateComboDto) {
    return this.combosService.create(createComboDto);
  }

  @Put(':id')
  update(@Param('id') id: string, @Body() updateComboDto: CreateComboDto) {
    return this.combosService.update(id, updateComboDto);
  }

  @Delete(':id')
  remove(@Param('id') id: string) {
    return this.combosService.remove(id);
  }

  @Post('generate')
  generateCards(@Body() generateDto: GenerateComboDto) {
    return this.combosService.generateCards(generateDto);
  }
}
