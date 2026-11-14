# Community timestamp pilot — Oppenheimer

User approval: “let's try it”, following the source comparison and explicit best-effort warning. Scope is one additional Oppenheimer version, with sex/nudity filtering on and language filtering off. Existing versions and seeding settings are preserved.

## Source and scope

- CleanStream provides an IMDb-keyed JSON endpoint. TMDb identity is resolved through the official external-ID endpoint, and the returned IMDb ID and media type must match before intervals are accepted.
- This deployment permits only TMDb **872585** through `ROCKFLIX_CLEANSTREAM_PILOT_TMDB_IDS=872585`. Other titles continue using the existing provider. Emptying the allowlist disables the community adapter without affecting published playback.
- The public Oppenheimer record returned three nudity intervals. Those intervals match the openly shared VideoSkip import file in the CleanStream repository. They are community edits, not independently verified timings; the API provided no release/runtime metadata.
- The adapter retains source URL/name/segment ID in the stored edit plan. It does not count CleanStream and its VideoSkip source as two independent endorsements. Source attribution and the edition/completeness warning appear at confirmation, in requests, and on stored-version playback.
- For this pilot, the community source supplies only nudity/high-severity sex cuts. It does not supply language filtering, remove violence, or turn low/medium generic sex labels into cuts. The existing provider remains available for the independent language option and fallback.
- Requests have a 115-second overall assessment deadline; community lookup has a 20-second limit. A bounded 100-title, 12-hour in-memory cache avoids repeated community calls. Invalid identities, malformed/oversized responses, bad ranges and duplicates are rejected or excluded. An outage does not silently produce an original labeled filtered.
- No MCF importer, bulk dataset mirror, external contributions, new subscription, or provider correspondence is part of this pilot.

## Reuse review

The VideoSkip author explicitly describes free sharing of skip files, and this file is publicly shared through its exchange and the CleanStream repository. CleanStream publishes its software under MIT. These observations support evaluating this public file in the requested personal pilot; they are **not a blanket clearance** for commercial distribution or every contributor's data. Broadening routine data reuse should include clarification of upstream contribution/reuse terms, separately from the software license. This integration uses independently written adapter code, not copied player code or screenshots.

Sources:

- [CleanStream project and API](https://github.com/ameen-roayan/stremio-cleanstream)
- [Live Oppenheimer record](https://cleanstream.elfhosted.com/api/filters/tt15398776)
- [Source skip file](https://github.com/ameen-roayan/stremio-cleanstream/blob/main/data/videoskip-imports/805_Oppenheimer_2023.skp)
- [VideoSkip sharing and synchronization instructions](https://videoskip.weebly.com/faq.html)

## Deployment and validation

- API and frontend: `rockflix-api:community-pilot-20260920`, `rockflix-client:community-pilot-20260920`. No new database migration.
- Full isolated media/database suite: **49 checks passed**, including six new community-parser/provenance/fallback/allowlist checks. Frontend targeted lint and production build passed. The existing scanner nullable warning remains.
- Backups: `/mnt/media/Movies/.rockflix-backups/community-20260920/` contains the prior environment/Compose files, full PostgreSQL dump, native Radarr backup, and SHA-256 of the existing Oppenheimer original.
- Live smoke checks passed for library/version mappings, favorites, resume and request endpoints, and movie HTTP range playback. The live community-enabled assessment took **5.1 seconds** and returned three limited-confidence intervals.
- Job: `a9fc7e18-b045-4498-80d3-b587a8fc33a3`, Radarr movie ID 15. The request was submitted through the ordinary confirmation endpoint with explicit `acceptUnverified=true` and no language filtering. Radarr accepted one roughly 2 GB download into the isolated acquisition directory.
- The existing original's SHA-256 was checked after path reassignment and matched. The temporary regression database/container/network were removed.
- **Current pilot state: filtering** (2026-09-20 01:31 UTC). Download and Radarr import succeeded; FFmpeg started applying the three cuts. Initial encoder/API memory use was approximately 543 MiB of the 2 GiB container budget. Publication still requires processing and full decode/duration validation; completeness of the community annotations is not guaranteed by those technical checks. The background worker continues independently of the assistant session, and status appears under “Your movie requests”.
- Imported source: 1920×1080 H.264, primary English AC-3 audio, duration 10,822.176 seconds. The three intervals total 211.33 seconds (3 minutes 31.33 seconds); expected output duration is 10,610.846 seconds. The source has image/PGS subtitles, which this release omits with a stored-version notice rather than pretending to retime them. Language filtering remains off; audio is encoded to browser-compatible AAC as part of the selected output.
- The extra Node image downloaded for validation was removed after the checks. Boot disk returned to approximately 60% used (11 GiB available); media disk was about 50% used (95 GiB available) during processing. Source/output temporarily coexist as documented.

Read-only monitoring: `python3 tests/pilot_status.py`. The explicit `pilot-oppenheimer` operation in `tests/deployment_smoke.py` submits the approved title/options through the idempotent request endpoint; it must not be used to initiate unrelated acquisitions.
