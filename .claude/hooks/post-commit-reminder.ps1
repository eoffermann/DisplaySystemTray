# PostToolUse hook: after any shell command that ran `git commit`, remind the
# agent to push and run the post-commit review pipeline (see CLAUDE.md).
try {
    $payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
} catch {
    exit 0
}

$cmd = $payload.tool_input.command
if (-not $cmd) { exit 0 }

if ($cmd -match 'git\b[^|;&]*\bcommit\b') {
    $msg = 'A git commit just landed. Per CLAUDE.md workflow: (1) push it to GitHub, (2) run the post-commit-review skill (.claude/skills/post-commit-review) which launches the ci-watchdog, security-analyst, stability-reviewer, and best-practices-reviewer agents in parallel, (3) file GitHub issues for anything they find, and (4) make addressing open review-filed issues the first order of business for the next coding step.'
    $out = @{
        hookSpecificOutput = @{
            hookEventName     = 'PostToolUse'
            additionalContext = $msg
        }
    } | ConvertTo-Json -Compress -Depth 4
    Write-Output $out
}
exit 0
