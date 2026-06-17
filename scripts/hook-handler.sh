#!/bin/bash
# Claude Console Hook Handler
# Called by Claude Code on every hook event (PreToolUse, PostToolUse, etc.)
# This is the REAL handler that would run in production.
# macOS/Linux only — Windows uses %TEMP% via C# plugin

PENDING_FILE="/tmp/claude-console-pending.json"
RESPONSE_FILE="/tmp/claude-console-response.json"
EVENTS_FILE="/tmp/claude-console-events.log"
STATE_FILE="/tmp/claude-console-state.json"
CONFIG_FILE="/tmp/claude-console-config.json"
HISTORY_FILE="/tmp/claude-console-history.jsonl"

# Read JSON event from stdin
INPUT=$(cat)

# Extract event type and session info
EVENT=$(echo "$INPUT" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('hook_event_name','unknown'))" 2>/dev/null || echo "unknown")
TOOL=$(echo "$INPUT" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('tool_name','n/a'))" 2>/dev/null || echo "n/a")
SESSION_ID=$(echo "$INPUT" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('session_id','default'))" 2>/dev/null || echo "default")
TIMESTAMP=$(date '+%Y-%m-%dT%H:%M:%S')

# Session-scoped file paths
if [ "$SESSION_ID" != "default" ]; then
  PENDING_FILE="/tmp/claude-console-pending-${SESSION_ID}.json"
  RESPONSE_FILE="/tmp/claude-console-response-${SESSION_ID}.json"
fi

# Log the event (atomic write via temp+mv)
LOG_LINE="[$TIMESTAMP] EVENT=$EVENT TOOL=$TOOL SESSION=$SESSION_ID"
echo "$LOG_LINE" >> "$EVENTS_FILE"

# Build structured history entry for dial navigation (PreToolUse and PostToolUse only)
if [ "$EVENT" = "PreToolUse" ] || [ "$EVENT" = "PostToolUse" ]; then
  HISTORY_ENTRY=$(echo "$INPUT" | python3 -c "
import sys, json, datetime
d = json.load(sys.stdin)
ti = d.get('tool_input', {})
tool = d.get('tool_name', '')
session = d.get('session_id', 'default')
event = d.get('hook_event_name', 'unknown')
# Pick the most relevant field for each tool type
summary = tool
if ti.get('command'): summary = ti['command'][:80]
elif ti.get('file_path'): summary = ti['file_path'].split('/')[-1]
elif ti.get('pattern'): summary = ti['pattern'][:60]
elif ti.get('query'): summary = ti['query'][:60]
elif ti.get('url'): summary = ti['url'][:60]
elif ti.get('prompt'): summary = ti['prompt'][:60]
elif ti.get('description'): summary = ti['description'][:60]
elif ti.get('skill'): summary = ti['skill']
elif ti.get('old_string'): summary = 'edit: ' + ti.get('file_path','?').split('/')[-1]
elif ti.get('content'): summary = 'write: ' + ti.get('file_path','?').split('/')[-1]
entry = {'ts': '$TIMESTAMP', 'event': event, 'tool': tool, 'summary': summary, 'session': session}
print(json.dumps(entry))
" 2>/dev/null)
  if [ -n "$HISTORY_ENTRY" ]; then
    echo "$HISTORY_ENTRY" >> "$HISTORY_FILE"
  fi
fi

# For PreToolUse events, we need to wait for Accept/Reject from hardware
if [ "$EVENT" = "PreToolUse" ]; then
  # Read-only / safe tools: auto-allow immediately without blocking the hardware UI.
  # These are informational — no risk of side effects. Prevents confusing brief flashes.
  SAFE_TOOLS="Read|Glob|Grep|WebSearch|WebFetch|Skill|TodoRead|TaskList|TaskGet|TaskCreate|TaskUpdate|TaskOutput|ListMcpResourcesTool|ReadMcpResourceTool|ExitPlanMode|AskUserQuestion|EnterPlanMode|EnterWorktree|mcp__claude-console__get_console_status|mcp__claude-console__get_dial_history"
  if echo "$TOOL" | grep -qE "^($SAFE_TOOLS)$"; then
    echo "{\"hookSpecificOutput\": {\"permissionDecision\": \"allow\"}}"
    exit 0
  fi

  # Write pending event for the plugin to read (atomic: write tmp, then mv)
  echo "$INPUT" > "${PENDING_FILE}.tmp.$$" && mv "${PENDING_FILE}.tmp.$$" "$PENDING_FILE"

  # Determine timeout from config (default 30 seconds)
  TIMEOUT_SECS=30
  if [ -f "$CONFIG_FILE" ]; then
    CONFIGURED_SECS=$(python3 -c "import sys,json; d=json.load(open('$CONFIG_FILE')); v=d.get('timeout_seconds',30); print(int(v))" 2>/dev/null)
    if [ -n "$CONFIGURED_SECS" ] && [ "$CONFIGURED_SECS" -gt 0 ] 2>/dev/null; then
      TIMEOUT_SECS=$CONFIGURED_SECS
    fi
  fi
  TIMEOUT=$((TIMEOUT_SECS * 10))  # iterations at 100ms

  # Write waiting status to state file so LCD keys can flash (include timeout for UI countdown)
  WAITING_JSON="{\"status\": \"waiting_approval\", \"tool\": \"$TOOL\", \"session_id\": \"$SESSION_ID\", \"timestamp\": \"$TIMESTAMP\", \"timeout_seconds\": $TIMEOUT_SECS}"
  echo "$WAITING_JSON" > "${STATE_FILE}.tmp.$$" && mv "${STATE_FILE}.tmp.$$" "$STATE_FILE"

  # Remove any stale response
  rm -f "$RESPONSE_FILE"

  # Poll for response from hardware (plugin writes this file when user presses Accept/Reject)
  ELAPSED=0
  while [ $ELAPSED -lt $TIMEOUT ]; do
    if [ -f "$RESPONSE_FILE" ]; then
      # Read the internal IPC decision from hardware plugin
      DECISION=$(cat "$RESPONSE_FILE")
      rm -f "$RESPONSE_FILE"
      rm -f "$PENDING_FILE"

      # Translate internal IPC format to Claude Code hook protocol
      # Internal: {"decision": "allow"} → Claude Code: {"hookSpecificOutput": {"permissionDecision": "allow"}}
      PERM=$(echo "$DECISION" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('decision','allow'))" 2>/dev/null || echo "allow")
      echo "{\"hookSpecificOutput\": {\"permissionDecision\": \"$PERM\"}}"
      exit 0
    fi
    sleep 0.1
    ELAPSED=$((ELAPSED + 1))
  done

  # Timeout — no response from hardware
  # Default to DENY for security (safe-fail). Can be overridden via config.
  TIMEOUT_ACTION="deny"
  if [ -f "$CONFIG_FILE" ]; then
    CONFIGURED_ACTION=$(python3 -c "import sys,json; d=json.load(open('$CONFIG_FILE')); print(d.get('timeout_action','deny'))" 2>/dev/null)
    if [ "$CONFIGURED_ACTION" = "allow" ]; then
      TIMEOUT_ACTION="allow"
    fi
  fi

  rm -f "$PENDING_FILE"
  # Output Claude Code hook protocol format for timeout
  echo "{\"hookSpecificOutput\": {\"permissionDecision\": \"$TIMEOUT_ACTION\"}, \"reason\": \"hardware timeout - no response received\"}"
  exit 0
fi

# For other events (PostToolUse, Notification, Stop), just log — no blocking
