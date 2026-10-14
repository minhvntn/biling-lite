import { useCallback, useEffect, useState } from 'react';
import {
  fetchVapidPublicKey,
  sendTestPushNotification,
  subscribePushNotification,
  urlBase64ToUint8Array,
} from '../api/notifications';

export function useWebPush() {
  const [isSupported, setIsSupported] = useState(false);
  const [isSubscribed, setIsSubscribed] = useState(false);
  const [permission, setPermission] = useState<NotificationPermission>('default');
  const [isLoading, setIsLoading] = useState(false);
  const [statusMessage, setStatusMessage] = useState<string | null>(null);

  useEffect(() => {
    const supported =
      typeof window !== 'undefined' &&
      'serviceWorker' in navigator &&
      'PushManager' in window &&
      'Notification' in window;

    setIsSupported(supported);

    if (supported) {
      setPermission(Notification.permission);
      checkExistingSubscription();
    }
  }, []);

  const checkExistingSubscription = async () => {
    try {
      if (!('serviceWorker' in navigator)) return;
      const reg = await navigator.serviceWorker.ready;
      const sub = await reg.pushManager.getSubscription();
      setIsSubscribed(sub !== null);
    } catch {
      // Ignore
    }
  };

  const subscribeToPush = useCallback(async (): Promise<boolean> => {
    if (!isSupported) {
      setStatusMessage('Trình duyệt này không hỗ trợ Web Push Notification');
      return false;
    }

    setIsLoading(true);
    setStatusMessage(null);

    try {
      // 1. Request Notification permission
      const perm = await Notification.requestPermission();
      setPermission(perm);
      if (perm !== 'granted') {
        setStatusMessage('Bạn chưa cấp quyền nhận thông báo cho trang web');
        setIsLoading(false);
        return false;
      }

      // 2. Ensure Service Worker is ready
      const reg = await navigator.serviceWorker.ready;

      // 3. Fetch VAPID public key
      const { publicKey } = await fetchVapidPublicKey();
      if (!publicKey) {
        throw new Error('Không lấy được VAPID public key từ server');
      }

      // 4. Subscribe to PushManager
      const convertedVapidKey = urlBase64ToUint8Array(publicKey);
      let sub = await reg.pushManager.getSubscription();

      if (!sub) {
        sub = await reg.pushManager.subscribe({
          userVisibleOnly: true,
          applicationServerKey: convertedVapidKey as any,
        });
      }

      // 5. Send subscription to backend
      const subJson = sub.toJSON();
      if (!subJson.endpoint || !subJson.keys?.p256dh || !subJson.keys?.auth) {
        throw new Error('Định dạng Push Subscription không hợp lệ');
      }

      await subscribePushNotification({
        endpoint: subJson.endpoint,
        keys: {
          p256dh: subJson.keys.p256dh,
          auth: subJson.keys.auth,
        },
        userAgent: navigator.userAgent,
      });

      setIsSubscribed(true);
      setStatusMessage('Đã đăng ký nhận thông báo đẩy thành công!');
      return true;
    } catch (err: any) {
      console.error('[WebPush] Subscription error:', err);
      setStatusMessage(err.message || 'Đăng ký thông báo thất bại');
      return false;
    } finally {
      setIsLoading(false);
    }
  }, [isSupported]);

  const testPush = useCallback(async () => {
    try {
      setIsLoading(true);
      await sendTestPushNotification();
      setStatusMessage('Đã gửi thông báo thử nghiệm! Kiểm tra màn hình điện thoại.');
    } catch (err: any) {
      setStatusMessage(`Lỗi gửi test: ${err.message}`);
    } finally {
      setIsLoading(false);
    }
  }, []);

  return {
    isSupported,
    isSubscribed,
    permission,
    isLoading,
    statusMessage,
    subscribeToPush,
    testPush,
  };
}
