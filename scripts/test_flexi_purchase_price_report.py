#!/usr/bin/env python3
"""Unit tests for flexi-purchase-price-report.py's pure sort key.

Only `distance()` is exercised. Importing the module needs the FLEXI_* env vars (read at
module scope) but performs no network I/O by itself: the live calls live in `get()` /
`stock_prices()` / `main()`, none of which this test calls. Run with:
    python3 -m unittest scripts.test_flexi_purchase_price_report -v
(run from the repo root; scripts/ has no __init__.py, so use importlib as below if invoked
directly with `python3 scripts/test_flexi_purchase_price_report.py`).
"""
import importlib.util
import os
import pathlib
import unittest

os.environ.setdefault("FLEXI_SERVER", "https://example.invalid")
os.environ.setdefault("FLEXI_COMPANY", "test")
os.environ.setdefault("FLEXI_LOGIN", "test")
os.environ.setdefault("FLEXI_PASSWORD", "test")

_MODULE_PATH = pathlib.Path(__file__).parent / "flexi-purchase-price-report.py"
_spec = importlib.util.spec_from_file_location("flexi_purchase_price_report", _MODULE_PATH)
report = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(report)


class DistanceTests(unittest.TestCase):
    def test_zero_nakupCena_write_row_sorts_first(self):
        # A row with nakupCena == 0 and action "write" is the most-wrong row on the sheet
        # (Flexi has no purchase price at all for a material with real stock value), so it
        # must sort first (distance() returns float("inf")), not last.
        row = {"nakupCena": 0, "nakupCena/prumCena": 0.0, "action": "write"}

        self.assertEqual(report.distance(row), float("inf"))

    def test_normal_write_row_uses_log_ratio(self):
        row = {"nakupCena": 3.0, "nakupCena/prumCena": 9.8, "action": "write"}

        self.assertAlmostEqual(report.distance(row), abs(__import__("math").log(9.8)))

    def test_skip_no_stock_price_row_sorts_last(self):
        row = {"nakupCena": 5.0, "nakupCena/prumCena": "", "action": "skip-no-stock-price"}

        self.assertEqual(report.distance(row), -1)

    def test_unchanged_row_uses_log_ratio_of_one(self):
        row = {"nakupCena": 3.0, "nakupCena/prumCena": 1.0, "action": "unchanged"}

        self.assertEqual(report.distance(row), 0)


if __name__ == "__main__":
    unittest.main()
