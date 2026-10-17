# SpaceLens

A native Windows storage explorer built with **C#, .NET 9, WPF/XAML, CommunityToolkit.Mvvm, dependency injection, and SQLite**. No browser, web server, Node, or Rust is needed.

## Run, build, test, publish

Requires Windows 10/11 and the .NET 9 SDK. From the repository root:

```powershell
dotnet restore SpaceLens.sln
dotnet run --project src/SpaceLens/SpaceLens.csproj
dotnet build SpaceLens.sln -c Release
dotnet test SpaceLens.sln -c Release
dotnet publish src/SpaceLens/SpaceLens.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
```

Launch `artifacts/publish/win-x64/SpaceLens.exe`. Keep the complete published directory together: it includes the desktop runtime and native SQLite library. This is a portable folder deployment. An installer and code signing are not included.

For a smaller deployment requiring the .NET 9 Windows Desktop Runtime, replace `--self-contained true` with `--self-contained false` and use a separate output directory.

## Using SpaceLens

1. Select a drive, enter a full path, or choose a folder. Click **Scan storage**. Cancel at any time.
2. Expand folders in the hierarchy or double-click a table folder. Breadcrumbs and the up button navigate back. Click a row to inspect its size, risk, categories, and largest files.
3. Search filenames. Enable **Subfolders** to search recursively. Filter files, folders, files at least 1 GiB, or review candidates. Column sorting applies across the complete result set, not just the current page.
4. Ctrl/Shift selects multiple rows. Context menus reveal items in Windows Explorer or copy paths. The table pages through 300 entries; tree children load on expansion.
5. Ask the assistant about the selected item. Offline rules use actual metadata. Answer references locate the corresponding scanned item.
6. On **Cleanup**, enter `25 GiB` or `I need to free 25 GB`. Review every suggested file and its consequences. SpaceLens has no deletion commands.
7. Completed scans save automatically. **Compare Scans** shows actual differences between two snapshots of the same location. Locate items in either snapshot, or ask the assistant about differences after comparison.
8. **Duplicates** verifies same-size files using SHA-256. Changed, linked, and inaccessible files are skipped. Up to 10,000 size-matched candidates are checked.

## Claude Haiku 5.5

In **Settings**, paste your Anthropic API key into the masked field and save. Windows DPAPI `CurrentUser` encrypts it in `%LOCALAPPDATA%\SpaceLens\anthropic.dpapi`. It is never saved as plaintext or included in logs.

**Test connection** makes a real, small billable Messages API request using synthetic text only. Authentication, model-access, billing, rate-limit, server, timeout, cancellation, and network failures have distinct error states. Success requires a valid key, credits, and access to `claude-haiku-5-5`.

Cloud chat is off by default. Explicitly select **Use Claude** and consent to sharing metadata. Consent resets each launch. Requests include the question, up to eight recent cloud conversation messages, a folder summary, category totals, at most 32 relevant entries, and up to 12 comparison rows. **File contents and full inventories are never sent.** Names are treated as untrusted data; clickable references are restricted to supplied local IDs. Model advice still requires human review.

Official references: [Messages API](https://platform.claude.com/docs/en/api/messages/create), [API overview](https://platform.claude.com/docs/en/api/overview).

## Accuracy and safety

- Scanned sizes are **logical file lengths**. Drive cards show OS-reported capacity usage, a different measurement. Sparse/compressed allocation, hard-link sharing, filesystem overhead, reserved storage, and inaccessible content explain discrepancies.
- Hard links count per path. Reparse points, symbolic links, and junctions are recorded but never traversed. A selected root with a linked ancestor is rejected.
- Permission failures are counted and shown without administrator rights. Unreadable content contributes no invented bytes. Scans are not atomic filesystem snapshots; concurrent changes can affect results.
- Scans stream into batched SQLite transactions on a background worker. Progress is throttled. Queries, hashing, and HTTP do not block WPF's dispatcher.
- Cancelled/interrupted results are labeled partial and excluded from cleanup planning, cloud analysis, and comparisons.
- Unknown files require review; application state and dependencies are high risk; system/application/repository locations are protected. **No files are classified Low Risk from metadata alone.** Potential savings are conditional logical bytes, not guaranteed reclaimed physical space.
- Comparison rows show observed changes. “Newly discovered” does not prove recent creation. Folder sizes include descendants; nested rows must not be summed. No application attribution is inferred.
- Snapshots persist in `%LOCALAPPDATA%\SpaceLens\scans.db`. One instance per Windows user prevents conflicting scan-state updates.

## Architecture and testing

`src/SpaceLens` separates native views, MVVM view models, domain models, scanner/AI/cleanup/security services, and the SQLite repository. xUnit tests in `tests/SpaceLens.Tests` cover actual traversal, denied-directory ACLs, junction loops, cancellation, totals, query pagination, comparisons, cleanup risks/plans, duplicates, DPAPI, HTTP errors, and consent.

After a Debug build and two completed scans of the source folder, run native UI smoke automation:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/SmokeTest.ps1
```

It launches a real WPF window, searches files, asks the offline assistant, generates a plan, compares scans, checks the masked key control, cancels a Windows directory scan, and resizes the window. It never uses an actual API key or deletes user files. Results are written to `artifacts/ui-smoke-results.json`.

The reference is preserved at `references/ui-reference.png`. The superseded Tauri scaffold is archived at `archive/initial-tauri-scaffold` and excluded from the native solution and published executable.

## Scope

Implemented: native explorer; real cancellable scans; storage details; offline analysis; secure, consent-based Claude integration; review-only goal solver; SQLite history/comparisons; SHA-256 duplicate review.

Not included: automatic deletion, guaranteed safe cleanup, physical allocation accounting, atomic VSS snapshots, guaranteed application attribution, installer, or code signing. Authenticated Claude responses require the user's API key; automated HTTP tests use synthetic responses and no real credential.
