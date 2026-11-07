import { Injectable, CanActivate, ExecutionContext, UnauthorizedException } from '@nestjs/common';
import { Request } from 'express';

@Injectable()
export class ApiKeyGuard implements CanActivate {
  canActivate(context: ExecutionContext): boolean {
    const request = context.switchToHttp().getRequest<Request>();
    
    // Ignore WebSocket connections and non-HTTP contexts if this gets applied globally
    if (!request || !request.headers) {
      return true;
    }

    if (request.method === 'OPTIONS') {
      return true;
    }

    // Public lock-screen and desktop wallpaper media assets streamed/displayed on client workstations
    const rawPath = (request.path || request.originalUrl || request.url || '').split('?')[0];
    if (
      (request.method === 'GET' || request.method === 'HEAD') &&
      (rawPath.includes('/pricing/client-settings/lock-screen-media/') ||
       rawPath.includes('/pricing/client-settings/desktop-wallpaper-media/'))
    ) {
      return true;
    }

    const expectedKey = process.env.SMB_API_KEY || 'SmbSecureLANKey2026';

    const apiKey =
      request.headers['x-api-key'] ||
      (request.query?.['x-api-key'] as string) ||
      (request.query?.['api_key'] as string) ||
      (request.query?.['apiKey'] as string) ||
      (request.query?.['key'] as string);

    if (apiKey === expectedKey) {
      return true;
    }

    throw new UnauthorizedException('Missing or invalid API Key');
  }
}

