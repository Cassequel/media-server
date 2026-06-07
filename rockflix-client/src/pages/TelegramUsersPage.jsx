import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../context/AuthContext'
import api from '../services/api'

const LIMIT_BYTES = 20 * 1024 * 1024 * 1024

function formatBytes(bytes) {
  if (!bytes || bytes === 0) return '0 GB'
  const gb = bytes / (1024 * 1024 * 1024)
  return gb >= 1 ? `${gb.toFixed(1)} GB` : `${(bytes / (1024 * 1024)).toFixed(0)} MB`
}

export default function TelegramUsersPage() {
  const { user } = useAuth()
  const navigate = useNavigate()
  const [users, setUsers] = useState([])
  const [loading, setLoading] = useState(true)
  const [fetchError, setFetchError] = useState('')
  const [actionLoading, setActionLoading] = useState(null)
  const [requestsModal, setRequestsModal] = useState(null) // { username, requests }

  useEffect(() => {
    if (!user?.isAdmin) { navigate('/'); return }
    fetchUsers()
  }, [])

  async function fetchUsers() {
    try {
      const { data } = await api.get('/admin/users')
      setUsers(data)
    } catch (err) {
      setFetchError(err.response?.data?.message ?? 'Failed to load users.')
    } finally {
      setLoading(false)
    }
  }

  async function toggleActive(u) {
    setActionLoading(u.id ?? u.telegramChatId)
    try {
      if (u.source === 'web') {
        const action = u.isActive ? 'revoke' : 'restore'
        await api.patch(`/admin/users/${u.id}/${action}`)
      } else {
        const action = u.isActive ? 'revoke' : 'restore'
        await api.patch(`/admin/telegram-users/${u.telegramChatId}/${action}`)
      }
      setUsers(prev => prev.map(x =>
        (x.id && x.id === u.id) || (x.telegramChatId && x.telegramChatId === u.telegramChatId)
          ? { ...x, isActive: !u.isActive }
          : x
      ))
    } catch {
      // silent
    } finally {
      setActionLoading(null)
    }
  }

  async function viewRequests(u) {
    if (!u.id) return
    try {
      const { data } = await api.get(`/admin/users/${u.id}/requests`)
      setRequestsModal({ username: u.username, requests: data })
    } catch {
      // silent
    }
  }

  async function toggleAdmin(u) {
    if (!u.id) return
    setActionLoading(`admin-${u.id}`)
    try {
      const { data } = await api.patch(`/admin/users/${u.id}/admin`)
      setUsers(prev => prev.map(x => x.id === u.id ? { ...x, isAdmin: data.isAdmin } : x))
    } catch {
      // silent
    } finally {
      setActionLoading(null)
    }
  }

  const webUsers = users.filter(u => u.source === 'web')
  const telegramOnly = users.filter(u => u.source === 'telegram')

  return (
    <div className="min-h-screen bg-[#1e100b] text-[#f9dcd4] pb-16">
      {/* Header */}
      <header className="fixed top-0 left-0 right-0 z-50 flex justify-between items-center px-12 h-16 bg-[#0e0e0e]/80 backdrop-blur-xl">
        <div className="flex items-center gap-6">
          <button
            onClick={() => navigate('/')}
            className="text-2xl font-black tracking-tighter text-[#ff9069]"
            style={{ fontFamily: 'Epilogue, sans-serif' }}
          >
            Rockflix
          </button>
          <span className="text-[#e3bfb3] text-sm hidden md:block" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
            / Users
          </span>
        </div>
        <button
          onClick={() => navigate('/')}
          className="text-[#e3bfb3] hover:text-[#ff9069] transition-colors text-sm flex items-center gap-2"
          style={{ fontFamily: 'Space Grotesk, sans-serif' }}
        >
          <span className="material-symbols-outlined text-base">arrow_back</span>
          Back
        </button>
      </header>

      <div className="pt-40 px-8 max-w-5xl mx-auto">
        {/* Title */}
        <div className="mb-10">
          <h1 className="text-4xl md:text-5xl font-black tracking-tighter uppercase mb-2" style={{ fontFamily: 'Epilogue, sans-serif' }}>
            Users
          </h1>
          <p className="text-[#e3bfb3] text-sm" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
            Manage accounts, storage usage, and access
          </p>
        </div>

        {/* Stats */}
        <div className="grid grid-cols-3 gap-4 mb-10">
          {[
            { label: 'Total Users', value: users.length },
            { label: 'Active', value: users.filter(u => u.isActive).length },
            { label: 'Total Requests', value: users.reduce((s, u) => s + u.requestCount, 0) },
          ].map(stat => (
            <div key={stat.label} className="bg-[#2b1c17] rounded-2xl p-7">
              <p className="text-3xl font-black text-[#ff9069]" style={{ fontFamily: 'Epilogue, sans-serif' }}>{stat.value}</p>
              <p className="text-[#e3bfb3] text-xs uppercase tracking-widest mt-1" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>{stat.label}</p>
            </div>
          ))}
        </div>

        {loading ? (
          <div className="flex items-center justify-center py-24">
            <span className="material-symbols-outlined text-4xl text-[#ff9069] animate-spin">progress_activity</span>
          </div>
        ) : fetchError ? (
          <div className="text-center py-24 text-[#ffb4ab]" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>{fetchError}</div>
        ) : (
          <div className="flex flex-col gap-8">
            {/* Web users */}
            {webUsers.length > 0 && (
              <section>
                <h2 className="text-xs uppercase tracking-widest text-[#e3bfb3] mb-4" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
                  Rockflix Accounts
                </h2>
                <div className="flex flex-col gap-3">
                  {webUsers.map(u => (
                    <UserCard
                      key={u.id}
                      u={u}
                      actionLoading={actionLoading}
                      onToggleActive={toggleActive}
                      onToggleAdmin={toggleAdmin}
                      onViewRequests={viewRequests}
                    />
                  ))}
                </div>
              </section>
            )}

            {/* Telegram-only users */}
            {telegramOnly.length > 0 && (
              <section>
                <h2 className="text-xs uppercase tracking-widest text-[#e3bfb3] mb-4" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
                  Telegram Only
                </h2>
                <div className="flex flex-col gap-3">
                  {telegramOnly.map(u => (
                    <UserCard
                      key={u.telegramChatId}
                      u={u}
                      actionLoading={actionLoading}
                      onToggleActive={toggleActive}
                      onToggleAdmin={null}
                      onViewRequests={null}
                    />
                  ))}
                </div>
              </section>
            )}
          </div>
        )}
      </div>
    </div>

    {/* Requests modal */}
    {requestsModal && (
      <div
        className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/70 backdrop-blur-sm"
        onClick={e => { if (e.target === e.currentTarget) setRequestsModal(null) }}
      >
        <div className="bg-[#1e100b] border border-white/10 rounded-2xl w-full max-w-lg shadow-2xl max-h-[80vh] flex flex-col">
          <div className="flex items-center justify-between px-6 py-5 border-b border-white/5">
            <h2 className="text-lg font-black uppercase tracking-tighter text-[#f9dcd4]" style={{ fontFamily: 'Epilogue, sans-serif' }}>
              {requestsModal.username}'s Requests
            </h2>
            <button onClick={() => setRequestsModal(null)} className="text-[#e3bfb3] hover:text-[#ff9069] transition-colors">
              <span className="material-symbols-outlined">close</span>
            </button>
          </div>
          <div className="overflow-y-auto flex-1 px-6 py-4 flex flex-col gap-3">
            {requestsModal.requests.length === 0 ? (
              <p className="text-[#e3bfb3] text-sm text-center py-8" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>No requests yet.</p>
            ) : requestsModal.requests.map(r => (
              <div key={r.id} className="flex items-start gap-3 bg-[#2b1c17] rounded-xl px-4 py-3">
                <span className={`material-symbols-outlined text-sm mt-0.5 flex-shrink-0 ${
                  r.status === 'completed' ? 'text-green-400' : r.status === 'failed' ? 'text-red-400' : 'text-[#ff9069]'
                }`} style={{ fontVariationSettings: "'FILL' 1, 'wght' 400, 'GRAD' 0, 'opsz' 24" }}>
                  {r.status === 'completed' ? 'check_circle' : r.status === 'failed' ? 'cancel' : 'downloading'}
                </span>
                <div className="flex-1 min-w-0">
                  <p className="text-sm text-[#f9dcd4] truncate" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
                    {r.resolvedTitle ?? r.requestText}
                    {r.resolvedTitle && r.requestText !== r.resolvedTitle && (
                      <span className="text-[#5b4138] ml-2 text-xs">"{r.requestText}"</span>
                    )}
                  </p>
                  <div className="flex items-center gap-3 mt-0.5">
                    {r.mediaType && <span className="text-[10px] uppercase tracking-widest text-[#e3bfb3]" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>{r.mediaType}</span>}
                    <span className="text-[10px] text-[#5b4138]" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>{new Date(r.requestedAt).toLocaleDateString()}</span>
                    {r.fileSizeBytes > 0 && <span className="text-[10px] text-[#5b4138]" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>{formatBytes(r.fileSizeBytes)}</span>}
                  </div>
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>
    )}
  )
}

function UserCard({ u, actionLoading, onToggleActive, onToggleAdmin, onViewRequests }) {
  const usedPct = u.limitBytes ? Math.min((u.totalSizeBytes / u.limitBytes) * 100, 100) : 0
  const isOverLimit = usedPct > 80

  return (
    <div className={`rounded-2xl border p-6 ${u.isActive ? 'border-white/5 bg-[#2b1c17]' : 'border-red-900/30 bg-[#231010]'}`}>
      <div className="flex items-start gap-4">
        {/* Avatar */}
        <div className={`w-10 h-10 rounded-full flex items-center justify-center flex-shrink-0 font-bold text-sm ${
          u.isAdmin ? 'bg-[#ff9069]/30 text-[#ff9069]' : 'bg-white/10 text-[#e3bfb3]'
        }`} style={{ fontFamily: 'Epilogue, sans-serif' }}>
          {(u.username?.[0] ?? '?').toUpperCase()}
        </div>

        {/* Info */}
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2 flex-wrap mb-1">
            <span className="font-bold" style={{ fontFamily: 'Epilogue, sans-serif' }}>{u.username}</span>
            {/* Account type badge */}
            <span className={`text-[10px] px-2 py-0.5 rounded-full uppercase tracking-widest font-bold ${
              u.isAdmin ? 'bg-[#ff9069]/20 text-[#ff9069]' : 'bg-white/10 text-[#e3bfb3]'
            }`} style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
              {u.isAdmin ? 'Admin' : 'User'}
            </span>
            {/* Status badge */}
            <span className={`text-[10px] px-2 py-0.5 rounded-full uppercase tracking-widest font-bold ${
              u.isActive ? 'bg-green-900/40 text-green-400' : 'bg-red-900/40 text-red-400'
            }`} style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
              {u.isActive ? 'Active' : 'Revoked'}
            </span>
            {u.isTelegram && (
              <span className="text-[10px] px-2 py-0.5 rounded-full uppercase tracking-widest font-bold bg-blue-900/30 text-blue-400" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
                Telegram
              </span>
            )}
          </div>
          {u.email && (
            <p className="text-[#e3bfb3] text-xs mb-3" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>{u.email}</p>
          )}

          {/* Storage bar — only for non-admins */}
          {!u.isAdmin && u.limitBytes && (
            <div className="mb-1">
              <div className="flex justify-between items-center mb-1">
                <span className="text-[10px] uppercase tracking-widest text-[#e3bfb3]" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
                  Storage
                </span>
                <span className={`text-[10px] font-bold tabular-nums ${isOverLimit ? 'text-red-400' : 'text-[#e3bfb3]'}`} style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
                  {formatBytes(u.totalSizeBytes)} / {formatBytes(u.limitBytes)}
                </span>
              </div>
              <div className="w-full h-1.5 bg-white/10 rounded-full overflow-hidden">
                <div
                  className="h-full rounded-full transition-all duration-500"
                  style={{
                    width: `${usedPct}%`,
                    background: isOverLimit ? '#ff4444' : 'linear-gradient(90deg, #ff9069, #ff7948)'
                  }}
                />
              </div>
            </div>
          )}

          <p className="text-[10px] text-[#5b4138] mt-2" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
            {u.requestCount} request{u.requestCount !== 1 ? 's' : ''} · Joined {new Date(u.createdAt).toLocaleDateString()}
          </p>
        </div>

        {/* Actions */}
        <div className="flex flex-col gap-2 flex-shrink-0">
          <button
            onClick={() => onToggleActive(u)}
            disabled={actionLoading === (u.id ?? u.telegramChatId)}
            className={`px-3 py-1.5 rounded-full text-xs font-bold transition-all active:scale-95 disabled:opacity-40 ${
              u.isActive ? 'bg-red-900/40 text-red-400 hover:bg-red-900/60' : 'bg-green-900/40 text-green-400 hover:bg-green-900/60'
            }`}
            style={{ fontFamily: 'Space Grotesk, sans-serif' }}
          >
            {actionLoading === (u.id ?? u.telegramChatId) ? '…' : u.isActive ? 'Revoke' : 'Restore'}
          </button>
          {onToggleAdmin && u.id && (
            <button
              onClick={() => onToggleAdmin(u)}
              disabled={actionLoading === `admin-${u.id}`}
              className="px-3 py-1.5 rounded-full text-xs font-bold bg-white/5 text-[#e3bfb3] hover:bg-white/10 transition-all active:scale-95 disabled:opacity-40"
              style={{ fontFamily: 'Space Grotesk, sans-serif' }}
            >
              {actionLoading === `admin-${u.id}` ? '…' : u.isAdmin ? 'Demote' : 'Make Admin'}
            </button>
          )}
          {onViewRequests && u.requestCount > 0 && (
            <button
              onClick={() => onViewRequests(u)}
              className="px-3 py-1.5 rounded-full text-xs font-bold bg-white/5 text-[#e3bfb3] hover:bg-white/10 transition-all active:scale-95"
              style={{ fontFamily: 'Space Grotesk, sans-serif' }}
            >
              {u.requestCount} Request{u.requestCount !== 1 ? 's' : ''}
            </button>
          )}
        </div>
      </div>
    </div>
  )
}
