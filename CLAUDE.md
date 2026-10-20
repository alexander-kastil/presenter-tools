# CLAUDE.md

Router for this repo. Rules live one line each; the detail lives in the skill, rule file or doc that owns it.

## Project

Presenter Tools: draw on the screen, capture and edit screenshots, and receive AirPlay screen mirroring on Windows 10/11 x64 (WPF on .NET 10). The AirPlay engine is UxPlay (GPL-3.0, licensed separately), a git submodule staged through MSYS2.

```text
src/
  PresenterTools.slnx      solution
  PresenterTools/          WPF app: pen bar, overlay, capture, library editor (Shots/), tray, receiver, settings
  PresenterTools/engine/   staged UxPlay engine and its DLLs (gitignored)
  PresenterTools.Setup/    WiX MSI, stage-engine.sh
  PresenterTools.Tests/    xUnit
  UxPlay/                  git submodule, FDH2/UxPlay
docs/                      user docs, one page per feature
setup/                     PresenterTools.msi (gitignored)
certs/                     public code-signing certificate
setup.ps1                  trusts that certificate (elevated, once)
sign-release.ps1           signs the CI-built MSI and uploads it to the GitHub Release
.github/workflows/         release.yml
tasks/                     lessons.md
```

## Where the detail lives

| Topic | Lives in |
| --- | --- |
| .NET, WPF and WiX, git history | `~/.claude/skills/` dotnet-conventions, git-history-ops |
| Desktop app, tests, lifecycle, tray, settings, installer, release | `.claude/rules/desktop.md` |
| Features as the user sees them | `readme.md`, `docs/*.md` |
| Corrections | `tasks/lessons.md` |

## Harness

Orchestration runs in the claude-mods plugins (`D:\git-projects\claude-mods`); this file does not repeat their rules.

- `subagent-delegation-plugin`: routes work to the owning agent (owners in `.claude/rules/`), sends fact gathering to the Haiku fact-gatherer, bounds every brief, keeps the agent ledger.
- `learn-loop-plugin`: runs `claude-learn` once a session goes idle.
- `report-plugin`: starts a status cron when an agent or workflow runs long.
- `intent-guard-plugin`: holds destructive commands nobody asked for.
- `ask-check-plugin`: stops questions the conversation already settled and unrequested findings.
- Skills: search `~/.claude/skills/` and `.claude/skills/` (none here) before any action; the directory on disk wins over the Skill listing; never copy a global skill into the repo.

## Hard Rules

- **Read the code on disk, never commit history, to learn what the app has.** Features, settings and tools are what `src/` holds now, uncommitted work included; no `git log`, `git show` or commit diff as the inventory, in a brief or on the main thread.
- **Blockers only while a task runs.** Do the named task with the named tools and nothing beside it. Until it is done, the only message is one blocker line: what the task cannot proceed past without the user. No progress, findings, observations, options, offers, reminders, or gates you were told to respect.
- **Findings wait for completion.** The done reply is what changed and where, then at most a numbered list of `/create-issue` proposals, one line each, for high-severity defects or drastic code smells met on the way. Security is excluded. Anything not on that list is dropped: never asked, never offered, never repeated.
- **No commit or push** without an explicit, in-the-moment user request. "Override the history" is that request: rewrite and force-push in one flow (`git-history-ops`). Git is otherwise not your issue: never mention commit state, branches or the working tree, never offer to commit, never run a git command the task did not require.
- **No shared package or library**: no NuGet or npm package, no shared class library, no local feed, no shared `Directory.Build.props`, no project referenced across repositories. Duplication is preferred; a plan that says otherwise is wrong: stop and ask.
- **No documentation unless explicitly asked.** No README, no doc, no guide, no handoff, no doc section appended to a code change. A behaviour change to a documented feature is reported to the owner instead.
- **The running app is the owner's**: never stop their instance unless they say so. It locks `src/PresenterTools/bin/Debug/net10.0-windows/PresenterTools.exe`; compile-check with a Release build or a build into `%TEMP%`.
- **Agent notation**: always refer to a subagent as `@<name> (<agent type>)`, e.g. `@tray-fix (wpf expert)`. Never the bare name, never the type alone. Give every spawn an explicit name so both halves exist.
- **Shell**: Bash for shell operations; PowerShell only where Windows makes it unavoidable.
- Write clean code with no noise: no inline comments, no placeholder notes, no error handling in scripts unless asked.
- No experimental or test files in the repo root: use `.claude/experiments/` or the scratchpad.
- Internal links are relative; anchors are `#heading-name`; every code fence declares a language.
- If a quality check fails, fix the cause. Never bypass with `--no-verify`.
- No em dashes in generated prose: use a colon, comma, semicolon, or parentheses.

## Working Method

- **Plan first** for anything non-trivial (3+ steps or an architectural decision): write it to `tasks/todo.md`, check in, then track progress there.
- **Verify before reporting done.** Run the check, show the command and its output. A claim is not a receipt.
- **Capture corrections** in `tasks/lessons.md` and review it at session start. Prune it, this file and the hooks with `claude-learn`'s `instruction-audit` gate: nothing else ever removes.
- **Parallelize** independent tool calls and agent dispatches in a single message.
- **Simplicity first**: minimal diff, root causes over patches, no rewrite to fix a typo.
