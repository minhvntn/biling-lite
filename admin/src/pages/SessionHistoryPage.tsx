import { useEffect, useMemo, useState } from 'react';
import { fetchDailyRevenue } from '../api/reports';
import { fetchSessions } from '../api/sessions';
import { TopNav } from '../components/TopNav';
import { DailyRevenueResponse, SessionItem } from '../types/session';

function toDayInputValue(date: Date): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

function formatDateTime(value: string | null): string {
  if (!value) {
    return 'N/A';
  }

  return new Date(value).toLocaleString();
}

function formatMoney(amount: number | null): string {
  if (amount === null) {
    return 'N/A';
  }

  return amount.toLocaleString('vi-VN');
}

export function SessionHistoryPage() {
  const [date, setDate] = useState(toDayInputValue(new Date()));
  const [sessions, setSessions] = useState<SessionItem[]>([]);
  const [revenue, setRevenue] = useState<DailyRevenueResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadData = async (targetDate: string) => {
    setLoading(true);
    setError(null);
    try {
      const startLocal = new Date(`${targetDate}T00:00:00`);
      const endLocal = new Date(`${targetDate}T23:59:59.999`);
      const from = startLocal.toISOString();
      const to = endLocal.toISOString();
      const [sessionData, revenueData] = await Promise.all([
        fetchSessions({ from, to }),
        fetchDailyRevenue(targetDate),
      ]);

      setSessions(sessionData.items);
      setRevenue(revenueData);
    } catch (loadError) {
      const message =
        loadError instanceof Error ? loadError.message : 'Unknown error';
      setError(message);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadData(date);
  }, [date]);

  const closedCount = useMemo(
    () => sessions.filter((session) => session.status === 'CLOSED').length,
    [sessions],
  );

  return (
    <main className="layout">
      <TopNav />
      <section className="hero">
        <h1>Session History</h1>
        <p>Daily revenue and session audit</p>
        <div className="meta">
          <label>
            Date:{' '}
            <input
              type="date"
              value={date}
              onChange={(event) => setDate(event.target.value)}
            />
          </label>
          <button onClick={() => void loadData(date)} disabled={loading}>
            Refresh
          </button>
        </div>
      </section>

      <section className="summary-grid">
        <article className="summary-card">
          <h2>{sessions.length}</h2>
          <p>Total Sessions</p>
        </article>
        <article className="summary-card">
          <h2>{closedCount}</h2>
          <p>Closed Sessions</p>
        </article>
        <article className="summary-card">
          <h2>{revenue?.closedSessions ?? 0}</h2>
          <p>Revenue Sessions</p>
        </article>
        <article className="summary-card">
          <h2>{formatMoney(revenue?.totalAmount ?? 0)}</h2>
          <p>Daily Revenue (VND)</p>
        </article>
      </section>

      {loading && <p className="info">Loading session data...</p>}
      {error && <p className="error">{error}</p>}

      <section className="table-wrap">
        <table className="history-table">
          <thead>
            <tr>
              <th>PC</th>
              <th>Status</th>
              <th>Start</th>
              <th>End</th>
              <th>Minutes</th>
              <th>Amount</th>
              <th>Reason</th>
            </tr>
          </thead>
          <tbody>
            {sessions.map((session) => (
              <tr key={session.id}>
                <td>
                  {session.pcName}
                  <div className="muted">{session.agentId}</div>
                </td>
                <td>{session.status}</td>
                <td>{formatDateTime(session.startedAt)}</td>
                <td>{formatDateTime(session.endedAt)}</td>
                <td>{session.billableMinutes ?? 'N/A'}</td>
                <td>{formatMoney(session.amount)}</td>
                <td>{session.closedReason ?? '-'}</td>
              </tr>
            ))}
            {!loading && sessions.length === 0 && (
              <tr>
                <td colSpan={7}>No sessions found</td>
              </tr>
            )}
          </tbody>
        </table>
      </section>
    </main>
  );
}
