#!/bin/bash
# Claude Console Statusline Handler
# Called by Claude Code on every assistant message with live session state
# macOS/Linux only — Windows uses %TEMP% via C# plugin

STATE_FILE="/tmp/claude-console-state.json"

# Read JSON from stdin and save as latest state (atomic: write tmp, then mv)
cat > "${STATE_FILE}.tmp.$$" && mv "${STATE_FILE}.tmp.$$" "$STATE_FILE"
