import { useEffect, useState, useRef } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../context/AuthContext'
import api from '../services/api'
import MovieFilterConfirmation from '../components/MovieFilterConfirmation'
import MovieFilterJobs from '../components/MovieFilterJobs'


function formatBytes(bytes) {
  if (bytes === 0) return '0 GB'
  const gb = bytes / (1024 * 1024 * 1024)
  return gb >= 1 ? `${gb.toFixed(1)} GB` : `${(bytes / (1024 * 1024)).toFixed(0)} MB`
}

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
  const [requestModal, setRequestModal] = useState(null) // { type, text } | null
  const [requestText, setRequestText] = useState('')
  const [requestType, setRequestType] = useState('movie') // 'movie' | 'tv'
  const [requestError, setRequestError] = useState(null)
  const [activeTab, setActiveTab] = useState('browse') // 'browse' | 'continue'
  const [pendingRequests, setPendingRequests] = useState([])
  const [usage, setUsage] = useState(null)
  const [showHowTo, setShowHowTo] = useState(false)
  const intervalRef = useRef(null)

  useEffect(() => {
    api.get('/movies').then(({ data }) => {
      setMovies(data)
      if (data.length > 0) {
        const shuffled = [...data].sort(() => Math.random() - 0.5).slice(0, 4)
        setHeroList(shuffled)
        setHero(shuffled[0])
      }
    }).catch(() => {})
    api.get('/tvshows').then(({ data }) => {
      setTvShows(data)
      if (data.length > 0 && !hero) {
        const shuffled = [...data].sort(() => Math.random() - 0.5).slice(0, 4)
        setHeroList(shuffled)
        setHero(shuffled[0])
      }
    }).catch(() => {})
    api.get('/watchhistory/continue-watching').then(({ data }) => setContinueWatching(data)).catch(() => {})
    api.get('/request/pending').then(({ data }) => setPendingRequests(data)).catch(() => {})
    api.get('/request/usage').then(({ data }) => setUsage(data)).catch(() => {})
  }, [])

  async function scanMedia() {
    setScanning(true)
    try {
      await api.post('/media/scan')
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

  async function removePendingRequest(id) {
    try {
      await api.delete(`/request/${id}`)
      setPendingRequests(requests => requests.filter(request => request.id !== id))
    } catch {
      setRequestError('Could not remove that download card. Please try again.')
    }
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
            <button
              onClick={scanMedia}
              disabled={scanning}
              className="text-[#e3bfb3] hover:text-[#ff9069] transition-colors duration-300 font-medium disabled:opacity-40"
            >
              {scanning ? 'Scanning…' : 'Scan Media'}
            </button>
            <button
              onClick={() => setShowHowTo(true)}
              className="text-[#e3bfb3] hover:text-[#ff9069] transition-colors duration-300 font-medium"
            >
              How To Use
            </button>
            {user?.isAdmin && (
              <button
                onClick={() => navigate('/admin/telegram-users')}
                className="text-[#e3bfb3] hover:text-[#ff9069] transition-colors duration-300 font-medium"
              >
                Users
              </button>
            )}
          </nav>
        </div>
        <div className="flex items-center gap-4">
          {usage && (
            <div className="hidden md:flex flex-col gap-1 items-end">
              {user?.isAdmin ? (
                <span className="text-[10px] text-[#e3bfb3] uppercase tracking-widest" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
                  {formatBytes(usage.usedBytes)} used
                </span>
              ) : (
                <>
                  <span className="text-[10px] text-[#e3bfb3] uppercase tracking-widest" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
                    {formatBytes(usage.usedBytes)} / {formatBytes(usage.limitBytes)}
                  </span>
                  <div className="w-24 h-1 bg-white/10 rounded-full overflow-hidden">
                    <div
                      className="h-full rounded-full transition-all duration-500"
                      style={{
                        width: `${Math.min((usage.usedBytes / usage.limitBytes) * 100, 100)}%`,
                        background: usage.usedBytes / usage.limitBytes > 0.8 ? '#ff4444' : 'linear-gradient(90deg, #ff9069, #ff7948)'
                      }}
                    />
                  </div>
                </>
              )}
            </div>
          )}
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
        <section className="relative h-[80vh] min-h-[500px] w-full overflow-hidden pt-16">
          <div className="absolute inset-x-0 bottom-0 z-0" style={{ top: '4rem' }}>
            <img
              src={hero.backdropPath ?? hero.posterPath}
              alt={hero.title}
              className="w-full h-full object-cover object-top opacity-60 transition-opacity duration-1000"
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

      {/* Request selector + bar */}
      <div className="max-w-2xl mx-auto w-full px-6 mt-4 mb-2">
        <div className="flex justify-center mb-3">
          <div className="inline-flex rounded-full bg-[#2b1c17] border border-white/10 p-1">
            {[
              { key: 'movie', label: 'Movie' },
              { key: 'tv', label: 'TV Show' },
            ].map(opt => (
              <button
                key={opt.key}
                type="button"
                onClick={() => { setRequestType(opt.key); setRequestError(null) }}
                className={`px-5 py-1.5 rounded-full text-xs font-bold uppercase tracking-wide transition-colors ${
                  requestType === opt.key ? 'text-[#5c1900]' : 'text-[#e3bfb3] hover:text-[#f9dcd4]'
                }`}
                style={requestType === opt.key
                  ? { fontFamily: 'Epilogue, sans-serif', background: 'linear-gradient(135deg, #ff9069 0%, #ff7948 100%)' }
                  : { fontFamily: 'Epilogue, sans-serif' }}
              >
                {opt.label}
              </button>
            ))}
          </div>
        </div>

        <form
          onSubmit={(e) => {
            e.preventDefault()
            const t = requestText.trim()
            if (!t) return
            if (requestType === 'tv' && !/(season\s*\d{1,2})|(\bs\d{1,2}\b)|(\b\d{1,2}\s*x\s*\d{1,2}\b)/i.test(t)) {
              setRequestError('Include which season you want — e.g. "Severance season 2". Only that season gets downloaded.')
              return
            }
            setRequestError(null)
            setRequestModal({ type: requestType, text: t })
          }}
          className="flex items-center gap-3 bg-[#2b1c17] border border-white/10 rounded-full pl-5 pr-2 py-2 shadow-lg focus-within:border-[#ff9069] transition-colors"
        >
          <span className="material-symbols-outlined text-[#ff9069]">add_circle</span>
          <input
            type="text"
            value={requestText}
            onChange={(e) => { setRequestText(e.target.value); if (requestError) setRequestError(null) }}
            placeholder={requestType === 'tv' ? 'e.g. Severance season 2 — include the season' : 'e.g. Dune Part Two'}
            className="flex-1 bg-transparent text-[#f9dcd4] placeholder-[#5b4138] focus:outline-none text-sm py-1.5"
            style={{ fontFamily: 'Space Grotesk, sans-serif' }}
          />
          <button
            type="submit"
            disabled={!requestText.trim()}
            className="px-5 py-2 rounded-full font-bold text-xs text-[#5c1900] hover:brightness-110 active:scale-95 transition-all disabled:opacity-40 uppercase tracking-wide"
            style={{ fontFamily: 'Epilogue, sans-serif', background: 'linear-gradient(135deg, #ff9069 0%, #ff7948 100%)' }}
          >
            Request
          </button>
        </form>

        {requestError ? (
          <p className="text-xs text-[#ffb4ab] mt-2 px-2 text-center" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
            {requestError}
          </p>
        ) : requestType === 'tv' ? (
          <p className="text-xs text-[#aa897f] mt-2 px-2 text-center" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
            TV requests must include a season number — only that season is downloaded.
          </p>
        ) : null}
      </div>

      {/* Tabs */}
      <div className="px-8 md:px-12 mt-4 flex gap-1 border-b border-[#5b4138]/30">
        {[
          { key: 'browse', label: 'Browse' },
          { key: 'continue', label: 'Continue Watching' },
        ].map(tab => (
          <button
            key={tab.key}
            onClick={() => setActiveTab(tab.key)}
            className={`px-5 py-3 font-bold text-sm uppercase tracking-wide transition-colors border-b-2 -mb-px ${
              activeTab === tab.key
                ? 'text-[#ff9069] border-[#ff9069]'
                : 'text-[#e3bfb3] border-transparent hover:text-[#f9dcd4]'
            }`}
            style={{ fontFamily: 'Epilogue, sans-serif' }}
          >
            {tab.label}
            {tab.key === 'continue' && continueWatching.length > 0 && (
              <span className="ml-2 text-[10px] bg-[#ff9069]/20 text-[#ffb59c] px-1.5 py-0.5 rounded-full align-middle">
                {continueWatching.length}
              </span>
            )}
          </button>
        ))}
      </div>

      {activeTab === 'browse' ? (
        <>
          <MovieFilterJobs />
          {/* Movies */}
          <MediaSection title="Movies">
            {pendingRequests.filter(r => r.mediaType === 'movie').map(r => (
              <DownloadingCard key={r.id} title={r.resolvedTitle} onRemove={() => removePendingRequest(r.id)} />
            ))}
            {movies.map(movie => (
              <PosterCard key={movie.id} item={movie} onClick={() => navigate(`/movie/${movie.id}`)} />
            ))}
          </MediaSection>

          {/* TV Shows */}
          <MediaSection title="TV Shows">
            {pendingRequests.filter(r => r.mediaType === 'tv').map(r => (
              <DownloadingCard key={r.id} title={r.resolvedTitle} onRemove={() => removePendingRequest(r.id)} />
            ))}
            {tvShows.map(show => (
              <PosterCard key={show.id} item={show} onClick={() => navigate(`/tvshow/${show.id}`)} />
            ))}
          </MediaSection>
        </>
      ) : (
        <ContinueWatchingGrid items={continueWatching} navigate={navigate} />
      )}

      {requestModal && (
        <RequestModal
          type={requestModal.type}
          initialText={requestModal.text}
          onClose={() => { setRequestModal(null); setRequestText(''); setRequestError(null) }}
          onConfirmed={() => api.get('/request/pending').then(({ data }) => setPendingRequests(data)).catch(() => {})}
        />
      )}

      {showHowTo && <HowToModal onClose={() => setShowHowTo(false)} />}

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
        <button onClick={scanMedia} disabled={scanning} className="flex flex-col items-center gap-1 text-[#adaaaa] disabled:opacity-40">
          <span className="material-symbols-outlined">refresh</span>
          <span className="text-[10px] uppercase tracking-widest" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>Scan</span>
        </button>
        <button onClick={logout} className="flex flex-col items-center gap-1 text-[#adaaaa] hover:text-[#ff9069] transition-colors">
          <span className="material-symbols-outlined">logout</span>
          <span className="text-[10px] uppercase tracking-widest" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>Sign Out</span>
        </button>
      </nav>
    </div>
  )
}

function MediaSection({ title, children }) {
  return (
    <div className="px-8 md:px-12 pt-8 pb-2">
      <h2
        className="text-2xl font-black mb-6 uppercase tracking-tight text-[#f9dcd4]"
        style={{ fontFamily: 'Epilogue, sans-serif' }}
      >
        {title}
      </h2>
      <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5 gap-x-5 gap-y-8">
        {children}
      </div>
    </div>
  )
}

function PosterCard({ item, onClick }) {
  return (
    <button onClick={onClick} className="w-full text-left group cursor-pointer">
      <div className="aspect-[2/3] rounded-xl overflow-hidden mb-3 relative bg-[#2b1c17]">
        {item.posterPath && (
          <img
            src={item.posterPath}
            alt={item.title}
            loading="lazy"
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
        className="font-bold text-base text-[#f9dcd4] group-hover:text-[#ffb59c] transition-colors truncate"
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

function ContinueWatchingGrid({ items, navigate }) {
  if (items.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center py-24 gap-4">
        <span className="material-symbols-outlined text-[#42312b] text-6xl">history</span>
        <p className="text-[#aa897f] text-sm text-center" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
          Nothing in progress. Start watching something and it&apos;ll show up here.
        </p>
      </div>
    )
  }

  return (
    <div className="px-8 md:px-12 pt-8 pb-2 grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-x-5 gap-y-8">
      {items.map((item, i) => {
        const percent = item.runtimeSeconds ? Math.min((item.progressSeconds / item.runtimeSeconds) * 100, 100) : 0
        const path = item.type === 'movie' ? `/watch/movie/${item.movieId}${item.movieVersionId ? `?version=${item.movieVersionId}` : ''}` : `/watch/episode/${item.episodeId}`

        return (
          <button key={i} onClick={() => navigate(path)} className="w-full text-left group cursor-pointer">
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
                  className="material-symbols-outlined text-white opacity-0 group-hover:opacity-100 transition-opacity text-5xl"
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
              className="text-base font-semibold truncate text-[#f9dcd4] group-hover:text-[#ffb59c] transition-colors"
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
      })}
    </div>
  )
}

function DownloadingCard({ title, onRemove }) {
  const [removing, setRemoving] = useState(false)

  async function remove() {
    setRemoving(true)
    await onRemove()
    setRemoving(false)
  }

  return (
    <div className="w-full text-left opacity-50">
      <div className="aspect-[2/3] rounded-xl overflow-hidden mb-3 relative bg-[#2b1c17] flex flex-col items-center justify-center gap-3">
        <button
          type="button"
          onClick={remove}
          disabled={removing}
          aria-label={`Remove ${title} from downloads`}
          className="absolute top-2 right-2 rounded-full bg-black/50 p-1 text-[#f9dcd4] hover:bg-black/80 disabled:opacity-50"
        >
          <span className="material-symbols-outlined text-base">close</span>
        </button>
        <span className="material-symbols-outlined text-4xl text-[#ff9069] animate-spin" style={{ animationDuration: '2s' }}>
          downloading
        </span>
        <span className="text-[10px] uppercase tracking-widest text-[#e3bfb3] text-center px-2" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
          Downloading
        </span>
      </div>
      <h3
        className="font-bold text-[#f9dcd4] truncate text-base"
        style={{ fontFamily: 'Epilogue, sans-serif' }}
      >
        {title}
      </h3>
      <button
        type="button"
        onClick={remove}
        disabled={removing}
        className="mt-1 text-[10px] uppercase tracking-widest text-[#e3bfb3] hover:text-[#ff9069] disabled:opacity-50"
        style={{ fontFamily: 'Space Grotesk, sans-serif' }}
      >
        {removing ? 'Removing…' : 'Remove'}
      </button>
    </div>
  )
}

function RequestModal({ type, initialText, onClose, onConfirmed }) {
  const [text, setText] = useState(initialText ?? '')
  const [loading, setLoading] = useState(false)
  const [confirmation, setConfirmation] = useState(null) // { title, mediaType, season }
  const [result, setResult] = useState(null)

  useEffect(() => {
    if (initialText && initialText.trim()) submit()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  async function submit(e) {
    e?.preventDefault()
    if (!text.trim()) return
    setLoading(true)
    try {
      const { data } = await api.post('/request/parse', { text: text.trim() })
      if (data.blocked) {
        setResult({ ok: false, message: "Adult content, pornographic material, and NC-17 rated titles cannot be requested on this server. Please request something else." })
      } else if (!data.found) {
        setResult({ ok: false, message: "Sorry, I couldn't understand that request. Try something like: 'Dune Part Two' or 'season 2 of Severance'." })
      } else {
        setConfirmation({ title: data.title, mediaType: data.mediaType, season: data.season, tmdbId: data.tmdbId, tvdbId: data.tvdbId })
      }
    } catch (err) {
      setResult({ ok: false, message: err.response?.data?.message ?? 'Something went wrong.' })
    } finally {
      setLoading(false)
    }
  }

  async function confirm(options = {}) {
    setLoading(true)
    try {
      const { data } = await api.post('/request/confirm', { ...confirmation, ...options })
      setConfirmation(null)
      setResult({ ok: true, message: data.message })
      onConfirmed()
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
            Request {type === 'movie' ? 'a Movie' : type === 'tv' ? 'a TV Show Season' : 'Media'}
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
                onClick={() => { setText(''); setResult(null); setConfirmation(null) }}
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
        ) : confirmation ? (
          <div>
            <p className="text-[#e3bfb3] text-sm mb-3" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
              I found:
            </p>
            <p className="text-2xl font-black text-[#f9dcd4] mb-1" style={{ fontFamily: 'Epilogue, sans-serif' }}>
              {confirmation.title}
              {confirmation.season && <span className="text-[#ff9069]"> — Season {confirmation.season}</span>}
            </p>
            <p className="text-[10px] uppercase tracking-widest text-[#5b4138] mb-8" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
              {confirmation.mediaType === 'movie' ? 'Movie' : 'TV Show'}
            </p>
            <p className="text-[#e3bfb3] text-sm mb-6" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
              Is this what you're looking for?
            </p>
            {confirmation.mediaType === 'movie' && <MovieFilterConfirmation movie={confirmation} onConfirm={confirm} busy={loading} />}
            <div className="flex gap-3">
              <button
                onClick={() => setConfirmation(null)}
                className="flex-1 bg-[#2b1c17] text-[#f9dcd4] px-6 py-3 rounded-full font-bold hover:bg-[#372621] transition-colors text-sm"
                style={{ fontFamily: 'Epilogue, sans-serif' }}
              >
                No, go back
              </button>
              {confirmation.mediaType !== 'movie' && <button
                onClick={() => confirm()}
                disabled={loading}
                className="flex-1 px-6 py-3 rounded-full font-bold text-sm text-[#5c1900] hover:brightness-110 active:scale-95 transition-all disabled:opacity-40"
                style={{ fontFamily: 'Epilogue, sans-serif', background: 'linear-gradient(135deg, #ff9069 0%, #ff7948 100%)' }}
              >
                {loading ? 'Adding…' : 'Yes, add it'}
              </button>}
            </div>
          </div>
        ) : (
          <form onSubmit={submit}>
            <input
              autoFocus
              type="text"
              value={text}
              onChange={e => setText(e.target.value)}
              placeholder={type === 'tv' ? 'e.g. Severance season #' : type === 'movie' ? 'e.g. Dune Part Two' : 'e.g. Dune Part Two, or Severance season 2'}
              className="w-full bg-[#2b1c17] border border-white/10 rounded-xl px-4 py-3 text-[#f9dcd4] placeholder-[#5b4138] focus:outline-none focus:border-[#ff9069] transition-colors mb-6 text-sm"
              style={{ fontFamily: 'Space Grotesk, sans-serif' }}
            />
            <button
              type="submit"
              disabled={loading || !text.trim()}
              className="w-full px-6 py-3 rounded-full font-bold text-[#5c1900] hover:brightness-110 active:scale-95 transition-all disabled:opacity-40 text-sm"
              style={{ fontFamily: 'Epilogue, sans-serif', background: 'linear-gradient(135deg, #ff9069 0%, #ff7948 100%)' }}
            >
              {loading ? 'Searching…' : 'Search'}
            </button>
          </form>
        )}
      </div>
    </div>
  )
}

function HowToModal({ onClose }) {
  const steps = [
    {
      icon: 'add_circle',
      title: 'Make a Request',
      body: 'Use the request bar below the hero banner. Set the toggle above it to Movie or TV Show, type the title, and hit Request. For a TV show you must include the season (e.g. "Severance season 2") — only that season is downloaded, not the whole series. A confirmation box shows what was found; click "Yes, add it".',
    },
    {
      icon: 'downloading',
      title: 'Track the Download',
      body: 'Your pick appears as a "Downloading" card in the Movies or TV Shows grid on the Browse tab.',
    },
    {
      icon: 'radar',
      title: 'Scan Media',
      body: 'Every so often, click "Scan Media" in the top bar ("Scan" on mobile) to pull in anything that has finished downloading. The page reloads each time it runs.',
    },
    {
      icon: 'play_circle',
      title: 'Watch',
      body: 'Once the file lands, the Downloading card becomes the real poster — click it to play. Anything you have partly watched lives under the "Continue Watching" tab.',
    },
  ]

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-sm"
      onClick={e => { if (e.target === e.currentTarget) onClose() }}
    >
      <div className="bg-[#1e100b] border border-white/10 rounded-2xl p-8 w-full max-w-lg shadow-2xl">
        <div className="flex items-center justify-between mb-8">
          <h2
            className="text-2xl font-black uppercase tracking-tighter text-[#f9dcd4]"
            style={{ fontFamily: 'Epilogue, sans-serif' }}
          >
            How To Use
          </h2>
          <button onClick={onClose} className="text-[#e3bfb3] hover:text-[#ff9069] transition-colors">
            <span className="material-symbols-outlined">close</span>
          </button>
        </div>

        <div className="space-y-6">
          {steps.map((step, i) => (
            <div key={i} className="flex gap-4">
              <div className="flex-shrink-0 w-10 h-10 rounded-full bg-[#2b1c17] flex items-center justify-center">
                <span
                  className="material-symbols-outlined text-[#ff9069]"
                  style={{ fontSize: '20px', fontVariationSettings: "'FILL' 1, 'wght' 400, 'GRAD' 0, 'opsz' 24" }}
                >
                  {step.icon}
                </span>
              </div>
              <div>
                <p
                  className="text-[10px] uppercase tracking-widest text-[#ffb59c] mb-1"
                  style={{ fontFamily: 'Space Grotesk, sans-serif' }}
                >
                  Step {i + 1}
                </p>
                <h3
                  className="text-lg font-bold text-[#f9dcd4] mb-1"
                  style={{ fontFamily: 'Epilogue, sans-serif' }}
                >
                  {step.title}
                </h3>
                <p className="text-[#e3bfb3] text-sm leading-relaxed" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
                  {step.body}
                </p>
              </div>
            </div>
          ))}
        </div>

        <button
          onClick={onClose}
          className="mt-8 w-full py-3 rounded-full font-bold text-[#5c1900] hover:brightness-110 active:scale-95 transition-all text-sm"
          style={{ fontFamily: 'Epilogue, sans-serif', background: 'linear-gradient(135deg, #ff9069 0%, #ff7948 100%)' }}
        >
          Got it
        </button>
      </div>
    </div>
  )
}
