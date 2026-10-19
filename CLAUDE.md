# Priority Sidebar

Open-source (GPLv3) WPF (.NET 10) app: a Windows app bar on the right edge of a display that shows one
Trello list as a ranked, drag-to-reorder column. See README.md for behaviour and structure; it is written
for the public, so keep it current when behaviour changes.

## Checks

- `dotnet build -c Release -warnaserror` must pass (CI runs the same). There are no automated tests yet.
- Releases: pushing a tag `vX.Y.Z` makes CI publish a GitHub release with a small and a standalone exe.
  Only tag when the owner asks.
- Docking can only be checked by running the app: launch it, confirm the sidebar sits at the right edge
  and maximized windows stop beside it, then close it from the header's right-click menu and confirm the
  full screen width comes back.

## Notes

- Close the app normally when testing. A forced stop leaves the strip reserved until the next launch
  clears it.
- UI copy is plain English and avoids em/en dashes.
