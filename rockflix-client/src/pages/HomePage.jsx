import { useEffect, useState, useRef } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../context/AuthContext'
import api from '../services/api'

const STUB_MOVIES = [
  { id: 's1', title: 'Neon City', year: 2023, genre: 'Sci-Fi', tmdbRating: 8.4, posterPath: 'https://picsum.photos/seed/neoncity/300/450', backdropPath: 'https://picsum.photos/seed/neoncity/1280/720', description: 'A rogue detective hunts a killer through a rain-soaked megacity where technology and humanity have blurred beyond recognition.' },
  { id: 's2', title: 'The Last Signal', year: 2022, genre: 'Thriller', tmdbRating: 7.9, posterPath: 'https://picsum.photos/seed/lastsignal/300/450', backdropPath: 'https://picsum.photos/seed/lastsignal/1280/720', description: 'After a deep-space probe goes silent, one woman risks everything to find out what it found.' },
  { id: 's3', title: 'Iron Meridian', year: 2024, genre: 'Action', tmdbRating: 7.5, posterPath: 'https://picsum.photos/seed/ironmeridian/300/450', backdropPath: 'https://picsum.photos/seed/ironmeridian/1280/720', description: 'A retired soldier is pulled back into the field when a global conspiracy targets the people he loves.' },
  { id: 's4', title: 'Coldwater', year: 2021, genre: 'Drama', tmdbRating: 8.1, posterPath: 'https://picsum.photos/seed/coldwater/300/450', backdropPath: 'https://picsum.photos/seed/coldwater/1280/720', description: 'Two estranged brothers must face the wilderness and each other after their father goes missing.' },
  { id: 's5', title: 'Parallax', year: 2023, genre: 'Mystery', tmdbRating: 7.7, posterPath: 'https://picsum.photos/seed/parallaxfilm/300/450', backdropPath: 'https://picsum.photos/seed/parallaxfilm/1280/720', description: 'A physicist wakes with no memory to find her research has been weaponized.' },
  { id: 's6', title: 'Ember', year: 2022, genre: 'Drama', tmdbRating: 8.6, posterPath: 'https://picsum.photos/seed/emberfilm/300/450', backdropPath: 'https://picsum.photos/seed/emberfilm/1280/720', description: 'Set against a dying coal town, a family fights to keep their legacy alive.' },
]

const STUB_TV = [
  { id: 't1', title: 'Outpost Nine', year: 2023, genre: 'Sci-Fi', tmdbRating: 8.7, posterPath: 'https://picsum.photos/seed/outpostnine/300/450', backdropPath: 'https://picsum.photos/seed/outpostnine/1280/720', description: 'The crew of a remote space station uncovers a signal that rewrites the history of human civilization.' },
  { id: 't2', title: 'Hollow Crown', year: 2022, genre: 'Drama', tmdbRating: 8.9, posterPath: 'https://picsum.photos/seed/hollowcrown/300/450', backdropPath: 'https://picsum.photos/seed/hollowcrown/1280/720', description: 'Political intrigue tears apart a fictional European monarchy across four sweeping seasons.' },
  { id: 't3', title: 'Static', year: 2024, genre: 'Horror', tmdbRating: 7.8, posterPath: 'https://picsum.photos/seed/staticshow/300/450', backdropPath: 'https://picsum.photos/seed/staticshow/1280/720', description: 'A journalist starts receiving transmissions from people missing for decades.' },
  { id: 't4', title: 'Meridian Falls', year: 2021, genre: 'Crime', tmdbRating: 8.2, posterPath: 'https://picsum.photos/seed/meridianfalls/300/450', backdropPath: 'https://picsum.photos/seed/meridianfalls/1280/720', description: 'A detective with a fractured past investigates murders that mirror her own forgotten childhood.' },
  { id: 't5', title: 'Groundwork', year: 2023, genre: 'Thriller', tmdbRating: 7.6, posterPath: 'https://picsum.photos/seed/groundwork/300/450', backdropPath: 'https://picsum.photos/seed/groundwork/1280/720', description: 'An architect discovers her firm has been secretly building something for a shadowy client.' },
]

