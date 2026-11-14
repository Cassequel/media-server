"""Read-only status for the explicitly approved Oppenheimer pilot. No secrets printed."""
import json
import subprocess
import urllib.request

def run(*args):
    return subprocess.check_output(args, text=True).strip()

info = json.loads(run('docker','inspect','rockflix-api-1'))[0]
env = dict(v.split('=',1) for v in info['Config']['Env'])
query = '''SELECT json_build_object('id',"Id",'status',"Status",'message',"Message",'sourceCleaned',"SourceCleaned",'versionId',"MovieVersionId",'updatedAt',"UpdatedAt",'radarrId',"RadarrMovieId") FROM "MovieFilterJobs" WHERE "Id" = 'a9fc7e18-b045-4498-80d3-b587a8fc33a3';'''
job = json.loads(run('docker','exec','rockflix-db-1','psql','-U','postgres','-d','rockflix','-Atc',query))
print(json.dumps(job))
request = urllib.request.Request(env['Radarr__BaseUrl']+'/api/v3/queue?page=1&pageSize=100&includeUnknownMovieItems=true',headers={'X-Api-Key':env['Radarr__ApiKey']})
with urllib.request.urlopen(request,timeout=15) as response:
    queue = json.load(response)
for entry in queue.get('records',[]):
    if entry.get('movieId') == job['radarrId']:
        print(json.dumps({k:entry.get(k) for k in ['status','trackedDownloadState','trackedDownloadStatus','size','sizeleft','timeleft','statusMessages']}))
