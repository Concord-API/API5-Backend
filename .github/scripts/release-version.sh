#!/usr/bin/env bash
set -euo pipefail

labels="${1-}"

release_labels=$(tr ',' '\n' <<< "$labels" | sed 's/^ *//; s/ *$//' | grep -E '^release:' || true)
count=$(grep -c . <<< "$release_labels" || true)

if [ "$count" -ne 1 ]; then
  echo "Exactly one release label is required (release:sprint, release:us, release:fix or release:none); found $count." >&2
  exit 1
fi

bump="${release_labels#release:}"

case "$bump" in
  none)
    echo none
    exit 0
    ;;
  sprint | us | fix) ;;
  *)
    echo "Unknown release label '$release_labels'." >&2
    exit 1
    ;;
esac

latest=$(git tag --list 'v*' --sort=-v:refname | grep -E '^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' | head -n 1 || true)
IFS=. read -r major minor patch <<< "${latest:-v0.0.0}"
major="${major#v}"

case "$bump" in
  sprint) major=$((major + 1)); minor=0; patch=0 ;;
  us) minor=$((minor + 1)); patch=0 ;;
  fix) patch=$((patch + 1)) ;;
esac

echo "v${major}.${minor}.${patch}"
