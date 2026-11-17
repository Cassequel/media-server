import { useEffect, useState } from 'react'
import api from '../services/api'
import { versionLabel } from '../services/movieVersions'
import FilterAttribution from './FilterAttribution'

export default function MovieFilterConfirmation({ movie, onConfirm, busy }) {
  const [enabled, setEnabled] = useState(false)
  const [versions, setVersions] = useState([])
  const [removeSex, setRemoveSex] = useState(false)
  const [muteLanguage, setMuteLanguage] = useState(false)
  const [assessment, setAssessment] = useState(null)
  const [error, setError] = useState('')
  const [accepted, setAccepted] = useState(false)
  const [retry, setRetry] = useState(0)
  const [confirmedExtra, setConfirmedExtra] = useState(false)
  const match = versions.find(v => v.removeSex === removeSex && v.muteLanguage === muteLanguage)
  const selected = removeSex || muteLanguage
  const loading = enabled && selected && !match && !assessment && !error

  useEffect(() => {
    let active = true
    Promise.all([api.get('/movie-filtering/capabilities'), api.get(`/movie-filtering/${movie.tmdbId}/versions`)])
      .then(([caps, existing]) => { if (active) { setEnabled(caps.data.enabled); setVersions(existing.data) } })
      .catch(() => { if (active) setError('Could not check available movie versions. Retry before requesting.') })
    return () => { active = false }
  }, [movie.tmdbId, retry])

  useEffect(() => {
    if (!enabled || !selected || match) return
    const controller = new AbortController()
    api.get(`/movie-filtering/${movie.tmdbId}/assessment`, { signal: controller.signal, timeout: 120000 })
      .then(({ data }) => setAssessment(data))
      .catch(err => { if (!controller.signal.aborted) setError(err.response?.data?.message ?? 'Timestamp lookup unavailable. Retry or choose original.') })
    return () => controller.abort()
  }, [enabled, movie.tmdbId, selected, match, retry])

  function change(setter, value) { setter(value); setAccepted(false); setConfirmedExtra(false); if (error) setRetry(r => r + 1); setError('') }
  const unavailable = assessment && ((removeSex && assessment.sex.status === 'unavailable') || (muteLanguage && assessment.language.status === 'unavailable'))
  const additional = versions.length > 0 && !match
  const disabled = busy || (!match && ((selected && (loading || !assessment || unavailable || !accepted || error)) || (additional && !confirmedExtra)))
  return <div className="space-y-4 mb-5 text-sm text-[#e3bfb3]">
    {enabled && <>
      <label className="flex gap-3 items-start"><input type="checkbox" checked={removeSex} disabled={busy} onChange={e => change(setRemoveSex, e.target.checked)} />
        <span>Remove sex and nudity<small className="block mt-1">Skips available annotated sexual-content scenes. Annotations can omit scenes or mismatch this edition; violence is unchanged.</small></span></label>
      <label className="flex gap-3 items-start"><input type="checkbox" checked={muteLanguage} disabled={busy} onChange={e => change(setMuteLanguage, e.target.checked)} />
        <span>Mute selected timestamped slurs<small className="block mt-1">Selected slurs where timestamps exist, not comprehensive spoken-profanity removal. Subtitle cleanup is separate. Primary English audio only. Mutes all sound during each interval.</small></span></label>
    </>}
    {versions.length > 0 && <p>Already available: {versions.map(versionLabel).join(', ')}.</p>}
    {match && <p>This exact version is available. No additional download is needed.</p>}
    {additional && <label className="flex gap-3"><input type="checkbox" checked={confirmedExtra} onChange={e => setConfirmedExtra(e.target.checked)} />
      <span>I want another version downloaded. It will use additional storage and download quota.</span></label>}
    {selected && !match && <div aria-live="polite" className="rounded-xl p-4 bg-black/20 space-y-3">
      {loading && <p>Checking timestamp sources… This can take up to two minutes.</p>}
      {error && <p role="alert">{error}</p>}
      {assessment && !loading && <>
        {removeSex && <p><strong>Sex/nudity:</strong> {assessment.sex.notice}</p>}
        {muteLanguage && <p><strong>Language:</strong> {assessment.language.notice}</p>}
        {!unavailable && <label className="flex gap-3"><input type="checkbox" checked={accepted} onChange={e => setAccepted(e.target.checked)} />
          <span>I understand the limitations and want to try filtering anyway.</span></label>}
      </>}
      <FilterAttribution notice={removeSex ? assessment?.sex?.notice : ''} muteLanguage={muteLanguage} />
      <div className="flex gap-4">
        <button type="button" className="underline" onClick={() => { setAssessment(null); setError(''); setRetry(r => r + 1) }}>Retry lookup</button>
        <button type="button" className="underline" onClick={() => { setRemoveSex(false); setMuteLanguage(false); setAccepted(false); setConfirmedExtra(false); setError('') }}>Choose original</button>
      </div>
    </div>}
    {error && !selected && <><p role="alert">{error}</p><button className="underline" onClick={() => { setError(''); setRetry(r => r + 1) }}>Retry</button></>}
    <button type="button" disabled={disabled || (!selected && !!error)}
      onClick={() => onConfirm({ removeSex, muteLanguage, acceptUnverified: accepted })}
      className="w-full px-5 py-3 rounded-full bg-[#ff9069] text-[#5c1900] font-bold disabled:opacity-40">
      {busy ? 'Submitting…' : match ? 'Use existing version' : selected ? 'Try filtering and download' : 'Download original'}
    </button>
  </div>
}
