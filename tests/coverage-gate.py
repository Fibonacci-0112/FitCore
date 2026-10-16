#!/usr/bin/env python3
"""Fails when a package's line coverage in a Cobertura report is below a minimum (NFR-MNT-1).

Usage: coverage-gate.py REPORT PACKAGE MINIMUM_PERCENT
"""
import sys
import xml.etree.ElementTree as ET

report, package, minimum = sys.argv[1], sys.argv[2], float(sys.argv[3])
match = next((p for p in ET.parse(report).getroot().iter("package") if p.get("name") == package), None)
if match is None:
    sys.exit(f"{package} is not in {report}.")

covered = float(match.get("line-rate", "0")) * 100
print(f"{package}: {covered:.1f}% line coverage (minimum {minimum:g}%)")
if covered < minimum:
    sys.exit(f"{package} line coverage is below {minimum:g}%.")
