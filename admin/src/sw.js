import { cleanupOutdatedCaches, precacheAndRoute } from 'workbox-precaching';

cleanupOutdatedCaches();
precacheAndRoute(self.__WB_MANIFEST || []);

// Listen for Web Push events from backend (when phone screen is OFF / locked)
self.addEventListener('push', (event) => {
  if (!event.data) return;

  try {
    const payload = event.data.json();
    const title = payload.title || '💰 Yêu cầu nạp tiền hội viên';
    const options = {
      body: payload.body || 'Hội viên vừa gửi yêu cầu nạp tiền',
      icon: payload.icon || '/pwa-192x192.png',
      badge: payload.badge || '/pwa-192x192.png',
      tag: payload.tag || `topup-${Date.now()}`,
      data: payload.data || { url: '/pcs' },
      vibrate: [300, 100, 300, 100, 300],
      requireInteraction: true,
    };

    event.waitUntil(self.registration.showNotification(title, options));
  } catch (err) {
    console.error('[SW] Error parsing push data:', err);
    event.waitUntil(
      self.registration.showNotification('Server Admin', {
        body: event.data.text() || 'Có thông báo mới',
        icon: '/pwa-192x192.png',
      })
    );
  }
});

// Handle notification click on phone lock screen
self.addEventListener('notificationclick', (event) => {
  event.notification.close();

  const targetUrl = event.notification.data?.url || '/pcs';

  event.waitUntil(
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((clientList) => {
      for (const client of clientList) {
        if (client.url.includes(self.location.origin) && 'focus' in client) {
          client.focus();
          if ('navigate' in client && targetUrl) {
            client.navigate(targetUrl);
          }
          return;
        }
      }
      if (self.clients.openWindow) {
        return self.clients.openWindow(targetUrl);
      }
    })
  );
});
