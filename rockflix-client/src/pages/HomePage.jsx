import { useEffect, useState, useRef } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../context/AuthContext'
import api from '../services/api'

export default function HomePage() {
  const { user, logout } = useAuth()
  const navigate = useNavigate()
  const [hero, setHero] = useState(null)
  const [heroList, setHeroList] = useState([])
  const [heroIndex, setHeroIndex] = useState(0)
  const [continueWatching, setContinueWatching] = useState([])
  const [movies, setMovies] = useState([])
  const [tvShows, setTvShows] = useState([])
  const [scanning, setScanning] = useState(false)
  const intervalRef = useRef(null)

  useEffect(() => {
    api.get('/movies').then(({ data }) => {
      setMovies(data)
      if (data.length === 0) return
      const shuffled = [...data].sort(() => Math.random() - 0.5).slice(0, 4)
      setHeroList(shuffled)
      setHero(shuffled[0] ?? null)
    }).catch(() => {})
    api.get('/tvshows').then(({ data }) => {
      setTvShows(data)
      setHeroList(prev => {
        if (prev.length > 0) return prev
        const shuffled = [...data].sort(() => Math.random() - 0.5).slice(0, 4)
        setHero(shuffled[0] ?? null)
        return shuffled
      })
    }).catch(() => {})
    api.get('/watchhistory/continue-watching').then(({ data }) => setContinueWatching(data)).catch(() => {})
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
      {movies.length > 0 && (
        <MediaRow title="Movies">
          {movies.map(movie => (
            <PosterCard key={movie.id} item={movie} onClick={() => navigate(`/movie/${movie.id}`)} />
          ))}
        </MediaRow>
      )}

      {/* TV Shows */}
      {tvShows.length > 0 && (
        <MediaRow title="TV Shows">
          {tvShows.map(show => (
            <PosterCard key={show.id} item={show} onClick={() => navigate(`/tvshow/${show.id}`)} />
          ))}
        </MediaRow>
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
      <div className="flex gap-5 overflow-x-auto pb-6" style={{ scrollbarWidth: 'thin' }}>
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