export default function HomePage() {
  const { user, logout } = useAuth()
  const navigate = useNavigate()
  const [hero, setHero] = useState(STUB_MOVIES[0])
  const [heroList, setHeroList] = useState(STUB_MOVIES.slice(0, 4))
  const [heroIndex, setHeroIndex] = useState(0)
  const [continueWatching, setContinueWatching] = useState([])
  const [movies, setMovies] = useState(STUB_MOVIES)
  const [tvShows, setTvShows] = useState(STUB_TV)
  const [scanning, setScanning] = useState(false)
  const [requestModal, setRequestModal] = useState(null) // 'movie' | 'tv' | null
  const [pendingRequests, setPendingRequests] = useState([])
  const intervalRef = useRef(null)

  useEffect(() => {
    api.get('/movies').then(({ data }) => {
      if (data.length === 0) return
      setMovies(data)
      const shuffled = [...data].sort(() => Math.random() - 0.5).slice(0, 4)
      setHeroList(shuffled)
      setHero(shuffled[0])
    }).catch(() => {})
    api.get('/tvshows').then(({ data }) => {
      if (data.length === 0) return
      setTvShows(data)
    }).catch(() => {})
    api.get('/watchhistory/continue-watching').then(({ data }) => setContinueWatching(data)).catch(() => {})
    api.get('/request/pending').then(({ data }) => setPendingRequests(data)).catch(() => {})
  }, [])

  async function scanMedia() {
    setScanning(true)
    try {
      await api.post('/admin/scan')
      window.location.reload()
    } catch {
      setScanning(false)
    }
  }

  useEffect(() => {
    if (heroList.length < 2) return
    intervalRef.current = setInterval(() => {
      setHeroIndex(i => {
        const next = (i + 1) % heroList.length
        setHero(heroList[next])
        return next
      })
    }, 8000)
    return () => clearInterval(intervalRef.current)
  }, [heroList])

  function goToHero(i) {
    clearInterval(intervalRef.current)
    setHeroIndex(i)
    setHero(heroList[i])
  }

  return (
    <div className="min-h-screen bg-[#1e100b] text-[#f9dcd4] pb-24 md:pb-0">
      {/* Header */}
      <header className="fixed top-0 left-0 right-0 z-50 flex justify-between items-center px-8 h-16 bg-[#0e0e0e]/80 backdrop-blur-xl">
        <div className="flex items-center gap-8">
          <span className="text-2xl font-bold tracking-tighter text-[#ff9069]" style={{ fontFamily: 'Epilogue, sans-serif' }}>
            Rockflix
          </span>
          <nav className="hidden md:flex gap-6 items-center">
            <button
              onClick={() => navigate('/favorites')}
              className="text-[#e3bfb3] hover:text-[#ff9069] transition-colors duration-300 font-medium"
            >
              Favorites
            </button>
            {user?.isAdmin && (
              <>
                <button
                  onClick={scanMedia}
                  disabled={scanning}
                  className="text-[#e3bfb3] hover:text-[#ff9069] transition-colors duration-300 font-medium disabled:opacity-40"
                >
                  {scanning ? 'Scanning…' : 'Scan Media'}
                </button>
                <button
                  onClick={() => navigate('/admin/telegram-users')}
                  className="text-[#e3bfb3] hover:text-[#ff9069] transition-colors duration-300 font-medium"
                >
                  Telegram Users
                </button>
              </>
            )}
          </nav>
        </div>
        <div className="flex items-center gap-4">
          <span className="text-[#e3bfb3] text-sm hidden md:block" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
            {user?.username}
          </span>
          <button
            onClick={logout}
            className="text-[#e3bfb3] hover:text-[#ff9069] transition-colors text-sm"
            style={{ fontFamily: 'Space Grotesk, sans-serif' }}
          >
            Sign out
          </button>
        </div>
      </header>

      {/* Hero */}
      {hero && (
        <section className="relative h-[80vh] min-h-[500px] w-full overflow-hidden">
          <div className="absolute inset-0 z-0">
            <img
              src={hero.backdropPath ?? hero.posterPath}
              alt={hero.title}
              className="w-full h-full object-cover opacity-60 transition-opacity duration-1000"
            />
            <div className="absolute inset-0" style={{ background: 'linear-gradient(to bottom, transparent 20%, #1e100b 100%)' }} />
            <div className="absolute inset-0 hidden md:block" style={{ background: 'linear-gradient(to right, #1e100b 0%, transparent 60%)' }} />
          </div>

          <div className="relative z-10 h-full max-w-[1400px] mx-auto px-8 md:px-16 flex flex-col md:flex-row items-end md:items-center gap-8 md:gap-16 pb-16 md:pb-0">
            {/* Poster */}
            {hero.posterPath && (
              <div className="hidden md:block w-56 lg:w-72 aspect-[2/3] flex-shrink-0 rounded-xl overflow-hidden shadow-2xl transform hover:scale-[1.02] transition-transform duration-500">
                <img src={hero.posterPath} alt={hero.title} className="w-full h-full object-cover" />
              </div>
            )}

            {/* Info */}
            <div className="flex-1 text-left">
              <div className="flex flex-wrap items-center gap-3 mb-4">
                {hero.genre && (
                  <span
                    className="bg-[#ffb59c]/20 text-[#ffb59c] px-3 py-1 rounded-full text-xs font-bold uppercase tracking-widest"
                    style={{ fontFamily: 'Space Grotesk, sans-serif' }}
                  >
                    {hero.genre}
                  </span>
                )}
                {hero.year && (
                  <span className="text-[#e3bfb3] text-sm" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
                    {hero.year}
                  </span>
                )}
                {hero.tmdbRating && (
                  <span className="flex items-center gap-1 text-[#ffb59c]">
                    <span className="material-symbols-outlined text-sm" style={{ fontVariationSettings: "'FILL' 1, 'wght' 400, 'GRAD' 0, 'opsz' 24" }}>star</span>
                    <span className="text-sm font-bold" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>{hero.tmdbRating.toFixed(1)}</span>
                  </span>
                )}
              </div>

              <h1
                className="text-5xl md:text-7xl font-black tracking-tighter mb-4 uppercase leading-none"
                style={{ fontFamily: 'Epilogue, sans-serif' }}
              >
                {hero.title}
              </h1>

              {hero.description && (
                <p className="text-[#e3bfb3] leading-relaxed line-clamp-3 mb-8 max-w-lg">{hero.description}</p>
              )}

              <div className="flex flex-wrap gap-4">
                <button
                  onClick={() => navigate(`/watch/movie/${hero.id}`)}
                  className="px-10 py-4 rounded-full font-bold flex items-center gap-3 shadow-lg hover:brightness-110 active:scale-95 transition-all text-[#5c1900]"
                  style={{ fontFamily: 'Epilogue, sans-serif', background: 'linear-gradient(135deg, #ff9069 0%, #ff7948 100%)' }}
                >
                  <span className="material-symbols-outlined" style={{ fontVariationSettings: "'FILL' 1, 'wght' 400, 'GRAD' 0, 'opsz' 24" }}>play_arrow</span>
                  Play Now
                </button>
                <button
                  onClick={() => navigate(`/movie/${hero.id}`)}
                  className="bg-[#372621] text-[#f9dcd4] px-8 py-4 rounded-full font-bold flex items-center gap-3 hover:bg-[#47352f] transition-colors active:scale-95"
                  style={{ fontFamily: 'Epilogue, sans-serif' }}
                >
                  <span className="material-symbols-outlined">info</span>
                  More Info
                </button>
              </div>
            </div>
          </div>

          {/* Hero dots */}
          {heroList.length > 1 && (
            <div className="absolute bottom-6 left-1/2 -translate-x-1/2 flex gap-2 z-10">
              {heroList.map((_, i) => (
                <button
                  key={i}
                  onClick={() => goToHero(i)}
                  className={`h-1.5 rounded-full transition-all duration-300 ${i === heroIndex ? 'bg-[#ffb59c] w-8' : 'bg-white/30 w-2'}`}
                />
              ))}
            </div>
          )}
        </section>
      )}

      {/* Continue Watching */}
      {continueWatching.length > 0 && (
        <MediaRow title="Continue Watching">
          {continueWatching.map((item, i) => (
            <ContinueCard key={i} item={item} navigate={navigate} />
          ))}
        </MediaRow>
      )}

      {/* Movies */}
      <MediaRow title="Movies">
        <RequestCard onClick={() => setRequestModal('movie')} />
        {pendingRequests.filter(r => r.mediaType === 'movie').map(r => (
          <DownloadingCard key={r.id} title={r.resolvedTitle} />
        ))}
        {movies.map(movie => (
          <PosterCard key={movie.id} item={movie} onClick={() => navigate(`/movie/${movie.id}`)} />
        ))}
      </MediaRow>

      {/* TV Shows */}
      <MediaRow title="TV Shows">
        <RequestCard onClick={() => setRequestModal('tv')} />
        {pendingRequests.filter(r => r.mediaType === 'tv').map(r => (
          <DownloadingCard key={r.id} title={r.resolvedTitle} />
        ))}
        {tvShows.map(show => (
          <PosterCard key={show.id} item={show} onClick={() => navigate(`/tvshow/${show.id}`)} />
        ))}
      </MediaRow>

      {requestModal && (
        <RequestModal type={requestModal} onClose={() => setRequestModal(null)} />
      )}

      {/* Mobile bottom nav */}
      <nav className="md:hidden fixed bottom-0 left-0 right-0 z-50 flex justify-around items-center h-20 bg-[#131313]/80 backdrop-blur-[24px] border-t border-white/5">
        <button className="flex flex-col items-center gap-1 text-[#ff9069]">
          <span className="material-symbols-outlined" style={{ fontVariationSettings: "'FILL' 1, 'wght' 400, 'GRAD' 0, 'opsz' 24" }}>home</span>
          <span className="text-[10px] uppercase tracking-widest" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>Home</span>
        </button>
        <button onClick={() => navigate('/favorites')} className="flex flex-col items-center gap-1 text-[#adaaaa] hover:text-[#ff9069] transition-colors">
          <span className="material-symbols-outlined">favorite</span>
          <span className="text-[10px] uppercase tracking-widest" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>Favorites</span>
        </button>
        {user?.isAdmin && (
          <button onClick={scanMedia} disabled={scanning} className="flex flex-col items-center gap-1 text-[#adaaaa] disabled:opacity-40">
            <span className="material-symbols-outlined">refresh</span>
            <span className="text-[10px] uppercase tracking-widest" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>Scan</span>
          </button>
        )}
        <button onClick={logout} className="flex flex-col items-center gap-1 text-[#adaaaa] hover:text-[#ff9069] transition-colors">
          <span className="material-symbols-outlined">logout</span>
          <span className="text-[10px] uppercase tracking-widest" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>Sign Out</span>
        </button>
      </nav>
    </div>
  )
}

