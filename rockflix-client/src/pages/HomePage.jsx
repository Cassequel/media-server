import { useEffect, useState, useRef } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../context/AuthContext'
import api from '../services/api'

const APP_CARDS = [
  { id: 'library', label: 'Library', bg: 'bg-blue-700', path: '/library' },
  { id: 'youtube', label: 'YouTube', bg: 'bg-red-600', external: 'https://youtube.com' },
  { id: 'spotify', label: 'Spotify', bg: 'bg-green-600', external: 'https://spotify.com' },
]

export default function HomePage() {
  const { user, logout } = useAuth()
  const navigate = useNavigate()
  const [hero, setHero] = useState(null)
  const [heroList, setHeroList] = useState([])
  const [heroIndex, setHeroIndex] = useState(0)
  const [continueWatching, setContinueWatching] = useState([])
  const intervalRef = useRef(null)

  useEffect(() => {
    api.get('/movies').then(({ data }) => {
      const shuffled = [...data].sort(() => Math.random() - 0.5).slice(0, 4)
      setHeroList(shuffled)
      setHero(shuffled[0] ?? null)
    })
    api.get('/watchhistory/continue-watching').then(({ data }) => setContinueWatching(data))
  }, [])

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

  function handleCardClick(card) {
    if (card.external) window.open(card.external, '_blank')
    else navigate(card.path)
  }

  return (
    <div className="min-h-screen bg-[#0f0f0f] text-white">
      {/* Top nav */}
      <nav className="fixed top-0 left-0 right-0 z-50 flex items-center justify-between px-8 py-4 bg-gradient-to-b from-black/80 to-transparent">
        <span className="text-2xl font-bold tracking-widest text-red-500">ROCKFLIX</span>
        <div className="flex items-center gap-4 text-sm text-zinc-300">
          <span>{user?.username}</span>
          <button onClick={logout} className="hover:text-white transition">Sign out</button>
        </div>
      </nav>

      {/* Hero Banner */}
      {hero && (
        <div className="relative h-[70vh] min-h-[400px] w-full overflow-hidden">
          <img
            src={hero.backdropPath ?? hero.posterPath}
            alt={hero.title}
            className="absolute inset-0 w-full h-full object-cover transition-opacity duration-1000"
          />
          <div className="absolute inset-0 bg-gradient-to-r from-black/80 via-black/30 to-transparent" />
          <div className="absolute inset-0 bg-gradient-to-t from-[#0f0f0f] via-transparent to-transparent" />
          <div className="absolute bottom-16 left-12 max-w-lg">
            <h1 className="text-5xl font-bold mb-3 drop-shadow-lg">{hero.title}</h1>
            {hero.year && <p className="text-zinc-300 mb-2">{hero.year}</p>}
            {hero.description && (
              <p className="text-zinc-300 text-sm line-clamp-3 mb-4">{hero.description}</p>
            )}
            <button
              onClick={() => navigate(`/watch/movie/${hero.id}`)}
              className="bg-white text-black font-bold px-6 py-2 rounded mr-3 hover:bg-zinc-200 transition"
            >
              ▶ Play
            </button>
            <button
              onClick={() => navigate(`/movie/${hero.id}`)}
              className="bg-zinc-700/80 text-white font-semibold px-6 py-2 rounded hover:bg-zinc-600 transition"
            >
              More Info
            </button>
          </div>
          {/* Hero dots */}
          <div className="absolute bottom-6 left-12 flex gap-2">
            {heroList.map((_, i) => (
              <button
                key={i}
                onClick={() => goToHero(i)}
                className={`w-2 h-2 rounded-full transition-all ${i === heroIndex ? 'bg-white w-4' : 'bg-white/40'}`}
              />
            ))}
          </div>
        </div>
      )}

      {/* App Cards Row */}
      <div className="px-12 py-6">
        <h2 className="text-xl font-semibold mb-4">Apps</h2>
        <div className="flex gap-4">
          {APP_CARDS.map(card => (
            <button
              key={card.id}
              onClick={() => handleCardClick(card)}
              className={`${card.bg} text-white font-bold text-lg w-32 h-20 rounded-xl shadow-lg hover:scale-105 transition-transform flex items-center justify-center`}
            >
              {card.label}
            </button>
          ))}
        </div>
      </div>

      {/* Continue Watching */}
      {continueWatching.length > 0 && (
        <div className="px-12 py-4">
          <h2 className="text-xl font-semibold mb-4">Continue Watching</h2>
          <div className="flex gap-4 overflow-x-auto pb-2">
            {continueWatching.map((item, i) => (
              <ContinueCard key={i} item={item} navigate={navigate} />
            ))}
          </div>
        </div>
      )}
    </div>
  )
}

function ContinueCard({ item, navigate }) {
  const percent = item.runtimeSeconds ? Math.min((item.progressSeconds / item.runtimeSeconds) * 100, 100) : 0
  const path = item.type === 'movie'
    ? `/watch/movie/${item.movieId}`
    : `/watch/episode/${item.episodeId}`

  return (
    <button onClick={() => navigate(path)} className="flex-shrink-0 w-52 text-left group">
      <div className="relative w-full h-32 rounded-lg overflow-hidden bg-zinc-800">
        {item.backdropPath && (
          <img src={item.backdropPath} alt={item.title} className="w-full h-full object-cover group-hover:scale-105 transition-transform" />
        )}
        <div className="absolute inset-0 flex items-center justify-center opacity-0 group-hover:opacity-100 transition">
          <span className="text-white text-3xl">▶</span>
        </div>
        <div className="absolute bottom-0 left-0 right-0 h-1 bg-zinc-600">
          <div className="h-full bg-red-500" style={{ width: `${percent}%` }} />
        </div>
      </div>
      <p className="text-sm mt-1 truncate text-zinc-200">{item.title}</p>
      {item.type === 'episode' && (
        <p className="text-xs text-zinc-400">S{String(item.seasonNumber).padStart(2,'0')}E{String(item.episodeNumber).padStart(2,'0')}</p>
      )}
    </button>
  )
}
