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

## A call that hangs again after the resume comes back to the main thread

**Pattern:** The wpf-expert's `dotnet test` call hung twice without ever starting: no dotnet or testhost process, and the version `sed` in the same call never ran. The resumed agent hung on the same command again, while the main thread ran the suite in 37 s.
**Rule:** Confirm a pending call never started (its side effects absent, no child process). After the second hang on the same command, stop the agent, run that command on the main thread, and ask the agent for its report with no further tool calls.

## Read monitor bounds DPI-aware before calling the toolbar off-screen

**Pattern:** `Screen.AllScreens` from a DPI-unaware PowerShell showed 2560x1440 for 3840x2160 monitors at 150%, so the brief called the saved toolbar spot 7301,789 off-screen. `settings.json` stores physical pixels and the spot was on the second monitor.
**Rule:** Compare a saved toolbar spot with DPI-aware monitor bounds (or the app's own `MonitorFromRect`) before naming it as a cause.

## Library and pen bar features must survive a big library and an --autostart start

**Pattern:** The first capture took about 20 s because `LibraryWindow` decoded all 264 full-size shots on the UI thread on every first open, and the delay grew by about 30 shots a day. After an `--autostart` start the pen bar was never shown, so opening the Library docked a hidden bar and its tools never appeared.
**Rule:** Test Library changes against a library of hundreds of large shots (`LibraryThumbnailTests`), and test every path that uses the pen bar with a bar that was never shown (`ToolbarPositionTests`).

## A vanishing app is a crash until the event log says otherwise

**Pattern:** "When I click an item in the Library it disappears" was read as a window or focus bug, so the main thread read the Library, chrome, docking and tray code before starting the app once. One start and the Application event log named the cause: a 0-byte sidecar made `LibraryStore.LoadSidecar` throw an unhandled `JsonException` in the thumbnail pump, which closed the whole app.
**Rule:** For a report that a window or the app disappears, start the app (or check that its process still runs) and read the `.NET Runtime` events in the Application log first; read code only along the stack. Sidecars are written temp-then-move and an unreadable one loads as empty (`LibraryThumbnailTests.Empty_sidecar_still_renders_the_shot`).
**Replaces:** none
