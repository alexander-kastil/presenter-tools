# Changelog

## [Unreleased]

### Added

- 1.3.0: Presenter Tools can be driven from a script. A second `PresenterTools.exe --cmd draw|write|clear|capture ...` passes the command to the running app over a per-user named pipe, prints `ok` or `error=<reason>` and exits 0 or 1. Draw rectangles, ellipses, arrows and lines with a colour, width and label, write text, clear the screen, and capture the screen or a region to PNG, on every monitor. `--cmd mirror` tells a script whether an iPad or iPhone is mirrored through the AirPlay receiver (device, name, picture size and position), and `--cmd capture --target mirror` captures the mirrored picture with any drawing on it.

### Changed

- Experiments and scratch output go to `assets/experiments/`, never `.claude/experiments/`: `.claude/` holds only harness items, so the old `.claude/experiments` folder is deleted.
- Plugin references follow the rename of `angular-plugin` to `angular-conventions` and `dotnet-toolkit` to `dotnet-conventions`, and of their router skills `angular-conventions` to `angular` and `dotnet-conventions` to `dotnet`.
- Git ignores the time ledger (`.time/`), personal Claude settings and the scheduler lock, and no longer tracks the ledger.
