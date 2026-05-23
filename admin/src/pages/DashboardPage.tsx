import { useEffect, useMemo, useState } from 'react';
import { io } from 'socket.io-client';
import { fetchPcs } from '../api/pcs';
import { fetchDashboardStats } from '../api/reports';
import { TopNav } from '../components/TopNav';
import { WS_BASE_URL } from '../lib/config';
import { PcListItem } from '../types/pc';

function formatDuration(totalSeconds: number): string {
  if (totalSeconds <= 0) return '0p';
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  if (hours > 0) {
    return `${hours}h ${minutes}p`;
  }
  return `${minutes}p`;
}

function formatMoney(amount: number): string {
  return amount.toLocaleString('vi-VN') + ' đ';
}

function formatClock(isoDate: string | null): string {
  if (!isoDate) return '-';
  return new Date(isoDate).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}

export function DashboardPage() {
  const [pcs, setPcs] = useState<PcListItem[]>([]);
  const [dashboardStats, setDashboardStats] = useState<any>(null);
  const [period, setPeriod] = useState<'week' | 'month' | 'year'>('week');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [lastUpdatedAt, setLastUpdatedAt] = useState<string | null>(null);
  const [tick, setTick] = useState(0);
  
  // Tab index for Rankings panel: 0 = Members, 1 = PCs, 2 = Services
  const [rankingTab, setRankingTab] = useState(0);

  const loadDashboardData = async (selectedPeriod = period) => {
    setLoading(true);
    setError(null);
    try {
      const [pcsData, statsData] = await Promise.all([
        fetchPcs(),
        fetchDashboardStats(selectedPeriod),
      ]);
      setPcs(pcsData.items);
      setLastUpdatedAt(pcsData.serverTime);
      setDashboardStats(statsData);
      setTick(0);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Lỗi tải dữ liệu');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadDashboardData(period);

    const socket = io(`${WS_BASE_URL}/billing`, {
      transports: ['websocket'],
    });

    const handleUpdate = () => {
      void loadDashboardData(period);
    };

    socket.on('pc.status.changed', handleUpdate);
    socket.on('command.updated', handleUpdate);

    return () => {
      socket.disconnect();
    };
  }, [period]);

  // Tick for updating elapsed seconds in active sessions
  useEffect(() => {
    const interval = setInterval(() => setTick((t) => t + 1), 1000);
    return () => clearInterval(interval);
  }, []);

  const stats = useMemo(() => {
    const total = pcs.length;
    const inUse = pcs.filter((pc) => pc.status === 'IN_USE').length;
    const locked = pcs.filter((pc) => pc.status === 'LOCKED' || pc.status === 'OFFLINE' || pc.status === 'BOOTING').length;
    const online = pcs.filter((pc) => pc.status === 'ONLINE').length;
    const estimatedTotal = pcs.reduce((sum, pc) => sum + (pc.activeSession?.estimatedAmount ?? 0), 0);
    const useRate = total > 0 ? Math.round((inUse / total) * 100) : 0;

    return { total, inUse, locked, online, estimatedTotal, useRate };
  }, [pcs]);

  // Active playing list for Dashboard details
  const activeSessions = useMemo(() => {
    return pcs.filter((pc) => pc.status === 'IN_USE' && pc.activeSession);
  }, [pcs]);

  const getUserName = (pc: PcListItem) => {
    if (pc.activeMember) return pc.activeMember.username;
    if (pc.activeGuest) return pc.activeGuest.displayName;
    if (pc.activeAdmin) return `Admin (${pc.activeAdmin.username})`;
    return 'Khách';
  };

  const renderGrowth = (growthStr: string) => {
    if (!growthStr || growthStr === '0.0%') return null;
    const isUp = growthStr.includes('▲');
    const isDown = growthStr.includes('▼');
    const color = isUp ? '#12b76a' : isDown ? '#f04438' : 'var(--muted)';
    return (
      <span style={{
        color,
        fontSize: '0.72rem',
        fontWeight: 700,
        marginLeft: '0.35rem',
        padding: '0.1rem 0.35rem',
        borderRadius: '999px',
        background: isUp ? '#d1fae5' : isDown ? '#fee2e2' : '#f1f5f9',
        display: 'inline-flex',
        alignItems: 'center',
        gap: '0.1rem',
        whiteSpace: 'nowrap'
      }}>
        {growthStr}
      </span>
    );
  };

  return (
    <main className="layout" style={{ maxWidth: '800px', margin: '0 auto' }}>
      <TopNav />
      
      {/* Title & Sync time */}
      <section className="hero" style={{ display: 'flex', flexDirection: 'column', gap: '0.4rem', marginBottom: '0.75rem' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', width: '100%' }}>
          <h1 style={{ margin: 0, fontSize: '1.25rem' }}>Tổng quan phòng máy</h1>
          <button 
            onClick={() => void loadDashboardData()} 
            disabled={loading}
            style={{
              padding: '0.35rem 0.65rem',
              borderRadius: '6px',
              fontSize: '0.78rem',
              background: '#0066cc',
              color: '#fff',
              border: 'none',
              cursor: 'pointer',
              fontWeight: 600
            }}
          >
            Làm mới
          </button>
        </div>
        <p style={{ margin: 0, fontSize: '0.8rem', color: 'var(--muted)' }}>
          Đồng bộ: {formatClock(lastUpdatedAt)}
        </p>
      </section>

      {error && <p className="error" style={{ color: '#b42318', fontWeight: 600, fontSize: '0.85rem', margin: '0.5rem 0' }}>{error}</p>}

      {/* Grid of Real-Time PC Status & Usage Rate */}
      <div style={{ display: 'flex', flexDirection: 'column', gap: '0.6rem', marginBottom: '0.85rem' }}>
        {/* Machine Status Row */}
        <div style={{
          background: 'var(--surface)',
          border: '1px solid var(--line)',
          borderRadius: '12px',
          padding: '0.85rem 1rem',
          display: 'flex',
          flexDirection: 'column',
          gap: '0.65rem'
        }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <h3 style={{ margin: 0, fontSize: '0.88rem', fontWeight: 700 }}>Trạng thái máy trạm</h3>
            <span style={{ fontSize: '0.78rem', color: 'var(--muted)', fontWeight: 600 }}>
              Đang hoạt động: <strong>{stats.inUse}</strong>/{stats.total} máy ({stats.useRate}%)
            </span>
          </div>

          {/* Progress bar of usage rate */}
          <div style={{
            height: '6px',
            background: 'var(--surface-2)',
            borderRadius: '999px',
            overflow: 'hidden',
            width: '100%',
            display: 'flex'
          }}>
            <div style={{
              width: `${stats.useRate}%`,
              background: 'linear-gradient(90deg, #0066cc, #12b76a)',
              borderRadius: '999px',
              height: '100%',
              transition: 'width 0.4s ease'
            }} />
          </div>

          {/* Detailed Count Pills */}
          <div style={{ display: 'flex', gap: '0.4rem', flexWrap: 'wrap', marginTop: '0.15rem' }}>
            <span style={{
              fontSize: '0.75rem',
              fontWeight: 600,
              padding: '0.2rem 0.5rem',
              borderRadius: '6px',
              background: '#e0f2fe',
              color: '#0369a1'
            }}>
              Tổng: {stats.total} máy
            </span>
            <span style={{
              fontSize: '0.75rem',
              fontWeight: 600,
              padding: '0.2rem 0.5rem',
              borderRadius: '6px',
              background: '#dbeafe',
              color: '#1d4ed8'
            }}>
              Đang dùng: {stats.inUse} máy
            </span>
            <span style={{
              fontSize: '0.75rem',
              fontWeight: 600,
              padding: '0.2rem 0.5rem',
              borderRadius: '6px',
              background: '#dcfce7',
              color: '#15803d'
            }}>
              Sẵn sàng: {stats.online} máy
            </span>
            <span style={{
              fontSize: '0.75rem',
              fontWeight: 600,
              padding: '0.2rem 0.5rem',
              borderRadius: '6px',
              background: '#fee2e2',
              color: '#b91c1c'
            }}>
              Tắt/Khóa: {stats.locked} máy
            </span>
          </div>
        </div>

        {/* Live Estimated Revenue Banner */}
        <div style={{
          background: 'linear-gradient(135deg, #0f172a, #1e293b)',
          color: '#fff',
          borderRadius: '12px',
          padding: '0.85rem 1rem',
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center'
        }}>
          <div>
            <p style={{ margin: 0, fontSize: '0.75rem', color: '#94a3b8', fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
              Tiền tạm tính (Phiên đang chạy)
            </p>
            <h2 style={{ margin: '0.15rem 0 0 0', fontSize: '1.4rem', fontWeight: 800, color: '#38bdf8' }}>
              {formatMoney(stats.estimatedTotal)}
            </h2>
          </div>
          <div style={{
            width: '40px',
            height: '40px',
            borderRadius: '999px',
            background: 'rgba(56, 189, 248, 0.15)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            fontSize: '1.25rem',
            color: '#38bdf8'
          }}>
            ⚡
          </div>
        </div>
      </div>

      {/* Revenue & Performance Overview Card (Period Customisable) */}
      <section style={{
        background: 'var(--surface)',
        border: '1px solid var(--line)',
        borderRadius: '14px',
        padding: '1rem',
        marginBottom: '0.85rem'
      }}>
        {/* Header with Selector */}
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '0.85rem', flexWrap: 'wrap', gap: '0.4rem' }}>
          <h3 style={{ margin: 0, fontSize: '0.92rem', fontWeight: 800 }}>Hiệu suất doanh thu</h3>
          <div style={{ display: 'flex', background: 'var(--surface-2)', borderRadius: '8px', padding: '0.15rem', border: '1px solid var(--line)' }}>
            {(['week', 'month', 'year'] as const).map((p) => (
              <button
                key={p}
                onClick={() => {
                  setPeriod(p);
                  void loadDashboardData(p);
                }}
                style={{
                  padding: '0.25rem 0.6rem',
                  fontSize: '0.75rem',
                  fontWeight: 600,
                  border: 'none',
                  borderRadius: '6px',
                  cursor: 'pointer',
                  background: period === p ? '#0066cc' : 'transparent',
                  color: period === p ? '#fff' : 'var(--text)'
                }}
              >
                {p === 'week' ? 'Tuần này' : p === 'month' ? 'Tháng này' : 'Năm nay'}
              </button>
            ))}
          </div>
        </div>

        {/* 2x2 Grid of metrics */}
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(160px, 1fr))', gap: '0.6rem' }}>
          {/* Card 1: Total Revenue */}
          <div style={{ background: 'var(--surface-2)', padding: '0.75rem', borderRadius: '10px', border: '1px solid var(--line)' }}>
            <span style={{ fontSize: '0.75rem', color: 'var(--muted)', fontWeight: 600 }}>Tổng doanh thu</span>
            <div style={{ display: 'flex', alignItems: 'baseline', marginTop: '0.15rem', flexWrap: 'wrap' }}>
              <strong style={{ fontSize: '1.05rem', color: 'var(--text)' }}>
                {formatMoney(dashboardStats?.totalRevenue ?? 0)}
              </strong>
              {renderGrowth(dashboardStats?.totalGrowth)}
            </div>
          </div>

          {/* Card 2: Playtime Revenue */}
          <div style={{ background: 'var(--surface-2)', padding: '0.75rem', borderRadius: '10px', border: '1px solid var(--line)' }}>
            <span style={{ fontSize: '0.75rem', color: 'var(--muted)', fontWeight: 600 }}>Tiền máy trạm</span>
            <div style={{ display: 'flex', alignItems: 'baseline', marginTop: '0.15rem', flexWrap: 'wrap' }}>
              <strong style={{ fontSize: '1.05rem', color: 'var(--text)' }}>
                {formatMoney(dashboardStats?.playtimeRevenue ?? 0)}
              </strong>
              {renderGrowth(dashboardStats?.playtimeGrowth)}
            </div>
          </div>

          {/* Card 3: Service Revenue */}
          <div style={{ background: 'var(--surface-2)', padding: '0.75rem', borderRadius: '10px', border: '1px solid var(--line)' }}>
            <span style={{ fontSize: '0.75rem', color: 'var(--muted)', fontWeight: 600 }}>Tiền dịch vụ</span>
            <div style={{ display: 'flex', alignItems: 'baseline', marginTop: '0.15rem', flexWrap: 'wrap' }}>
              <strong style={{ fontSize: '1.05rem', color: 'var(--text)' }}>
                {formatMoney(dashboardStats?.serviceRevenue ?? 0)}
              </strong>
              {renderGrowth(dashboardStats?.serviceGrowth)}
            </div>
          </div>

          {/* Card 4: Accumulated Play Hours */}
          <div style={{ background: 'var(--surface-2)', padding: '0.75rem', borderRadius: '10px', border: '1px solid var(--line)' }}>
            <span style={{ fontSize: '0.75rem', color: 'var(--muted)', fontWeight: 600 }}>Tổng số giờ chơi</span>
            <div style={{ display: 'flex', alignItems: 'baseline', marginTop: '0.15rem', flexWrap: 'wrap' }}>
              <strong style={{ fontSize: '1.05rem', color: 'var(--text)' }}>
                {dashboardStats?.totalPlayHours ?? 0} giờ
              </strong>
              {renderGrowth(dashboardStats?.playhoursGrowth)}
            </div>
          </div>
        </div>
      </section>

      {/* Rankings Section - Multi-Tab Selector */}
      <section style={{
        background: 'var(--surface)',
        border: '1px solid var(--line)',
        borderRadius: '14px',
        padding: '1rem',
        marginBottom: '0.85rem'
      }}>
        {/* Tab Header */}
        <div style={{
          display: 'flex',
          borderBottom: '1px solid var(--line)',
          marginBottom: '0.75rem'
        }}>
          {(['Hội viên', 'Máy trạm', 'Dịch vụ'] as const).map((tab, idx) => (
            <button
              key={tab}
              onClick={() => setRankingTab(idx)}
              style={{
                flex: 1,
                background: 'transparent',
                border: 'none',
                borderBottom: rankingTab === idx ? '3px solid #0066cc' : '3px solid transparent',
                color: rankingTab === idx ? 'var(--text)' : 'var(--muted)',
                padding: '0.5rem 0.25rem',
                fontSize: '0.82rem',
                fontWeight: 700,
                borderRadius: 0,
                cursor: 'pointer',
                textAlign: 'center'
              }}
            >
              {tab}
            </button>
          ))}
        </div>

        {/* Tab 0: Top Members */}
        {rankingTab === 0 && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
            {(!dashboardStats?.topMembers || dashboardStats.topMembers.length === 0) ? (
              <p style={{ margin: 0, color: 'var(--muted)', fontSize: '0.8rem', textAlign: 'center', padding: '1rem 0' }}>Chưa có dữ liệu hội viên.</p>
            ) : (
              dashboardStats.topMembers.slice(0, 5).map((member: any, index: number) => (
                <div key={member.username} style={{ display: 'flex', flexDirection: 'column', gap: '0.2rem' }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontSize: '0.8rem' }}>
                    <span style={{ fontWeight: 600 }}>
                      #{index + 1} {member.username}
                    </span>
                    <strong style={{ color: 'var(--muted)' }}>{member.playHours} giờ</strong>
                  </div>
                  <div style={{ height: '5px', background: 'var(--surface-2)', borderRadius: '999px', overflow: 'hidden' }}>
                    <div style={{ width: `${member.progress}%`, background: '#0066cc', height: '100%', borderRadius: '999px' }} />
                  </div>
                </div>
              ))
            )}
          </div>
        )}

        {/* Tab 1: Top PCs */}
        {rankingTab === 1 && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
            {(!dashboardStats?.topPcs || dashboardStats.topPcs.length === 0) ? (
              <p style={{ margin: 0, color: 'var(--muted)', fontSize: '0.8rem', textAlign: 'center', padding: '1rem 0' }}>Chưa có dữ liệu máy trạm.</p>
            ) : (
              dashboardStats.topPcs.slice(0, 5).map((pc: any, index: number) => (
                <div key={pc.name} style={{ display: 'flex', flexDirection: 'column', gap: '0.2rem' }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontSize: '0.8rem' }}>
                    <span style={{ fontWeight: 600 }}>
                      #{index + 1} Máy {pc.name}
                    </span>
                    <strong style={{ color: 'var(--muted)' }}>{pc.playHours} giờ</strong>
                  </div>
                  <div style={{ height: '5px', background: 'var(--surface-2)', borderRadius: '999px', overflow: 'hidden' }}>
                    <div style={{ width: `${pc.progress}%`, background: '#12b76a', height: '100%', borderRadius: '999px' }} />
                  </div>
                </div>
              ))
            )}
          </div>
        )}

        {/* Tab 2: Top Services */}
        {rankingTab === 2 && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: '0.45rem' }}>
            {(!dashboardStats?.topServiceItems || dashboardStats.topServiceItems.length === 0) ? (
              <p style={{ margin: 0, color: 'var(--muted)', fontSize: '0.8rem', textAlign: 'center', padding: '1rem 0' }}>Chưa có dịch vụ nào được bán.</p>
            ) : (
              <div style={{ border: '1px solid var(--line)', borderRadius: '8px', overflow: 'hidden' }}>
                <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.78rem' }}>
                  <thead>
                    <tr style={{ background: 'var(--surface-2)', borderBottom: '1px solid var(--line)' }}>
                      <th style={{ padding: '0.4rem 0.5rem', textAlign: 'left', fontWeight: 600 }}>Dịch vụ</th>
                      <th style={{ padding: '0.4rem 0.5rem', textAlign: 'center', fontWeight: 600 }}>Số lượng</th>
                      <th style={{ padding: '0.4rem 0.5rem', textAlign: 'right', fontWeight: 600 }}>Doanh thu</th>
                    </tr>
                  </thead>
                  <tbody>
                    {dashboardStats.topServiceItems.slice(0, 5).map((item: any) => (
                      <tr key={item.name} style={{ borderBottom: '1px solid var(--line)' }}>
                        <td style={{ padding: '0.4rem 0.5rem', fontWeight: 600 }}>{item.name}</td>
                        <td style={{ padding: '0.4rem 0.5rem', textAlign: 'center' }}>{item.quantity}</td>
                        <td style={{ padding: '0.4rem 0.5rem', textAlign: 'right', fontWeight: 600 }}>{formatMoney(item.revenue)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        )}
      </section>

      {/* Real-time Active Playing PCs Panel */}
      <section style={{
        background: 'var(--surface)',
        border: '1px solid var(--line)',
        borderRadius: '14px',
        padding: '1rem'
      }}>
        <h3 style={{ margin: '0 0 0.75rem', fontSize: '0.92rem', fontWeight: 800 }}>
          Phiên máy đang hoạt động ({activeSessions.length})
        </h3>
        
        {activeSessions.length === 0 ? (
          <p style={{ color: 'var(--muted)', fontSize: '0.8rem', margin: 0, textAlign: 'center', padding: '1rem 0' }}>
            Không có máy nào đang hoạt động.
          </p>
        ) : (
          <>
            {/* Desktop View Table */}
            <div className="desktop-only" style={{ border: '1px solid var(--line)', borderRadius: '10px', overflow: 'hidden' }}>
              <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.8rem' }}>
                <thead>
                  <tr style={{ background: 'var(--surface-2)', borderBottom: '1px solid var(--line)' }}>
                    <th style={{ padding: '0.5rem', textAlign: 'left', fontWeight: 600 }}>Máy</th>
                    <th style={{ padding: '0.5rem', textAlign: 'left', fontWeight: 600 }}>Người sử dụng</th>
                    <th style={{ padding: '0.5rem', textAlign: 'left', fontWeight: 600 }}>Bắt đầu</th>
                    <th style={{ padding: '0.5rem', textAlign: 'left', fontWeight: 600 }}>Đã chơi</th>
                    <th style={{ padding: '0.5rem', textAlign: 'right', fontWeight: 600 }}>Tạm tính</th>
                  </tr>
                </thead>
                <tbody>
                  {activeSessions.map((pc) => {
                    const elapsed = pc.activeSession
                      ? Math.max(0, pc.activeSession.elapsedSeconds + tick)
                      : 0;
                    return (
                      <tr key={pc.id} style={{ borderBottom: '1px solid var(--line)' }}>
                        <td style={{ padding: '0.5rem' }}><strong>{pc.name}</strong></td>
                        <td style={{ padding: '0.5rem' }}>{getUserName(pc)}</td>
                        <td style={{ padding: '0.5rem' }}>{formatClock(pc.activeSession?.startedAt ?? null)}</td>
                        <td style={{ padding: '0.5rem' }}>{formatDuration(elapsed)}</td>
                        <td style={{ padding: '0.5rem', textAlign: 'right' }}>
                          <strong>{formatMoney(pc.activeSession?.estimatedAmount ?? 0)}</strong>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>

            {/* Mobile View Cards */}
            <div className="mobile-only" style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
              {activeSessions.map((pc) => {
                const elapsed = pc.activeSession
                  ? Math.max(0, pc.activeSession.elapsedSeconds + tick)
                  : 0;
                return (
                  <div
                    key={pc.id}
                    style={{
                      background: 'var(--surface-2)',
                      border: '1px solid var(--line)',
                      borderRadius: '10px',
                      padding: '0.65rem 0.8rem',
                      display: 'flex',
                      flexDirection: 'column',
                      gap: '0.3rem'
                    }}
                  >
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                      <strong style={{ fontSize: '0.88rem' }}>Máy {pc.name}</strong>
                      <span style={{
                        fontSize: '0.75rem',
                        fontWeight: 700,
                        padding: '0.15rem 0.45rem',
                        borderRadius: '6px',
                        background: '#dbeafe',
                        color: '#0066cc'
                      }}>
                        {getUserName(pc)}
                      </span>
                    </div>
                    
                    <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.78rem', color: 'var(--muted)' }}>
                      <span>Bắt đầu: {formatClock(pc.activeSession?.startedAt ?? null)}</span>
                      <span>Đã chơi: {formatDuration(elapsed)}</span>
                    </div>

                    <div style={{ display: 'flex', justifyContent: 'flex-end', borderTop: '1px dashed var(--line)', paddingTop: '0.3rem', marginTop: '0.1rem' }}>
                      <span style={{ fontSize: '0.78rem', color: 'var(--muted)', marginRight: '0.35rem' }}>Tạm tính:</span>
                      <strong style={{ fontSize: '0.82rem', color: '#0066cc' }}>
                        {formatMoney(pc.activeSession?.estimatedAmount ?? 0)}
                      </strong>
                    </div>
                  </div>
                );
              })}
            </div>
          </>
        )}
      </section>
    </main>
  );
}
