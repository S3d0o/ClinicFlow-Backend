#!/usr/bin/env bash
# PostToolUse hook: after an edit to a controller, DTO or error catalog, remind Claude
# that the API contract docs must change in the same change (CLAUDE.md, "Endpoints").
# Reads the hook payload on stdin; matches only the file_path field, not file content.
payload=$(cat)
if printf '%s' "$payload" | grep -qE '"file_path" *: *"[^"]*(Presentation[\\/]+Controllers|Shared[\\/]+(DTOs|Errors))[\\/]'; then
  cat <<'EOF'
{"hookSpecificOutput":{"hookEventName":"PostToolUse","additionalContext":"Contract reminder: this file is part of the API contract shared with the frontend. If the edit changes a route, request/response field, status code, error code or auth rule, update docs/API_CONTRACT.md (endpoint section, error catalog, data types, §10 changelog, version) and regenerate docs/openapi.json in the same change (see /contract-change and /regen-openapi). Pure refactors with no observable change need nothing."}}
EOF
fi
exit 0
