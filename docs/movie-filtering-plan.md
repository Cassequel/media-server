# Rockflix movie filtering — implementation plan

Status: approved scope implemented, validated, and deployed with user approval. Filtering is enabled. The initially blocked Oppenheimer pilot is now trying an explicitly approved, title-allowlisted community source; see community-timestamp-pilot.md and movie-filtering-rollout.md for current status and limitations.
Date: 2026-09-20.

## 1. Agreed product behavior

- Offer two independent options at movie download confirmation: **Remove sex and nudity** and **Mute strong language**.
- Sex filtering skips nudity, sexual activity, and pornographic images/scenes within the movie. Keep ordinary kissing, romance, and mild innuendo.
- Language filtering mutes strong profanity and slurs; keep mild swearing. Muting silences the full audio mix during that interval.
- Do not filter violence.
- Produce only the requested version. Do not automatically retain an original or generate alternative filter combinations.
- If somebody explicitly requests another combination, treat that as another download request. Reuse a ready copy of the exact requested combination when it already exists.
- No mandatory human review or admin scene-editing workflow.
- Check available timestamp quality quickly: target seconds for cached responses and cap external lookup at two minutes. This checks evidence, not the entire movie, and does not include download or encoding time.
- For uncertain but usable timestamps, explain the limitations and offer **Try filtering anyway** or **Download original**. Record that choice.
- For a selected category with no usable intervals and no trustworthy evidence that no edits are needed, mark it unavailable. Do not create an unchanged movie labeled filtered.
- Do not silently switch to original playback or silently omit a requested filter.

## 2. Proposed defaults and scope for approval

- First release: movies requested through the web interface. Both filter checkboxes initially unchecked, making selection explicit.
- Telegram and SMS retain their current original-download behavior and use the same version-aware duplicate checks. Filtering requests through those channels are a later extension; they must never return a filtered copy as an original.
- Initial audio support: the primary English track. Do not publish an unfiltered alternate audio track when language filtering is selected. Show other audio languages as unsupported until implemented.
- Support text subtitles by removing excluded cues, retiming cues after cuts, and masking the selected strong-language text. Omit unsupported image subtitles with a clear notice. Burned-in subtitles require different processing; disclose this limitation rather than claiming their text has been filtered.
- Existing library files become original-version records without being moved, edited, or redownloaded during the database migration.
- New durable movie versions are owned by Rockflix. Generated versions are library assets, not disposable playback cache entries.
- One title card lists the versions actually present, with clear labels. Watch progress belongs to the selected version because scene cuts change its timeline.
- No automatic content detection, full-film transcription, HLS migration, or frame-by-frame visual censorship in this release. The provider interface and edit format will allow those later without redesigning the download flow.

## 3. User flow

1. User enters a movie request; Rockflix resolves the title/year and TMDb ID.
2. Confirmation shows the two filter checkboxes and any existing versions.
3. If the exact requested version is ready, offer playback without a new download or quota charge. If it is already processing, attach the request to that job.
4. Otherwise assess available timestamps for each selected filter. Original requests skip the provider lookup.
5. Display one of: **Higher-confidence timestamps available**, **Limited/unverified timestamps**, **No usable timestamps**, or **Source temporarily unavailable**. Never describe automated screening as a guarantee that all unwanted content will be removed.
6. Limited data requires the user's explicit “Try filtering anyway” choice. With no usable data, allow the user to remove the unsupported option, choose original, or cancel. A timeout offers retry/original/cancel, rather than pretending the movie lacks data.
7. If another version exists, explicitly state that this selection adds a separate version and consumes additional storage/quota.
8. Download into staging, verify the actual media file, prepare edits, process, validate, then publish. Display **Downloading → Checking file → Filtering → Validating → Ready**, with failed/waiting-for-space states as needed.
9. If post-download inspection reveals a changed confidence level, unsupported track, or edition mismatch outside the accepted scope, ask again before proceeding. Do not publish the staged original automatically.
10. Retain best-effort notices on the resulting version so later viewers also see them.

## 4. Data model and duplicate detection

Separate the movie identity from the stored files:

- `Movie`: shared title, TMDb ID, artwork, and metadata.
- `MovieVersion`: filter flags, selected audio language/track, edition/source fingerprint, output path, measured runtime, bytes, timestamp-source details, confidence, and processing-policy revision.
- `FilterAssessment` / `EditPlan`: source IDs, fetch time, category-specific coverage, validated original-timeline intervals, and diagnostics.
- `ProcessingJob`: durable state, owning request, download correlation, chosen options, accepted uncertainty, retries, staging/output paths, and cleanup state.
- Update request records, playback APIs, history, library scanner, and quota accounting to reference the appropriate version/job.

