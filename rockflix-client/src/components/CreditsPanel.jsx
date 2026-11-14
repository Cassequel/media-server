import { useEffect, useState } from 'react'
import api from '../services/api'

export default function CreditsPanel({ endpoint }) {
  const [data, setData] = useState(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(false)

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setError(false)
    api.get(endpoint)
      .then(({ data }) => { if (!cancelled) setData(data) })
      .catch(() => { if (!cancelled) setError(true) })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [endpoint])

  if (loading) {
    return (
      <p className="text-[#aa897f] text-sm" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
        Loading cast &amp; crew…
      </p>
    )
  }

  const cast = data?.cast ?? []
  const crew = data?.crew ?? []

  if (error || (cast.length === 0 && crew.length === 0)) {
    return (
      <p className="text-[#aa897f] text-sm" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
        No cast &amp; crew information available for this title.
      </p>
    )
  }

  return (
    <div className="space-y-10">
      {crew.length > 0 && (
        <div>
          <h3
            className="text-xs font-black uppercase tracking-widest text-[#ffb59c] mb-4"
            style={{ fontFamily: 'Space Grotesk, sans-serif' }}
          >
            Crew
          </h3>
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-x-8 gap-y-2">
            {crew.map((c, i) => (
              <div key={i} className="text-sm flex justify-between gap-3 border-b border-[#5b4138]/20 py-1.5">
                <span className="text-[#f9dcd4] font-semibold truncate" style={{ fontFamily: 'Epilogue, sans-serif' }}>
                  {c.name}
                </span>
                <span className="text-[#aa897f] flex-shrink-0" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
                  {c.job}
                </span>
              </div>
            ))}
          </div>
        </div>
      )}

      {cast.length > 0 && (
        <div>
          <h3
            className="text-xs font-black uppercase tracking-widest text-[#ffb59c] mb-4"
            style={{ fontFamily: 'Space Grotesk, sans-serif' }}
          >
            Cast
          </h3>
          <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-6 gap-x-4 gap-y-6">
            {cast.map((p, i) => (
              <div key={i} className="text-left">
                <div className="aspect-[2/3] rounded-xl overflow-hidden mb-2 bg-[#2b1c17]">
                  {p.profilePath ? (
                    <img
                      src={p.profilePath}
                      alt={p.name}
                      loading="lazy"
                      className="w-full h-full object-cover"
                    />
                  ) : (
                    <div className="w-full h-full flex items-center justify-center text-[#5b4138]">
                      <span className="material-symbols-outlined text-4xl">person</span>
                    </div>
                  )}
                </div>
                <p
                  className="text-sm font-semibold text-[#f9dcd4] truncate"
                  style={{ fontFamily: 'Epilogue, sans-serif' }}
                >
                  {p.name}
                </p>
                {p.character && (
                  <p
                    className="text-xs text-[#aa897f] truncate"
                    style={{ fontFamily: 'Space Grotesk, sans-serif' }}
                  >
                    {p.character}
                  </p>
                )}
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  )
}
