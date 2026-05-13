import { useEffect, useState } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import api from '../services/api'

export default function MovieDetailPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const [movie, setMovie] = useState(null)
  const [favorited, setFavorited] = useState(false)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    Promise.all([
      api.get(`/movies/${id}`),
      api.get(`/favorites/movie/${id}/status`)
    ]).then(([{ data: m }, { data: f }]) => {
      setMovie(m)
      setFavorited(f.favorited)
    }).finally(() => setLoading(false))
  }, [id])

  async function toggleFavorite() {
    const { data } = await api.post(`/favorites/movie/${id}`)
    setFavorited(data.favorited)
  }

  if (loading) return (
    <div className="min-h-screen bg-[#1e100b] flex items-center justify-center">
      <span className="text-[#ffb59c] text-sm" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>Loading…</span>
    </div>
  )
  if (!movie) return (
    <div className="min-h-screen bg-[#1e100b] flex items-center justify-center text-[#e3bfb3]">Not found</div>
  )

  const progress = movie.runtimeMinutes
    ? Math.min((movie.progressSeconds ?? 0) / (movie.runtimeMinutes * 60) * 100, 100)
    : 0

  return (
    <div className="min-h-screen bg-[#1e100b] text-[#f9dcd4]">
      {/* Backdrop hero */}
      <div className="relative h-[60vh] min-h-[400px] w-full overflow-hidden">
        {movie.backdropPath && (
          <img
            src={movie.backdropPath}
            alt={movie.title}
            className="absolute inset-0 w-full h-full object-cover opacity-60"
          />
        )}
        <div className="absolute inset-0" style={{ background: 'linear-gradient(to bottom, transparent 30%, #1e100b 100%)' }} />
        <div className="absolute inset-0 hidden md:block" style={{ background: 'linear-gradient(to right, #1e100b 0%, transparent 60%)' }} />

        {/* Back button */}
        <button
          onClick={() => navigate(-1)}
          className="absolute top-6 left-6 flex items-center justify-center w-10 h-10 rounded-full bg-black/30 backdrop-blur-md text-[#e3bfb3] hover:text-[#f9dcd4] hover:bg-black/50 transition-all"
        >
          <span className="material-symbols-outlined">arrow_back</span>
        </button>
      </div>

      {/* Detail content */}
      <div className="px-8 md:px-12 -mt-48 relative z-10 max-w-[1200px] mx-auto">
        <div className="flex gap-8">
          {/* Poster */}
          {movie.posterPath && (
            <div className="hidden md:block w-44 lg:w-52 aspect-[2/3] flex-shrink-0 rounded-xl overflow-hidden shadow-2xl">
              <img src={movie.posterPath} alt={movie.title} className="w-full h-full object-cover" />
            </div>
          )}

          {/* Info */}
          <div className="flex-1 pt-10 min-w-0">
            {/* Meta badges */}
            <div className="flex flex-wrap items-center gap-3 mb-4">
              {movie.genre && (
                <span
                  className="bg-[#ffb59c]/20 text-[#ffb59c] px-3 py-1 rounded-full text-xs font-bold uppercase tracking-widest"
                  style={{ fontFamily: 'Space Grotesk, sans-serif' }}
                >
                  {movie.genre}
                </span>
              )}
              {movie.year && (
                <span className="text-[#e3bfb3] text-sm" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
                  {movie.year}
                </span>
              )}
              {movie.runtimeMinutes && (
                <span className="text-[#e3bfb3] text-sm" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
                  • {movie.runtimeMinutes}m
                </span>
              )}
              {movie.tmdbRating && (
                <span className="flex items-center gap-1 text-[#ffb59c]">
                  <span className="material-symbols-outlined text-sm" style={{ fontVariationSettings: "'FILL' 1, 'wght' 400, 'GRAD' 0, 'opsz' 24" }}>star</span>
                  <span className="text-sm font-bold" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>{movie.tmdbRating.toFixed(1)}</span>
                </span>
              )}
            </div>

            <h1
              className="text-4xl md:text-5xl font-black tracking-tighter uppercase mb-5 leading-tight"
              style={{ fontFamily: 'Epilogue, sans-serif' }}
            >
              {movie.title}
            </h1>

            {movie.description && (
              <p className="text-[#e3bfb3] max-w-2xl mb-6 leading-relaxed">{movie.description}</p>
            )}

            {/* Progress bar */}
            {progress > 0 && (
              <div className="w-48 h-1 bg-[#42312b] rounded-full mb-6">
                <div className="h-full bg-[#ffb59c] rounded-full" style={{ width: `${progress}%` }} />
              </div>
            )}

            {/* Action buttons */}
            <div className="flex flex-wrap gap-3">
              <button
                onClick={() => navigate(`/watch/movie/${id}`)}
                className="px-8 py-3 rounded-full font-bold flex items-center gap-2 shadow-lg hover:brightness-110 active:scale-95 transition-all text-[#5c1900]"
                style={{ fontFamily: 'Epilogue, sans-serif', background: 'linear-gradient(135deg, #ff9069 0%, #ff7948 100%)' }}
              >
                <span className="material-symbols-outlined" style={{ fontVariationSettings: "'FILL' 1, 'wght' 400, 'GRAD' 0, 'opsz' 24" }}>play_arrow</span>
                {progress > 0 ? 'Resume' : 'Play Now'}
              </button>
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
      </div>
    </div>
  )
}