Use movie ID + filter combination + supported language/edition selection for logical duplicate checks. Use the exact source fingerprint and edit-plan revision for processing reuse. An edition we cannot identify is not a verified match.

Serialize acquisition per title and use a unique active-job identity to prevent duplicate downloads from simultaneous confirmations. Do not treat every Radarr HTTP 400 as “already downloaded.”

Deduplicate metadata records carefully during migration, preserving existing file IDs/history relationships. Keep favorites at movie level and watch progress at version level. Never infer a completed filtered request merely because the movie title exists in the library.

## 5. DoesTheDogDie integration and confidence

- Read the API key server-side from environment configuration; normalize the nonstandard `.env` entry if still needed. Ensure `.env` is ignored by Git and never expose the key in frontend code or logs.
- Use API v3 with `X-API-KEY`; match by TMDb/IMDb ID and verify title/type/year. Fetch relevant topics and ratings only.
- Map provider topic IDs explicitly to sex/nudity and strong language. A broad violence or content-warning vote must not become an edit interval.
- Interpret community and professional records separately; account for missing/sentinel timestamps, duplicates, conflicts, partial category coverage, and absent safe-resume times.
- Store normalized, source-attributed edit intervals with positive duration. Reject impossible or inverted ranges and require a usable endpoint. Never guess the end of a scene from a yes/no rating or vague comment.
- Higher confidence requires evidence about both source quality and file/edition alignment. Vote count, matching runtime, or a professional flag alone does not prove complete coverage. The initial free-tier integration will often report limited data.
- Cache targeted lookups and provider topic mappings within permitted use, share results between requests, and use bounded retries/rate limiting. Account for the key's tested limits: 30 requests/minute and 5,000/month.
- Provider failures affect new assessments; ready local versions remain playable.
- Include the required linked attribution where provider data appears: “Powered by DoesTheDogDie.com.”
- Scene Alerts are an optional separately licensed dependency, not an assumed capability of the current key. Do not use provider data to train or evaluate detection models under the standard terms.

## 6. Download orchestration and ownership

The current code delegates movie addition directly to Radarr, while both the scanner and import webhook can mark a title completed before filtering exists. Replace those shortcuts with the durable job pipeline.

Proposed design: keep Radarr as the acquisition service and let Rockflix own completed versions in a separate directory outside Radarr's managed movie folders. Establish a dedicated acquisition/staging root, mapped consistently into the API and Radarr containers.

- Resolve an existing Radarr title before adding it; request acquisition through its supported API rather than adding another identical title record.
- For a title already managed in the current library, preserve the original file and its Rockflix reference. When enrolling that title in the new acquisition flow, redirect its Radarr acquisition path without moving/deleting the existing version, refresh its file state, and perform a controlled new search. Validate this behavior against the installed Radarr version during the first implementation milestone.
- Monitor/search only as needed for the queued acquisition; avoid automatic replacement downloads after Rockflix moves the completed asset out of staging. Preserve unrelated Radarr behavior.
- Correlate imports by request/job, movie ID, download ID, and actual file path. Authenticate the relevant webhook and reject paths outside configured roots. Repeated webhook delivery must not process or charge twice.
- Keep staging and filtered outputs out of generic automatic library scanning until publication. Background reconciliation recovers missed webhooks/restarts.
- A differently filtered existing movie must not be used as a substitute source: removed content cannot be restored. The agreed workflow requires an explicit additional acquisition for another combination.

This is the first technical milestone because it determines whether the single-version storage promise works with the current downloader configuration.

## 7. FFmpeg processing

- Run a restart-resilient worker backed by PostgreSQL jobs, with one encoder at a time initially. HTTP requests enqueue work and return status instead of waiting for a whole movie to encode.
- Probe source duration, streams, language, and file identity. Matching runtime is only a plausibility check, not proof of aligned scenes.
- Normalize/merge scene-cut ranges. Skip takes precedence over muting when intervals overlap. Maintain an original-to-output timeline map.
- Apply muting against original timestamps before cuts, or transform intervals through the same timeline map, so preceding cuts cannot shift later mutes incorrectly.
- For language-only edits, copy compatible video and re-encode audio. For exact scene removal, use a tested trim/concatenate encoding pipeline; do not assume stream-copy cuts are frame-exact.
- When both filters are selected, produce one final output in one processing pass rather than two full intermediate renditions.
- Produce a seekable browser-compatible MP4 to avoid a second full playback conversion. Handle subtitle timing/text alongside the video and audio.
- Validate full output decoding, stream presence, duration against the edit plan, and cut/mute boundaries before publishing through an atomic move and database state transition. These checks verify processing, not semantic completeness.
- Delete the processing source only after output validation and durable publication. Track source deletion as a retryable cleanup task. Failed jobs retain a bounded retry source and report an actionable status.
- Once all source copies are cleaned up, cuts cannot be undone locally. Restoring original content or rebuilding from a corrected plan requires another acquisition, consistent with the one-retained-output policy.
- An empty plan with uncertain coverage must not be marked successfully filtered. Stop and offer the original decision again.

