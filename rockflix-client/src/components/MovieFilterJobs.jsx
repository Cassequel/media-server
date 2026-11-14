import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import api from '../services/api'
import { versionLabel } from '../services/movieVersions'
import FilterAttribution from './FilterAttribution'

export default function MovieFilterJobs() {
  const [jobs, setJobs] = useState([])
  const [error, setError] = useState('')
  useEffect(() => {
    let active = true
    const refresh = () => api.get('/movie-filtering/jobs').then(({ data }) => { if (active) setJobs(data) }).catch(() => {})
    refresh()
    const timer = setInterval(refresh, 10000)
    return () => { active = false; clearInterval(timer) }
  }, [])
  async function action(id, choice) {
    setError('')
    try {
      await api.post(`/movie-filtering/jobs/${id}/${choice}`)
      setJobs((await api.get('/movie-filtering/jobs')).data)
    } catch (err) { setError(err.response?.data?.message ?? 'Could not update this request.') }
  }
  if (!jobs.length) return null
  return <section className="px-8 md:px-12 py-6 text-[#e3bfb3]" aria-label="Movie request progress">
    <h2 className="font-bold text-lg mb-4">Your movie requests</h2>
    {error && <p role="alert">{error}</p>}
    <div className="grid gap-3 md:grid-cols-2">{jobs.slice(0, 10).map(j => <div className="bg-[#2b1c17] p-4 rounded-xl text-sm" key={j.id}>
      <p className="font-bold">{j.resolvedTitle} · {versionLabel(j)}</p>
      <p className="mt-2" aria-live="polite">{j.message}</p>
      {j.notice && <p className="mt-2 text-xs">{j.notice}</p>}
      {(j.removeSex || j.muteLanguage) && <FilterAttribution notice={j.notice} muteLanguage={j.muteLanguage} />}
      {j.status === 'ready' && <Link className="underline block mt-3" to={`/watch/movie/${j.movieId}?version=${j.movieVersionId}`}>Watch this version</Link>}
      {j.status === 'needs_decision' && <div className="flex gap-4 mt-3">
        <button className="underline" onClick={() => action(j.id, 'retry')}>Retry processing</button>
        <button className="underline" onClick={() => action(j.id, 'original')}>Request original instead</button>
      </div>}
    </div>)}</div>
  </section>
}
