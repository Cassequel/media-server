import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import api from '../services/api'

export default function FavoritesPage() {
  const navigate = useNavigate()
  const [favorites, setFavorites] = useState([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    api.get('/favorites').then(({ data }) => setFavorites(data)).finally(() => setLoading(false))
  }, [])

  function handleClick(fav) {
    if (fav.type === 'movie') navigate(`/movie/${fav.movieId}`)
    else navigate(`/tvshow/${fav.tvShowId}`)
  }

  async function removeFavorite(fav, e) {
    e.stopPropagation()
    if (fav.type === 'movie') await api.post(`/favorites/movie/${fav.movieId}`)
    else await api.post(`/favorites/tvshow/${fav.tvShowId}`)
    setFavorites(prev => prev.filter(f => f.id !== fav.id))
  }

  return (
    <div className="min-h-screen bg-[#1e100b] text-[#f9dcd4]">
      {/* Header */}
      <div className="px-8 md:px-12 pt-8 pb-6 flex items-center gap-4 border-b border-[#5b4138]/40">
        <button
          onClick={() => navigate(-1)}
          className="flex items-center justify-center w-10 h-10 rounded-full bg-[#271813] text-[#e3bfb3] hover:text-[#f9dcd4] hover:bg-[#2b1c17] transition-all"
        >
          <span className="material-symbols-outlined">arrow_back</span>
        </button>
        <h1
          className="text-3xl font-black uppercase tracking-tight"
          style={{ fontFamily: 'Epilogue, sans-serif' }}
        >
          My Favorites
        </h1>
      </div>

      <div className="px-8 md:px-12 py-8">
        {loading && (
          <p className="text-[#e3bfb3] text-sm" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>Loading…</p>
        )}

        {!loading && favorites.length === 0 && (
          <div className="flex flex-col items-center justify-center py-24 gap-4">
            <span className="material-symbols-outlined text-[#42312b] text-6xl">favorite_border</span>
            <p className="text-[#aa897f] text-sm text-center" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
              No favorites yet. Add some from a movie or TV show page.
            </p>
          </div>
        )}

        <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 gap-5">
          {favorites.map(fav => (
            <button
              key={fav.id}
              onClick={() => handleClick(fav)}
              className="text-left group relative cursor-pointer"
            >
              <div className="relative w-full aspect-[2/3] rounded-xl overflow-hidden bg-[#2b1c17]">
                {fav.posterPath
                  ? (
                    <img
                      src={fav.posterPath}
                      alt={fav.title}
                      className="w-full h-full object-cover group-hover:scale-110 transition-transform duration-700"
                    />
                  )
                  : (
                    <div className="w-full h-full flex items-center justify-center text-[#5b4138]">
                      <span className="material-symbols-outlined text-5xl">movie</span>
                    </div>
                  )
                }

                {/* Hover overlay */}
                <div className="absolute inset-0 bg-black/0 group-hover:bg-black/40 transition-colors flex items-center justify-center">
                  <span
                    className="material-symbols-outlined text-white opacity-0 group-hover:opacity-100 scale-50 group-hover:scale-100 transition-all duration-300 text-5xl"
                    style={{ fontVariationSettings: "'FILL' 1, 'wght' 400, 'GRAD' 0, 'opsz' 48" }}
                  >
                    play_circle
                  </span>
                </div>

                {/* Remove button */}
                <button
                  onClick={(e) => removeFavorite(fav, e)}
                  className="absolute top-2 right-2 bg-black/60 backdrop-blur-md text-[#e3bfb3] hover:text-[#ffb4ab] rounded-full w-7 h-7 flex items-center justify-center opacity-0 group-hover:opacity-100 transition-opacity active:scale-90"
                >
                  <span className="material-symbols-outlined text-sm">close</span>
                </button>

                {/* Type badge */}
                <div className="absolute top-2 left-2">
                  <span
                    className="bg-black/60 backdrop-blur-md text-[#e3bfb3] text-[10px] px-2 py-0.5 rounded font-bold uppercase tracking-wider"
                    style={{ fontFamily: 'Space Grotesk, sans-serif' }}
                  >
                    {fav.type === 'movie' ? 'Movie' : 'TV'}
                  </span>
                </div>

                {/* Rating */}
                {fav.tmdbRating && (
                  <div className="absolute bottom-2 left-2">
                    <span
                      className="bg-black/60 backdrop-blur-md px-2 py-0.5 rounded text-[10px] font-bold text-[#ffb59c]"
                      style={{ fontFamily: 'Space Grotesk, sans-serif' }}
                    >
                      {fav.tmdbRating.toFixed(1)}
                    </span>
                  </div>
                )}
              </div>

              <p
                className="text-sm mt-2 truncate text-[#f9dcd4] group-hover:text-[#ffb59c] transition-colors font-semibold"
                style={{ fontFamily: 'Epilogue, sans-serif' }}
              >
                {fav.title}
              </p>
            </button>
          ))}
        </div>
      </div>
    </div>
  )
}
