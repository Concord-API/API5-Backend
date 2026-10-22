#!/usr/bin/env bash
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/../qa-checklist.sh"
checklist="$here/../../qa-checklist.md"
heading="## QA da história"
failures=0

fail() {
  echo "FAIL: $1"
  failures=$((failures + 1))
}

assert_eq() {
  if [[ "$2" != "$3" ]]; then
    fail "$1 (expected '$2', got '$3')"
  fi
}

assert_contains() {
  if ! grep -qF -- "$2" <<<"$3"; then
    fail "$1 (missing '$2')"
  fi
}

assert_not_contains() {
  if grep -qF -- "$2" <<<"$3"; then
    fail "$1 (unexpected '$2')"
  fi
}

run() {
  local body="$1"
  shift
  output="$(printf '%s' "$body" | env "$@" bash "$script" 2>&1)"
  status=$?
}

if [[ ! -f "$checklist" ]]; then
  fail "the checklist file exists"
fi

run "" BASE=main HEAD_REF=us3
assert_eq "a US branch into main exits cleanly" 0 "$status"
assert_contains "a US branch into main receives the checklist" "$heading" "$output"
assert_contains "the checklist asks for the exploratory session" "Teste exploratório" "$output"
assert_not_contains "the checklist is a self-review without the PO acceptance" "Aceite do PO" "$output"
assert_not_contains "the checklist does not ask for someone else to test" "não implementou" "$output"

run "" BASE=us3 HEAD_REF=3.4-Build-the-result-item
assert_eq "a task into a US branch exits cleanly" 0 "$status"
assert_eq "a task into a US branch stays empty" "" "$output"

run "" BASE=main HEAD_REF=us0
assert_eq "the Technical Foundation into main stays empty" "" "$output"

run "" BASE=main HEAD_REF=sync-us4-with-main
assert_eq "a branch that is not a US into main stays empty" "" "$output"

existing="$(printf '%s' "" | BASE=main HEAD_REF=us3 bash "$script" 2>/dev/null)"
run "$existing" BASE=main HEAD_REF=us3
assert_eq "a body that already has the checklist is not duplicated" 1 "$(grep -cF -- "$heading" <<<"$output")"

run "Entrega da US-03" BASE=main HEAD_REF=us3
assert_eq "a written body is kept first" "Entrega da US-03" "$(head -n 1 <<<"$output")"
assert_contains "a written body still receives the checklist" "$heading" "$output"

if ((failures > 0)); then
  echo "$failures failure(s)"
  exit 1
fi
echo "all QA checklist tests passed"