function MediaRow({ title, children }) {
  return (
    <div className="px-8 md:px-12 pt-8 pb-2">
      <h2
        className="text-2xl font-black mb-6 uppercase tracking-tight text-[#f9dcd4]"
        style={{ fontFamily: 'Epilogue, sans-serif' }}
      >
        {title}
      </h2>
      <div className="flex gap-5 overflow-x-auto pb-6" style={{ scrollbarWidth: 'thin', scrollbarColor: '#5b4138 transparent' }}>
        {children}
      </div>
    </div>
  )
}

function PosterCard({ item, onClick }) {
  return (
    <button onClick={onClick} className="flex-shrink-0 w-40 text-left group cursor-pointer">
      <div className="aspect-[2/3] rounded-xl overflow-hidden mb-3 relative bg-[#2b1c17]">
        {item.posterPath && (
          <img
            src={item.posterPath}
            alt={item.title}
            className="w-full h-full object-cover group-hover:scale-110 transition-transform duration-700"
          />
        )}
        <div className="absolute inset-0 bg-black/0 group-hover:bg-black/40 transition-colors flex items-center justify-center">
          <span
            className="material-symbols-outlined text-white opacity-0 group-hover:opacity-100 scale-50 group-hover:scale-100 transition-all duration-300 text-5xl"
            style={{ fontVariationSettings: "'FILL' 1, 'wght' 400, 'GRAD' 0, 'opsz' 48" }}
          >
            play_circle
          </span>
        </div>
        {item.tmdbRating && (
          <div className="absolute bottom-2 left-2">
            <span
              className="bg-black/60 backdrop-blur-md px-2 py-0.5 rounded text-[10px] font-bold text-[#ffb59c]"
              style={{ fontFamily: 'Space Grotesk, sans-serif' }}
            >
              {item.tmdbRating.toFixed(1)}
            </span>
          </div>
        )}
      </div>
      <h3
        className="font-bold text-[#f9dcd4] group-hover:text-[#ffb59c] transition-colors truncate"
        style={{ fontFamily: 'Epilogue, sans-serif' }}
      >
        {item.title}
      </h3>
      <p className="text-[10px] text-[#e3bfb3] uppercase tracking-widest mt-1" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
        {[item.genre, item.year].filter(Boolean).join(' • ')}
      </p>
    </button>
  )
}

