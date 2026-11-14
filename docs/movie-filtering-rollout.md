# Movie filtering: implementation and rollout

The feature is deployed and enabled with user approval as of 2026-09-20. The initial Oppenheimer assessment found no usable DoesTheDogDie intervals. The user subsequently approved an allowlisted CleanStream/VideoSkip pilot, which supplied three community intervals and entered acquisition; see [the community pilot record](community-timestamp-pilot.md) for its current status. The configuration default remains `ROCKFLIX_FILTERING_ENABLED=false` for other deployments. The initial rollout record below is historical.

## Implemented behavior

- Independent sex/nudity and strong-language options in web movie confirmation, with exact-version reuse and an explicit additional-version notice.
- A server-side assessment with a 115-second provider deadline and 120-second browser timeout. API failures and missing intervals do not silently fall back to original.
- Current provider mappings: nude scenes (279), sexual content (197), on-screen sexual assault (292); language uses slur topics 251, 272, 247, 314. These mappings were checked against the live topic catalog. That catalog does not expose comprehensive profanity timing, so language coverage is explicitly described as partial.
- All provider-derived edits are labeled unverified for the local edition. No paid Scene Alerts access or “rock-solid” coverage is assumed.
- PostgreSQL-backed jobs, one shared encoder with playback conversions, exact filter-combination tracking, per-version playback/progress, and subtitle retiming/text masking.
- Radarr retains one title identity. Each acquisition gets its own staging directory. Previously stored versions remain at their existing locations; the new path assignment uses `moveFiles=false`.
- Only the selected output is published. The imported source reference is removed after validation and publication. Radarr/qBittorrent retain torrent data until existing seeding/completed-download rules permit its removal. Both completed handling and removal flags were confirmed enabled on production Radarr on 2026-09-20; seeding completion itself is not controlled by Rockflix.
- Original requests from linked Telegram users use the version-aware job flow. Legacy SMS and unlinked Telegram requests can still acquire new ordinary titles and detect existing originals; titles already enrolled in variant management direct those users to web confirmation to protect acquisition ownership and quota attribution.
- If a sole requester explicitly switches to original after a filtering check fails, before anything is published, the intact staged source can satisfy that changed request. This still produces one output. An additional version alongside an existing/shared filtered version requires its own acquisition.
- Unsupported primary audio languages stop language processing. Unsupported image subtitles are omitted with a notice; burned-in text cannot be filtered separately. These limitations remain visible on stored versions.

## Recovery and storage details

- Temporary source and output can coexist during processing. A downloaded original is also copied through a temporary publication file to recover safely from a crash; it is not retained as a second library version.
- Pending downloads reserve 8 GiB each initially; worker checks actual source/output size against user quota after download and before publication. Reservations are estimates, not a download-client bandwidth/size cap.
- Media work requires the configured media root to be mounted. A 10 GiB media free-space floor and additional job reservations are checked before work starts. Playback cache is capped separately and never evicts durable versions.
- Failed processing retains its source while a decision is pending, for up to seven days. Cancelled/expired jobs clean their known processing/import files after a 24-hour grace period; torrent data still follows downloader retention rules. Cancelling a shared request transfers its quota reservation only if the remaining owner has capacity.
- If a crash occurs during an external download search and its outcome cannot be established, the job requests administrator reconciliation rather than automatically issuing another download. Routine import polling recovers missed webhooks.
- Once all sources are removed, undoing edits requires another download. Disabling the feature stops new managed requests and the worker; already-published versions remain playable with the new application.

## Required deployment preparation

1. Back up PostgreSQL, Radarr configuration, and current Compose/environment configuration. Keep copies of current application images. Do not use a schema-only test export as a production backup.
2. Build the API and frontend candidate images under distinct tags, without replacing running containers. Frontend build requires Node 22 for current Vite; use the updated Dockerfile.
3. Confirm `/mnt/media` is mounted and writable from the API. Prepare these directories on that disk:

   - `/mnt/media/Movies/.rockflix-acquisition`: shared with Radarr, group ID 1000, set-group-ID mode `2775`. Radarr's configured PUID/PGID are 1000/1000. The API creates descendant acquisition directories with group-write/set-group-ID permissions.
   - `/mnt/media/Rockflix/Movies`: durable outputs, writable by the API.
   - `/mnt/media/Rockflix/.processing`: temporary files, writable by the API.
   - `/mnt/media/Rockflix/.stream-cache`: bounded playback cache, writable by the API.

