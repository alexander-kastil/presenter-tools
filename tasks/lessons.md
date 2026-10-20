# Lessons

## A tool cursor on the image layer misses most of the drawing area

**Pattern:** The library editor set the tool cursor on the image-sized `layer` inside a `DownOnly` `Viewbox`. A small screenshot leaves a wide grey margin with no cursor, so the user saw the plain pointer for most tools; the same margin failed the wheel handler's `viewport.IsMouseOver` guard, so zoom only worked over the image and only with Select.
**Rule:** Put cursors and wheel handling on the whole drawing host (`area`), and test both the image and the empty margin.

## The pen sidebar position must be saved on close, not only after a drag

**Pattern:** `ToolbarWindow` saved its position only from `OnDrag`, so any move that was not a `DragMove` was lost at restart. Closing with the Library open would have saved the docked spot.
**Rule:** Save the window rect in `Closing`; while docked beside the Library, save the pre-dock standalone spot. Covered by `ToolbarPositionTests`.

## Check what the installed app actually is before calling a fix missing

**Pattern:** The user tested installed 1.1.3 while the tree was already at 1.2.1 with partial fixes; the MSI in `setup/` was a week old.
**Rule:** Before judging a report against the code, compare the installed `PresenterTools.exe` version with `<Version>` in the csproj, and always ship a fix as a bumped, rebuilt MSI.

## Delegates keep diagnostics outside the repo

**Pattern:** The wpf-expert left `Diag.cs` and `Diag2.cs` scratch tests in the new test project; the intent guard blocked its `rm`, so the cleanup came back to the user.
**Rule:** Briefs that allow throwaway diagnostics name a place outside the repo (scratchpad or `%TEMP%`).

## A subagent frozen on a pending tool call is resumed, not replaced

**Pattern:** The wpf-expert's `Write` of `EditorViewTests.cs` hung 16 minutes with no result; the report cron showed an unchanged transcript for three ticks.
**Rule:** Parse the transcript's last record; a `tool_use` with no `tool_result` is a hung call. `TaskStop` then `SendMessage` to the same agent id resumes it with its context. `agent-probe.py` now prints it as `hung`.