## 8. Storage, seeding, and quotas

- Put staging, completed versions, and bounded playback caches on `/mnt/media`, not the container boot disk.
- Reserve estimated input/output/temporary space before acquisition or processing, with a configurable free-space floor (proposed starting floor: 10 GiB on the media disk). Account for concurrent downloads as well as the encoder.
- Cache eviction must never delete durable movie versions. Explicitly budget pending and abandoned temporary files; clean only job-owned files with known lifecycle states.
- Do not edit a torrent source or hardlinked imported file in place. Write a new output, publish it, then remove the appropriate source reference safely.
- The torrent client may still retain the unfiltered download while seeding. Proposed policy: preserve existing seeding requirements, then remove the job-associated completed source through supported cleanup. Report remaining source bytes honestly; deleting the library link alone may free no space.
- If immediate removal of all source bytes is required, that needs a different seeding policy. This plan does not silently change ratio/time requirements or delete unrelated torrents.
- Keep the existing 20 GB per-user limit, count an additional requested version against its requester, and reserve quota for pending requests. Exact-version reuse adds no new storage charge. Shared jobs have one designated quota owner; retries/webhooks do not charge again.
- After successful cleanup, charge the durable version's actual size; keep physical staging/seeding usage visible separately for disk-capacity enforcement.

## 9. Implementation sequence and verification

1. **Acquisition proof and migration design:** inspect installed Radarr/qBittorrent behavior, validate per-title reacquisition and staging ownership with isolated fixtures, finalize schema migration and source cleanup. Preserve existing uncommitted repository changes.
2. **Version-aware library:** add schema/API support and migrate existing originals. Update scanner, requests, progress, duplicate detection, quotas, and non-web callers.
3. **Provider assessment and confirmation UI:** implement the two independent options, API integration, attribution, two-minute lookup cap, per-category availability, and explicit best-effort acceptance.
4. **Worker and media processing:** implement durable jobs, webhooks/reconciliation, deterministic edits, output validation, safe publication, and source cleanup.
5. **Playback and storage integration:** serve exact versions, show measured runtime and limitations, configure media-disk paths, and avoid duplicate playback encodes.
6. **End-to-end validation and rollout:** use synthetic media with known frames/audio for automated boundary tests and an isolated movie acquisition pilot for integration behavior. Back up database/configuration and gate the feature for controlled enablement.

Acceptance checks:

- All four filter combinations resolve correctly; identical concurrent requests share one job.
- Sex filtering leaves violence untouched; language remains independent.
- Missing, partial, malformed, conflicting, timed-out, and edition-mismatched data produce the correct user decisions.
- Cuts/mutes remain synchronized across multiple edits, overlapping intervals, seeks, and subtitles.
- A crash, full disk, failed encoder, duplicate webhook, or invalid output cannot publish a broken version or delete the only usable source.
- A new version never overwrites a previously requested version; a second version requires explicit acquisition.
- Source cleanup handles hardlinks/seeding correctly and reports bytes still retained.
- Existing playback, favorites, TV requests, Telegram/SMS requests, and quotas continue to work.

No automated test or two-minute lookup can establish that all undesired content has been identified. Best-effort labeling is part of the feature, not a temporary testing limitation.

## 10. Approval boundary

The planning phase changed only this document. Approved implementation and isolated tests are complete. The user separately approved production deployment; its targets, backups, live checks, and blocked Oppenheimer pilot are recorded in movie-filtering-rollout.md. Existing library files and the old cache volume remain untouched. No API subscription purchase or external correspondence is included.

The main proposed defaults to approve or change are: web/movie-first scope, initially unchecked filters, primary-English audio support, provider timestamps without an AI fallback, and retaining download sources only as long as existing seeding requirements demand.

## References

- [DoesTheDogDie API v3: authentication, lookup, ratings, access tiers, attribution](https://www.doesthedogdie.com/api/3.0)
- [DoesTheDogDie API terms: data limitations and separately licensed access](https://www.doesthedogdie.com/api/terms)
- [Radarr API](https://radarr.video/docs/api/)
- [Servarr Radarr guide: importing and retained seeding sources](https://github.com/Servarr/Wiki/blob/master/radarr/quick-start-guide.md)
- [FFmpeg filters: trim, audio trim, concatenation, and volume](https://ffmpeg.org/ffmpeg-filters.html)
