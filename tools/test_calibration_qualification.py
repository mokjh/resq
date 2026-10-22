"""Expose isolated calibration mutation tests to the native unittest recorder."""
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parent / "art"))
import test_qualify_calibration


def load_tests(loader, tests, pattern):
    return loader.loadTestsFromModule(test_qualify_calibration)


if __name__ == "__main__":
    unittest.main()
