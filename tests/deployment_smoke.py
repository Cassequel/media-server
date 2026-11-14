"""Operational rollout checks. Never prints tokens or API keys; no user-data writes.

Smoke uses a five-minute, in-memory token for the existing active administrator.
The backup/webhook and pilot subcommands are explicit production operations.
"""
import base64
import hashlib
import hmac
import json
import subprocess
import sys
import time
import urllib.request


def run(*args):
    return subprocess.check_output(args, text=True).strip()


info = json.loads(run('docker', 'inspect', 'rockflix-api-1'))[0]
env = dict(v.split('=', 1) for v in info['Config']['Env'])


def radarr(path, data=None, method=None):
    request = urllib.request.Request(env['Radarr__BaseUrl'] + '/api/v3/' + path,
        data=None if data is None else json.dumps(data).encode(), method=method,
        headers={'X-Api-Key': env['Radarr__ApiKey'], 'Content-Type': 'application/json'})
    with urllib.request.urlopen(request, timeout=30) as response:
        body = response.read()
        return json.loads(body) if body else None


if sys.argv[1] == 'backup-radarr':
    command = radarr('command', {'name': 'Backup'})
    for _ in range(30):
        result = radarr('command/' + str(command['id']))
        if result['status'] == 'completed':
            backups = radarr('system/backup')
            latest = max(backups, key=lambda b: b['time'])
            folder = latest['path']
            if not folder.startswith('/backup/') or '..' in folder.split('/'):
                raise RuntimeError('Unexpected backup path')
            print('/config/Backups/' + folder.removeprefix('/backup/'))
            break
        if result['status'] in ('failed', 'aborted'):
            raise RuntimeError('Radarr native backup failed')
        time.sleep(1)
    else:
        raise RuntimeError('Radarr native backup timed out')
    sys.exit(0)

if sys.argv[1] == 'configure-webhook':
    secret = env.get('Filtering__WebhookSecret')
    if not secret:
        raise RuntimeError('Webhook secret is not configured')
    count = 0
    for notification in radarr('notification'):
        fields = {f['name']: f for f in notification.get('fields', [])}
        if notification['implementation'] != 'Webhook' or '/api/webhooks/radarr' not in fields.get('url', {}).get('value', ''):
            continue
        headers = fields['headers'].get('value') or []
        if not isinstance(headers, list):
            raise RuntimeError('Unexpected webhook header format')
        headers = [h for h in headers if h.get('key', '').lower() != 'x-rockflix-webhook-key']
        headers.append({'key': 'X-Rockflix-Webhook-Key', 'value': secret})
        fields['headers']['value'] = headers
        radarr('notification/' + str(notification['id']), notification, 'PUT')
        radarr('notification/test', notification, 'POST')
        count += 1
    if count != 1:
        raise RuntimeError('Expected exactly one Rockflix Radarr webhook')
    print('PASS: existing Radarr webhook authenticated and connection test passed')
    sys.exit(0)

if sys.argv[1] not in ('smoke', 'assess', 'pilot-oppenheimer'):
    raise RuntimeError('Unknown command')
user_id = run('docker', 'exec', 'rockflix-db-1', 'psql', '-U', 'postgres', '-d', 'rockflix', '-Atc',
    'SELECT "Id" FROM "Users" WHERE "IsAdmin" AND "IsActive" ORDER BY "Id" LIMIT 1;')
if not user_id:
    raise RuntimeError('No active administrator for read-only checks')


def b64(value):
    return base64.urlsafe_b64encode(value).decode().rstrip('=')


header = b64(json.dumps({'alg': 'HS256', 'typ': 'JWT'}).encode())
payload = b64(json.dumps({'sub': user_id, 'iss': env['Jwt__Issuer'], 'aud': env['Jwt__Audience'],
    'exp': int(time.time()) + 300}).encode())
unsigned = header + '.' + payload
token = unsigned + '.' + b64(hmac.new(env['Jwt__Secret'].encode(), unsigned.encode(), hashlib.sha256).digest())


def get(path, ranged=False):
    headers = {'Authorization': 'Bearer ' + token}
    if ranged:
        headers['Range'] = 'bytes=0-99'
    with urllib.request.urlopen(urllib.request.Request('http://127.0.0.1:3000' + path, headers=headers), timeout=60) as response:
        body = response.read(100) if ranged else response.read()
        if ranged:
            assert response.status == 206 and len(body) == 100, 'Range playback failed'
            return None
        return json.loads(body)


if sys.argv[1] == 'pilot-oppenheimer':
    # Explicit user-approved title/options only. The endpoint remains idempotent.
    assessment = get('/api/movie-filtering/872585/assessment')
    assert assessment['sex']['status'] == 'limited' and assessment['sex']['intervalCount'] > 0
    assert 'CleanStream / VideoSkip' in assessment['sex']['notice']
    request = urllib.request.Request('http://127.0.0.1:3000/api/request/confirm',
        data=json.dumps({'title':'Oppenheimer','mediaType':'movie','tmdbId':872585,
            'removeSex':True,'muteLanguage':False,'acceptUnverified':True}).encode(),
        headers={'Authorization':'Bearer '+token,'Content-Type':'application/json'})
    with urllib.request.urlopen(request,timeout=120) as response:
        print(json.dumps(json.load(response),indent=2))
    sys.exit(0)

if sys.argv[1] == 'assess':
    tmdb_id = int(sys.argv[2])
    started = time.monotonic()
    result = get('/api/movie-filtering/' + str(tmdb_id) + '/assessment')
    print(json.dumps(result, indent=2))
    print('Assessment seconds: ' + str(round(time.monotonic() - started, 2)))
    sys.exit(0)


with urllib.request.urlopen('http://127.0.0.1:3000', timeout=15) as response:
    assert response.status == 200 and b'<div id="root"' in response.read()
print('PASS: frontend HTML served')
capabilities = get('/api/movie-filtering/capabilities')
assert capabilities['enabled'] == (sys.argv[2] == 'enabled')
print('PASS: filtering flag ' + str(capabilities['enabled']))
movies = get('/api/movies')
for movie in movies:
    detail = get('/api/movies/' + str(movie['id']))
    assert detail['versions'], 'Existing movie missing original-version mapping'
print('PASS: movie library/detail and original-version mappings (' + str(len(movies)) + ' titles)')
shows = get('/api/tvshows')
for show in shows:
    get('/api/tvshows/' + str(show['id']))
print('PASS: TV library/detail (' + str(len(shows)) + ' shows)')
for path in ['/api/favorites', '/api/watchhistory/continue-watching', '/api/request/usage',
             '/api/request/pending', '/api/movie-filtering/jobs']:
    get(path)
    print('PASS: ' + path)
# Only native MP4 checks: avoid triggering a new expensive playback conversion.
for kind, table in [('movie', 'MovieVersions'), ('episode', 'Episodes')]:
    query = ('SELECT ' + ('"MovieId"||\':\'||"Id"' if kind == 'movie' else '"Id"') +
        ' FROM "' + table + '" WHERE lower("FilePath") LIKE \'%.mp4\' ORDER BY "Id" LIMIT 1;')
    found = run('docker', 'exec', 'rockflix-db-1', 'psql', '-U', 'postgres', '-d', 'rockflix', '-Atc', query)
    if not found:
        print('SKIP: ' + kind + ' range check: no native MP4 available')
        continue
    parts = found.split(':')
    path = '/api/stream/' + kind + '/' + parts[0]
    if kind == 'movie':
        path += '?versionId=' + parts[1]
    get(path, True)
    print('PASS: ' + kind + ' HTTP range playback')