function ContinueCard({ item, navigate }) {
  const percent = item.runtimeSeconds ? Math.min((item.progressSeconds / item.runtimeSeconds) * 100, 100) : 0
  const path = item.type === 'movie' ? `/watch/movie/${item.movieId}` : `/watch/episode/${item.episodeId}`

  return (
    <button onClick={() => navigate(path)} className="flex-shrink-0 w-56 text-left group cursor-pointer">
      <div className="relative w-full aspect-video rounded-xl overflow-hidden mb-3 bg-[#2b1c17]">
        {item.backdropPath && (
          <img
            src={item.backdropPath}
            alt={item.title}
            className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-500"
          />
        )}
        <div className="absolute inset-0 bg-black/0 group-hover:bg-black/40 transition-colors flex items-center justify-center">
          <span
            className="material-symbols-outlined text-white opacity-0 group-hover:opacity-100 transition-opacity text-4xl"
            style={{ fontVariationSettings: "'FILL' 1, 'wght' 400, 'GRAD' 0, 'opsz' 48" }}
          >
            play_circle
          </span>
        </div>
        <div className="absolute bottom-0 left-0 right-0 h-1 bg-white/10">
          <div className="h-full bg-[#ffb59c] rounded-full" style={{ width: `${percent}%` }} />
        </div>
      </div>
      <p
        className="text-sm font-semibold truncate text-[#f9dcd4] group-hover:text-[#ffb59c] transition-colors"
        style={{ fontFamily: 'Epilogue, sans-serif' }}
      >
        {item.title}
      </p>
      {item.type === 'episode' && (
        <p className="text-[10px] text-[#e3bfb3] mt-0.5" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
          S{String(item.seasonNumber).padStart(2, '0')}E{String(item.episodeNumber).padStart(2, '0')}
        </p>
      )}
    </button>
  )
}

