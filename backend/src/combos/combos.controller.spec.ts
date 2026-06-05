import { Test, TestingModule } from '@nestjs/testing';
import { CombosController } from './combos.controller';

describe('CombosController', () => {
  let controller: CombosController;

  beforeEach(async () => {
    const module: TestingModule = await Test.createTestingModule({
      controllers: [CombosController],
    }).compile();

    controller = module.get<CombosController>(CombosController);
  });

  it('should be defined', () => {
    expect(controller).toBeDefined();
  });
});
