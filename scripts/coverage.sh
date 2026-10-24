#!/usr/bin/env bash
# Run unit and integration coverage, merge the reports, and enforce the ratchet.
# Integration coverage requires SQL Server on localhost:1433.
set -euo pipefail

root="${EMPLYO_COVERAGE_DIR:-TestResults/coverage}"
rm -rf "$root"

dotnet test tests/Emplyo.UnitTests \
  --collect:"XPlat Code Coverage" \
  --results-directory "$root/unit"

dotnet test tests/Emplyo.IntegrationTests \
  --collect:"XPlat Code Coverage" \
  --results-directory "$root/integration"

python3 scripts/coverage_gate.py "$root"
