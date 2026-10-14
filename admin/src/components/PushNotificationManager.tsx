import { useState } from 'react';
import { useWebPush } from '../lib/useWebPush';

export function PushNotificationManager() {
  const {
    isSupported,
    isSubscribed,
    isLoading,
    statusMessage,
    subscribeToPush,
    testPush,
  } = useWebPush();

  const [isOpen, setIsOpen] = useState(false);

  if (!isSupported) {
    return null;
  }

  return (
    <>
      {/* Floating Bell Button in bottom-left */}
      <button
        onClick={() => setIsOpen(!isOpen)}
        title="Cài đặt thông báo đẩy (Web Push)"
        style={{
          position: 'fixed',
          bottom: '20px',
          left: '20px',
          backgroundColor: isSubscribed ? '#16a34a' : '#2563eb',
          color: '#ffffff',
          border: 'none',
          borderRadius: '50%',
          width: '46px',
          height: '46px',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          boxShadow: '0 4px 14px rgba(0, 0, 0, 0.25)',
          cursor: 'pointer',
          zIndex: 9990,
          fontSize: '20px',
        }}
      >
        {isSubscribed ? '🔔' : '🔕'}
      </button>

      {/* Settings Dialog */}
      {isOpen && (
        <div
          onClick={() => setIsOpen(false)}
          style={{
            position: 'fixed',
            top: 0,
            left: 0,
            right: 0,
            bottom: 0,
            backgroundColor: 'rgba(0, 0, 0, 0.4)',
            backdropFilter: 'blur(3px)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            zIndex: 9999,
            padding: '16px',
          }}
        >
          <div
            onClick={(e) => e.stopPropagation()}
            style={{
              backgroundColor: '#ffffff',
              borderRadius: '16px',
              maxWidth: '380px',
              width: '100%',
              padding: '20px',
              boxShadow: '0 20px 25px -5px rgba(0, 0, 0, 0.2)',
              display: 'flex',
              flexDirection: 'column',
              gap: '14px',
              color: '#0f172a',
            }}
          >
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <h3 style={{ margin: 0, fontSize: '17px', fontWeight: '700' }}>
                🔔 Thông báo đẩy (Web Push)
              </h3>
              <button
                onClick={() => setIsOpen(false)}
                style={{
                  background: 'transparent',
                  border: 'none',
                  color: '#64748b',
                  fontSize: '20px',
                  cursor: 'pointer',
                  padding: 0,
                }}
              >
                &times;
              </button>
            </div>

            <p style={{ margin: 0, fontSize: '13px', color: '#475569', lineHeight: '1.4' }}>
              Nhận thông báo và chuông báo ngay cả khi <strong>tắt màn hình điện thoại</strong> hoặc để app chạy ngầm.
            </p>

            <div
              style={{
                backgroundColor: isSubscribed ? '#f0fdf4' : '#fef2f2',
                border: `1px solid ${isSubscribed ? '#bbf7d0' : '#fecaca'}`,
                borderRadius: '8px',
                padding: '10px 12px',
                fontSize: '13px',
                display: 'flex',
                alignItems: 'center',
                gap: '8px',
              }}
            >
              <span>{isSubscribed ? '✅' : '⚠️'}</span>
              <span style={{ color: isSubscribed ? '#15803d' : '#b91c1c', fontWeight: '600' }}>
                {isSubscribed
                  ? 'Đã bật thông báo màn hình khóa'
                  : 'Chưa bật thông báo màn hình khóa'}
              </span>
            </div>

            {statusMessage && (
              <div
                style={{
                  fontSize: '12px',
                  color: '#2563eb',
                  backgroundColor: '#eff6ff',
                  padding: '8px 10px',
                  borderRadius: '6px',
                }}
              >
                ℹ️ {statusMessage}
              </div>
            )}

            <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', marginTop: '4px' }}>
              {!isSubscribed ? (
                <button
                  onClick={() => subscribeToPush()}
                  disabled={isLoading}
                  style={{
                    backgroundColor: '#16a34a',
                    color: '#ffffff',
                    border: 'none',
                    borderRadius: '8px',
                    padding: '11px',
                    fontWeight: '700',
                    fontSize: '14px',
                    cursor: isLoading ? 'wait' : 'pointer',
                  }}
                >
                  {isLoading ? 'Đang kích hoạt...' : 'Bật thông báo ngay'}
                </button>
              ) : (
                <button
                  onClick={() => testPush()}
                  disabled={isLoading}
                  style={{
                    backgroundColor: '#2563eb',
                    color: '#ffffff',
                    border: 'none',
                    borderRadius: '8px',
                    padding: '11px',
                    fontWeight: '700',
                    fontSize: '14px',
                    cursor: isLoading ? 'wait' : 'pointer',
                  }}
                >
                  {isLoading ? 'Đang gửi...' : '📲 Gửi thử nghiệm (Tắt màn hình test)'}
                </button>
              )}

              <button
                onClick={() => setIsOpen(false)}
                style={{
                  backgroundColor: '#f1f5f9',
                  color: '#475569',
                  border: 'none',
                  borderRadius: '8px',
                  padding: '9px',
                  fontWeight: '600',
                  fontSize: '13px',
                  cursor: 'pointer',
                }}
              >
                Đóng
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
