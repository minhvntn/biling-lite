import { Body, Controller, Get, Post } from '@nestjs/common';
import { PushNotificationsService } from './push-notifications.service';
import { PushSubscriptionDto, PushUnsubscribeDto } from './dto/push-subscription.dto';

@Controller('notifications')
export class PushNotificationsController {
  constructor(private readonly pushService: PushNotificationsService) {}

  @Get('vapid-public-key')
  async getVapidPublicKey() {
    const publicKey = await this.pushService.getVapidPublicKey();
    return { publicKey };
  }

  @Post('subscribe')
  async subscribe(@Body() payload: PushSubscriptionDto) {
    return this.pushService.subscribe(payload);
  }

  @Post('unsubscribe')
  async unsubscribe(@Body() payload: PushUnsubscribeDto) {
    return this.pushService.unsubscribe(payload.endpoint);
  }

  @Post('test')
  async sendTestPush() {
    return this.pushService.sendPushNotification({
      title: '🔔 Thông báo thử nghiệm',
      body: 'Web Push Notification đang hoạt động tốt trên điện thoại của bạn!',
      icon: '/pwa-192x192.png',
      badge: '/pwa-192x192.png',
      tag: 'test-notification',
      data: { url: '/pcs' },
    });
  }
}