4. Set `ROCKFLIX_CACHE_PATH=/mnt/media/Rockflix/.stream-cache`. Existing files in the old `rockflix_stream_cache` volume are not moved or deleted by deployment; decide their cleanup separately. Keep `ROCKFLIX_CACHE_MAX_BYTES=8589934592` initially.
5. The `.env` key has been normalized to `DOESTHEDOGDIE_API_KEY` without exposing its value, and `.env` is now ignored by Git. Keep the key server-side. Generate a separate `ROCKFLIX_WEBHOOK_SECRET` and configure Radarr's webhook to send it as `X-Rockflix-Webhook-Key`. Import polling is also available if the webhook is unavailable.
6. Apply the additive `20260920002520_MovieFilteringVersions` migration during the application rollout. It creates original-version rows and migrates existing movie watch progress. It does not move or edit existing media files. The existing app runs migrations on startup; account for that when starting the candidate API.
7. Deploy with filtering disabled first. Verify existing movie/TV playback, original-version labels, favorites, resume progress, and requests. Then enable `ROCKFLIX_FILTERING_ENABLED=true` for the movie pilot.
8. Pilot one explicit request through the confirmation/warning flow. Verify download association, actual file language/edition, progress states, the produced output, and source retention. Inspect storage before and after seeding cleanup. No real movie acquisition was initiated during implementation tests.

## Tests and tools

The self-contained test project is `tests/Rockflix.Filtering.Tests`. Build it with .NET SDK 10. Run its output in a .NET 10 environment with FFmpeg/FFprobe, passing `--media` for synthetic media checks. `--radarr` exercises a separately configured empty Radarr instance, without indexers or download clients.

Database tests require a disposable PostgreSQL database initialized from a schema-only production export and its migration history. Set `ROCKFLIX_TEST_CONNECTION` for that test database only. The tests intentionally insert fixtures and apply migrations; never point this variable at production.

Validation covers merged/invalid intervals, unrelated provider topics, strong/mild subtitle words, subtitle retiming, frame removal, audio-only independence, mute alignment after earlier cuts, unknown audio language rejection, migration backfill/history, version reuse, best-effort acceptance, quota deduplication, and real Radarr path reassignment preserving earlier files.

### Validation results — 2026-09-20

- Final synthetic-media/disposable-PostgreSQL run: **41 checks passed**, including explicit original selection reusing an unpublished staged source without creating a second output or acquisition.
- Isolated real Radarr run: **6 additional checks passed** (15 total including 9 shared checks). The instance had no indexers/download clients; no real movie was downloaded.
- Isolated authenticated API HTTP run: **7 additional checks passed** (16 total including 9 shared checks), including version-specific resume history, range/seek playback, unknown-version rejection, and the disabled rollout flag.
- API/test compilation, frontend production build, migration model consistency, targeted frontend lint, Compose validation, and patch whitespace checks passed. Existing scanner nullable and Home effect-dependency warnings were observed during full builds/lint; no new blocking errors remain.
- Review images built as `rockflix-api:filtering-review` and `rockflix-client:filtering-review`. The API candidate uses the existing offline runtime-base Dockerfile, including FFmpeg; neither running production container nor its image tag was replaced.
- Disposable test containers/database/network were removed after validation. Ignored test fixtures/build assets remain in `artifacts/` and `.build-tools/` for reproducibility. Existing production media and database were not modified by these tests.
- Final storage check: boot disk 59% used (about 11 GiB available); media disk 47% used (about 101 GiB available).

This is implementation validation, not a completed production movie pilot. The explicit rollout/pilot above remains necessary before offering the feature broadly.