function DownloadingCard({ title }) {
  return (
    <div className="flex-shrink-0 w-40 text-left opacity-50">
      <div className="aspect-[2/3] rounded-xl overflow-hidden mb-3 relative bg-[#2b1c17] flex flex-col items-center justify-center gap-3">
        <span className="material-symbols-outlined text-4xl text-[#ff9069] animate-spin" style={{ animationDuration: '2s' }}>
          downloading
        </span>
        <span className="text-[10px] uppercase tracking-widest text-[#e3bfb3] text-center px-2" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
          Downloading
        </span>
      </div>
      <h3
        className="font-bold text-[#f9dcd4] truncate text-sm"
        style={{ fontFamily: 'Epilogue, sans-serif' }}
      >
        {title}
      </h3>
    </div>
  )
}

function RequestCard({ onClick }) {
  return (
    <button onClick={onClick} className="flex-shrink-0 w-40 text-left group cursor-pointer">
      <div className="aspect-[2/3] rounded-xl overflow-hidden mb-3 relative bg-[#2b1c17] border-2 border-dashed border-[#5b4138] group-hover:border-[#ff9069] transition-colors flex flex-col items-center justify-center gap-2">
        <span
          className="material-symbols-outlined text-4xl text-[#5b4138] group-hover:text-[#ff9069] transition-colors"
        >
          add
        </span>
      </div>
      <h3
        className="font-bold text-[#5b4138] group-hover:text-[#ff9069] transition-colors truncate text-sm"
        style={{ fontFamily: 'Epilogue, sans-serif' }}
      >
        Request
      </h3>
    </button>
  )
}

