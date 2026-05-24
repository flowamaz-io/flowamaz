#!/bin/bash
# clear-after-prompt.sh
# Signals token hygiene after every prompt completion
# PM Agent calls this after logging result to results.md
# /clear is executed by Claude Code after this script runs

PROMPT_ID=${1:-"unknown"}
PROJECT_NAME=${2:-"Project"}

echo ""
echo "--------------------------------------------------"
echo "  PROMPT COMPLETE: ${PROMPT_ID}"
echo "  Project: ${PROJECT_NAME}"
echo "  Result logged to results.md"
echo "  Checkpoint updated"
echo "  Executing /clear for token hygiene..."
echo "--------------------------------------------------"
echo ""
