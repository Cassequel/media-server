import { useEffect, useRef, useState, useCallback } from 'react'
import { useParams, useNavigate, useLocation } from 'react-router-dom'
import api from '../services/api'

export default function PlayerPage() {
  const { id } = useParams()
  const { pathname } = useLocation()
  const navigate = useNavigate()
  const isMovie = pathname.startsWith('/watch/movie')
  const videoRef = useRef(null)
  const saveRef = useRef(null)
  const [meta, setMeta] = useState(null)
  const [token, setToken] = useState('')
  const [bufferPct, setBufferPct] = useState(0)
  const [playerReady, setPlayerReady] = useState(false)

  useEffect(() => {
    const t = localStorage.getItem('token') ?? ''
    setToken(t)
    if (isMovie) {
      api.get(`/movies/${id}`).then(({ data }) => setMeta(data)).catch(() => {})
    }
  }, [id, isMovie])

  const saveProgress = useCallback(async () => {
    const video = videoRef.current
    if (!video || video.duration === 0) return
    const progressSeconds = Math.floor(video.currentTime)
    const completed = video.currentTime / video.duration > 0.9
    if (isMovie) {
      await api.post(`/movies/${id}/progress`, { progressSeconds, completed }).catch(() => {})
    } else {
      await api.post(`/tvshows/episodes/${id}/progress`, { progressSeconds, completed }).catch(() => {})
    }
  }, [id, isMovie])

  useEffect(() => {
    const video = videoRef.current
    if (!video) return

    const onLoaded = () => {
      if (meta?.progressSeconds > 10) video.currentTime = meta.progressSeconds
    }
    video.addEventListener('loadedmetadata', onLoaded)

    saveRef.current = setInterval(saveProgress, 10000)

    return () => {
      video.removeEventListener('loadedmetadata', onLoaded)
      clearInterval(saveRef.current)
      saveProgress()
    }
  }, [meta, saveProgress])

  const streamUrl = token
    ? `/api/stream/${isMovie ? 'movie' : 'episode'}/${id}?token=${encodeURIComponent(token)}`
    : ''

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
      <div className="flex-1 flex items-center justify-center bg-black relative">
        {streamUrl && (
          <video
            ref={videoRef}
            src={streamUrl}
            controls
            onLoadedMetadata={e => { e.target.muted = false; e.target.volume = 1 }}
            onCanPlay={() => setPlayerReady(true)}
            onWaiting={() => setPlayerReady(false)}
            onProgress={handleProgress}
            className="w-full max-h-[calc(100vh-56px)]"
            onEnded={saveProgress}
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
                  {bufferPct > 0 ? `${bufferPct}%` : 'Loading…'}
                </p>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
