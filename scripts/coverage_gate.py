#!/usr/bin/env python3
"""Merge Cobertura reports and enforce a minimal coverage ratchet.

Usage:
    python3 scripts/coverage_gate.py [results-directory]

Environment:
    EMPLYO_MIN_LINE_COVERAGE    default 57
    EMPLYO_MIN_BRANCH_COVERAGE  default 63
"""
from __future__ import annotations

import glob
import os
import sys
import xml.etree.ElementTree as ET


def merge(reports: list[str]) -> tuple[dict[tuple[str, str], int], dict[tuple[str, str, str], bool]]:
    lines: dict[tuple[str, str], int] = {}
    branches: dict[tuple[str, str, str], bool] = {}
    for report in reports:
        root = ET.parse(report).getroot()
        for cls in root.findall(".//class"):
            filename = cls.get("filename", "")
            for line in cls.findall("./lines/line"):
                number = line.get("number", "")
                hits = int(line.get("hits", "0"))
                key = (filename, number)
                lines[key] = max(lines.get(key, 0), hits)
                for condition in line.findall("./conditions/condition"):
                    condition_id = condition.get("number") or condition.get("type") or ""
                    coverage = float(str(condition.get("coverage", "0")).rstrip("%"))
                    branch_key = (filename, number, condition_id)
                    branches[branch_key] = branches.get(branch_key, False) or coverage > 0
    return lines, branches


def percentage(covered: int, total: int) -> float:
    return covered / total * 100 if total else 100.0


def main() -> int:
    results_root = sys.argv[1] if len(sys.argv) > 1 else "TestResults/coverage"
    minimum_lines = float(os.environ.get("EMPLYO_MIN_LINE_COVERAGE", "57"))
    minimum_branches = float(os.environ.get("EMPLYO_MIN_BRANCH_COVERAGE", "63"))

    reports = sorted(glob.glob(os.path.join(results_root, "**", "coverage.cobertura.xml"), recursive=True))
    if not reports:
        print(f"No Cobertura reports found under '{results_root}'.", file=sys.stderr)
        return 2

    lines, branches = merge(reports)
    covered_lines = sum(1 for hits in lines.values() if hits > 0)
    covered_branches = sum(1 for covered in branches.values() if covered)
    line_percentage = percentage(covered_lines, len(lines))
    branch_percentage = percentage(covered_branches, len(branches))

    print(f"Merged {len(reports)} Cobertura report(s).")
    print(f"Line coverage:   {covered_lines}/{len(lines)} = {line_percentage:.2f}% (minimum {minimum_lines:.2f}%)")
    print(f"Branch coverage: {covered_branches}/{len(branches)} = {branch_percentage:.2f}% (minimum {minimum_branches:.2f}%)")

    if line_percentage < minimum_lines or branch_percentage < minimum_branches:
        print("Coverage gate failed.", file=sys.stderr)
        return 1

    print("Coverage gate passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
