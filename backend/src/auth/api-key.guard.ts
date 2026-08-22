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

    const apiKey = request.headers['x-api-key'];
    const expectedKey = process.env.SMB_API_KEY || 'SmbSecureLANKey2026';

    if (apiKey === expectedKey) {
      return true;
    }

    throw new UnauthorizedException('Missing or invalid API Key');
  }
}
