import { useEffect, useState } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import api from '../services/api'

export default function TvShowPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const [show, setShow] = useState(null)
  const [episodes, setEpisodes] = useState([])
  const [favorited, setFavorited] = useState(false)
  const [selectedSeason, setSelectedSeason] = useState(1)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    Promise.all([
      api.get(`/tvshows/${id}`),
      api.get(`/tvshows/${id}/episodes`),
      api.get(`/favorites/tvshow/${id}/status`)
    ]).then(([{ data: s }, { data: eps }, { data: f }]) => {
      setShow(s)
      setEpisodes(eps)
      setFavorited(f.favorited)
      if (eps.length > 0) setSelectedSeason(eps[0].seasonNumber)
    }).finally(() => setLoading(false))
  }, [id])

  async function toggleFavorite() {
    const { data } = await api.post(`/favorites/tvshow/${id}`)
    setFavorited(data.favorited)
  }

  if (loading) return (
    <div className="min-h-screen bg-[#1e100b] flex items-center justify-center">
      <span className="text-[#ffb59c] text-sm" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>Loading…</span>
    </div>
  )
  if (!show) return (
    <div className="min-h-screen bg-[#1e100b] flex items-center justify-center text-[#e3bfb3]">Not found</div>
  )

  const seasons = [...new Set(episodes.map(e => e.seasonNumber))].sort()
  const seasonEpisodes = episodes.filter(e => e.seasonNumber === selectedSeason)

  return (
    <div className="min-h-screen bg-[#1e100b] text-[#f9dcd4]">
      {/* Backdrop hero */}
      <div className="relative h-[55vh] min-h-[360px] w-full overflow-hidden">
        {show.backdropPath && (
          <img
            src={show.backdropPath}
            alt={show.title}
            className="absolute inset-0 w-full h-full object-cover opacity-60"
          />
        )}
        <div className="absolute inset-0" style={{ background: 'linear-gradient(to bottom, transparent 30%, #1e100b 100%)' }} />
        <div className="absolute inset-0 hidden md:block" style={{ background: 'linear-gradient(to right, #1e100b 0%, transparent 60%)' }} />

        <button
          onClick={() => navigate(-1)}
          className="absolute top-6 left-6 flex items-center justify-center w-10 h-10 rounded-full bg-black/30 backdrop-blur-md text-[#e3bfb3] hover:text-[#f9dcd4] hover:bg-black/50 transition-all"
        >
          <span className="material-symbols-outlined">arrow_back</span>
        </button>
      </div>

      {/* Show info */}
      <div className="px-8 md:px-12 -mt-40 relative z-10 max-w-[1200px] mx-auto mb-10">
        <div className="flex gap-8">
          {show.posterPath && (
            <div className="hidden md:block w-40 lg:w-48 aspect-[2/3] flex-shrink-0 rounded-xl overflow-hidden shadow-2xl">
              <img src={show.posterPath} alt={show.title} className="w-full h-full object-cover" />
            </div>
          )}
          <div className="flex-1 pt-8 min-w-0">
            {/* Meta */}
            <div className="flex flex-wrap items-center gap-3 mb-4">
              {show.genre && (
                <span
                  className="bg-[#ffb59c]/20 text-[#ffb59c] px-3 py-1 rounded-full text-xs font-bold uppercase tracking-widest"
                  style={{ fontFamily: 'Space Grotesk, sans-serif' }}
                >
                  {show.genre}
                </span>
              )}
              {show.year && (
                <span className="text-[#e3bfb3] text-sm" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>{show.year}</span>
              )}
              <span className="text-[#e3bfb3] text-sm" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
                • {show.seasonCount} Season{show.seasonCount !== 1 ? 's' : ''}
              </span>
              <span className="text-[#e3bfb3] text-sm" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
                • {show.episodeCount} Eps
              </span>
              {show.tmdbRating && (
                <span className="flex items-center gap-1 text-[#ffb59c]">
                  <span className="material-symbols-outlined text-sm" style={{ fontVariationSettings: "'FILL' 1, 'wght' 400, 'GRAD' 0, 'opsz' 24" }}>star</span>
                  <span className="text-sm font-bold" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>{show.tmdbRating.toFixed(1)}</span>
                </span>
              )}
            </div>

            <h1
              className="text-4xl md:text-5xl font-black tracking-tighter uppercase mb-5 leading-tight"
              style={{ fontFamily: 'Epilogue, sans-serif' }}
            >
              {show.title}
            </h1>

            {show.description && (
              <p className="text-[#e3bfb3] max-w-2xl mb-6 leading-relaxed line-clamp-3">{show.description}</p>
            )}

            <button
              onClick={toggleFavorite}
              className={`px-6 py-3 rounded-full font-bold flex items-center gap-2 transition-all active:scale-95 ${
                favorited
                  ? 'bg-[#ffb59c]/20 text-[#ffb59c] hover:bg-[#ffb59c]/30'
                  : 'bg-[#372621] text-[#f9dcd4] hover:bg-[#47352f]'
              }`}
              style={{ fontFamily: 'Epilogue, sans-serif' }}
            >
              <span
                className="material-symbols-outlined"
                style={{ fontVariationSettings: `'FILL' ${favorited ? 1 : 0}, 'wght' 400, 'GRAD' 0, 'opsz' 24` }}
              >
                favorite
              </span>
              {favorited ? 'Favorited' : 'Add to Favorites'}
            </button>
          </div>
        </div>
      </div>

      {/* Season tabs */}
      <div className="px-8 md:px-12 mb-6 max-w-[1200px] mx-auto">
        <div className="flex gap-1 border-b border-[#5b4138]">
          {seasons.map(s => (
            <button
              key={s}
              onClick={() => setSelectedSeason(s)}
              className={`px-5 py-2.5 text-sm font-semibold border-b-2 -mb-px transition-all ${
                selectedSeason === s
                  ? 'border-[#ffb59c] text-[#ffb59c]'
                  : 'border-transparent text-[#e3bfb3] hover:text-[#f9dcd4]'
              }`}
              style={{ fontFamily: 'Space Grotesk, sans-serif' }}
            >
              Season {s}
            </button>
          ))}
        </div>
      </div>

      {/* Episodes */}
      <div className="px-8 md:px-12 pb-16 max-w-[1200px] mx-auto space-y-3">
        {seasonEpisodes.map(ep => {
          const progress = ep.runtimeMinutes
            ? Math.min((ep.progressSeconds ?? 0) / (ep.runtimeMinutes * 60) * 100, 100)
            : 0
          return (
            <button
              key={ep.id}
              onClick={() => navigate(`/watch/episode/${ep.id}`)}
              className="w-full flex gap-4 bg-[#271813] hover:bg-[#2b1c17] rounded-xl p-4 text-left transition-colors group"
            >
              {/* Thumbnail */}
              <div className="relative w-40 h-24 flex-shrink-0 rounded-lg overflow-hidden bg-[#372621]">
                {ep.stillPath
                  ? <img src={ep.stillPath} alt={ep.title} className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-500" />
                  : (
                    <div className="w-full h-full flex items-center justify-center text-[#5b4138]">
                      <span className="material-symbols-outlined text-3xl">play_circle</span>
                    </div>
                  )
                }
                {progress > 0 && (
                  <div className="absolute bottom-0 left-0 right-0 h-1 bg-black/40">
                    <div className="h-full bg-[#ffb59c]" style={{ width: `${progress}%` }} />
                  </div>
                )}
                {ep.completed && (
                  <div className="absolute top-1.5 right-1.5 bg-[#ffb59c] text-[#5c1900] text-[10px] px-1.5 py-0.5 rounded font-bold">
                    ✓
                  </div>
                )}
              </div>

              {/* Info */}
              <div className="flex-1 min-w-0 py-1">
                <div className="flex items-baseline gap-2 mb-1.5">
                  <span className="text-[#e3bfb3] text-sm flex-shrink-0" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
                    E{String(ep.episodeNumber).padStart(2, '0')}
                  </span>
                  <span className="font-semibold truncate text-[#f9dcd4] group-hover:text-[#ffb59c] transition-colors" style={{ fontFamily: 'Epilogue, sans-serif' }}>
                    {ep.title}
                  </span>
                  {ep.runtimeMinutes && (
                    <span className="text-[#aa897f] text-xs ml-auto flex-shrink-0" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
                      {ep.runtimeMinutes}m
                    </span>
                  )}
                </div>
                {ep.description && (
                  <p className="text-[#e3bfb3] text-sm line-clamp-2 leading-relaxed">{ep.description}</p>
                )}
              </div>
            </button>
          )
        })}
      </div>
    </div>
  )
}
