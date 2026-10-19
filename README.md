# Priority Sidebar

**Your Trello priority list, always on screen.** Priority Sidebar is a slim sidebar for Windows. It
docks on the right edge of a display and shows one Trello list as a ranked column of cards. It works
like a sticky-note board on the wall next to your desk, but digital, and you can update it from your
phone.

It docks the way the Windows taskbar does. Maximized and snapped windows stop at its edge instead of
covering it, so your priorities are always in view while you work.

## Why

When priorities shift every few days, a task list in a browser tab gets out of sight and out of mind.
You end up busy with something that slipped down the list last week. Priority Sidebar keeps the list
visible all the time, and reordering it takes one drag.

## Features

- **Always visible.** It reserves a strip on the right edge of the display you choose, so other windows
  sit beside it.
- **Drag to reorder.** Drag a card up or down to change its rank. The new order is saved to Trello
  straight away.
- **Works with your phone.** Reorder or add cards in the Trello app on your phone, and the sidebar picks
  up the change within 30 seconds. Cards that moved are briefly outlined.
- **Quick actions on hover.** Move up, move down, open in Trello, and done.
- **Done means done.** Marking a card done adds a green "Done" label and archives it, so you can still
  find everything you finished in Trello's archive. Undo puts it back where it was.
- **Quick add.** Type in the box at the bottom and press Enter.
- **Labels shown.** Each card shows its Trello labels, so you can tell projects apart in one list.
- **Card details at a click.** Click a card to open its description and checklist right in the sidebar.
  Tick checklist items off as you go, and a bar on the card shows how far along it is. It turns green
  when everything is ticked.
- **Offline friendly.** Without a connection it shows the last list it loaded and pauses changes until
  Trello can be reached again.
- **Stays out of the way.** It steps behind full-screen apps such as videos and presentations, and it
  doesn't appear in Alt+Tab or the taskbar.

## Requirements

- Windows 10 or Windows 11 (64-bit)
- A Trello account

## Install

### Download (easiest)

Go to the [latest release](https://github.com/edreanernst/PrioritySidebar/releases/latest) and download
one of the two files:

- **`PrioritySidebar-<version>-win-x64-standalone.exe`** (about 60 MB): runs as is, nothing else to
  install. Pick this one if you're not sure.
- **`PrioritySidebar-<version>-win-x64.exe`** (under 1 MB): needs the
  [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) installed first.

Put the file in a folder where it can stay (for example `Documents\PrioritySidebar`) and run it. If you
turn on "Start when Windows starts", Windows starts it from that spot, so move it before setting up.

The app isn't code-signed yet, so the first time you run it Windows may show "Windows protected your PC".
Choose **More info**, then **Run anyway**.

### Build from source

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Clone the repository and build a single-file app:

   ```bash
   git clone https://github.com/edreanernst/PrioritySidebar.git
   cd PrioritySidebar
   dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
   ```

3. Run `publish\PrioritySidebar.exe`.

## Connect it to Trello

Priority Sidebar talks to Trello with your own API key and token. You create them once, and they never
leave your computer.

1. Open the [Trello Power-Up admin page](https://trello.com/power-ups/admin) and choose **New**. Give it
   a name (for example "Priority Sidebar") and pick your workspace. The iframe connector URL can stay
   empty.
2. Open the new app and go to **Authorization > Trello Auth**. Copy the **API key** (generate one if
   there isn't one yet). You can ignore the OAuth 2.0 page.
3. Start Priority Sidebar. The settings window opens on first run. Paste the API key.
4. Press **Get a token**. Trello opens in your browser and asks you to allow access. Approve it, then copy
   the token Trello shows and paste it into settings.
5. Press **Connect**, choose the board and list to show, and press **Save**.

In the same settings window you can pick the display, the sidebar width and how often to check Trello,
and set it to start when Windows starts.

## Using it

| To do this | Do this |
| --- | --- |
| Change a card's rank | Drag it up or down, or hover and use the arrows |
| Mark a card done | Hover and press the tick, then **Undo** if you change your mind |
| Open a card in Trello | Hover and press the open button |
| See a card's description and checklist | Click the card. Click it again to close it |
| See just the description or just the checklist | Press the lines icon or the checklist icon under the card's name |
| Tick off a checklist item | Open the checklist and click the item |
| Add a card | Type in the box at the bottom and press Enter |
| Check Trello right now | Press the refresh arrow at the top |
| Change settings | Press the settings button at the top |
| Close the sidebar | Right-click the header and choose **Close Priority Sidebar** |

Please close it from the header menu rather than ending it in Task Manager. A forced stop leaves the
reserved strip in place until the next time the sidebar starts.

## Privacy

- The sidebar only talks to Trello (`api.trello.com`). There is no server, account or tracking of its
  own.
- Settings are stored in `%APPDATA%\PrioritySidebar\settings.json`. Your Trello token is encrypted with
  Windows data protection, so only your Windows user account can read it.
- The last loaded list is cached in the same folder so it can be shown while offline.
- Errors are written to `errors.log` in that folder, which is useful when reporting a bug.

## How it works

- **Docking:** `AppBar.cs` registers the window as a Windows app bar (`SHAppBarMessage`), the same
  mechanism the taskbar uses. It is sized in real pixels for each display's scaling and re-docks when
  the display layout or scaling changes. On startup it registers and removes once, which makes Windows
  forget a strip left behind by a run that crashed.
- **Trello:** `TrelloClient.cs` holds the few Trello REST calls. Trello orders cards by a hidden
  position number, so a moved card gets the midpoint between its new neighbours
  (`Ranking.PosBetween`), or `top` / `bottom` at either end.
- **Sidebar:** `MainWindow` shows the list, handles drag and drop, and saves changes to Trello one at a
  time. If a save fails, the list is reloaded from Trello so it always shows the truth.
- **Card details:** the list request includes each card's description and its checklist counts (Trello's
  badges), so the regular check stays one request. The full checklist loads only when a card is opened,
  and loads again if its counts change while it is open.
- **Settings:** `SettingsWindow` and `Storage.cs` handle the connection, list choice, display, width,
  polling interval and starting with Windows.

## Contributing

Contributions are welcome, from bug reports to pull requests.

- **Bugs and ideas:** open an [issue](https://github.com/edreanernst/PrioritySidebar/issues). For bugs,
  include your Windows version, your display setup (how many displays and their scaling), and the
  relevant lines from `errors.log`.
- **Pull requests:** keep them focused on one change and describe how you tested it.
  - `dotnet build` must pass with no warnings.
  - There are no automated tests yet, so check docking by hand: start the sidebar, make sure maximized
    windows stop beside it, then close it from the header menu and make sure the full screen width
    comes back. If you have more than one display or mixed scaling, test on those too.
  - Keep wording in the app plain and short.

To work on it:

```bash
git clone https://github.com/edreanernst/PrioritySidebar.git
cd PrioritySidebar
dotnet build
dotnet run
```

Every push and pull request to `main` is built by GitHub Actions (`.github/workflows/build.yml`).
Pushing a version tag such as `v0.2.0` also publishes a release with both downloads attached.

## License

Priority Sidebar is free software, licensed under the
[GNU General Public License v3.0](LICENSE). You may use, study, share and change it. If you distribute a
modified version, it must also be under the GPLv3, with its source code available.

Trello is a trademark of Atlassian. This project is not affiliated with or endorsed by Atlassian.
