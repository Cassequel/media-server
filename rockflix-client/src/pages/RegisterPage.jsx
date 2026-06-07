import { useState } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import { useAuth } from '../context/AuthContext'

export default function RegisterPage() {
  const { register } = useAuth()
  const navigate = useNavigate()
  const [form, setForm] = useState({ username: '', email: '', password: '' })
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  async function handleSubmit(e) {
    e.preventDefault()
    setError('')
    setLoading(true)
    try {
      await register(form.username, form.email, form.password)
      navigate('/')
    } catch (err) {
      setError(err.response?.data?.message ?? 'Registration failed.')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="min-h-screen bg-[#1e100b] text-[#f9dcd4] flex overflow-hidden">
      {/* Left — cinematic panel */}
      <div className="hidden lg:flex relative w-3/5 h-screen overflow-hidden">
        <div className="absolute inset-0 bg-gradient-to-br from-[#0e0e0e] via-[#1e100b] to-[#2b1c17]" />
        <div className="absolute inset-0" style={{ background: 'linear-gradient(to right, transparent 0%, #1e100b 100%)' }} />
        <div className="relative z-10 flex flex-col justify-end p-12 w-full h-full">
          <span
            className="text-[#ffb59c] text-xs uppercase tracking-[0.4em] mb-2"
            style={{ fontFamily: 'Space Grotesk, sans-serif' }}
          >
            Your Private Theater
          </span>
          <h1
            className="text-8xl font-black tracking-tighter leading-none mb-3"
            style={{ fontFamily: 'Epilogue, sans-serif' }}
          >
            Rockflix
          </h1>
          <p className="text-[#e3bfb3] max-w-md text-lg leading-snug">
            Your personal media server. Stream your collection anywhere, anytime.
          </p>
          <div className="mt-6 flex gap-4 items-center">
            <div className="h-px w-12 bg-[#5b4138]" />
            <span
              className="text-[10px] text-[#aa897f] tracking-widest uppercase"
              style={{ fontFamily: 'Space Grotesk, sans-serif' }}
            >
              Personal Collection
            </span>
          </div>
        </div>
      </div>

      {/* Right — form */}
      <div className="w-full lg:w-2/5 h-screen bg-[#271813] flex items-center justify-center p-8 md:p-16 relative overflow-hidden">
        {/* Mobile branding */}
        <div className="absolute top-12 left-12 lg:hidden">
          <span className="text-2xl font-bold tracking-tighter text-[#ff9069]" style={{ fontFamily: 'Epilogue, sans-serif' }}>
            Rockflix
          </span>
        </div>

        <div className="w-full max-w-sm">
          <header className="mb-8">
            <h2
              className="text-4xl font-bold tracking-tight mb-2"
              style={{ fontFamily: 'Epilogue, sans-serif' }}
            >
              Join Rockflix
            </h2>
            <p className="text-[#e3bfb3]">Create your account to start watching.</p>
          </header>

          <form onSubmit={handleSubmit} className="space-y-5">
            {error && (
              <p className="text-[#ffb4ab] text-sm">{error}</p>
            )}

            {/* Username */}
            <div>
              <label
                className="block text-[10px] uppercase tracking-widest text-[#e3bfb3] mb-2"
                style={{ fontFamily: 'Space Grotesk, sans-serif' }}
              >
                Username
              </label>
              <div className="relative flex items-center">
                <span className="material-symbols-outlined absolute left-0 text-[#e3bfb3] text-xl">badge</span>
                <input
                  type="text"
                  value={form.username}
                  onChange={e => setForm(f => ({ ...f, username: e.target.value }))}
                  placeholder="your_username"
                  required
                  className="w-full bg-transparent border-0 border-b border-[#5b4138] pb-3 pl-9 text-[#f9dcd4] focus:ring-0 focus:outline-none focus:border-[#ffb59c] transition-colors placeholder:text-[#42312b]"
                />
              </div>
            </div>

            {/* Email */}
            <div>
              <label
                className="block text-[10px] uppercase tracking-widest text-[#e3bfb3] mb-2"
                style={{ fontFamily: 'Space Grotesk, sans-serif' }}
              >
                Email
              </label>
              <div className="relative flex items-center">
                <span className="material-symbols-outlined absolute left-0 text-[#e3bfb3] text-xl">person</span>
                <input
                  type="email"
                  value={form.email}
                  onChange={e => setForm(f => ({ ...f, email: e.target.value }))}
                  placeholder="your@email.com"
                  required
                  className="w-full bg-transparent border-0 border-b border-[#5b4138] pb-3 pl-9 text-[#f9dcd4] focus:ring-0 focus:outline-none focus:border-[#ffb59c] transition-colors placeholder:text-[#42312b]"
                />
              </div>
            </div>

            {/* Password */}
            <div>
              <label
                className="block text-[10px] uppercase tracking-widest text-[#e3bfb3] mb-2"
                style={{ fontFamily: 'Space Grotesk, sans-serif' }}
              >
                Password
              </label>
              <div className="relative flex items-center">
                <span className="material-symbols-outlined absolute left-0 text-[#e3bfb3] text-xl">lock</span>
                <input
                  type="password"
                  value={form.password}
                  onChange={e => setForm(f => ({ ...f, password: e.target.value }))}
                  placeholder="Min. 6 characters"
                  required
                  minLength={6}
                  className="w-full bg-transparent border-0 border-b border-[#5b4138] pb-3 pl-9 text-[#f9dcd4] focus:ring-0 focus:outline-none focus:border-[#ffb59c] transition-colors placeholder:text-[#42312b]"
                />
              </div>
            </div>

            <div className="pt-2">
              <button
                type="submit"
                disabled={loading}
                className="w-full py-4 rounded-xl font-bold text-[#5c1900] shadow-lg hover:brightness-110 active:scale-[0.98] transition-all disabled:opacity-50"
                style={{ fontFamily: 'Epilogue, sans-serif', background: 'linear-gradient(135deg, #ff9069 0%, #ff7948 100%)' }}
              >
                {loading ? 'Creating account…' : 'Create Account'}
              </button>
            </div>

            <div className="pt-1 text-center">
              <p className="text-[#e3bfb3] text-sm">
                Already have an account?{' '}
                <Link to="/login" className="text-[#ffb59c] font-bold ml-1 hover:underline underline-offset-4">
                  Sign In
                </Link>
              </p>
            </div>
          </form>
        </div>

        {/* Ornament */}
        <div className="absolute bottom-0 right-0 p-8 pointer-events-none select-none">
          <span className="material-symbols-outlined text-[#42312b]/20 font-light" style={{ fontSize: '120px' }}>movie</span>
        </div>
      </div>
    </div>
  )
}
