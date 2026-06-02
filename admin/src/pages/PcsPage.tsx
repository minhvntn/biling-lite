import { useEffect, useMemo, useState } from 'react';
import { io } from 'socket.io-client';
import { lockPc, openPc, guestOpenPc, shutdownPc, restartPc, wakePc } from '../api/commands';
import { fetchPcs } from '../api/pcs';

import {
  fetchServiceItems,
  createPcServiceOrder,
  payPcServiceOrders,
  fetchPcServiceOrders,
  ServiceItem,
  PcServiceOrder,
} from '../api/services';
import { TopNav } from '../components/TopNav';
import { WS_BASE_URL } from '../lib/config';
import { PcListItem } from '../types/pc';

function formatDuration(totalSeconds: number): string {
  if (totalSeconds <= 0) return '00:00';
  const totalMinutes = Math.floor(totalSeconds / 60);
  const hours = Math.floor(totalMinutes / 60);
  const minutes = totalMinutes % 60;
  const paddedHours = hours.toString().padStart(2, '0');
  const paddedMinutes = minutes.toString().padStart(2, '0');
  return `${paddedHours}:${paddedMinutes}`;
}

function getEffectiveElapsedSeconds(pc: PcListItem, currentTick: number): number {
  if (!pc.activeSession) return 0;
  return pc.activeSession.elapsedSeconds + currentTick;
}

function formatClock(isoDate: string | null): string {
  if (!isoDate) {
    return '-';
  }
  return new Date(isoDate).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}

function formatMoney(amount: number): string {
  return amount.toLocaleString('vi-VN') + ' đ';
}

function statusText(status: string): string {
  switch (status) {
    case 'IN_USE':
      return 'Đang dùng';
    case 'LOCKED':
      return 'Đang khóa';
    case 'ONLINE':
      return 'Sẵn sàng';
    case 'BOOTING':
      return 'Đang khởi động';
    default:
      return 'Đang tắt';
  }
}

function statusClass(status: string): string {
  switch (status) {
    case 'IN_USE':
      return 'status-cell status-in-use';
    case 'LOCKED':
      return 'status-cell status-locked';
    case 'ONLINE':
      return 'status-cell status-online';
    case 'BOOTING':
      return 'status-cell status-booting';
    default:
      return 'status-cell status-offline';
  }
}

function getPcIconPath(pc: PcListItem): string {
  if (pc.status === 'ONLINE') return '/pc-available.svg';
  if (pc.status === 'IN_USE') {
    if (pc.activeAdmin) return '/pc-admin.svg';
    if (pc.activeMember) return '/pc-blue-user.svg';
    return '/pc-guest.svg';
  }
  if (pc.status === 'LOCKED' || pc.status === 'OFFLINE') return '/pc-offline.svg';
  return '/pc-default.svg';
}

