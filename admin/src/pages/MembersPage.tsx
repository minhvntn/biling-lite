import { FormEvent, useEffect, useMemo, useState } from 'react';
import {
  createMember,
  fetchMembers,
  topupMember,
} from '../api/members';
import { TopNav } from '../components/TopNav';
import { MemberItem } from '../types/member';

function formatMoney(value: number): string {
  return value.toLocaleString('vi-VN');
}

export function MembersPage() {
  const [members, setMembers] = useState<MemberItem[]>([]);
  const [selectedMemberId, setSelectedMemberId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState('');
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [showDetailModal, setShowDetailModal] = useState(false);
  const [isMounted, setIsMounted] = useState(false);

  const [newUsername, setNewUsername] = useState('');
  const [newFullName, setNewFullName] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [newPhone, setNewPhone] = useState('');
  const [newIdentityNumber, setNewIdentityNumber] = useState('');

  const [topupAmount, setTopupAmount] = useState('50000');

  const [currentPage, setCurrentPage] = useState(1);
  const itemsPerPage = 10;

  const paginatedMembers = useMemo(() => {
    const startIndex = (currentPage - 1) * itemsPerPage;
    return members.slice(startIndex, startIndex + itemsPerPage);
  }, [members, currentPage]);

  const totalPages = useMemo(() => {
    return Math.ceil(members.length / itemsPerPage) || 1;
  }, [members]);

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
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : 'Tải dữ liệu thất bại');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    setCurrentPage(1);
    if (!isMounted) {
      setIsMounted(true);
      void loadMembers(search);
      return;
    }

    const delayDebounceFn = setTimeout(() => {
      void loadMembers(search);
    }, 250);

    return () => clearTimeout(delayDebounceFn);
  }, [search]);

  useEffect(() => {
    if (currentPage > totalPages) {
      setCurrentPage(totalPages);
    }
  }, [members, totalPages, currentPage]);

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

      setMessage('Tạo hội viên thành công');
      setNewUsername('');
      setNewFullName('');
      setNewPassword('');
      setNewPhone('');
      setNewIdentityNumber('');
      setShowCreateModal(false);
      await loadMembers(search);
    } catch (createError) {
      setError(createError instanceof Error ? createError.message : 'Tạo hội viên thất bại');
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

      setMessage('Nạp tiền thành công');
      await loadMembers(search);
    } catch (topupError) {
      setError(topupError instanceof Error ? topupError.message : 'Nạp tiền thất bại');
    }
  };

  return (
    <main className="layout" style={{ maxWidth: '800px', margin: '0 auto' }}>
      <TopNav />

      {/* Hero / Header Row */}
      <section className="hero" style={{ display: 'flex', flexDirection: 'column', gap: '0.4rem', marginBottom: '0.75rem' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', width: '100%' }}>
          <h1 style={{ margin: 0, fontSize: '1.25rem' }}>Hội viên</h1>
          <button
            onClick={() => {
              setError(null);
              setMessage(null);
              setShowCreateModal(true);
            }}
            style={{
              background: '#0066cc',
              color: '#fff',
              padding: '0.4rem 0.8rem',
              borderRadius: '6px',
              fontWeight: 600,
              cursor: 'pointer',
              border: 'none',
              fontSize: '0.8rem',
              display: 'flex',
              alignItems: 'center',
              gap: '0.25rem',
              boxShadow: '0 2px 4px rgba(0,102,204,0.15)'
            }}
          >
            <span style={{ fontSize: '1rem', fontWeight: 'bold', lineHeight: 0.9 }}>+</span> Thêm hội viên
          </button>
        </div>
        <p style={{ margin: 0, fontSize: '0.8rem', color: 'var(--muted)' }}>Tạo tài khoản, nạp tiền và quy đổi tiền thành giờ chơi</p>
      </section>

      {message && <p className="info" style={{ color: '#155724', background: '#d4edda', border: '1px solid #c3e6cb', padding: '0.5rem 0.75rem', borderRadius: '6px', marginBottom: '0.6rem', fontSize: '0.8rem' }}>{message}</p>}
      {error && <p className="error" style={{ color: '#721c24', background: '#f8d7da', border: '1px solid #f5c6cb', padding: '0.5rem 0.75rem', borderRadius: '6px', marginBottom: '0.6rem', fontSize: '0.8rem' }}>{error}</p>}

      {/* Auto Search Bar - Compact style */}
      <div style={{ display: 'flex', gap: '0.4rem', marginBottom: '0.6rem' }}>
        <input
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          placeholder="Tìm username / họ tên / số điện thoại..."
          type="search"
          style={{
            flex: 1,
            padding: '0.45rem 0.65rem',
            borderRadius: '6px',
            border: '1px solid var(--line)',
            background: 'var(--surface)',
            color: 'var(--text)',
            fontSize: '0.85rem'
          }}
        />
        {search && (
          <button
            onClick={() => setSearch('')}
            disabled={loading}
            style={{
              padding: '0.45rem 0.85rem',
              borderRadius: '6px',
              background: 'var(--surface-2)',
              border: '1px solid var(--line)',
              color: 'var(--text)',
              fontWeight: 600,
              cursor: 'pointer',
              fontSize: '0.8rem',
              whiteSpace: 'nowrap'
            }}
          >
            Làm mới
          </button>
        )}
      </div>

      {/* Compact Member Table */}
      <section style={{ margin: 0, width: '100%', overflowX: 'auto', WebkitOverflowScrolling: 'touch' }}>
        <div style={{
          background: 'var(--surface)',
          border: '1px solid var(--line)',
          borderRadius: '8px',
          overflow: 'hidden',
          width: '100%'
        }}>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.82rem', tableLayout: 'fixed' }}>
            <thead>
              <tr style={{ background: 'var(--surface-2)', borderBottom: '1px solid var(--line)' }}>
                <th style={{ padding: '0.5rem 0.4rem', textAlign: 'left', fontWeight: 600, color: 'var(--text)', width: '45%' }}>Username</th>
                <th style={{ padding: '0.5rem 0.4rem', textAlign: 'left', fontWeight: 600, color: 'var(--text)', width: '35%' }}>Số dư</th>
                <th style={{ padding: '0.5rem 0.4rem', textAlign: 'center', fontWeight: 600, color: 'var(--text)', width: '20%' }}>Chọn</th>
              </tr>
            </thead>
            <tbody>
              {paginatedMembers.map((member) => (
                <tr
                  key={member.id}
                  onClick={() => {
                    setSelectedMemberId(member.id);
                    setShowDetailModal(true);
                  }}
                  style={{
                    borderBottom: '1px solid var(--line)',
                    cursor: 'pointer',
                    background: selectedMemberId === member.id ? 'var(--surface-2)' : 'transparent'
                  }}
                >
                  <td style={{ padding: '0.5rem 0.4rem', fontWeight: 600, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                    {member.username}
                  </td>
                  <td style={{ padding: '0.5rem 0.4rem', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                    {formatMoney(member.balance)} đ
                  </td>
                  <td style={{ padding: '0.5rem 0.4rem', textAlign: 'center' }}>
                    <button
                      onClick={(e) => {
                        e.stopPropagation();
                        setSelectedMemberId(member.id);
                        setShowDetailModal(true);
                      }}
                      style={{
                        background: selectedMemberId === member.id ? '#0066cc' : 'var(--surface-2)',
                        color: selectedMemberId === member.id ? '#fff' : 'var(--text)',
                        padding: '0.25rem 0.5rem',
                        borderRadius: '4px',
                        fontSize: '0.75rem',
                        fontWeight: 600,
                        border: '1px solid var(--line)',
                        cursor: 'pointer'
                      }}
                    >
                      Chọn
                    </button>
                  </td>
                </tr>
              ))}
              {!loading && members.length === 0 && (
                <tr>
                  <td colSpan={3} style={{ textAlign: 'center', padding: '1.5rem', color: 'var(--muted)' }}>
                    Chưa có hội viên nào
                  </td>
                </tr>
              )}
            </tbody>
          </table>

          {/* Pagination Controls */}
          {totalPages > 1 && (
            <div style={{
              display: 'flex',
              justifyContent: 'space-between',
              alignItems: 'center',
              padding: '0.5rem 0.75rem',
              borderTop: '1px solid var(--line)',
              background: 'var(--surface-2)',
              fontSize: '0.8rem',
              userSelect: 'none'
            }}>
              <button
                type="button"
                onClick={() => setCurrentPage(prev => Math.max(prev - 1, 1))}
                disabled={currentPage === 1}
                style={{
                  padding: '0.25rem 0.5rem',
                  borderRadius: '4px',
                  border: '1px solid var(--line)',
                  background: currentPage === 1 ? 'var(--surface)' : 'var(--surface-3, #fff)',
                  color: currentPage === 1 ? 'var(--muted)' : 'var(--text)',
                  cursor: currentPage === 1 ? 'default' : 'pointer',
                  opacity: currentPage === 1 ? 0.5 : 1,
                  fontWeight: 600,
                  fontSize: '0.75rem'
                }}
              >
                Trước
              </button>
              <span style={{ color: 'var(--muted)', fontSize: '0.75rem' }}>
                Trang <strong>{currentPage}</strong> / {totalPages} (Tổng {members.length})
              </span>
              <button
                type="button"
                onClick={() => setCurrentPage(prev => Math.min(prev + 1, totalPages))}
                disabled={currentPage === totalPages}
                style={{
                  padding: '0.25rem 0.5rem',
                  borderRadius: '4px',
                  border: '1px solid var(--line)',
                  background: currentPage === totalPages ? 'var(--surface)' : 'var(--surface-3, #fff)',
                  color: currentPage === totalPages ? 'var(--muted)' : 'var(--text)',
                  cursor: currentPage === totalPages ? 'default' : 'pointer',
                  opacity: currentPage === totalPages ? 0.5 : 1,
                  fontWeight: 600,
                  fontSize: '0.75rem'
                }}
              >
                Sau
              </button>
            </div>
          )}
        </div>
      </section>

      {/* Popup / Modal: Thêm hội viên */}
      {showCreateModal && (
        <div className="modal-backdrop" onClick={() => setShowCreateModal(false)}>
          <div className="modal-box" style={{ maxWidth: '400px' }} onClick={(e) => e.stopPropagation()}>
            <div className="modal-header" style={{ padding: '0.75rem 1rem' }}>
              <h2 style={{ margin: 0, fontSize: '1.05rem' }}>Thêm hội viên mới</h2>
              <button
                className="modal-close-btn"
                onClick={() => setShowCreateModal(false)}
                style={{ cursor: 'pointer', border: 'none', background: 'transparent', fontSize: '1.3rem', color: 'var(--muted)' }}
              >
                ×
              </button>
            </div>
            <form onSubmit={handleCreateMember}>
              <div className="modal-body" style={{ display: 'flex', flexDirection: 'column', gap: '0.65rem', padding: '1rem' }}>
                <div className="form-group">
                  <label htmlFor="modal-username" style={{ fontSize: '0.78rem', marginBottom: '0.2rem' }}>Tên đăng nhập *</label>
                  <input
                    id="modal-username"
                    value={newUsername}
                    onChange={(event) => setNewUsername(event.target.value)}
                    placeholder="Nhập tên đăng nhập..."
                    required
                    style={{ padding: '0.45rem 0.6rem', fontSize: '0.82rem' }}
                  />
                </div>
                <div className="form-group">
                  <label htmlFor="modal-fullname" style={{ fontSize: '0.78rem', marginBottom: '0.2rem' }}>Họ tên *</label>
                  <input
                    id="modal-fullname"
                    value={newFullName}
                    onChange={(event) => setNewFullName(event.target.value)}
                    placeholder="Nhập họ và tên..."
                    required
                    style={{ padding: '0.45rem 0.6rem', fontSize: '0.82rem' }}
                  />
                </div>
                <div className="form-group">
                  <label htmlFor="modal-password" style={{ fontSize: '0.78rem', marginBottom: '0.2rem' }}>Mật khẩu *</label>
                  <input
                    id="modal-password"
                    type="password"
                    value={newPassword}
                    onChange={(event) => setNewPassword(event.target.value)}
                    placeholder="Nhập mật khẩu..."
                    required
                    style={{ padding: '0.45rem 0.6rem', fontSize: '0.82rem' }}
                  />
                </div>
                <div className="form-group">
                  <label htmlFor="modal-phone" style={{ fontSize: '0.78rem', marginBottom: '0.2rem' }}>Số điện thoại</label>
                  <input
                    id="modal-phone"
                    value={newPhone}
                    onChange={(event) => setNewPhone(event.target.value)}
                    placeholder="Nhập số điện thoại (tùy chọn)..."
                    style={{ padding: '0.45rem 0.6rem', fontSize: '0.82rem' }}
                  />
                </div>
                <div className="form-group">
                  <label htmlFor="modal-identity" style={{ fontSize: '0.78rem', marginBottom: '0.2rem' }}>Số CCCD/CMND</label>
                  <input
                    id="modal-identity"
                    value={newIdentityNumber}
                    onChange={(event) => setNewIdentityNumber(event.target.value)}
                    placeholder="Nhập số CCCD/CMND (tùy chọn)..."
                    style={{ padding: '0.45rem 0.6rem', fontSize: '0.82rem' }}
                  />
                </div>
              </div>
              <div className="modal-footer" style={{ padding: '0.65rem 1rem', display: 'flex', justifyContent: 'flex-end', gap: '0.4rem', background: 'var(--surface-2)', borderTop: '1px solid var(--line)' }}>
                <button
                  type="button"
                  className="btn-secondary"
                  onClick={() => setShowCreateModal(false)}
                  style={{ padding: '0.35rem 0.85rem', borderRadius: '5px', cursor: 'pointer', border: '1px solid var(--line)', background: 'var(--surface)', color: 'var(--text)', fontSize: '0.8rem' }}
                >
                  Hủy
                </button>
                <button
                  type="submit"
                  style={{
                    padding: '0.35rem 1rem',
                    borderRadius: '5px',
                    cursor: 'pointer',
                    border: 'none',
                    background: '#0066cc',
                    color: '#fff',
                    fontWeight: 600,
                    fontSize: '0.8rem'
                  }}
                >
                  Tạo hội viên
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Popup / Modal: Chi tiết hội viên */}
      {showDetailModal && selectedMember && (
        <div className="modal-backdrop" onClick={() => setShowDetailModal(false)}>
          <div className="modal-box" style={{ maxWidth: '400px' }} onClick={(e) => e.stopPropagation()}>
            <div className="modal-header" style={{ padding: '0.75rem 1rem' }}>
              <h2 style={{ margin: 0, fontSize: '1.05rem' }}>Chi tiết hội viên</h2>
              <button
                className="modal-close-btn"
                onClick={() => setShowDetailModal(false)}
                style={{ cursor: 'pointer', border: 'none', background: 'transparent', fontSize: '1.3rem', color: 'var(--muted)' }}
              >
                ×
              </button>
            </div>
            <div className="modal-body" style={{ display: 'flex', flexDirection: 'column', gap: '1rem', padding: '1rem', overflowY: 'auto', maxHeight: '70vh' }}>

              <div>
                <div style={{ display: 'flex', flexDirection: 'column', gap: '0.35rem', fontSize: '0.82rem' }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                    <span style={{ color: 'var(--muted)' }}>Tên đăng nhập:</span>
                    <strong style={{ color: 'var(--text)' }}>{selectedMember.username}</strong>
                  </div>
                  <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                    <span style={{ color: 'var(--muted)' }}>Họ tên:</span>
                    <span style={{ color: 'var(--text)' }}>{selectedMember.fullName}</span>
                  </div>
                  <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                    <span style={{ color: 'var(--muted)' }}>Số điện thoại:</span>
                    <span style={{ color: 'var(--text)' }}>{selectedMember.phone ?? '-'}</span>
                  </div>
                  <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                    <span style={{ color: 'var(--muted)' }}>CCCD/CMND:</span>
                    <span style={{ color: 'var(--text)' }}>{selectedMember.identityNumber ?? '-'}</span>
                  </div>
                  <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                    <span style={{ color: 'var(--muted)' }}>Trạng thái:</span>
                    <span style={{
                      color: selectedMember.isActive ? '#12b76a' : '#f04438',
                      fontWeight: 600
                    }}>
                      {selectedMember.isActive ? 'Hoạt động' : 'Tạm khóa'}
                    </span>
                  </div>
                  <div style={{ display: 'flex', justifyContent: 'space-between', borderTop: '1px solid var(--line)', paddingTop: '0.4rem', marginTop: '0.2rem' }}>
                    <span style={{ color: 'var(--muted)' }}>Số dư tài khoản:</span>
                    <strong style={{ color: '#0066cc', fontSize: '0.95rem' }}>{formatMoney(selectedMember.balance)} đ</strong>
                  </div>
                </div>
              </div>

              {/* Form Nạp tiền */}
              <div style={{ borderTop: '1px solid var(--line)', paddingTop: '0.75rem' }}>
                <h4 style={{ margin: '0 0 0.4rem 0', fontSize: '0.85rem' }}>Nạp tiền tài khoản</h4>
                <div style={{ display: 'flex', gap: '0.3rem' }}>
                  <input
                    type="number"
                    min={1000}
                    step={1000}
                    value={topupAmount}
                    onChange={(event) => setTopupAmount(event.target.value)}
                    placeholder="Số tiền nạp"
                    style={{
                      flex: 1,
                      padding: '0.4rem 0.5rem',
                      borderRadius: '5px',
                      border: '1px solid var(--line)',
                      background: 'var(--surface)',
                      color: 'var(--text)',
                      fontSize: '0.8rem'
                    }}
                  />
                  <button
                    onClick={() => void handleTopup()}
                    style={{
                      background: '#12b76a',
                      color: '#fff',
                      border: 'none',
                      padding: '0.4rem 0.8rem',
                      borderRadius: '5px',
                      fontWeight: 600,
                      cursor: 'pointer',
                      fontSize: '0.8rem'
                    }}
                  >
                    Nạp tiền
                  </button>
                </div>
                {/* Actions chọn nhanh */}
                <div style={{ display: 'flex', flexWrap: 'wrap', gap: '0.35rem', marginTop: '0.5rem' }}>
                  {[1000, 2000, 3000, 4000, 5000, 6000, 7000, 8000, 9000, 10000, 20000].map((val) => (
                    <button
                      key={val}
                      onClick={() => setTopupAmount(String(val))}
                      style={{
                        padding: '0.25rem 0.45rem',
                        borderRadius: '4px',
                        border: '1px solid var(--line)',
                        background: topupAmount === String(val) ? '#12b76a' : 'var(--surface-2)',
                        color: topupAmount === String(val) ? '#fff' : 'var(--text)',
                        fontSize: '0.72rem',
                        fontWeight: 600,
                        cursor: 'pointer'
                      }}
                    >
                      {val.toLocaleString('vi-VN')}
                    </button>
                  ))}
                </div>
              </div>
            </div>
            <div className="modal-footer" style={{ padding: '0.65rem 1rem', display: 'flex', justifyContent: 'flex-end', background: 'var(--surface-2)', borderTop: '1px solid var(--line)' }}>
              <button
                type="button"
                className="btn-secondary"
                onClick={() => setShowDetailModal(false)}
                style={{ padding: '0.35rem 0.85rem', borderRadius: '5px', cursor: 'pointer', border: '1px solid var(--line)', background: 'var(--surface)', color: 'var(--text)', fontSize: '0.8rem' }}
              >
                Đóng
              </button>
            </div>
          </div>
        </div>
      )}
    </main>
  );
}
