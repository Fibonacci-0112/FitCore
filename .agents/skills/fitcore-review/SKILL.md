---
name: fitcore-review
description: Review a FitCore diff, branch or pull request against the BRD, the NFRs, the accepted ADRs and the spike rules, using the same checklist as the GitHub Copilot and Claude Code review skills. Use when asked to review changes in this repository, or before declaring a non-trivial change done.
---

# FitCore review for Codex

This skill runs the FitCore review in Codex. The review rules live in one place, the GitHub Copilot skill at [`.github/skills/code-review/SKILL.md`](../../../.github/skills/code-review/SKILL.md), so Copilot, Claude Code and Codex review against the same checklist. This file adds only what differs in Codex: where the project instructions are, how to find the diff, which local checks to run, and how to verify and report findings.

Use this skill instead of Codex's built-in `/review` for this repository: `/review` doesn't know FitCore's business, privacy and architecture rules.

## 1. Load the rules

1. Read [`CLAUDE.md`](../../../CLAUDE.md). It is this repository's agent guide, with the layout, commands, business rules and conventions; there is no `AGENTS.md`.
2. Read [`.github/skills/code-review/SKILL.md`](../../../.github/skills/code-review/SKILL.md) in full before looking at the diff. Its sections are the review:
   - §1 lists the sources of truth.
   - §2 gives the review steps: roadmap phase, file classes, data tracing, the six actors, silent divergence.
   - §3 sets the severities and the report format.
   - §4 holds the checklists for business rules, privacy and audit, database and RLS, API identity, auth and MFA, clients, domain tests, documentation, spikes and CI.
   - §5 says what not to flag.

If this file and the Copilot skill ever disagree, the Copilot skill wins; fix this file in the same PR.

## 2. Find the change

Pick the target from the request:

| Request | Diff to review |
|---|---|
| Nothing named | `git diff origin/main...HEAD`, plus uncommitted changes (`git diff HEAD`) and untracked files (`git ls-files --others --exclude-standard`) |
| A branch | `git diff origin/main...<branch>` |
| A PR number | `gh pr diff <number>`, `gh pr view <number> --comments` for the conversation, and `gh api repos/{owner}/{repo}/pulls/<number>/comments` for inline review comments, if `gh` is installed and signed in; otherwise ask for the branch name |
| A path | The current content of that path, reviewed as a whole |

Run `git fetch origin main` first so the base is current. List the changed and untracked files, then read each one in full, not only the hunks: a rule break often sits in unchanged lines that the change now reaches.

## 3. Read the requirements the diff touches

For each checklist in Copilot skill §4 that applies, open the cited BRD section, NFR rows or spike rules and read them. Don't review from memory or from the checklist summary alone; the documents are the source of truth and may have changed since the checklist was written.

## 4. Run the local checks

Run what CI runs for the files the diff touches, as `CLAUDE.md` lists:

- **Markdown changed:** `npx --yes markdownlint-cli2@0.18.1 "**/*.md"`. Use the pinned version; newer releases add rules CI doesn't run.
- **Markdown or links changed:** `lychee --include-fragments '**/*.md'`. If lychee isn't installed, check relative links and heading anchors in the changed files with a short script, and say in the report that the external link check didn't run.
- **Spike code changed:** `dotnet build` for each changed spike project. If the .NET 10 SDK isn't installed, say so in the report.
- **Application code, once it exists:** the build, test, pgTAP and lint commands that `CLAUDE.md` lists at that time.

`npx` and lychee need network access. If the Codex sandbox blocks it, report the check as not run rather than retrying with weaker sandbox settings.

A failing check is a Blocking finding. Never report a check as passing unless you ran it.

## 5. Verify each finding

Before reporting a finding, confirm it:

1. Quote the line from the diff and the rule it breaks, by section or ID.
2. Trace a concrete path to harm: an input, an actor or a sequence of calls that produces the wrong result, the leak or the CI failure.
3. Check that the code or another file in the diff doesn't already handle it.

Drop a finding that fails any step. Mark one you believe but couldn't fully trace as "unverified" rather than inflating its severity.

## 6. Report

Use the format in Copilot skill §3: severity, `path:line`, the rule cited, what goes wrong and for whom, and the smallest fix. Order findings Blocking first. After the findings, list the local checks you ran and their results, and any you couldn't run.

Report in the conversation. Don't edit files, post comments on a pull request, push or resolve threads unless the user asks for that.
