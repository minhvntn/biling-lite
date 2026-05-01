import { FormEvent, useEffect, useMemo, useState } from 'react';
import {
  buyHours,
  createMember,
  fetchMembers,
  fetchMemberTransactions,
  topupMember,
} from '../api/members';
import { TopNav } from '../components/TopNav';
import { MemberItem, MemberTransactionItem } from '../types/member';

function formatMoney(value: number): string {
  return value.toLocaleString('vi-VN');
}

function formatDateTime(value: string): string {
  return new Date(value).toLocaleString();
}

function txTypeLabel(type: MemberTransactionItem['type']): string {
  switch (type) {
    case 'TOPUP':
      return 'Nap tien';
    case 'BUY_PLAYTIME':
      return 'Mua gio';
    default:
      return 'Dieu chinh';
  }
}

export function MembersPage() {
  const [members, setMembers] = useState<MemberItem[]>([]);
  const [selectedMemberId, setSelectedMemberId] = useState<string | null>(null);
  const [transactions, setTransactions] = useState<MemberTransactionItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState('');
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [newUsername, setNewUsername] = useState('');
  const [newFullName, setNewFullName] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [newPhone, setNewPhone] = useState('');
  const [newIdentityNumber, setNewIdentityNumber] = useState('');

  const [topupAmount, setTopupAmount] = useState('50000');
  const [buyHoursValue, setBuyHoursValue] = useState('2');
  const [ratePerHourValue, setRatePerHourValue] = useState('15000');

  const selectedMember = useMemo(
    () => members.find((member) => member.id === selectedMemberId) ?? null,
    [members, selectedMemberId],
  );

  const loadMembers = async (searchKeyword?: string) => {
    setLoading(true);
    setError(null);

    try {
      const response = await fetchMembers(searchKeyword);
      setMembers(response.items);

      const nextSelectedId =
        response.items.find((item) => item.id === selectedMemberId)?.id ??
        response.items[0]?.id ??
        null;

      setSelectedMemberId(nextSelectedId);
      if (nextSelectedId) {
        const tx = await fetchMemberTransactions(nextSelectedId);
        setTransactions(tx.items);
      } else {
        setTransactions([]);
      }
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : 'Tai du lieu that bai');
    } finally {
      setLoading(false);
    }
  };

  const loadTransactions = async (memberId: string) => {
    try {
      const response = await fetchMemberTransactions(memberId);
      setTransactions(response.items);
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : 'Tai giao dich that bai');
    }
  };

  useEffect(() => {
    void loadMembers();
  }, []);

  const handleCreateMember = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setMessage(null);
    setError(null);

    try {
      await createMember({
        username: newUsername,
        fullName: newFullName,
        password: newPassword,
        phone: newPhone.trim() || undefined,
        identityNumber: newIdentityNumber.trim() || undefined,
      });

      setMessage('Tao hoi vien thanh cong');
      setNewUsername('');
      setNewFullName('');
      setNewPassword('');
      setNewPhone('');
      setNewIdentityNumber('');
      await loadMembers(search);
    } catch (createError) {
      setError(createError instanceof Error ? createError.message : 'Tao hoi vien that bai');
    }
  };

  const handleTopup = async () => {
    if (!selectedMemberId) {
      return;
    }

    setMessage(null);
    setError(null);

    try {
      await topupMember(selectedMemberId, {
        amount: Number(topupAmount),
        createdBy: 'admin.web',
      });

      setMessage('Nap tien thanh cong');
      await loadMembers(search);
      await loadTransactions(selectedMemberId);
    } catch (topupError) {
      setError(topupError instanceof Error ? topupError.message : 'Nap tien that bai');
    }
  };

  const handleBuyHours = async () => {
    if (!selectedMemberId) {
      return;
    }

    setMessage(null);
    setError(null);

    try {
      await buyHours(selectedMemberId, {
        hours: Number(buyHoursValue),
        ratePerHour: Number(ratePerHourValue),
        createdBy: 'admin.web',
      });

      setMessage('Mua gio choi thanh cong');
      await loadMembers(search);
      await loadTransactions(selectedMemberId);
    } catch (buyError) {
      setError(buyError instanceof Error ? buyError.message : 'Mua gio that bai');
    }
  };

  return (
    <main className="layout">
      <TopNav />
      <section className="hero">
        <h1>Hoi vien</h1>
        <p>Tao tai khoan, nap tien va quy doi tien thanh gio choi</p>
        <div className="meta">
          <input
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Tim username / ten / SDT"
          />
          <button onClick={() => void loadMembers(search)} disabled={loading}>
            Lam moi
          </button>
        </div>
      </section>

      {message && <p className="info">{message}</p>}
      {error && <p className="error">{error}</p>}

      <section className="summary-grid" style={{ gridTemplateColumns: '2fr 1fr 1fr 1fr' }}>
        <article className="summary-card">
          <h2>{members.length}</h2>
          <p>Tong hoi vien</p>
        </article>
        <article className="summary-card">
          <h2>{selectedMember ? formatMoney(selectedMember.balance) : '-'}</h2>
          <p>So du duoc chon</p>
        </article>
        <article className="summary-card">
          <h2>{selectedMember ? selectedMember.playHours.toFixed(2) : '-'}</h2>
          <p>Gio choi duoc chon</p>
        </article>
        <article className="summary-card">
          <h2>{transactions.length}</h2>
          <p>Giao dich</p>
        </article>
      </section>

      <section className="toolbar-row" style={{ marginTop: '1rem' }}>
        <form onSubmit={handleCreateMember} className="toolbar-item" style={{ gap: '0.5rem', flexWrap: 'wrap' }}>
          <input
            value={newUsername}
            onChange={(event) => setNewUsername(event.target.value)}
            placeholder="Username"
            required
          />
          <input
            value={newFullName}
            onChange={(event) => setNewFullName(event.target.value)}
            placeholder="Ho ten"
            required
          />
          <input
            value={newPassword}
            onChange={(event) => setNewPassword(event.target.value)}
            placeholder="Mat khau"
            required
            minLength={1}
          />
          <input
            value={newPhone}
            onChange={(event) => setNewPhone(event.target.value)}
            placeholder="So dien thoai"
          />
          <input
            value={newIdentityNumber}
            onChange={(event) => setNewIdentityNumber(event.target.value)}
            placeholder="CCCD/CMND"
          />
          <button type="submit">Tao hoi vien</button>
        </form>
      </section>

      <section className="table-wrap" style={{ marginTop: '0.8rem' }}>
        <table className="history-table">
          <thead>
            <tr>
              <th>Username</th>
              <th>Ho ten</th>
              <th>SDT</th>
              <th>CCCD/CMND</th>
              <th>So du</th>
              <th>Gio choi</th>
              <th>Trang thai</th>
              <th>Hanh dong</th>
            </tr>
          </thead>
          <tbody>
            {members.map((member) => (
              <tr key={member.id}>
                <td>{member.username}</td>
                <td>{member.fullName}</td>
                <td>{member.phone ?? '-'}</td>
                <td>{member.identityNumber ?? '-'}</td>
                <td>{formatMoney(member.balance)}</td>
                <td>{member.playHours.toFixed(2)}</td>
                <td>{member.isActive ? 'Hoat dong' : 'Tam khoa'}</td>
                <td>
                  <button
                    onClick={() => {
                      setSelectedMemberId(member.id);
                      void loadTransactions(member.id);
                    }}
                  >
                    Chon
                  </button>
                </td>
              </tr>
            ))}
            {!loading && members.length === 0 && (
              <tr>
                <td colSpan={8}>Chua co hoi vien</td>
              </tr>
            )}
          </tbody>
        </table>
      </section>

      <section className="toolbar-row" style={{ marginTop: '1rem' }}>
        <div className="toolbar-item">
          <strong>Hoi vien dang chon:</strong>
          <span>{selectedMember ? `${selectedMember.username} - ${selectedMember.fullName}` : 'Chua chon'}</span>
        </div>
      </section>

      <section className="toolbar-row" style={{ marginTop: '0.5rem' }}>
        <div className="toolbar-item" style={{ gap: '0.45rem' }}>
          <input
            type="number"
            min={1000}
            step={1000}
            value={topupAmount}
            onChange={(event) => setTopupAmount(event.target.value)}
            placeholder="So tien nap"
          />
          <button onClick={() => void handleTopup()} disabled={!selectedMemberId}>
            Nap tien
          </button>
        </div>

        <div className="toolbar-item" style={{ gap: '0.45rem' }}>
          <input
            type="number"
            min={0.5}
            max={24}
            step={0.5}
            value={buyHoursValue}
            onChange={(event) => setBuyHoursValue(event.target.value)}
            placeholder="So gio"
          />
          <input
            type="number"
            min={1000}
            step={1000}
            value={ratePerHourValue}
            onChange={(event) => setRatePerHourValue(event.target.value)}
            placeholder="Gia/gio"
          />
          <button onClick={() => void handleBuyHours()} disabled={!selectedMemberId}>
            Mua gio choi
          </button>
        </div>
      </section>

      <section className="table-wrap" style={{ marginTop: '0.8rem' }}>
        <table className="history-table">
          <thead>
            <tr>
              <th>Thoi gian</th>
              <th>Loai</th>
              <th>Tien thay doi</th>
              <th>Gio thay doi</th>
              <th>Nguoi tao</th>
              <th>Ghi chu</th>
            </tr>
          </thead>
          <tbody>
            {transactions.map((item) => (
              <tr key={item.id}>
                <td>{formatDateTime(item.createdAt)}</td>
                <td>{txTypeLabel(item.type)}</td>
                <td>{formatMoney(item.amountDelta)}</td>
                <td>{(item.playSecondsDelta / 3600).toFixed(2)}</td>
                <td>{item.createdBy}</td>
                <td>{item.note ?? '-'}</td>
              </tr>
            ))}
            {!loading && transactions.length === 0 && (
              <tr>
                <td colSpan={6}>Chua co giao dich</td>
              </tr>
            )}
          </tbody>
        </table>
      </section>
    </main>
  );
}
