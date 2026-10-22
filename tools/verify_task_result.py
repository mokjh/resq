#!/usr/bin/env python3
"""Validate provided execution evidence without creating or executing it."""
import argparse
from pathlib import Path
from validate_spec_freeze import ROOT,Invalid,read,validate,validate_task_result,validate_assets,validate_release


def main():
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('kind',choices=['task','assets','release']);ap.add_argument('path',type=Path);ap.add_argument('--root',type=Path,default=ROOT)
    args=ap.parse_args()
    try:
        validate(args.root)
        {'task':validate_task_result,'assets':validate_assets,'release':validate_release}[args.kind](args.root,read(args.path))
    except (Invalid,KeyError,TypeError) as exc:print('FAILED: '+str(exc));return 1
    print('Provided '+args.kind+' evidence is internally valid; authenticity still requires the real execution/human record.');return 0


if __name__=='__main__':raise SystemExit(main())