## Production rollout record — 2026-09-20

The user approved deployment and selected **Oppenheimer** for the pilot. Sex/nudity filtering was selected; language filtering was left off, as communicated before assessment.

- Backups are under `/mnt/media/Movies/.rockflix-backups/filtering-20260920/` (mode `0700`): a full PostgreSQL custom-format dump, native consistent Radarr ZIP, pre-rollout `.env`, and deployment Compose configuration. The dump archive listing was verified. The Compose backup includes the optional image selectors added during preparation; their unset defaults resolve to the previous application image names.
- Previous running images remain tagged `rockflix-api:pre-filtering-20260920` and `rockflix-client:pre-filtering-20260920`.
- Deployed images: `rockflix-api:filtering-20260920` and `rockflix-client:filtering-review`. Only the API/client containers were replaced/restarted; the database and download services were not restarted.
- The media mount root was not writable from the container. Rather than changing its ownership, deployment uses existing writable space on the same disk: `/mnt/media/Movies/.rockflix-library`, `.rockflix-processing`, `.rockflix-stream-cache`, and `.rockflix-acquisition`. Hidden paths prevent the scanner from treating work files or not-yet-published outputs as originals. The acquisition directory has group 1000 and mode `2775`; both applications' write access was checked. Environment overrides pin these paths.
- Startup applied `20260920002520_MovieFilteringVersions`. All six existing movies acquired original-version rows; six history rows were retained and no movie history was left without its version association. Existing movie files were not moved or edited.
- Filtering was initially disabled for live checks, then enabled. The existing Radarr webhook now sends a separately generated secret header, and its connection test passed. An unsigned test event was rejected with HTTP 401 after enablement. No seeding settings changed.
- Live checks passed for the HTML shell, feature flag, all six movie details/version mappings, the one-show TV library/detail, favorites, continue-watching, quota/request endpoints, and movie HTTP range playback. TV range playback was **not exercised** because all existing episode files require conversion; the rollout checks intentionally avoided initiating a full episode conversion. No interactive-browser visual review was performed.
- The live provider all-topic response returned exactly 200 entries and omitted a relevant entry found by querying its topic directly. The integration now queries all seven relevant topics separately when the all-topic result reaches 200, within the existing 115-second deadline/rate limiter. Two new regression checks cover this fallback and avoiding extra requests for smaller responses; the final no-database synthetic-media suite passed **23 checks**. This fix required no additional migration.
- Oppenheimer (TMDb 872585; provider item 1031479) returned **zero usable intervals** for both categories. Its individual nudity and sexual-content topics had entries, but no structured start timestamps. The complete live reassessment, including the response-limit fallback, took **16.93 seconds**. No job, quota reservation, additional download, or source cleanup was initiated; its existing original remains available.
- The pilot is **blocked on timestamp coverage**, not completed. Choose another movie or approve work on an additional timestamp source to continue a real acquisition/editing pilot. No subscription purchase or provider correspondence was made.
- Boot disk remains **59% used**, about **11 GiB free**; media disk remains **47% used**, about **101 GiB free**. The old stream-cache volume was retained untouched; new playback-cache writes go to the media disk.

Read-only production checks can be repeated with `python3 tests/deployment_smoke.py smoke enabled` and `python3 tests/deployment_smoke.py assess 872585` from the host with Docker access. The script creates only a short-lived in-memory administrator token and does not print credentials. Its explicit `backup-radarr` and `configure-webhook` commands perform administrative writes and should be used only as part of an approved rollout.

Provider endpoint/field semantics: [DoesTheDogDie API v3 documentation](https://www.doesthedogdie.com/api/3.0). The unavailable result above was verified against the authenticated live API, not inferred from title-level content warnings.

## Rollback

First disable filtering and let or stop the worker as appropriate; retain the new version-aware application for playback. Restore previous application images/database only from the coordinated pre-rollout backup. Do not automatically downgrade the schema after multiple version histories or new outputs have been created: the old app cannot represent those versions. Source cleanup is not reversed by database rollback.
