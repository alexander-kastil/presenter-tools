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

Orchestration runs in the installed plugins and the house rules in `~/.claude/CLAUDE.md` apply as they stand; this file repeats neither.

## Hard Rules

- **Read the code on disk, never commit history, to learn what the app has.** Features, settings and tools are what `src/` holds now, uncommitted work included; no `git log`, `git show` or commit diff as the inventory, in a brief or on the main thread.
- **Blockers only while a task runs.** Do the named task with the named tools and nothing beside it. Until it is done, the only message is one blocker line: what the task cannot proceed past without the user. No progress, findings, observations, options, offers, reminders, or gates you were told to respect.
- **Findings wait for completion.** The done reply is what changed and where, then at most a numbered list of `/create-issue` proposals, one line each, for high-severity defects or drastic code smells met on the way. Security is excluded. Anything not on that list is dropped: never asked, never offered, never repeated.
- **Git is not your issue**: never mention commit state, branches or the working tree, and never run a git command the task did not require.
- **Documented features** (`readme.md`, `docs/`): a behaviour change to one is reported to the owner instead of written into its doc.
- **The running app is the owner's**: never stop their instance unless they say so. It locks `src/PresenterTools/bin/Debug/net10.0-windows/PresenterTools.exe`; compile-check with a Release build or a build into `%TEMP%`.
- **Agent notation**: always refer to a subagent as `@<name> (<agent type>)`, e.g. `@tray-fix (wpf expert)`. Never the bare name, never the type alone. Give every spawn an explicit name so both halves exist.
- **Shell**: Bash for shell operations; PowerShell only where Windows makes it unavoidable.
- Write clean code with no noise: no inline comments, no placeholder notes, no error handling in scripts unless asked.
- No experimental or test files in the repo root: use `.claude/experiments/` or the scratchpad.
- Internal links are relative; anchors are `#heading-name`; every code fence declares a language.
- If a quality check fails, fix the cause. Never bypass with `--no-verify`.

## Working Method

- **Plan first** for anything non-trivial (3+ steps or an architectural decision): write it to `tasks/todo.md`, check in, then track progress there.
- **Verify before reporting done.** Run the check, show the command and its output. A claim is not a receipt.
- **Review `tasks/lessons.md`** at session start; `claude-learn`'s `instruction-audit` gate prunes it and this file.
- **Simplicity first**: minimal diff, root causes over patches, no rewrite to fix a typo.
