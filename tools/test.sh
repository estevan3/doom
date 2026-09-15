#!/usr/bin/env bash
# Doom Clone — headless test runner.
#
# Runs the EditMode and PlayMode suites via Unity batchmode and writes machine-readable
# results under <project>/TestResults/.
#
# Usage:
#   UNITY_BIN=/path/to/Unity ./tools/test.sh            # both suites
#   UNITY_BIN=/path/to/Unity ./tools/test.sh editonly   # EditMode only
#   UNITY_BIN=/path/to/Unity ./tools/test.sh playonly   # PlayMode only
#
# Notes:
#   * This script targets CI/headless machines. Do NOT run it while the Unity Editor is
#     open on the same project (the project is locked and the runs will conflict).
#   * ScreenshotCapture renders the camera, so do NOT pass -nographics; on a headless
#     Linux box run under a virtual display (e.g. xvfb-run).
#   * The Unity binary can also be configured via the UNITY_BIN environment variable.
set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY_BIN="${UNITY_BIN:-/home/estevan/Unity/Hub/Editor/6000.6.0f1/Editor/Unity}"
RESULTS_DIR="${PROJECT_DIR}/TestResults"

if [ ! -x "${UNITY_BIN}" ]; then
  echo "ERROR: Unity binary not found at '${UNITY_BIN}'. Set UNITY_BIN." >&2
  exit 2
fi

mkdir -p "${RESULTS_DIR}"

run_suite() {
  local mode="$1"
  echo "==> Running ${mode} tests"
  "${UNITY_BIN}" \
    -batchmode \
    -projectPath "${PROJECT_DIR}" \
    -runTests \
    -testPlatform "${mode}" \
    -testResults "${RESULTS_DIR}/${mode}-results.xml" \
    -logFile "${RESULTS_DIR}/${mode}.log"

  echo "==> ${mode} log: ${RESULTS_DIR}/${mode}.log"
  summarize "${RESULTS_DIR}/${mode}-results.xml" "${mode}"
}

summarize() {
  local results_xml="$1"
  local mode="$2"
  if [ ! -f "${results_xml}" ]; then
    echo "==> ${mode}: no results file produced (tests likely failed to run)."
    return 1
  fi

  echo "==> ${mode} results: ${results_xml}"
  grep -oE '(total|passed|failed|inconclusive|skipped)="[0-9]+"' "${results_xml}" \
    | sort -u || true
}

MODE="${1:-all}"

case "${MODE}" in
  all)
    run_suite EditMode
    run_suite PlayMode
    ;;
  editonly)
    run_suite EditMode
    ;;
  playonly)
    run_suite PlayMode
    ;;
  *)
    echo "ERROR: unknown mode '${MODE}' (use all|editonly|playonly)" >&2
    exit 2
    ;;
esac

echo "==> Done. Results under ${RESULTS_DIR}"