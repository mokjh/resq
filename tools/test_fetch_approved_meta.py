"""Archive retention guard fixtures; these are not SDK installation evidence."""
import base64
import hashlib
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import fetch_approved_meta as fetch


class FetchTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='resq-archive-guard-')
        self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)
        self.data=b'SYNTHETIC ARCHIVE GUARD FIXTURE ONLY'
        packages=[];rows=[]
        for i in range(4):
            name='com.meta.synthetic'+str(i)
            dist={'tarball':'https://npm.developer.oculus.com/'+name+'/-/fixture.tgz',
                  'shasum':hashlib.sha1(self.data).hexdigest(),
                  'integrity':'sha512-'+base64.b64encode(hashlib.sha512(self.data).digest()).decode()}
            packages.append({'name':name,'version':'207.0.1','published_dist':dist})
            rows.append(dict(name=name,url=dist['tarball'],sha1=dist['shasum'],integrity=dist['integrity'],compressed_bytes_read=len(self.data)))
        candidate=self.root/'candidate.json';candidate.write_text(json.dumps({'packages':packages}))
        measured=self.root/'measurement.json';measured.write_text(json.dumps({'archives':rows}))
        for key,value in {'ROOT':self.root,'CANDIDATE':candidate,'MEASURED':measured,'ARCHIVES':self.root/'evidence/archives','owned':lambda p:Path(p)}.items():
            change=patch.object(fetch,key,value);change.start();self.addCleanup(change.stop)
        change=patch.object(fetch,'preflight',return_value={'required_free_bytes':100,'memory_pressure_level':1,'volumes':[{'free_bytes':100}]})
        change.start();self.addCleanup(change.stop)
        self.packages=packages;self.rows=rows

    def test_capacity_denial_never_opens_network_or_archive_directory(self):
        with patch.object(fetch.urllib.request,'urlopen') as network:
            self.assertEqual(fetch.fetch(self.root/'evidence/run'),75)
            network.assert_not_called()
        self.assertFalse(fetch.ARCHIVES.exists())
        self.assertEqual(json.loads((self.root/'evidence/run/outcome.json').read_text())['status'],'blocked')

    def test_bad_existing_bytes_never_overwritten(self):
        fetch.ARCHIVES.mkdir(parents=True)
        path=fetch.ARCHIVES/(self.packages[0]['name']+'-207.0.1.tgz');path.write_bytes(b'changed')
        with patch.object(fetch.urllib.request,'urlopen') as network:
            with self.assertRaisesRegex(ValueError,'bytes mismatch'):fetch.fetch(self.root/'evidence/run')
            network.assert_not_called()
        self.assertEqual(path.read_bytes(),b'changed')

    def test_exact_digest_checks_length_and_both_published_hashes(self):
        path=self.root/'fixture';path.write_bytes(self.data)
        self.assertTrue(fetch.check_archive(path,self.packages[0],self.rows[0])['verified'])
        path.write_bytes(self.data+b'x')
        with self.assertRaises(ValueError):fetch.check_archive(path,self.packages[0],self.rows[0])

    def test_midstream_capacity_drop_preserves_partial_and_never_installs(self):
        response=io.BytesIO(self.data);response.geturl=lambda:self.rows[0]['url']
        with patch.object(fetch,'preflight',return_value={'required_free_bytes':100,'memory_pressure_level':1,'volumes':[{'free_bytes':100000000}]}),patch.object(fetch.urllib.request,'urlopen',return_value=response),patch.object(fetch.shutil,'disk_usage',return_value=type('Disk',(),{'free':0})()):
            with self.assertRaisesRegex(RuntimeError,'native-stage reserve'):fetch.fetch(self.root/'evidence/run')
        self.assertTrue((self.root/'evidence/run'/('com.meta.synthetic0.part')).exists())
        self.assertEqual(list(fetch.ARCHIVES.iterdir()),[])


if __name__=='__main__':unittest.main()
