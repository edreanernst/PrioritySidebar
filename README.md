# Priority Sidebar

A slim, always-visible sidebar on the right edge of a Windows display that shows one Trello list as a
ranked column of cards. Windows treats it like the taskbar: maximized and snapped windows stop at its
edge instead of covering it. Reorder by dragging (or with the up/down buttons on hover); changes go
straight to Trello, and changes made in the Trello app on your phone show up within the polling interval.

## Using it

- **Drag a card** up or down to change its rank. Hover a card for move up, move down, open in Trello and done.
- **Done** adds a green "Done" label (created on the board the first time) and archives the card. Undo
  brings it back to the same place and removes the label.
- **Add a card** with the box at the bottom; it goes to the bottom of the list.
- **Refresh arrow** checks Trello now. The app also checks every 30 seconds (configurable). Cards that
  were moved or added elsewhere are briefly outlined.
- **Right-click the header** for settings, check now, and closing the sidebar.
- **Offline**, it shows the last list it loaded and pauses changes until Trello can be reached again.

## First-time setup

1. Open <https://trello.com/power-ups/admin>, choose **New**, give it a name (for example "Priority
   Sidebar") and your workspace. The iframe connector URL can stay empty. Create it.
2. Open it, go to **API key**, choose **Generate a new API key** and copy the key.
3. Start the sidebar. In settings, paste the key, press **Get a token**, approve access in Trello and
   paste the token it shows. Press **Connect**, pick the board and list, and **Save**.

Settings live in `%APPDATA%\PrioritySidebar\settings.json`; the token is encrypted for your Windows user
account. Errors are logged to `errors.log` in the same folder.

## Building

Requires the .NET 10 SDK.

```bash
dotnet build
dotnet run
```

A single-file build to run day to day (needs the .NET 10 desktop runtime, which the SDK includes):

```bash
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

Run `publish\PrioritySidebar.exe`. "Start when Windows starts" registers whichever exe was running when
settings were saved, so save settings once from the published exe.

## How it works

- `AppBar.cs` registers the window as a Windows app bar (`SHAppBarMessage`) on the right edge of the chosen
  display, sized in physical pixels for that display's scaling. It re-docks when the display layout or
  scaling changes and steps behind full-screen apps. On startup it registers and removes once first,
  which makes Windows drop a strip left behind by a run that crashed or was force-stopped.
- `TrelloClient.cs` holds the few Trello REST calls. Card order is Trello's `pos` number; a moved card
  gets the midpoint of its new neighbours (`Ranking.PosBetween`), or `top` / `bottom` at the ends.
- `MainWindow` shows the list, handles drag and drop, and saves changes one at a time. If a save fails
  the list is reloaded from Trello.
