import { ValidationPipe } from '@nestjs/common';
import { NestFactory } from '@nestjs/core';
import { json, urlencoded } from 'express';
import { AppModule } from './app.module';
import { ApiKeyGuard } from './auth/api-key.guard';

async function bootstrap() {
  const app = await NestFactory.create(AppModule);
  app.use(json({ limit: '8mb' }));
  app.use(urlencoded({ extended: true, limit: '8mb' }));
  app.useGlobalPipes(
    new ValidationPipe({
      transform: true,
      whitelist: true,
      forbidNonWhitelisted: true,
    }),
  );
  app.useGlobalGuards(new ApiKeyGuard());
  app.setGlobalPrefix('api/v1');
  app.enableCors({
    origin: true,
    credentials: true,
  });
  await app.listen(process.env.PORT ?? 3000);
}
bootstrap();
