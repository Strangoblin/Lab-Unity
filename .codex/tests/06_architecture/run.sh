#!/bin/bash
# Shared-library ownership, plain routes and architecture-source controls.
set -euo pipefail
cd "$(dirname "$0")"
PYTHONDONTWRITEBYTECODE=1 python3 test_scan_scope.py
PYTHONDONTWRITEBYTECODE=1 python3 test_ownership.py
PYTHONDONTWRITEBYTECODE=1 python3 verify.py
