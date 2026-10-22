"""Read approved public archives as bounded streams; never install or extract."""
import base64
import hashlib
import io
import json
from pathlib import Path, PurePosixPath
import shutil
import tarfile
import urllib.request

ROOT=Path(__file__).resolve().parents[1]
BASE=ROOT/'evidence/resource-reassessment-20261016/meta-archive-measurement'


class DigestStream(io.RawIOBase):
    def __init__(self,response):
        self.response=response
        self.sha1=hashlib.sha1()
        self.sha512=hashlib.sha512()
        self.count=0
    def readable(self):return True
    def read(self,size=-1):
        if shutil.disk_usage(ROOT).free<2*1024**3:
            raise RuntimeError('Read-only measurement stopped: volume below2GiB reserve')
        data=self.response.read(min(size if size>=0 else 1024**2,1024**2))
        self.sha1.update(data);self.sha512.update(data);self.count+=len(data)
        return data


def main():
    candidate=json.loads((ROOT/'evidence/toolchain/preparation/20261015-222738Z-registry-resolution/candidate-dependency-set.json').read_text())
    packages=[p for p in candidate['packages'] if p['name'].startswith('com.meta.')]
    if len(packages)!=4 or any(p['version']!='207.0.1' for p in packages):
        raise RuntimeError('Exact approved candidate set required')
    if (BASE/'summary.json').exists():raise FileExistsError('Completed measurement is immutable')
    BASE.mkdir(parents=True,exist_ok=True)
    results=[]
    for package in packages:
        dist=package['published_dist'];url=dist['tarball']
        if not url.startswith('https://npm.developer.oculus.com/'+package['name']+'/-/'):
            raise ValueError('Official exact approved archive URL required')
        retained=BASE/(package['name']+'.json')
        if retained.exists():
            row=json.loads(retained.read_text())
            if (row.get('name')!=package['name'] or row.get('version')!=package['version']
                or row.get('sha1')!=dist['shasum'] or row.get('integrity')!=dist['integrity']
                or row.get('published_integrity_verified') is not True
                or row.get('archive_body_saved') is not False or row.get('archive_extracted') is not False):
                raise ValueError('Prior measurement does not bind the exact approved package')
            results.append(row)
            print(json.dumps({'retained_verified_measurement':package['name'],
                              'sha256':hashlib.sha256(retained.read_bytes()).hexdigest()}),flush=True)
            continue
        payload=0;largest=0;regular=0;links=[];metadata=None
        with urllib.request.urlopen(url,timeout=30) as response:
            source=DigestStream(response)
            with tarfile.open(fileobj=source,mode='r|gz') as archive:
                for member in archive:
                    name=PurePosixPath(member.name)
                    if name.is_absolute() or '..' in name.parts:
                        raise ValueError('Unsafe archive member: '+member.name)
                    if member.isfile():
                        payload+=member.size;largest=max(largest,member.size);regular+=1
                    if member.issym() or member.islnk():links.append({'path':member.name,'target':member.linkname})
                    if member.name=='package/package.json':
                        if member.size>65536:raise ValueError('Unexpected oversized package metadata')
                        metadata=json.loads(archive.extractfile(member).read())
            while source.read(1024**2):pass
        sha512='sha512-'+base64.b64encode(source.sha512.digest()).decode()
        if source.sha1.hexdigest()!=dist['shasum'] or sha512!=dist['integrity']:
            raise ValueError('Published archive digest mismatch: '+package['name'])
        if metadata is None or metadata['name']!=package['name'] or metadata['version']!=package['version']:
            raise ValueError('Native archive metadata identity mismatch')
        row={'name':package['name'],'version':package['version'],'url':url,
             'compressed_bytes_read':source.count,'regular_payload_bytes':payload,
             'regular_files':regular,'largest_regular_file_bytes':largest,'link_entries':links,
             'sha1':source.sha1.hexdigest(),'integrity':sha512,'published_integrity_verified':True,
             'package_dependencies':metadata.get('dependencies',{}),
             'archive_body_saved':False,'archive_extracted':False,'accepted_task':False}
        with retained.open('x') as stream:stream.write(json.dumps(row,indent=2)+'\n')
        results.append(row)
        print(json.dumps({k:row[k] for k in ('name','compressed_bytes_read','regular_payload_bytes','largest_regular_file_bytes')}),flush=True)
    report={'accepted_task':False,'installed':False,'archives':results,
            'total_compressed_bytes_read':sum(r['compressed_bytes_read'] for r in results),
            'total_regular_payload_bytes':sum(r['regular_payload_bytes'] for r in results),
            'limits':['Streaming input measurements only; no UPM resolution/import/build peak or package qualification inferred.']}
    (BASE/'summary.json').write_text(json.dumps(report,indent=2)+'\n')


if __name__=='__main__':main()