function RequestModal({ type, onClose }) {
  const [text, setText] = useState('')
  const [loading, setLoading] = useState(false)
  const [result, setResult] = useState(null)

  async function submit(e) {
    e.preventDefault()
    if (!text.trim()) return
    setLoading(true)
    try {
      const { data } = await api.post('/request', { text: text.trim() })
      setResult({ ok: true, message: data.message })
    } catch (err) {
      setResult({ ok: false, message: err.response?.data?.message ?? 'Something went wrong.' })
    } finally {
      setLoading(false)
    }
  }

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-sm"
      onClick={e => { if (e.target === e.currentTarget) onClose() }}
    >
      <div className="bg-[#1e100b] border border-white/10 rounded-2xl p-8 w-full max-w-md shadow-2xl">
        <div className="flex items-center justify-between mb-6">
          <h2
            className="text-2xl font-black uppercase tracking-tighter text-[#f9dcd4]"
            style={{ fontFamily: 'Epilogue, sans-serif' }}
          >
            Request {type === 'movie' ? 'a Movie' : 'a TV Show'}
          </h2>
          <button onClick={onClose} className="text-[#e3bfb3] hover:text-[#ff9069] transition-colors">
            <span className="material-symbols-outlined">close</span>
          </button>
        </div>

        {result ? (
          <div>
            <p
              className={`text-sm mb-6 leading-relaxed ${result.ok ? 'text-[#f9dcd4]' : 'text-[#ffb4ab]'}`}
              style={{ fontFamily: 'Space Grotesk, sans-serif' }}
            >
              {result.message}
            </p>
            <div className="flex gap-3">
              <button
                onClick={() => { setText(''); setResult(null) }}
                className="flex-1 bg-[#2b1c17] text-[#f9dcd4] px-6 py-3 rounded-full font-bold hover:bg-[#372621] transition-colors text-sm"
                style={{ fontFamily: 'Epilogue, sans-serif' }}
              >
                Request Another
              </button>
              <button
                onClick={onClose}
                className="flex-1 px-6 py-3 rounded-full font-bold text-sm text-[#5c1900] hover:brightness-110 active:scale-95 transition-all"
                style={{ fontFamily: 'Epilogue, sans-serif', background: 'linear-gradient(135deg, #ff9069 0%, #ff7948 100%)' }}
              >
                Done
              </button>
            </div>
          </div>
        ) : (
          <form onSubmit={submit}>
            <input
              autoFocus
              type="text"
              value={text}
              onChange={e => setText(e.target.value)}
              placeholder={type === 'movie' ? 'e.g. Dune Part Two' : 'e.g. latest season of Severance'}
              className="w-full bg-[#2b1c17] border border-white/10 rounded-xl px-4 py-3 text-[#f9dcd4] placeholder-[#5b4138] focus:outline-none focus:border-[#ff9069] transition-colors mb-6 text-sm"
              style={{ fontFamily: 'Space Grotesk, sans-serif' }}
            />
            <button
              type="submit"
              disabled={loading || !text.trim()}
              className="w-full px-6 py-3 rounded-full font-bold text-[#5c1900] hover:brightness-110 active:scale-95 transition-all disabled:opacity-40 text-sm"
              style={{ fontFamily: 'Epilogue, sans-serif', background: 'linear-gradient(135deg, #ff9069 0%, #ff7948 100%)' }}
            >
              {loading ? 'Requesting…' : 'Request'}
            </button>
          </form>
        )}
      </div>
    </div>
  )
}
