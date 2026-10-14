import { Injectable, Logger, OnModuleInit } from '@nestjs/common';
import * as webPush from 'web-push';
import { PrismaService } from '../prisma/prisma.service';
import { PushSubscriptionDto } from './dto/push-subscription.dto';

interface VapidKeys {
  publicKey: string;
  privateKey: string;
}

interface StoredSubscription {
  endpoint: string;
  keys: {
    p256dh: string;
    auth: string;
  };
  userAgent?: string;
  subscribedAt: string;
}

const VAPID_SETTING_KEY = '__VAPID_KEYS__';
const SUBSCRIPTIONS_SETTING_KEY = '__PUSH_SUBSCRIPTIONS__';

@Injectable()
export class PushNotificationsService implements OnModuleInit {
  private readonly logger = new Logger(PushNotificationsService.name);
  private vapidKeys: VapidKeys | null = null;

  constructor(private readonly prisma: PrismaService) {}

  async onModuleInit() {
    await this.initVapidKeys();
  }

  private async initVapidKeys() {
    try {
      const setting = await this.prisma.appSetting.findUnique({
        where: { key: VAPID_SETTING_KEY },
      });

      if (setting && setting.value) {
        this.vapidKeys = JSON.parse(setting.value);
      } else {
        const generated = webPush.generateVAPIDKeys();
        this.vapidKeys = {
          publicKey: generated.publicKey,
          privateKey: generated.privateKey,
        };

        await this.prisma.appSetting.upsert({
          where: { key: VAPID_SETTING_KEY },
          update: { value: JSON.stringify(this.vapidKeys) },
          create: {
            key: VAPID_SETTING_KEY,
            value: JSON.stringify(this.vapidKeys),
          },
        });
        this.logger.log('Generated and stored new VAPID keys for Web Push');
      }

      if (this.vapidKeys) {
        webPush.setVapidDetails(
          'mailto:admin@servermanagerbilling.local',
          this.vapidKeys.publicKey,
          this.vapidKeys.privateKey,
        );
        this.logger.log('Web Push initialized with VAPID');
      }
    } catch (err) {
      this.logger.error('Failed to initialize VAPID keys', err);
    }
  }

  async getVapidPublicKey(): Promise<string> {
    if (!this.vapidKeys) {
      await this.initVapidKeys();
    }
    return this.vapidKeys?.publicKey || '';
  }

  private async getSubscriptions(): Promise<StoredSubscription[]> {
    try {
      const setting = await this.prisma.appSetting.findUnique({
        where: { key: SUBSCRIPTIONS_SETTING_KEY },
      });
      if (!setting || !setting.value) {
        return [];
      }
      return JSON.parse(setting.value);
    } catch {
      return [];
    }
  }

  private async saveSubscriptions(subs: StoredSubscription[]): Promise<void> {
    await this.prisma.appSetting.upsert({
      where: { key: SUBSCRIPTIONS_SETTING_KEY },
      update: { value: JSON.stringify(subs) },
      create: {
        key: SUBSCRIPTIONS_SETTING_KEY,
        value: JSON.stringify(subs),
      },
    });
  }

  async subscribe(subDto: PushSubscriptionDto): Promise<{ success: boolean; total: number }> {
    const list = await this.getSubscriptions();
    const existingIndex = list.findIndex((s) => s.endpoint === subDto.endpoint);

    const newSub: StoredSubscription = {
      endpoint: subDto.endpoint,
      keys: subDto.keys,
      userAgent: subDto.userAgent,
      subscribedAt: new Date().toISOString(),
    };

    if (existingIndex >= 0) {
      list[existingIndex] = newSub;
    } else {
      list.push(newSub);
    }

    await this.saveSubscriptions(list);
    this.logger.log(`Saved push subscription. Total active: ${list.length}`);
    return { success: true, total: list.length };
  }

  async unsubscribe(endpoint: string): Promise<{ success: boolean; total: number }> {
    const list = await this.getSubscriptions();
    const filtered = list.filter((s) => s.endpoint !== endpoint);
    await this.saveSubscriptions(filtered);
    this.logger.log(`Removed push subscription. Total active: ${filtered.length}`);
    return { success: true, total: filtered.length };
  }

  async sendPushNotification(payload: {
    title: string;
    body: string;
    icon?: string;
    badge?: string;
    tag?: string;
    data?: any;
  }): Promise<{ sent: number; failed: number }> {
    if (!this.vapidKeys) {
      await this.initVapidKeys();
      if (!this.vapidKeys) {
        return { sent: 0, failed: 0 };
      }
    }

    const subscriptions = await this.getSubscriptions();
    if (subscriptions.length === 0) {
      return { sent: 0, failed: 0 };
    }

    const stringPayload = JSON.stringify({
      title: payload.title,
      body: payload.body,
      icon: payload.icon || '/pwa-192x192.png',
      badge: payload.badge || '/pwa-192x192.png',
      tag: payload.tag || 'server-admin-push',
      data: payload.data || {},
    });

    let sent = 0;
    let failed = 0;
    const deadEndpoints: string[] = [];

    await Promise.all(
      subscriptions.map(async (sub) => {
        try {
          await webPush.sendNotification(
            {
              endpoint: sub.endpoint,
              keys: sub.keys,
            },
            stringPayload,
            {
              TTL: 60 * 60 * 24, // 24 hours
              urgency: 'high',
            },
          );
          sent++;
        } catch (err: any) {
          failed++;
          this.logger.warn(`Push to ${sub.endpoint.slice(0, 35)}... failed: ${err.message}`);
          if (err.statusCode === 404 || err.statusCode === 410) {
            deadEndpoints.push(sub.endpoint);
          }
        }
      }),
    );

    if (deadEndpoints.length > 0) {
      const remaining = subscriptions.filter((s) => !deadEndpoints.includes(s.endpoint));
      await this.saveSubscriptions(remaining);
      this.logger.log(`Cleaned up ${deadEndpoints.length} expired subscriptions`);
    }

    this.logger.log(`Push notification sent: ${sent} succeeded, ${failed} failed`);
    return { sent, failed };
  }
}
