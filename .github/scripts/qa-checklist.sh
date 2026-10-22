#!/usr/bin/env bash
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
checklist="$here/../qa-checklist.md"
heading="$(head -n 1 "$checklist")"

body="$(cat)"

if [[ "${BASE-}" != "main" || ! "${HEAD_REF-}" =~ ^us[1-9][0-9]*$ ]] || grep -qF -- "$heading" <<<"$body"; then
  printf '%s' "$body"
  exit 0
fi

if [[ -n "$body" ]]; then
  printf '%s\n\n' "$body"
fi
cat "$checklist"
