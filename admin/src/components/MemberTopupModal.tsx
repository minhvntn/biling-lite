import { useEffect, useRef, useState } from 'react';
import { io, Socket } from 'socket.io-client';
import { WS_BASE_URL } from '../lib/config';
import { MemberTopupRequestItem } from '../types/member';
import { approveTopupRequest, fetchPendingTopupRequests, rejectTopupRequest } from '../api/members';
import { useWebPush } from '../lib/useWebPush';

export function MemberTopupModal() {
  const [requests, setRequests] = useState<MemberTopupRequestItem[]>([]);
  const [currentIndex, setCurrentIndex] = useState(0);
  const [isProcessing, setIsProcessing] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [isRejectingWithNote, setIsRejectingWithNote] = useState(false);
  const [rejectNote, setRejectNote] = useState('');
  const [isMinimized, setIsMinimized] = useState(false);
  const [hasAudioPermission, setHasAudioPermission] = useState(false);

  const { isSubscribed, subscribeToPush } = useWebPush();

  const audioRef = useRef<HTMLAudioElement | null>(null);
  const socketRef = useRef<Socket | null>(null);
  const handledIdsRef = useRef<Set<string>>(new Set());

  // Initialize audio
  useEffect(() => {
    const audio = new Audio('/audio/member-top-up.mp3');
    audio.preload = 'auto';
    audioRef.current = audio;

    // Check notification permission on mount
    if ('Notification' in window && Notification.permission === 'granted') {
      setHasAudioPermission(true);
    }
  }, []);

  const playNotificationSound = () => {
    try {
      if (audioRef.current) {
        audioRef.current.currentTime = 0;
        const playPromise = audioRef.current.play();
        if (playPromise !== undefined) {
          playPromise.catch((err) => {
            console.log('[MemberTopupModal] Audio autoplay prevented:', err);
          });
        }
      }
    } catch {
      // Ignore audio error
    }

    // Trigger mobile vibration if supported
    if ('vibrate' in navigator) {
      try {
        navigator.vibrate([250, 100, 250]);
      } catch {
        // Ignore
      }
    }
  };

  const showBrowserNotification = (req: MemberTopupRequestItem) => {
    if ('Notification' in window && Notification.permission === 'granted') {
      try {
        const title = '💰 Yêu cầu nạp tiền hội viên';
        const memberDisplayName = req.fullName ? `${req.fullName} (${req.username})` : req.username;
        const machine = req.pcName || req.agentId || 'Máy trạm';
        const formattedAmount = Number(req.amount || 0).toLocaleString('vi-VN');

        const notif = new Notification(title, {
          body: `Hội viên: ${memberDisplayName}\nSố tiền: ${formattedAmount} đ\nMáy: ${machine}`,
          icon: '/pwa-192x192.png',
          badge: '/pwa-192x192.png',
          tag: `topup-${req.requestId}`,
        });

        notif.onclick = () => {
          window.focus();
          setIsMinimized(false);
          notif.close();
        };
      } catch {
        // Ignore notification errors
      }
    }
  };

  const requestPermissions = async () => {
    // 1. Play audio to unlock audio context on mobile
    if (audioRef.current) {
      audioRef.current.play().catch(() => {});
      setHasAudioPermission(true);
    }
    // 2. Subscribe to Web Push for lock-screen notifications
    await subscribeToPush();
  };

  const handleNewRequest = (item: MemberTopupRequestItem) => {
    if (!item || !item.requestId) return;

    setRequests((prev) => {
      if (prev.some((r) => r.requestId === item.requestId)) {
        return prev;
      }
      return [...prev, item];
    });

    if (!handledIdsRef.current.has(item.requestId)) {
      handledIdsRef.current.add(item.requestId);
      playNotificationSound();
      showBrowserNotification(item);
    }

    setIsMinimized(false);
  };

  const handleResolvedRequest = (item: { requestId: string }) => {
    if (!item?.requestId) return;
    setRequests((prev) => {
      const next = prev.filter((r) => r.requestId !== item.requestId);
      if (next.length === 0) {
        setIsRejectingWithNote(false);
        setRejectNote('');
      }
      return next;
    });
  };

  // Sync pending topup requests from backend
  const loadPendingRequests = async () => {
    try {
      const res = await fetchPendingTopupRequests();
      if (res?.items && Array.isArray(res.items)) {
        const pendingItems = res.items.filter((item: MemberTopupRequestItem) => item.status === 'PENDING');
        setRequests(pendingItems);
        
        // Notify for any unhandled
        pendingItems.forEach((item: MemberTopupRequestItem) => {
          if (!handledIdsRef.current.has(item.requestId)) {
            handledIdsRef.current.add(item.requestId);
            playNotificationSound();
            showBrowserNotification(item);
          }
        });
      }
    } catch (err) {
      console.warn('[MemberTopupModal] Failed to fetch pending requests:', err);
    }
  };

  // Setup Socket.IO
  useEffect(() => {
    loadPendingRequests();

    const socket = io(`${WS_BASE_URL}/billing`, {
      transports: ['websocket'],
    });
    socketRef.current = socket;

    socket.on('connect', () => {
      loadPendingRequests();
    });

    socket.on('member.topup.requested', (data: MemberTopupRequestItem) => {
      handleNewRequest(data);
    });

    socket.on('member.topup.resolved', (data: { requestId: string }) => {
      handleResolvedRequest(data);
    });

    return () => {
      socket.disconnect();
      socketRef.current = null;
    };
  }, []);

  // Ensure currentIndex is in bounds
  useEffect(() => {
    if (currentIndex >= requests.length && requests.length > 0) {
      setCurrentIndex(requests.length - 1);
    }
  }, [requests.length, currentIndex]);

  const currentRequest = requests[currentIndex] || requests[0];

  const handleApprove = async () => {
    if (!currentRequest || isProcessing) return;
    setIsProcessing(true);
    setErrorMessage(null);

    try {
      await approveTopupRequest(currentRequest.requestId, { approvedBy: 'admin.web' });
      // Remove from list
      setRequests((prev) => prev.filter((r) => r.requestId !== currentRequest.requestId));
      setIsRejectingWithNote(false);
      setRejectNote('');
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Duyệt nạp tiền thất bại';
      setErrorMessage(msg);
      if (msg.includes('đã được xử lý') || msg.includes('Không tìm thấy') || msg.includes('409') || msg.includes('404')) {
        setTimeout(() => {
          setRequests((prev) => prev.filter((r) => r.requestId !== currentRequest.requestId));
          setErrorMessage(null);
        }, 1500);
      }
    } finally {
      setIsProcessing(false);
    }
  };

  const handleReject = async () => {
    if (!currentRequest || isProcessing) return;
    setIsProcessing(true);
    setErrorMessage(null);

    try {
      await rejectTopupRequest(currentRequest.requestId, {
        rejectedBy: 'admin.web',
        note: rejectNote.trim() || 'Từ chối bởi admin web',
      });
      // Remove from list
      setRequests((prev) => prev.filter((r) => r.requestId !== currentRequest.requestId));
      setIsRejectingWithNote(false);
      setRejectNote('');
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Từ chối nạp tiền thất bại';
      setErrorMessage(msg);
      if (msg.includes('đã được xử lý') || msg.includes('Không tìm thấy') || msg.includes('409') || msg.includes('404')) {
        setTimeout(() => {
          setRequests((prev) => prev.filter((r) => r.requestId !== currentRequest.requestId));
          setErrorMessage(null);
        }, 1500);
      }
    } finally {
      setIsProcessing(false);
    }
  };

  if (requests.length === 0) {
    return null;
  }

  const formatDateTime = (isoDate?: string) => {
    if (!isoDate) return '-';
    try {
      const d = new Date(isoDate);
      const pad = (n: number) => n.toString().padStart(2, '0');
      return `${pad(d.getDate())}-${pad(d.getMonth() + 1)}-${d.getFullYear()} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
    } catch {
      return isoDate;
    }
  };

  // Minimized floating banner / notification trigger
  if (isMinimized) {
    return (
      <div
        onClick={() => setIsMinimized(false)}
        style={{
          position: 'fixed',
          bottom: '24px',
          right: '24px',
          backgroundColor: '#16a34a',
          color: '#ffffff',
          padding: '12px 18px',
          borderRadius: '30px',
          boxShadow: '0 8px 24px rgba(22, 163, 74, 0.45)',
          display: 'flex',
          alignItems: 'center',
          gap: '10px',
          cursor: 'pointer',
          zIndex: 99999,
          fontWeight: 'bold',
          fontSize: '14px',
          animation: 'pulse 1.8s infinite',
        }}
      >
        <span style={{ fontSize: '18px' }}>💰</span>
        <span>Có {requests.length} yêu cầu nạp tiền chờ duyệt</span>
      </div>
    );
  }

  const memberDisplayName = currentRequest.fullName
    ? `${currentRequest.fullName} (${currentRequest.username})`
    : currentRequest.username;

  const machineLabel = currentRequest.pcName || currentRequest.agentId || 'Máy trạm';

  return (
    <div
      className="modal-backdrop"
      style={{
        position: 'fixed',
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        backgroundColor: 'rgba(15, 23, 42, 0.72)',
        backdropFilter: 'blur(5px)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        zIndex: 99999,
        padding: '16px',
      }}
    >
      <div
        className="modal-box"
        style={{
          backgroundColor: '#ffffff',
          borderRadius: '18px',
          boxShadow: '0 25px 50px -12px rgba(0, 0, 0, 0.35)',
          width: '100%',
          maxWidth: '460px',
          overflow: 'hidden',
          display: 'flex',
          flexDirection: 'column',
          border: '1px solid rgba(226, 232, 240, 0.8)',
          animation: 'modalFadeIn 0.2s ease-out',
        }}
      >
        {/* Modal Header */}
        <div
          style={{
            padding: '14px 20px',
            backgroundColor: '#f8fafc',
            borderBottom: '1px solid #e2e8f0',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
          }}
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <span style={{ fontSize: '20px' }}>🖥️</span>
            <span style={{ fontWeight: '700', fontSize: '16px', color: '#0f172a' }}>
              Yêu cầu nạp tiền hội viên
            </span>
          </div>

          <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
            {requests.length > 1 && (
              <span
                style={{
                  backgroundColor: '#fee2e2',
                  color: '#dc2626',
                  fontSize: '12px',
                  fontWeight: '700',
                  padding: '2px 8px',
                  borderRadius: '12px',
                }}
              >
                {currentIndex + 1}/{requests.length}
              </span>
            )}
            <button
              onClick={() => setIsMinimized(true)}
              title="Thu nhỏ"
              style={{
                background: 'transparent',
                border: 'none',
                color: '#64748b',
                cursor: 'pointer',
                fontSize: '18px',
                padding: '4px 8px',
                lineHeight: 1,
              }}
            >
              _
            </button>
          </div>
        </div>

        {/* Permission tip if not granted */}
        {(!hasAudioPermission || !isSubscribed) && (
          <div
            onClick={requestPermissions}
            style={{
              backgroundColor: '#eff6ff',
              borderBottom: '1px solid #bfdbfe',
              color: '#1d4ed8',
              fontSize: '12px',
              padding: '8px 16px',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              cursor: 'pointer',
            }}
          >
            <span>🔔 Bấm để bật chuông & nhận thông báo khi tắt màn hình</span>
            <span style={{ fontWeight: 'bold' }}>Bật ngay →</span>
          </div>
        )}

        {/* Modal Body */}
        <div
          style={{
            padding: '20px',
            display: 'flex',
            flexDirection: 'column',
            gap: '14px',
            color: '#1e293b',
          }}
        >
          <h2
            style={{
              margin: '0 0 4px 0',
              fontSize: '20px',
              fontWeight: '800',
              color: '#0f172a',
            }}
          >
            Hội viên gửi yêu cầu nạp tiền
          </h2>

          <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', fontSize: '15px' }}>
            <div style={{ display: 'flex', alignItems: 'baseline', gap: '6px' }}>
              <span style={{ color: '#64748b', minWidth: '95px' }}>Hội viên:</span>
              <strong style={{ color: '#0f172a' }}>{memberDisplayName}</strong>
            </div>

            <div style={{ display: 'flex', alignItems: 'baseline', gap: '6px' }}>
              <span style={{ color: '#64748b', minWidth: '95px' }}>Số tiền nạp:</span>
              <span
                style={{
                  color: '#16a34a',
                  fontWeight: '800',
                  fontSize: '20px',
                  letterSpacing: '0.3px',
                }}
              >
                {Number(currentRequest.amount || 0).toLocaleString('vi-VN')} VND
              </span>
            </div>

            <div style={{ display: 'flex', alignItems: 'baseline', gap: '6px' }}>
              <span style={{ color: '#64748b', minWidth: '95px' }}>Máy trạm:</span>
              <strong style={{ color: '#334155' }}>{machineLabel}</strong>
            </div>

            <div style={{ display: 'flex', alignItems: 'baseline', gap: '6px' }}>
              <span style={{ color: '#64748b', minWidth: '95px' }}>Ghi chú:</span>
              <span style={{ color: '#64748b' }}>{currentRequest.note?.trim() || '-'}</span>
            </div>

            <div style={{ display: 'flex', alignItems: 'baseline', gap: '6px' }}>
              <span style={{ color: '#64748b', minWidth: '95px' }}>Gửi lúc:</span>
              <span style={{ color: '#64748b', fontSize: '14px' }}>
                {formatDateTime(currentRequest.requestedAt)}
              </span>
            </div>
          </div>

          {/* Multiple requests pager */}
          {requests.length > 1 && (
            <div
              style={{
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
                backgroundColor: '#f1f5f9',
                padding: '6px 12px',
                borderRadius: '8px',
                marginTop: '4px',
              }}
            >
              <button
                type="button"
                disabled={currentIndex === 0}
                onClick={() => setCurrentIndex((idx) => Math.max(0, idx - 1))}
                style={{
                  background: 'transparent',
                  border: 'none',
                  color: currentIndex === 0 ? '#cbd5e1' : '#2563eb',
                  fontWeight: 'bold',
                  cursor: currentIndex === 0 ? 'default' : 'pointer',
                  padding: '4px 8px',
                }}
              >
                ← Trước
              </button>
              <span style={{ fontSize: '13px', color: '#475569' }}>
                Yêu cầu {currentIndex + 1} / {requests.length}
              </span>
              <button
                type="button"
                disabled={currentIndex >= requests.length - 1}
                onClick={() => setCurrentIndex((idx) => Math.min(requests.length - 1, idx + 1))}
                style={{
                  background: 'transparent',
                  border: 'none',
                  color: currentIndex >= requests.length - 1 ? '#cbd5e1' : '#2563eb',
                  fontWeight: 'bold',
                  cursor: currentIndex >= requests.length - 1 ? 'default' : 'pointer',
                  padding: '4px 8px',
                }}
              >
                Sau →
              </button>
            </div>
          )}

          {/* Reject note expander */}
          {isRejectingWithNote && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '6px', marginTop: '6px' }}>
              <label htmlFor="reject-note-input" style={{ fontSize: '13px', fontWeight: '600', color: '#dc2626' }}>
                Lý do từ chối:
              </label>
              <input
                id="reject-note-input"
                type="text"
                value={rejectNote}
                onChange={(e) => setRejectNote(e.target.value)}
                placeholder="Ví dụ: Chưa nhận được tiền chuyển khoản..."
                style={{
                  padding: '8px 12px',
                  borderRadius: '6px',
                  border: '1px solid #f87171',
                  fontSize: '14px',
                  outline: 'none',
                }}
                autoFocus
              />
            </div>
          )}

          {/* Error Message */}
          {errorMessage && (
            <div
              style={{
                backgroundColor: '#fef2f2',
                color: '#b91c1c',
                padding: '8px 12px',
                borderRadius: '6px',
                fontSize: '13px',
                border: '1px solid #fecaca',
              }}
            >
              ⚠️ {errorMessage}
            </div>
          )}
        </div>

        {/* Modal Actions */}
        <div
          style={{
            padding: '14px 20px',
            backgroundColor: '#f8fafc',
            borderTop: '1px solid #e2e8f0',
            display: 'flex',
            justifyContent: 'flex-end',
            gap: '12px',
          }}
        >
          {isRejectingWithNote ? (
            <>
              <button
                type="button"
                onClick={() => setIsRejectingWithNote(false)}
                disabled={isProcessing}
                style={{
                  backgroundColor: '#e2e8f0',
                  color: '#475569',
                  border: 'none',
                  borderRadius: '8px',
                  padding: '10px 18px',
                  fontWeight: '600',
                  fontSize: '14px',
                  cursor: 'pointer',
                }}
              >
                Quay lại
              </button>
              <button
                type="button"
                onClick={handleReject}
                disabled={isProcessing}
                style={{
                  backgroundColor: '#dc2626',
                  color: '#ffffff',
                  border: 'none',
                  borderRadius: '8px',
                  padding: '10px 20px',
                  fontWeight: '700',
                  fontSize: '14px',
                  cursor: isProcessing ? 'wait' : 'pointer',
                }}
              >
                {isProcessing ? 'Đang xử lý...' : 'Xác nhận Từ chối'}
              </button>
            </>
          ) : (
            <>
              <button
                type="button"
                onClick={() => {
                  setIsRejectingWithNote(true);
                }}
                disabled={isProcessing}
                style={{
                  backgroundColor: '#dc2626',
                  color: '#ffffff',
                  border: 'none',
                  borderRadius: '8px',
                  padding: '10px 24px',
                  fontWeight: '700',
                  fontSize: '15px',
                  cursor: isProcessing ? 'wait' : 'pointer',
                  minWidth: '100px',
                }}
              >
                Hủy
              </button>

              <button
                type="button"
                onClick={handleApprove}
                disabled={isProcessing}
                style={{
                  backgroundColor: '#22c55e',
                  color: '#ffffff',
                  border: 'none',
                  borderRadius: '8px',
                  padding: '10px 28px',
                  fontWeight: '700',
                  fontSize: '15px',
                  cursor: isProcessing ? 'wait' : 'pointer',
                  minWidth: '120px',
                  boxShadow: '0 4px 12px rgba(34, 197, 94, 0.35)',
                }}
              >
                {isProcessing ? 'Đang xử lý...' : 'Chấp nhận'}
              </button>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
