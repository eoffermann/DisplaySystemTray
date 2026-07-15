---
name: post-commit-review
description: Run the automated review pipeline after a commit lands - launches the ci-watchdog, security-analyst, stability-reviewer, and best-practices-reviewer agents in parallel; their findings become labeled GitHub issues that the next coding step must address. Use after every push to the repository.
---

# Post-commit review pipeline

Run this after every push to GitHub (the post-commit hook reminds you). It fans out the four review agents, collects their findings as GitHub issues, and queues those issues for the next coding step.

## Preconditions

- The commit(s) to review are already pushed (CI runs on GitHub, so the ci-watchdog needs the push).
- If multiple commits were pushed at once, one pipeline run covers the whole push — do not run it per-commit.

## Procedure

1. **Launch all four agents in parallel** (single message, four Agent tool calls), using the project agent types:
   - `ci-watchdog` — prompt it with the pushed commit SHA(s); it waits for the GitHub Actions run and files `ci`-labeled issues for failures and warnings.
   - `security-analyst` — prompt it with the pushed commit SHA(s) to review.
   - `stability-reviewer` — prompt it with the pushed commit SHA(s) to review.
   - `best-practices-reviewer` — prompt it with the pushed commit SHA(s) to review.

2. **Collect results.** Each agent returns a report of issues filed (or "no findings"). Summarize the combined outcome for the user: total issues filed with numbers, titles, and labels.

3. **Queue the fixes.** Open issues labeled `automated-review` are the first order of business for the next coding step:
   - Before starting the next milestone task, run `gh issue list --state open --label automated-review`.
   - Fix each issue in its own commit with a message body containing `Fixes #<n>` so the push closes the issue and cross-references the commit.
   - If an issue is judged not worth fixing, close it with a comment explaining why (`gh issue close <n> --comment "..."`) — never silently ignore it.

4. **Do not recurse.** The fix commits from step 3 get reviewed by the *next* pipeline run after they are pushed; do not launch a new pipeline from within this one.

## Notes

- If `gh run list` shows CI still in progress, the ci-watchdog handles the waiting — launch it anyway.
- Trivial pushes that change no code or config (e.g. README typo) still get the ci-watchdog, but the three code reviewers may be skipped at your discretion.
