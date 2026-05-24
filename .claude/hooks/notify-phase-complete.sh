#!/bin/bash
# notify-phase-complete.sh
# Fires when PM Agent completes a phase and phase-report.md is ready
# macOS only — uses osascript for native notification

PHASE_NUMBER=${1:-"Unknown"}
PROJECT_NAME=${2:-"Project"}
REPORT_PATH=${3:-"phase-report.md"}

# Native macOS notification
osascript -e "display notification \"Phase ${PHASE_NUMBER} complete. Upload phase-report.md to ClaudeCode Foundation chat for review.\" with title \"${PROJECT_NAME} — Claude Code\" subtitle \"Phase ${PHASE_NUMBER} Ready for Review\" sound name \"Glass\""

# Also log to terminal for visibility
echo ""
echo "=================================================="
echo "  PHASE ${PHASE_NUMBER} COMPLETE — ${PROJECT_NAME}"
echo "=================================================="
echo "  Phase report ready: ${REPORT_PATH}"
echo "  Next step: Upload to ClaudeCode Foundation chat"
echo "=================================================="
echo ""