export function PcsPage() {
  type PowerActionType = 'restart' | 'shutdown';
  const [pcs, setPcs] = useState<PcListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [tick, setTick] = useState(0);
  const [lastUpdatedAt, setLastUpdatedAt] = useState<string | null>(null);
  const [pendingPcActions, setPendingPcActions] = useState<Record<string, boolean>>({});
  const [actionMessage, setActionMessage] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<'ALL' | 'IN_USE' | 'LOCKED' | 'ONLINE' | 'OFFLINE' | 'BOOTING'>('ALL');
  const [search, setSearch] = useState('');

  // Service items & Unpaid orders
  const [serviceItems, setServiceItems] = useState<ServiceItem[]>([]);
  const [selectedPc, setSelectedPc] = useState<PcListItem | null>(null);
  const [showDrawer, setShowDrawer] = useState(false);
  const [drawerTab, setDrawerTab] = useState<'session' | 'service' | 'topup'>('session');
  
  // Drawer data & action loading states
  const [unpaidOrders, setUnpaidOrders] = useState<PcServiceOrder[]>([]);
  const [loadingOrders, setLoadingOrders] = useState(false);
  const [drawerError, setDrawerError] = useState<string | null>(null);
  const [drawerSuccess, setDrawerSuccess] = useState<string | null>(null);
  const [actionPending, setActionPending] = useState(false);
  const [powerActionConfirm, setPowerActionConfirm] = useState<{
    action: PowerActionType;
    pcId: string;
    pcName: string;
  } | null>(null);

  const [guestAmount, setGuestAmount] = useState<string>('0');

  // Order service form
  const [selectedServiceId, setSelectedServiceId] = useState('');
  const [serviceQty, setServiceQty] = useState(1);
  const [serviceNote, setServiceNote] = useState('');

  // Topup member form
  const [topupAmountValue, setTopupAmountValue] = useState('50000');

  const loadPcs = async () => {
    try {
      const data = await fetchPcs();
      setPcs(data.items);
      setLastUpdatedAt(data.serverTime);
      setTick(0);
    } catch (loadError) {
      const msg = loadError instanceof Error ? loadError.message : 'Unknown error';
      setError(msg);
    } finally {
      setLoading(false);
    }
  };

  // Initial load
  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    fetchPcs(controller.signal)
      .then((data) => {
        setPcs(data.items);
        setLastUpdatedAt(data.serverTime);
        setTick(0);
      })
      .catch((loadError) => {
        if (controller.signal.aborted) return;
        setError(loadError instanceof Error ? loadError.message : 'Unknown error');
      })
      .finally(() => setLoading(false));

    // Pre-fetch service items once
    fetchServiceItems()
      .then((res) => {
        setServiceItems(res.items);
        if (res.items.length > 0) {
          setSelectedServiceId(res.items[0].id);
        }
      })
      .catch((err) => console.error('Failed to load service items', err));

    return () => controller.abort();
  }, []);

  // Web socket sync
  useEffect(() => {
    const socket = io(`${WS_BASE_URL}/billing`, {
      transports: ['websocket'],
    });

    socket.on('pc.status.changed', () => {
      void loadPcs();
    });
    socket.on('command.updated', () => {
      void loadPcs();
    });

    return () => {
      socket.disconnect();
    };
  }, []);

  // Sync tick for elapsed seconds
  useEffect(() => {
    const interval = window.setInterval(() => setTick((v) => v + 1), 1000);
    return () => window.clearInterval(interval);
  }, []);

  // Removed Debounced Member Search

  // When drawer selected PC updates, refresh its unpaid orders
  const loadUnpaidOrders = async (pcId: string) => {
    setLoadingOrders(true);
    try {
      const res = await fetchPcServiceOrders(pcId);
      setUnpaidOrders(res.items.filter((item) => !item.isPaid));
    } catch (e) {
      console.error(e);
    } finally {
      setLoadingOrders(false);
    }
  };

  // Open action drawer for a PC
  const handleOpenDrawer = (pc: PcListItem) => {
    setSelectedPc(pc);
    setShowDrawer(true);
    setDrawerTab('session');
    setDrawerError(null);
    setDrawerSuccess(null);
    setGuestAmount('0');
    setServiceQty(1);
    setServiceNote('');

    void loadUnpaidOrders(pc.id);
  };

  // Compute stats
  const filteredPcs = useMemo(() => {
    const keyword = search.trim().toLowerCase();
    return pcs.filter((pc) => {
      const statusOk = statusFilter === 'ALL' ? true : pc.status === statusFilter;
      const keywordOk = keyword
        ? pc.name.toLowerCase().includes(keyword) ||
          pc.agentId.toLowerCase().includes(keyword)
        : true;
      return statusOk && keywordOk;
    });
  }, [pcs, search, statusFilter]);


  // Quick Action from Desktop Table
  const applyAction = async (pcId: string, action: 'open' | 'lock') => {
    setPendingPcActions((prev) => ({ ...prev, [pcId]: true }));
    setActionMessage(null);
    try {
      if (action === 'open') {
        await openPc(pcId);
      } else {
        await lockPc(pcId);
      }
      setActionMessage(`Đã gửi lệnh ${action.toUpperCase()}`);
      await loadPcs();
    } catch (applyError) {
      setActionMessage(applyError instanceof Error ? applyError.message : 'Lỗi không xác định');
    } finally {
      setPendingPcActions((prev) => ({ ...prev, [pcId]: false }));
    }
  };

  // Drawer Submit handlers
  const handleWOL = async () => {
    if (!selectedPc) return;
    setActionPending(true);
    setDrawerError(null);
    setDrawerSuccess(null);
    try {
      const mac = selectedPc.macAddress || '';
      if (!mac) {
        throw new Error('Chưa cấu hình MAC address cho máy này');
      }
      await wakePc(selectedPc.id, mac);
      setDrawerSuccess('Đã phát tín hiệu khởi động từ xa (Wake-on-LAN)');
      await loadPcs();
    } catch (e) {
      setDrawerError(e instanceof Error ? e.message : 'Khởi động thất bại');
    } finally {
      setActionPending(false);
    }
  };

  const handleOpenGuest = async () => {
    if (!selectedPc) return;
    setActionPending(true);
    setDrawerError(null);
    setDrawerSuccess(null);
    try {
      const amt = Number(guestAmount) || 0;
      await guestOpenPc(selectedPc.id, amt);
      setDrawerSuccess('Đã gửi lệnh mở máy khách vãng lai');
      await loadPcs();
      setTimeout(() => setShowDrawer(false), 800);
    } catch (e) {
      setDrawerError(e instanceof Error ? e.message : 'Mở máy thất bại');
    } finally {
      setActionPending(false);
    }
  };



  const handleLock = async () => {
    if (!selectedPc) return;
    if (!window.confirm(`Xác nhận khóa máy & thanh toán cho ${selectedPc.name}?`)) return;
    setActionPending(true);
    setDrawerError(null);
    setDrawerSuccess(null);
    try {
      await lockPc(selectedPc.id);
      setDrawerSuccess('Đã khóa máy & kết thúc phiên chơi');
      await loadPcs();
      setTimeout(() => setShowDrawer(false), 800);
    } catch (e) {
      setDrawerError(e instanceof Error ? e.message : 'Khóa máy thất bại');
    } finally {
      setActionPending(false);
    }
  };

  const executePowerAction = async () => {
    if (!powerActionConfirm) return;

    const { action, pcId } = powerActionConfirm;
    setActionPending(true);
    setDrawerError(null);
    setDrawerSuccess(null);
    try {
      if (action === 'shutdown') {
        await shutdownPc(pcId);
        setDrawerSuccess('Đã gửi lệnh tắt máy');
      } else {
        await restartPc(pcId);
        setDrawerSuccess('Đã gửi lệnh khởi động lại');
      }
      await loadPcs();
    } catch (e) {
      setDrawerError(e instanceof Error ? e.message : 'Gửi lệnh thất bại');
    } finally {
      setActionPending(false);
      setPowerActionConfirm(null);
    }
  };

  const requestPowerActionConfirm = (action: PowerActionType) => {
    if (!selectedPc) return;
    setPowerActionConfirm({
      action,
      pcId: selectedPc.id,
      pcName: selectedPc.name,
    });
  };

  const handleAddService = async () => {
    if (!selectedPc || !selectedServiceId) return;
    setActionPending(true);
    setDrawerError(null);
    setDrawerSuccess(null);
    try {
      await createPcServiceOrder(selectedPc.id, {
        serviceItemId: selectedServiceId,
        quantity: serviceQty,
        note: serviceNote.trim() || undefined,
        requestedBy: 'admin.web',
      });
      setDrawerSuccess('Đã gọi món / thêm dịch vụ thành công');
      setServiceNote('');
      setServiceQty(1);
      void loadUnpaidOrders(selectedPc.id);
      await loadPcs();
    } catch (e) {
      setDrawerError(e instanceof Error ? e.message : 'Thêm dịch vụ thất bại');
    } finally {
      setActionPending(false);
    }
  };

  const handlePayServices = async () => {
    if (!selectedPc || unpaidOrders.length === 0) return;
    if (!window.confirm('Xác nhận thanh toán toàn bộ dịch vụ cho máy này?')) return;
    setActionPending(true);
    setDrawerError(null);
    setDrawerSuccess(null);
    try {
      await payPcServiceOrders(selectedPc.id, {
        requestedBy: 'admin.web',
      });
      setDrawerSuccess('Đã thanh toán toàn bộ dịch vụ');
      setUnpaidOrders([]);
      await loadPcs();
    } catch (e) {
      setDrawerError(e instanceof Error ? e.message : 'Thanh toán dịch vụ thất bại');
    } finally {
      setActionPending(false);
    }
  };

  const handleGuestTopupSubmit = async () => {
    if (!selectedPc || !selectedPc.activeGuest) return;
    setActionPending(true);
    setDrawerError(null);
    setDrawerSuccess(null);
    try {
      const additionalAmt = Number(topupAmountValue) || 0;
      const currentAmt = selectedPc.activeGuest.prepaidAmount || 0;
      const newTotal = currentAmt + additionalAmt;
      await guestOpenPc(selectedPc.id, newTotal);
      setDrawerSuccess(`Đã nạp thành công thêm ${formatMoney(additionalAmt)} cho khách vãng lai. Tổng tiền hiện tại: ${formatMoney(newTotal)}`);
      await loadPcs();
      // Update selected PC info in real-time
      setSelectedPc((prev) => {
        if (!prev || !prev.activeGuest) return prev;
        return {
          ...prev,
          activeGuest: {
            ...prev.activeGuest,
            prepaidAmount: newTotal,
          },
        };
      });
    } catch (e) {
      setDrawerError(e instanceof Error ? e.message : 'Nạp tiền thất bại');
    } finally {
      setActionPending(false);
    }
  };

  // Helper getters
  const getUserName = (pc: PcListItem) => {
    if (pc.activeMember) return pc.activeMember.username;
    if (pc.activeGuest) return pc.activeGuest.displayName;
    if (pc.activeAdmin) return `Admin (${pc.activeAdmin.username})`;
    return '-';
  };

  const getUserIcon = (pc: PcListItem) => {
    if (pc.activeAdmin) return '👑';
    if (pc.activeGuest && pc.activeGuest.prepaidAmount > 0) return '💳';
    return '👤';
  };

  const getRemainingTime = (pc: PcListItem, elapsedSeconds: number) => {
    if (pc.activeMember) {
      if (pc.activeMember.memberType === 'VIP') {
        const remainingSeconds = Math.max(0, 360000 - elapsedSeconds);
        return formatDuration(remainingSeconds);
      }
      const balance = Number(pc.activeMember.balance) || 0;
      const playSeconds = Number(pc.activeMember.playSeconds) || 0;
      const pricePerMinute =
        (pc.activeSession?.pricePerMinute && pc.activeSession.pricePerMinute > 0)
          ? pc.activeSession.pricePerMinute
          : ((pc.hourlyRate || 10000) / 60);
      const balanceSeconds = pricePerMinute > 0 ? ((balance / pricePerMinute) * 60) : 0;
      const totalRemainingSeconds = Math.max(0, balanceSeconds) + Math.max(0, playSeconds);
      const remainingMinutes = totalRemainingSeconds <= 0
        ? 0
        : Math.max(1, Math.ceil(totalRemainingSeconds / 60));
      const displayRemainingSeconds = remainingMinutes * 60;
      return formatDuration(displayRemainingSeconds);
    }
    return '-';
  };

  const getMoneyOrRemainingTime = (pc: PcListItem, elapsedSeconds: number) => {
    if (!pc.activeSession) return { icon: '💰', text: '-' };
    if (pc.activeGuest && pc.activeGuest.prepaidAmount > 0 && pc.hourlyRate) {
      const prepaidMinutes = Math.floor((pc.activeGuest.prepaidAmount / pc.hourlyRate) * 60);
      const usedMinutes = Math.floor(Math.max(0, elapsedSeconds) / 60);
      const remainingMinutes = Math.max(0, prepaidMinutes - usedMinutes);
      // Format manually since formatDuration uses Math.ceil now
      const hours = Math.floor(remainingMinutes / 60);
      const minutes = remainingMinutes % 60;
      const paddedHours = hours.toString().padStart(2, '0');
      const paddedMinutes = minutes.toString().padStart(2, '0');
      return { icon: '⌛', text: `${paddedHours}:${paddedMinutes}` };
    }
    if (pc.activeMember && pc.activeMember.memberType === 'VIP') {
      const balance = Number(pc.activeMember.balance) || 0;
      if (balance < 0 && pc.hourlyRate) {
        const rawDebt = (elapsedSeconds / 3600) * pc.hourlyRate;
        const roundedDebt = Math.ceil(rawDebt / 500) * 500;
        return { icon: '💰', text: formatMoney(roundedDebt) };
      }
    }
    
    const serverAmount = pc.activeSession?.estimatedAmount || 0;
    const rawCost = (elapsedSeconds / 3600) * (pc.hourlyRate || 5000);
    const roundedRawCost = Math.ceil(rawCost / 500) * 500;
    const dynamicEstimatedAmount = Math.max(serverAmount, roundedRawCost);
    return { icon: '💰', text: formatMoney(dynamicEstimatedAmount) };
  };

  const getUnpaidServicesTotal = (orders: PcServiceOrder[]) => {
    return orders.reduce((sum, item) => sum + item.lineTotal, 0);
  };

  const getPlayAmountToPay = (pc: PcListItem, elapsedSeconds: number) => {
    const serverAmount = pc.activeSession?.estimatedAmount || 0;

    if (pc.activeMember) {
      const balance = Number(pc.activeMember.balance) || 0;
      if (balance < 0) {
        const rawDebt = Math.abs(balance);
        return Math.ceil(rawDebt / 500) * 500;
      }
      return serverAmount;
    }

    const rawCost = (elapsedSeconds / 3600) * (pc.hourlyRate || 5000);
    const roundedRawCost = Math.ceil(rawCost / 500) * 500;
    const dynamicEstimatedAmount = Math.max(serverAmount, roundedRawCost);

    if (pc.activeGuest) {
      return Math.max(0, dynamicEstimatedAmount - (pc.activeGuest.prepaidAmount || 0));
    }
    return dynamicEstimatedAmount;
  };

  return (
    <main className="layout">
      <TopNav />
      {error && <p className="error">{error}</p>}
      {actionMessage && <p className="info">{actionMessage}</p>}

      <section className="toolbar-row">
        {/* Hiding filters temporarily as requested */}
        {false && (
          <>
            <div className="toolbar-item">
              <label htmlFor="status-filter">Trạng thái:</label>
              <select
                id="status-filter"
                value={statusFilter}
                onChange={(event) =>
                  setStatusFilter(
                    event.target.value as any
                  )
                }
              >
                <option value="ALL">Tất cả</option>
                <option value="IN_USE">Đang sử dụng</option>
                <option value="LOCKED">Đang khóa</option>
                <option value="ONLINE">Sẵn sàng</option>
                <option value="BOOTING">Đang khởi động</option>
                <option value="OFFLINE">Offline</option>
              </select>
            </div>

            <div className="toolbar-item toolbar-search">
              <label htmlFor="search-pc">Tìm máy:</label>
              <input
                id="search-pc"
                value={search}
                onChange={(event) => setSearch(event.target.value)}
                placeholder="Nhập tên máy / agent id"
              />
            </div>
          </>
        )}

        <div className="toolbar-item" style={{ display: 'flex', flexDirection: 'row', alignItems: 'center', gap: '0.5rem', alignSelf: 'flex-end', marginLeft: 'auto' }}>
          <span style={{ fontSize: '0.8rem', color: 'var(--muted)' }} className="desktop-only">
            Đồng bộ: {formatClock(lastUpdatedAt)}
          </span>
          <button onClick={() => void loadPcs()} disabled={loading} style={{ padding: '0.35rem 0.75rem', fontSize: '0.85rem' }}>
            {loading ? '...' : 'Làm mới'}
          </button>
        </div>
      </section>

      {loading && <p className="info">Đang tải danh sách máy...</p>}

      {/* Desktop Layout - Only shown on screen widths > 768px */}
      <section className="table-wrap desktop-only" style={{ marginTop: '1rem' }}>
        <table className="history-table" style={{ minWidth: '850px' }}>
          <thead>
            <tr>
              <th>Tên máy</th>
              <th>Tình trạng</th>
              <th>Người sử dụng</th>
              <th>Bắt đầu lúc</th>
              <th>Đã sử dụng</th>
              <th>Còn lại</th>
              <th>Tiền giờ tạm tính</th>
              <th>Thao tác</th>
            </tr>
          </thead>
          <tbody>
            {filteredPcs.map((pc) => {
              const elapsed = pc.activeSession
                ? Math.max(0, getEffectiveElapsedSeconds(pc, tick))
                : 0;

              return (
                <tr key={pc.id}>
                  <td><strong>{pc.name}</strong></td>
                  <td>
                    <span className={statusClass(pc.status)}>{statusText(pc.status)}</span>
                  </td>
                  <td>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '0.4rem' }}>
                      <span style={{ fontSize: '1.1rem' }}>{getUserIcon(pc)}</span>
                      <span>{getUserName(pc)}</span>
                    </div>
                  </td>
                  <td>{formatClock(pc.activeSession?.startedAt ?? null)}</td>
                  <td>{pc.activeSession ? formatDuration(getEffectiveElapsedSeconds(pc, tick)) : '-'}</td>
                  <td>{getRemainingTime(pc, getEffectiveElapsedSeconds(pc, tick))}</td>
                  <td>
                    {pc.activeSession ? (() => {
                      const moneyOrTime = getMoneyOrRemainingTime(pc, elapsed);
                      return <span className="highlight">{moneyOrTime.text}</span>;
                    })() : '-'}
                  </td>
                  <td>
                    <div className="actions">
                      <button
                        disabled={pendingPcActions[pc.id] || pc.status === 'IN_USE' || pc.status === 'BOOTING'}
                        onClick={() => void applyAction(pc.id, 'open')}
                      >
                        Mở
                      </button>
                      <button
                        disabled={pendingPcActions[pc.id] || pc.status === 'LOCKED' || pc.status === 'OFFLINE' || pc.status === 'BOOTING'}
                        onClick={() => void applyAction(pc.id, 'lock')}
                        className="btn-danger"
                      >
                        Khóa
                      </button>
                      <button
                        onClick={() => handleOpenDrawer(pc)}
                        className="btn-secondary"
                      >
                        Thao tác
                      </button>
                    </div>
                  </td>
                </tr>
              );
            })}
            {!loading && filteredPcs.length === 0 && (
              <tr>
                <td colSpan={8}>Không có máy phù hợp bộ lọc</td>
              </tr>
            )}
          </tbody>
        </table>
      </section>

      {/* Mobile Card Layout - Only shown on screen widths <= 768px */}
      <section className="pc-card-grid mobile-only">
        {filteredPcs.map((pc) => {
          const elapsed = pc.activeSession
            ? Math.max(0, getEffectiveElapsedSeconds(pc, tick))
            : 0;
          const userName = getUserName(pc);

          return (
            <div key={pc.id} className="pc-card-item" onClick={() => handleOpenDrawer(pc)}>
              <div className="pc-card-header" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                  <img src={getPcIconPath(pc)} alt="" style={{ width: '28px', height: '28px' }} />
                  <span className="pc-card-title">{pc.name}</span>
                </div>
                <span className={statusClass(pc.status)} style={{ fontSize: '0.68rem', padding: '0.15rem 0.4rem' }}>{statusText(pc.status)}</span>
              </div>
              {userName !== '-' && (
                <div className="pc-card-body" style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '0.4rem', marginTop: '0.4rem' }}>
                  <div className="pc-card-row">
                    <span className="pc-card-icon">{getUserIcon(pc)}</span>
                    <span className="pc-card-value" style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{userName}</span>
                  </div>
                  {pc.activeSession ? (
                    <div className="pc-card-row">
                      <span className="pc-card-icon">⏱</span>
                      <span className="pc-card-value">{formatDuration(elapsed)}</span>
                    </div>
                  ) : <div />}
                  {pc.activeSession ? (() => {
                    const moneyOrTime = getMoneyOrRemainingTime(pc, elapsed);
                    return (
                      <div className="pc-card-row">
                        <span className="pc-card-icon">{moneyOrTime.icon}</span>
                        <span className="pc-card-value highlight">{moneyOrTime.text}</span>
                      </div>
                    );
                  })() : <div />}
                  {pc.hasUnpaidServices ? (
                    <div className="pc-card-row">
                      <span className="pc-card-icon" style={{ color: '#0284c7' }}>🍔</span>
                      <span className="pc-card-value" style={{ color: '#0284c7', fontWeight: 500 }}>Có dịch vụ</span>
                    </div>
                  ) : <div />}
                </div>
              )}
            </div>
          );
        })}
        {!loading && filteredPcs.length === 0 && (
          <p className="info">Không có máy phù hợp bộ lọc</p>
        )}
      </section>

      {/* Action Drawer Modal */}
      {showDrawer && selectedPc && (
        <div className="modal-backdrop" onClick={() => setShowDrawer(false)}>
          <div className="modal-box" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h2>Thao tác {selectedPc.name}</h2>
              <button className="modal-close-btn" onClick={() => setShowDrawer(false)}>&times;</button>
            </div>
            
            <div className="modal-tabs">
              <button
                className={`modal-tab-btn ${drawerTab === 'session' ? 'active' : ''}`}
                onClick={() => setDrawerTab('session')}
              >
                Phiên máy
              </button>
              <button
                className={`modal-tab-btn ${drawerTab === 'service' ? 'active' : ''}`}
                onClick={() => setDrawerTab('service')}
                disabled={selectedPc.status !== 'IN_USE'}
              >
                Dịch vụ {unpaidOrders.length > 0 ? `(${unpaidOrders.length})` : ''}
              </button>
              {selectedPc.activeGuest && (selectedPc.activeGuest.prepaidAmount ?? 0) > 0 && (
                <button
                  className={`modal-tab-btn ${drawerTab === 'topup' ? 'active' : ''}`}
                  onClick={() => setDrawerTab('topup')}
                >
                  Nạp tiền
                </button>
              )}
            </div>

            <div className="modal-body">
              {drawerError && <div className="error">{drawerError}</div>}
              {drawerSuccess && <div className="info" style={{ color: '#155724' }}>{drawerSuccess}</div>}

              {/* TAB 1: SESSION */}
              {drawerTab === 'session' && (
                <>
                  {/* Info subcard */}
                  <div className="active-member-info">
                    <div className="status-summary-row">
                      <span><strong>Tình trạng:</strong></span>
                      <span className="status-summary-value">{statusText(selectedPc.status)}</span>
                    </div>
                    {selectedPc.activeSession && (
                      <>
                        <div style={{ marginTop: '0.25rem' }}>
                          <span><strong>Loại khách:</strong></span>
                          <span>{selectedPc.activeMember ? 'Hội viên' : 'Khách vãng lai'}</span>
                        </div>
                        {selectedPc.activeMember && (
                          <div>
                            <span><strong>Username:</strong></span>
                            <span>{selectedPc.activeMember.username}</span>
                          </div>
                        )}
                        <div>
                          <span><strong>Thời gian dùng:</strong></span>
                          <span>
                            {formatDuration(getEffectiveElapsedSeconds(selectedPc, tick))}
                          </span>
                        </div>
                        {selectedPc.activeMember && (
                          <div>
                            <span><strong>Thời gian còn lại:</strong></span>
                            <span>{getRemainingTime(selectedPc, getEffectiveElapsedSeconds(selectedPc, tick))}</span>
                          </div>
                        )}
                        <div style={{ borderTop: '1px solid rgba(0,0,0,0.1)', marginTop: '0.4rem', paddingTop: '0.4rem' }}>
                          <span><strong>Tiền máy tạm tính:</strong></span>
                          <span><strong>{(() => {
                            const elapsed = getEffectiveElapsedSeconds(selectedPc, tick);
                            const rawCost = (elapsed / 3600) * (selectedPc.hourlyRate || 5000);
                            const roundedRawCost = Math.ceil(rawCost / 500) * 500;
                            const serverAmount = selectedPc.activeSession?.estimatedAmount || 0;
                            return formatMoney(Math.max(serverAmount, roundedRawCost));
                          })()}</strong></span>
                        </div>
                        {(() => {
                          const elapsed = getEffectiveElapsedSeconds(selectedPc, tick);
                          const totalToPay = getPlayAmountToPay(selectedPc, elapsed);
                          
                          let playAmount = selectedPc.activeSession?.estimatedAmount || 0;
                          const oldDebt = Math.max(0, totalToPay - playAmount);
                          
                          if (oldDebt > 0) {
                              return (
                                <div style={{ marginTop: '0.2rem' }}>
                                  <span><strong>Tiền nợ (từ trước):</strong></span>
                                  <span style={{ color: '#d32f2f' }}>
                                    +{formatMoney(oldDebt)}
                                  </span>
                                </div>
                              );
                          }
                          return null;
                        })()}
                        {unpaidOrders.length > 0 && (
                          <div style={{ marginTop: '0.2rem' }}>
                            <span><strong>Tiền dịch vụ chưa trả:</strong></span>
                            <span style={{ color: '#d32f2f' }}>
                              +{formatMoney(getUnpaidServicesTotal(unpaidOrders))}
                            </span>
                          </div>
                        )}
                        <div style={{ borderTop: '1px dashed rgba(0,0,0,0.15)', marginTop: '0.4rem', paddingTop: '0.4rem', fontSize: '1.05rem', color: '#b42318' }}>
                          <span><strong>Tổng thanh toán:</strong></span>
                          <span><strong>{formatMoney(getPlayAmountToPay(selectedPc, getEffectiveElapsedSeconds(selectedPc, tick)) + getUnpaidServicesTotal(unpaidOrders))}</strong></span>
                        </div>
                      </>
                    )}
                    {selectedPc.ipAddress && (
                      <div className="machine-network-meta">
                        <span>IP: {selectedPc.ipAddress}</span>
                        <span>MAC: {selectedPc.macAddress ?? 'Chưa rõ'}</span>
                      </div>
                    )}
                  </div>

                  {/* Actions based on state */}
                  {selectedPc.status === 'OFFLINE' && (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
                      {selectedPc.activeSession ? (
                        <button
                          onClick={handleLock}
                          disabled={actionPending}
                          className="btn-danger"
                          style={{ padding: '0.75rem', fontSize: '1rem', marginTop: '0.5rem' }}
                        >
                          {actionPending ? 'Đang gửi...' : 'Tính tiền & Khóa máy'}
                        </button>
                      ) : (
                        <>
                          <button
                            onClick={handleWOL}
                            disabled={actionPending || !selectedPc.macAddress}
                            style={{ background: '#2e7d32', borderColor: '#2e7d32' }}
                          >
                            {actionPending ? 'Đang gửi...' : 'Mở máy từ xa (Wake-on-LAN)'}
                          </button>
                          {!selectedPc.macAddress && (
                            <p style={{ fontSize: '0.8rem', color: 'red', margin: 0 }}>
                              * Máy này chưa có thông tin địa chỉ MAC nên không thể bật từ xa.
                            </p>
                          )}
                        </>
                      )}
                    </div>
                  )}

                  {(selectedPc.status === 'ONLINE' || selectedPc.status === 'LOCKED' || selectedPc.status === 'BOOTING') && (
                      <div style={{ display: 'flex', flexDirection: 'column', gap: '0.8rem' }}>
                          <div className="form-group">
                            <label htmlFor="guest-amount">Nạp tiền giờ trước (để trống nếu không giới hạn):</label>
                            <input
                              id="guest-amount"
                              type="number"
                              min={0}
                              step={1000}
                              placeholder="Mở máy tự do"
                              value={guestAmount}
                              onChange={(e) => setGuestAmount(e.target.value)}
                            />
                          </div>
                          <div className="preset-grid">
                            {[0, 1000, 2000, 3000, 4000, 5000, 6000, 7000, 8000, 9000, 10000].map((preset) => (
                              <button
                                key={preset}
                                type="button"
                                className={`preset-btn ${Number(guestAmount) === preset ? 'active' : ''}`}
                                onClick={() => setGuestAmount(preset.toString())}
                              >
                                {preset === 0 ? 'Tự do' : `${preset / 1000}k`}
                              </button>
                            ))}
                          </div>
                          <button
                            type="button"
                            onClick={handleOpenGuest}
                            disabled={actionPending}
                            style={{ background: '#2e7d32', borderColor: '#2e7d32', marginTop: '0.5rem' }}
                          >
                            {actionPending ? 'Đang mở máy...' : 'Xác nhận mở máy'}
                          </button>
                    </div>
                  )}

                  {(selectedPc.status === 'IN_USE') && (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '0.6rem', marginTop: '0.5rem' }}>
                      <button
                        onClick={handleLock}
                        disabled={actionPending}
                        className="btn-danger"
                        style={{ padding: '0.75rem', fontSize: '1rem' }}
                      >
                        {actionPending ? 'Đang gửi...' : 'Tính tiền & Khóa máy'}
                      </button>
                      <div style={{ display: 'flex', gap: '0.5rem' }}>
                        <button
                          onClick={() => requestPowerActionConfirm('restart')}
                          disabled={actionPending}
                          className="btn-secondary"
                          style={{ flex: 1 }}
                        >
                          Khởi động lại
                        </button>
                        <button
                          onClick={() => requestPowerActionConfirm('shutdown')}
                          disabled={actionPending}
                          className="btn-secondary"
                          style={{ flex: 1, color: '#d32f2f' }}
                        >
                          Tắt máy
                        </button>
                      </div>
                    </div>
                  )}
                </>
              )}

              {/* TAB 2: SERVICE */}
              {drawerTab === 'service' && selectedPc.status === 'IN_USE' && (
                <>
                  <div className="form-group">
                    <label htmlFor="service-select">Chọn dịch vụ:</label>
                    <select
                      id="service-select"
                      value={selectedServiceId}
                      onChange={(e) => setSelectedServiceId(e.target.value)}
                    >
                      {serviceItems.map((item) => (
                        <option key={item.id} value={item.id}>
                          {item.name} - {formatMoney(item.unitPrice)}
                        </option>
                      ))}
                    </select>
                  </div>

                  <div className="form-group">
                    <label>Số lượng:</label>
                    <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'center' }}>
                      <button
                        type="button"
                        className="btn-secondary"
                        onClick={() => setServiceQty((q) => Math.max(1, q - 1))}
                        style={{ padding: '0.4rem 0.8rem' }}
                      >
                        -
                      </button>
                      <input
                        type="number"
                        min={1}
                        value={serviceQty}
                        onChange={(e) => setServiceQty(Math.max(1, Number(e.target.value) || 1))}
                        style={{ textAlign: 'center', width: '60px' }}
                      />
                      <button
                        type="button"
                        className="btn-secondary"
                        onClick={() => setServiceQty((q) => q + 1)}
                        style={{ padding: '0.4rem 0.8rem' }}
                      >
                        +
                      </button>
                    </div>
                  </div>

                  <div className="form-group">
                    <label htmlFor="service-note">Ghi chú:</label>
                    <input
                      id="service-note"
                      type="text"
                      placeholder="Không đường, ít đá..."
                      value={serviceNote}
                      onChange={(e) => setServiceNote(e.target.value)}
                    />
                  </div>

                  <button
                    onClick={handleAddService}
                    disabled={actionPending || !selectedServiceId}
                    style={{ background: '#2e7d32', borderColor: '#2e7d32' }}
                  >
                    Gọi món dịch vụ
                  </button>

                  <h3 className="sub-section-title">Hóa đơn dịch vụ chưa thanh toán</h3>
                  {loadingOrders ? (
                    <p>Đang tải hóa đơn...</p>
                  ) : unpaidOrders.length === 0 ? (
                    <p style={{ color: '#666', fontSize: '0.9rem' }}>Chưa gọi dịch vụ hoặc đã trả hết.</p>
                  ) : (
                    <>
                      <div className="drawer-order-list">
                        {unpaidOrders.map((order) => (
                          <div key={order.id} className="drawer-order-item">
                            <div>
                              <span className="drawer-order-name">{order.serviceItem.name}</span>
                              <span style={{ color: '#666', marginLeft: '0.5rem' }}>x{order.quantity}</span>
                            </div>
                            <span className="drawer-order-total">{formatMoney(order.lineTotal)}</span>
                          </div>
                        ))}
                      </div>
                      <div style={{ display: 'flex', justifyContent: 'space-between', fontWeight: 'bold', fontSize: '1rem', marginTop: '0.2rem' }}>
                        <span>Tổng dịch vụ:</span>
                        <span style={{ color: '#d32f2f' }}>{formatMoney(getUnpaidServicesTotal(unpaidOrders))}</span>
                      </div>
                      <button
                        onClick={handlePayServices}
                        disabled={actionPending}
                        style={{ background: '#1976d2', borderColor: '#1976d2', marginTop: '0.4rem' }}
                      >
                        Thanh toán dịch vụ
                      </button>
                    </>
                  )}
                </>
              )}

              {/* TAB 3: TOPUP */}
              {drawerTab === 'topup' && selectedPc.activeGuest && (
                <>
                  <div className="active-member-info" style={{ background: '#f6ffed', borderColor: '#b7eb8f', color: '#389e0d' }}>
                    <div><strong>Loại tài khoản:</strong> Khách vãng lai</div>
                    <div><strong>Tiền giờ hiện tại:</strong> {formatMoney(selectedPc.activeGuest.prepaidAmount)}</div>
                  </div>

                  <div className="form-group">
                    <label htmlFor="topup-amount">Số tiền nạp thêm (VND):</label>
                    <input
                      id="topup-amount"
                      type="number"
                      min={1000}
                      step={1000}
                      value={topupAmountValue}
                      onChange={(e) => setTopupAmountValue(e.target.value)}
                    />
                  </div>

                  <div className="preset-grid">
                    {[10000, 20000, 50000, 100000, 200000, 500000].map((preset) => (
                      <button
                        key={preset}
                        type="button"
                        className={`preset-btn ${Number(topupAmountValue) === preset ? 'active' : ''}`}
                        onClick={() => setTopupAmountValue(preset.toString())}
                      >
                        {preset / 1000}k
                      </button>
                    ))}
                  </div>

                  <button
                    onClick={handleGuestTopupSubmit}
                    disabled={actionPending || !topupAmountValue}
                    style={{ background: '#2e7d32', borderColor: '#2e7d32', marginTop: '0.5rem' }}
                  >
                    {actionPending ? 'Đang nạp...' : 'Xác nhận nạp tiền'}
                  </button>
                </>
              )}
            </div>

            <div className="modal-footer">
              <button
                className="btn-secondary"
                onClick={() => {
                  setPowerActionConfirm(null);
                  setShowDrawer(false);
                }}
              >
                Đóng
              </button>
            </div>
          </div>
        </div>
      )}

      {powerActionConfirm && (
        <div className="modal-backdrop" style={{ zIndex: 1400 }}>
          <div className="modal-container" style={{ maxWidth: '420px', width: '94vw' }}>
            <div className="modal-header">
              <h2>Xác nhận</h2>
              <button
                className="modal-close"
                onClick={() => setPowerActionConfirm(null)}
                disabled={actionPending}
              >
                ×
              </button>
            </div>
            <div className="drawer-body" style={{ display: 'grid', gap: '0.9rem' }}>
              <p style={{ margin: 0 }}>
                {powerActionConfirm.action === 'shutdown'
                  ? `Bạn chắc chắn muốn tắt máy ${powerActionConfirm.pcName}?`
                  : `Bạn chắc chắn muốn khởi động lại máy ${powerActionConfirm.pcName}?`}
              </p>
              <div style={{ display: 'flex', gap: '0.6rem', justifyContent: 'flex-end' }}>
                <button
                  className="btn-secondary"
                  onClick={() => setPowerActionConfirm(null)}
                  disabled={actionPending}
                >
                  Hủy
                </button>
                <button
                  className="btn-danger"
                  onClick={executePowerAction}
                  disabled={actionPending}
                  style={{ minWidth: '132px' }}
                >
                  {actionPending ? 'Đang gửi...' : 'Xác nhận'}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </main>
  );
}
