"""Synthetic executed-case envelope mutations, never Android execution proof."""
from copy import deepcopy
import json
from pathlib import Path
import tempfile
import unittest

from native_build_cases import validate_native_build_cases
from runner_core import ReportError,sha256_file


class NativeCasesTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='resq-native-envelope-');self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name).resolve();self.path=self.root/'native.json'
        self.binding={'build_guid':'synthetic','apk_sha256':'1'*64,'build_report_sha256':'2'*64,'toolchain_lock_sha256':'3'*64}
        self.summary={'schema_version':1,'status':'passed','evidence_kind':'automated','unity_version':'synthetic',
            'build_binding':self.binding,'executed':4,'passed':4,'failed':0,'skipped':0,
            'cases':[{'case_id':f'check-f04/assertion/assertion_id=check-f04-a{i:02}','executed':True,'status':'passed','observations':{'synthetic_fixture_only':True}} for i in range(1,5)]}
        self.build={'build_guid':'synthetic','apk_sha256':'1'*64,'sha256':'2'*64,'unity_version':'synthetic'}
        self.refresh()

    def refresh(self):
        self.path.write_text(json.dumps(self.summary))
        digest=sha256_file(self.path)
        self.packet={'status':self.summary['status'],'build_binding':deepcopy(self.binding),
            'native_summary':{'path':str(self.path),'sha256':digest,'byte_length':self.path.stat().st_size},
            'case_outcomes':[{'case_id':r['case_id'],'status':r['status'],'observations':dict(r['observations'],native_summary_sha256=digest,unity_version='synthetic')} for r in self.summary['cases']]}

    def validate(self):return validate_native_build_cases(self.packet,self.root,self.build)
    def test_exact_executed_envelope(self):self.assertEqual(self.validate()['executed'],4)
    def test_missing_summary_rejected(self):
        self.packet.pop('native_summary')
        with self.assertRaises(ReportError):self.validate()
    def test_stale_hash_rejected(self):
        self.path.write_text('{}')
        with self.assertRaises(ReportError):self.validate()
    def test_duplicate_ids_and_false_counts_rejected(self):
        for mutation in ('duplicate','counts','unexecuted'):
            with self.subTest(mutation=mutation):
                original=deepcopy(self.summary)
                if mutation=='duplicate':self.summary['cases'][1]['case_id']=self.summary['cases'][0]['case_id']
                elif mutation=='counts':self.summary['passed']=True
                else:self.summary['cases'][1]['executed']=False
                self.refresh()
                with self.assertRaises(ReportError):self.validate()
                self.summary=original
    def test_changed_packet_observations_rejected(self):
        self.packet['case_outcomes'][0]['observations']['synthetic_fixture_only']=False
        with self.assertRaises(ReportError):self.validate()
    def test_failed_native_status_cannot_be_upgraded(self):
        self.summary['cases'][0]['status']='failed';self.summary.update(status='failed',passed=3,failed=1);self.refresh()
        self.packet['status']='passed'
        with self.assertRaises(ReportError):self.validate()
    def test_wrong_build_binding_rejected(self):
        self.build['apk_sha256']='4'*64
        with self.assertRaises(ReportError):self.validate()
    def test_linked_or_escaping_summary_rejected(self):
        link=self.root/'link.json';link.symlink_to(self.path);self.packet['native_summary']['path']=str(link)
        with self.assertRaises(ReportError):self.validate()
        self.packet['native_summary']['path']=str(self.root/'../native.json')
        with self.assertRaises(ReportError):self.validate()


if __name__=='__main__':unittest.main()
