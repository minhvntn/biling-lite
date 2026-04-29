import { useEffect, useMemo, useState } from 'react';
import { io } from 'socket.io-client';
import { lockPc, openPc } from '../api/commands';
import { fetchPcs } from '../api/pcs';
import { TopNav } from '../components/TopNav';
import { WS_BASE_URL } from '../lib/config';
import { CommandUpdatedEvent } from '../types/command';
import { PcListItem, PcStatusChangedEvent } from '../types/pc';

function formatDuration(totalSeconds: number): string {
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  return `${hours}h ${minutes}p`;
}

function formatClock(isoDate: string | null): string {
  if (!isoDate) {
    return '-';
  }

  return new Date(isoDate).toLocaleTimeString();
}

function formatMoney(amount: number): string {
  return amount.toLocaleString('vi-VN');
}

function statusText(status: string): string {
  switch (status) {
    case 'IN_USE':
      return 'Dang su dung';
    case 'LOCKED':
      return 'Dang tat';
    case 'ONLINE':
      return 'Online ranh';
    default:
      return 'Offline';
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
    default:
      return 'status-cell status-offline';
  }
}

export function PcsPage() {
  const [pcs, setPcs] = useState<PcListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [tick, setTick] = useState(0);
  const [lastUpdatedAt, setLastUpdatedAt] = useState<string | null>(null);
  const [pendingPcActions, setPendingPcActions] = useState<Record<string, boolean>>(
    {},
  );
  const [actionMessage, setActionMessage] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<'ALL' | 'IN_USE' | 'LOCKED' | 'ONLINE' | 'OFFLINE'>('ALL');
  const [search, setSearch] = useState('');

  const loadPcs = async () => {
    try {
      setError(null);
      const data = await fetchPcs();
      setPcs(data.items);
      setLastUpdatedAt(data.serverTime);
      setTick(0);
    } catch (loadError) {
      const message =
        loadError instanceof Error ? loadError.message : 'Unknown error';
      setError(message);
    } finally {
      setLoading(false);
    }
  };

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
        if (controller.signal.aborted) {
          return;
        }

        const message =
          loadError instanceof Error ? loadError.message : 'Unknown error';
        setError(message);
      })
      .finally(() => setLoading(false));

    return () => controller.abort();
  }, []);

  useEffect(() => {
    const socket = io(`${WS_BASE_URL}/billing`, {
      transports: ['websocket'],
    });

    socket.on('pc.status.changed', (_event: PcStatusChangedEvent) => {
      void loadPcs();
    });
    socket.on('command.updated', (_event: CommandUpdatedEvent) => {
      void loadPcs();
    });

    return () => {
      socket.disconnect();
    };
  }, []);

  useEffect(() => {
    const interval = window.setInterval(() => setTick((v) => v + 1), 1000);
    return () => window.clearInterval(interval);
  }, []);

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

  const summary = useMemo(() => {
    const total = pcs.length;
    const inUse = pcs.filter((pc) => pc.status === 'IN_USE').length;
    const locked = pcs.filter((pc) => pc.status === 'LOCKED').length;
    const onlineIdle = pcs.filter((pc) => pc.status === 'ONLINE').length;

    const runningRevenue = pcs.reduce((acc, pc) => {
      return acc + (pc.activeSession?.estimatedAmount ?? 0);
    }, 0);

    return { total, inUse, locked, onlineIdle, runningRevenue };
  }, [pcs]);

  const applyAction = async (pcId: string, action: 'open' | 'lock') => {
    setPendingPcActions((prev) => ({ ...prev, [pcId]: true }));
    setActionMessage(null);
    try {
      if (action === 'open') {
        await openPc(pcId);
      } else {
        await lockPc(pcId);
      }
      setActionMessage(`Lenh ${action.toUpperCase()} da gui`);
      await loadPcs();
    } catch (applyError) {
      const message =
        applyError instanceof Error ? applyError.message : 'Unknown error';
      setActionMessage(message);
    } finally {
      setPendingPcActions((prev) => ({ ...prev, [pcId]: false }));
    }
  };

  return (
    <main className="layout">
      <TopNav />
      <section className="hero">
        <h1>Dieu hanh may tram</h1>
        <p>Phong cach bang don gian de van hanh nhanh</p>
        <div className="meta">
          <span>Dong bo luc: {formatClock(lastUpdatedAt)}</span>
          <button onClick={() => void loadPcs()} disabled={loading}>
            Lam moi
          </button>
        </div>
      </section>

      <section className="summary-grid">
        <article className="summary-card">
          <h2>{summary.total}</h2>
          <p>Tong may</p>
        </article>
        <article className="summary-card">
          <h2>{summary.inUse}</h2>
          <p>Dang su dung</p>
        </article>
        <article className="summary-card">
          <h2>{summary.locked}</h2>
          <p>Dang tat</p>
        </article>
        <article className="summary-card">
          <h2>{formatMoney(summary.runningRevenue)}</h2>
          <p>Tien tam tinh</p>
        </article>
      </section>

      <section className="toolbar-row">
        <div className="toolbar-item">
          <label htmlFor="status-filter">Trang thai:</label>
          <select
            id="status-filter"
            value={statusFilter}
            onChange={(event) =>
              setStatusFilter(
                event.target.value as
                  | 'ALL'
                  | 'IN_USE'
                  | 'LOCKED'
                  | 'ONLINE'
                  | 'OFFLINE',
              )
            }
          >
            <option value="ALL">Tat ca</option>
            <option value="IN_USE">Dang su dung</option>
            <option value="LOCKED">Dang tat</option>
            <option value="ONLINE">Online ranh</option>
            <option value="OFFLINE">Offline</option>
          </select>
        </div>

        <div className="toolbar-item toolbar-search">
          <label htmlFor="search-pc">Tim may:</label>
          <input
            id="search-pc"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Nhap ten may / agent id"
          />
        </div>
      </section>

      {loading && <p className="info">Dang tai danh sach may...</p>}
      {error && <p className="error">{error}</p>}
      {actionMessage && <p className="info">{actionMessage}</p>}

      <section className="table-wrap">
        <table className="history-table">
          <thead>
            <tr>
              <th>Ten may</th>
              <th>Tinh trang</th>
              <th>Nguoi su dung</th>
              <th>Bat dau</th>
              <th>Da su dung</th>
              <th>Con lai</th>
              <th>Tien</th>
              <th>Nhom</th>
              <th>Thao tac</th>
            </tr>
          </thead>
          <tbody>
            {filteredPcs.map((pc) => {
              const elapsed = pc.activeSession
                ? Math.max(0, pc.activeSession.elapsedSeconds + tick)
                : 0;

              return (
                <tr key={pc.id}>
                  <td>{pc.name}</td>
                  <td>
                    <span className={statusClass(pc.status)}>{statusText(pc.status)}</span>
                  </td>
                  <td>-</td>
                  <td>{formatClock(pc.activeSession?.startedAt ?? null)}</td>
                  <td>{pc.activeSession ? formatDuration(elapsed) : '-'}</td>
                  <td>-</td>
                  <td>
                    {pc.activeSession
                      ? formatMoney(pc.activeSession.estimatedAmount)
                      : '-'}
                  </td>
                  <td>Mac dinh</td>
                  <td>
                    <div className="actions">
                      <button
                        disabled={pendingPcActions[pc.id]}
                        onClick={() => void applyAction(pc.id, 'open')}
                      >
                        Mo
                      </button>
                      <button
                        disabled={pendingPcActions[pc.id]}
                        onClick={() => void applyAction(pc.id, 'lock')}
                      >
                        Khoa
                      </button>
                    </div>
                  </td>
                </tr>
              );
            })}
            {!loading && filteredPcs.length === 0 && (
              <tr>
                <td colSpan={9}>Khong co may phu hop bo loc</td>
              </tr>
            )}
          </tbody>
        </table>
      </section>
    </main>
  );
}
