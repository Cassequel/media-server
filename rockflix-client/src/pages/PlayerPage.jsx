import { useEffect, useRef, useState } from 'react'
import { useParams, useNavigate, useLocation } from 'react-router-dom'
import api from '../services/api'
import FilterAttribution from '../components/FilterAttribution'

export default function PlayerPage() {
  const { id } = useParams()
  const { pathname, search } = useLocation()
  const requestedVersion = Number(new URLSearchParams(search).get('version')) || null
  const [retry, setRetry] = useState(0)
  return <PlayerSession key={`${pathname}:${id}:${requestedVersion}:${retry}`} id={id} isMovie={pathname.startsWith('/watch/movie')} requestedVersion={requestedVersion} retry={() => setRetry(n => n + 1)} />
}

function PlayerSession({ id, isMovie, requestedVersion, retry }) {
  const navigate = useNavigate()
  const videoRef = useRef(null)
  const resumed = useRef(false)
  const [meta, setMeta] = useState(null)
  const [error, setError] = useState('')
  const [token] = useState(() => localStorage.getItem('token') ?? '')
  const [bufferPct, setBufferPct] = useState(0)
  const [playerReady, setPlayerReady] = useState(false)
  const [hasPlayed, setHasPlayed] = useState(false)
  const version = meta?.versions?.find(v => v.id === (requestedVersion ?? meta?.defaultVersionId))
  const versionId = version?.id
  const failure = error || (!token ? 'Your session is missing. Sign in again.' : '') ||
    (isMovie && meta && !version ? 'This movie version is unavailable. Choose an available version from the movie page.' : '')

  useEffect(() => {
    let active = true
    api.get(isMovie ? `/movies/${id}` : `/tvshows/episodes/${id}`)
      .then(({ data }) => { if (active) setMeta(data) })
      .catch(() => { if (active) setError('Could not load video details. Please retry.') })
    return () => { active = false }
  }, [id, isMovie])

  // Capture the actual element and its identity; cleanup must never read a new source's ref.
  useEffect(() => {
    const video = videoRef.current
    if (!video) return
    const save = () => {
      if (!resumed.current || !Number.isFinite(video.duration) || video.duration <= 0) return
      const payload = { progressSeconds: Math.floor(video.currentTime), completed: video.currentTime / video.duration > 0.9 }
      if (isMovie) payload.versionId = versionId
      api.post(isMovie ? `/movies/${id}/progress` : `/tvshows/episodes/${id}/progress`, payload).catch(() => {})
    }
    const timer = setInterval(save, 10000)
    video.addEventListener('ended', save)
    return () => { clearInterval(timer); video.removeEventListener('ended', save); save() }
  }, [id, isMovie, versionId, meta, failure])

  const streamUrl = meta && !failure
    ? `/api/stream/${isMovie ? 'movie' : 'episode'}/${id}?token=${encodeURIComponent(token)}${isMovie ? `&versionId=${versionId}` : ''}`
    : ''

  function resumeOnce(e) {
    if (resumed.current) return
    const video = e.currentTarget
    const resume = isMovie ? version?.progressSeconds : meta?.progressSeconds
    if (Number.isFinite(resume) && resume > 10 && Number.isFinite(video.duration) && video.duration > 0)
      video.currentTime = Math.min(resume, Math.max(0, video.duration - 1))
    resumed.current = true
  }

  function handleProgress(e) {
    const v = e.target
    if (!v.duration || !v.buffered.length) return
    setBufferPct(Math.round((v.buffered.end(v.buffered.length - 1) / v.duration) * 100))
  }

  return (
    <div className="min-h-screen bg-black flex flex-col">
      {/* Top bar */}
      <header className="flex items-center gap-4 px-6 h-14 bg-[#0e0e0e]/80 backdrop-blur-xl flex-shrink-0 z-10">
        <button
          onClick={() => navigate(-1)}
          className="flex items-center justify-center w-10 h-10 rounded-full hover:bg-white/10 transition-colors active:scale-95"
        >
          <span className="material-symbols-outlined text-[#f9dcd4]">arrow_back</span>
        </button>
        {meta && (
          <div>
            <h1 className="text-sm font-bold text-[#f9dcd4]" style={{ fontFamily: 'Epilogue, sans-serif' }}>
              {meta.title}
            </h1>
            <p className="text-[10px] uppercase tracking-widest text-[#e3bfb3]" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
              {meta.runtimeMinutes ? `${meta.runtimeMinutes}m` : 'Now Playing'}
            </p>
          </div>
        )}
      </header>

      {/* Video */}
      {version?.notice && <div className="px-6 py-3 text-xs text-[#e3bfb3] bg-[#2b1c17]">{version.notice} {(version.removeSex || version.muteLanguage) && <FilterAttribution notice={version.notice} muteLanguage={version.muteLanguage} />}</div>}
      <div className="flex-1 flex items-center justify-center bg-black relative">
        {failure && <div role="alert" className="p-6 text-white text-center"><p>{failure}</p><button className="m-3 underline" onClick={retry}>Retry</button><button className="m-3 underline" onClick={() => navigate(-1)}>Back</button></div>}
        {!meta && !failure && <p className="text-white">Loading video details...</p>}
        {streamUrl && (
          <video
            key={`${id}-${versionId ?? 'episode'}`}
            ref={videoRef}
            src={streamUrl}
            controls
            onLoadedMetadata={resumeOnce}
            onCanPlay={() => { setPlayerReady(true); setHasPlayed(true) }}
            onError={() => setError("Playback failed. The file may be unavailable or unsupported. Retry or return to the library.")}
            onWaiting={() => setPlayerReady(false)}
            onProgress={handleProgress}
            className="w-full max-h-[calc(100vh-56px)]"
          />
        )}

        {/* Buffer loading overlay — solid bg so it covers the blank video frame */}
        {streamUrl && !playerReady && (
          <div className="absolute inset-0 flex flex-col items-center justify-center bg-black">
            {meta?.backdropPath && (
              <img
                src={meta.backdropPath}
                alt=""
                className="absolute inset-0 w-full h-full object-cover opacity-20 blur-2xl scale-110"
              />
            )}
            <div className="relative z-10 flex flex-col items-center gap-5 px-8 w-full max-w-sm">
              {meta?.title && (
                <h2 className="text-xl font-black text-[#f9dcd4] text-center tracking-tight" style={{ fontFamily: 'Epilogue, sans-serif' }}>
                  {meta.title}
                </h2>
              )}
              <div className="w-full flex flex-col gap-2">
                <div className="w-full h-1 bg-white/10 rounded-full overflow-hidden">
                  <div
                    className="h-full bg-[#ff9069] rounded-full transition-all duration-500"
                    style={{ width: `${bufferPct}%` }}
                  />
                </div>
                <p className="text-[#e3bfb3] text-xs text-right tabular-nums" style={{ fontFamily: 'Space Grotesk, sans-serif' }}>
                  {hasPlayed ? `Buffering (${bufferPct}% buffered)` : 'Preparing or loading video'}
                </p>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
