#!/usr/bin/env python3
"""Execute actual unittest cases and preserve their measured native outcomes."""
import argparse
import json
from pathlib import Path
import sys
import time
import unittest


class Recorder(unittest.TextTestResult):
    def __init__(self,*args,**kwargs):super().__init__(*args,**kwargs);self.records=[];self.started={}
    def startTest(self,test):self.started[test.id()]=time.monotonic();super().startTest(test)
    def record(self,test,status):
        self.records.append({'case_id':test.id(),'status':status,'duration_seconds':time.monotonic()-self.started[test.id()]})
    def addSuccess(self,test):super().addSuccess(test);self.record(test,'passed')
    def addError(self,test,err):super().addError(test,err);self.record(test,'failed')
    def addFailure(self,test,err):super().addFailure(test,err);self.record(test,'failed')
    def addSkip(self,test,reason):super().addSkip(test,reason);self.record(test,'skipped')
    def addExpectedFailure(self,test,err):super().addExpectedFailure(test,err);self.record(test,'failed')
    def addUnexpectedSuccess(self,test):super().addUnexpectedSuccess(test);self.record(test,'failed')


def main():
    ap=argparse.ArgumentParser();ap.add_argument('--pattern',required=True);ap.add_argument('--json',type=Path,required=True);args=ap.parse_args()
    if Path(args.pattern).name!=args.pattern or not args.pattern.startswith('test_'):ap.error('only a local test filename pattern is accepted')
    if args.json.exists():ap.error('native output already exists')
    suite=unittest.defaultTestLoader.discover(str(Path(__file__).parent),pattern=args.pattern)
    result=unittest.TextTestRunner(verbosity=2,resultclass=Recorder).run(suite)
    failed=len(result.failures)+len(result.errors)+len(result.expectedFailures)+len(result.unexpectedSuccesses)
    skipped=len(result.skipped);passed=result.wasSuccessful() and result.testsRun>0 and failed==0 and skipped==0 and len(result.records)==result.testsRun
    data={'schema_version':1,'framework':'python_unittest','status':'passed' if passed else 'failed','executed_count':result.testsRun,
      'failed_count':failed,'required_skipped_count':skipped,'case_records':result.records}
    args.json.parent.mkdir(parents=True,exist_ok=True)
    with args.json.open('x') as f:json.dump(data,f,indent=2);f.write('\n')
    return 0 if passed else 1


if __name__=='__main__':raise SystemExit(main())
