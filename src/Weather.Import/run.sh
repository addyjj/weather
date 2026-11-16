#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "$0")"
# ZIP uploads made on Windows may not preserve the executable bit.
chmod +x ./Weather.Import
exec ./Weather.Import "$@"
