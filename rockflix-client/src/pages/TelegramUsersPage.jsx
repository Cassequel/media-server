import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../context/AuthContext'
import api from '../services/api'

export default function TelegramUsersPage() {
  const { user } = useAuth()
  const navigate = useNavigate()
  const [users, setUsers] = useState([])
  const [loading, setLoading] = useState(true)
  const [expandedChat, setExpandedChat] = useState(null)
  const [actionLoading, setActionLoading] = useState(null)

  useEffect(() => {
    if (!user?.isAdmin) { navigate('/'); return }
    fetchUsers()
  }, [])

  async function fetchUsers() {
    try {
      const { data } = await api.get('/admin/telegram-users')
      setUsers(data)
    } catch {
      // handle error
    } finally {
      setLoading(false)
    }
  }

  async function toggleAccess(chatId, currentlyActive) {
    setActionLoading(chatId)
    try {
      const action = currentlyActive ? 'revoke' : 'restore'
      await api.patch(`/admin/telegram-users/${chatId}/${action}`)
      setUsers(prev => prev.map(u =>
        u.chatId === chatId ? { ...u, isActive: !currentlyActive } : u
      ))
    } catch {
      // handle error
    } finally {
      setActionLoading(null)
    }
  }

  const totalRequests = users.reduce((sum, u) => sum + u.requests.length, 0)
  const activeUsers = users.filter(u => u.isActive).length

  return (
    <div className="min-h-screen bg-[#1e100b] text-[#f9dcd4] pb-16">
      {/* Header */}
      <header className="fixed top-0 left-0 right-0 z-50 flex justify-between items-center px-8 h-16 bg-[#0e0e0e]/80 backdrop-blur-xl">
        <div className="flex items-center gap-6">
          <button
            onClick={() => navigate('/')}
            className="text-2xl font-black tracking-tighter text-[#ff9069]"
            style={{ fontFamily: 'Epilogue, sans-serif' }}
          >
            Rockflix
          </button>
          <span className="text-[#e3bfb3] text-sm hidden md:block" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
            / Telegram Users
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

      <div className="pt-24 px-8 max-w-5xl mx-auto">
        {/* Page title */}
        <div className="mb-10">
          <h1
            className="text-4xl md:text-5xl font-black tracking-tighter uppercase mb-2"
            style={{ fontFamily: 'Epilogue, sans-serif' }}
          >
            Telegram Users
          </h1>
          <p className="text-[#e3bfb3] text-sm" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
            Manage who can request media via the Telegram bot
          </p>
        </div>

        {/* Stats */}
        <div className="grid grid-cols-3 gap-4 mb-10">
          {[
            { label: 'Total Users', value: users.length },
            { label: 'Active', value: activeUsers },
            { label: 'Total Requests', value: totalRequests },
          ].map(stat => (
            <div key={stat.label} className="bg-[#2b1c17] rounded-2xl p-5">
              <p
                className="text-3xl font-black text-[#ff9069]"
                style={{ fontFamily: 'Epilogue, sans-serif' }}
              >
                {stat.value}
              </p>
              <p
                className="text-[#e3bfb3] text-xs uppercase tracking-widest mt-1"
                style={{ fontFamily: 'Space Grotesk, sans-serif' }}
              >
                {stat.label}
              </p>
            </div>
          ))}
        </div>

        {/* User list */}
        {loading ? (
          <div className="flex items-center justify-center py-24">
            <span className="material-symbols-outlined text-4xl text-[#ff9069] animate-spin">progress_activity</span>
          </div>
        ) : users.length === 0 ? (
          <div className="text-center py-24 text-[#e3bfb3]" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
            No authorized users yet. Share your invite link to get started.
          </div>
        ) : (
          <div className="flex flex-col gap-4">
            {users.map(u => (
              <div
                key={u.chatId}
                className={`rounded-2xl overflow-hidden border transition-colors ${
                  u.isActive ? 'border-white/5 bg-[#2b1c17]' : 'border-red-900/30 bg-[#231010]'
                }`}
              >
                {/* User row */}
                <div className="flex items-center gap-4 px-6 py-4">
                  {/* Avatar */}
                  <div className={`w-10 h-10 rounded-full flex items-center justify-center flex-shrink-0 font-bold text-sm ${
                    u.isActive ? 'bg-[#ff9069]/20 text-[#ff9069]' : 'bg-red-900/30 text-red-400'
                  }`} style={{ fontFamily: 'Epilogue, sans-serif' }}>
                    {(u.displayName?.[0] ?? '?').toUpperCase()}
                  </div>

                  {/* Info */}
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2 flex-wrap">
                      <span className="font-bold truncate" style={{ fontFamily: 'Epilogue, sans-serif' }}>
                        {u.displayName ?? 'Unknown'}
                      </span>
                      <span
                        className={`text-[10px] px-2 py-0.5 rounded-full uppercase tracking-widest font-bold ${
                          u.isActive
                            ? 'bg-green-900/40 text-green-400'
                            : 'bg-red-900/40 text-red-400'
                        }`}
                        style={{ fontFamily: 'Space Grotesk, sans-serif' }}
                      >
                        {u.isActive ? 'Active' : 'Revoked'}
                      </span>
                    </div>
                    <p className="text-[#e3bfb3] text-xs mt-0.5" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
                      ID: {u.chatId} · Joined {new Date(u.authorizedAt).toLocaleDateString()} · {u.requests.length} request{u.requests.length !== 1 ? 's' : ''}
                    </p>
                  </div>

                  {/* Actions */}
                  <div className="flex items-center gap-3 flex-shrink-0">
                    {u.requests.length > 0 && (
                      <button
                        onClick={() => setExpandedChat(expandedChat === u.chatId ? null : u.chatId)}
                        className="text-[#e3bfb3] hover:text-[#ff9069] transition-colors text-xs flex items-center gap-1"
                        style={{ fontFamily: 'Space Grotesk, sans-serif' }}
                      >
                        <span className="material-symbols-outlined text-base">
                          {expandedChat === u.chatId ? 'expand_less' : 'expand_more'}
                        </span>
                        History
                      </button>
                    )}
                    <button
                      onClick={() => toggleAccess(u.chatId, u.isActive)}
                      disabled={actionLoading === u.chatId}
                      className={`px-4 py-2 rounded-full text-xs font-bold transition-all active:scale-95 disabled:opacity-40 ${
                        u.isActive
                          ? 'bg-red-900/40 text-red-400 hover:bg-red-900/60'
                          : 'bg-green-900/40 text-green-400 hover:bg-green-900/60'
                      }`}
                      style={{ fontFamily: 'Space Grotesk, sans-serif' }}
                    >
                      {actionLoading === u.chatId ? '…' : u.isActive ? 'Revoke' : 'Restore'}
                    </button>
                  </div>
                </div>

                {/* Request history */}
                {expandedChat === u.chatId && u.requests.length > 0 && (
                  <div className="border-t border-white/5 px-6 py-4">
                    <p
                      className="text-[10px] uppercase tracking-widest text-[#e3bfb3] mb-3"
                      style={{ fontFamily: 'Space Grotesk, sans-serif' }}
                    >
                      Recent Requests
                    </p>
                    <div className="flex flex-col gap-2">
                      {u.requests.map((r, i) => (
                        <div key={i} className="flex items-start gap-3">
                          <span className={`material-symbols-outlined text-sm mt-0.5 flex-shrink-0 ${r.success ? 'text-green-400' : 'text-red-400'}`}
                            style={{ fontVariationSettings: "'FILL' 1, 'wght' 400, 'GRAD' 0, 'opsz' 24" }}>
                            {r.success ? 'check_circle' : 'cancel'}
                          </span>
                          <div className="flex-1 min-w-0">
                            <p className="text-sm text-[#f9dcd4] truncate" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
                              "{r.requestText}"
                              {r.resolvedTitle && (
                                <span className="text-[#ff9069] ml-2">→ {r.resolvedTitle}</span>
                              )}
                            </p>
                            <p className="text-[10px] text-[#e3bfb3] mt-0.5" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
                              {r.mediaType && <span className="uppercase mr-2">{r.mediaType}</span>}
                              {new Date(r.requestedAt).toLocaleString()}
                            </p>
                          </div>
                        </div>
                      ))}
                    </div>
                  </div>
                )}
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
