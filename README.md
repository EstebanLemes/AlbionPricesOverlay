# AlbionPrices Overlay

A lightweight Windows overlay for checking real-time item prices in Albion Online without leaving the game.

Press `Ctrl+D` from anywhere — the overlay appears centered on screen, shows prices, and hides when you click away.

## Features

- **Global hotkey** `Ctrl+D` — show/hide from anywhere, even inside the game
- **Item search** — search by name in Spanish, English, or Portuguese
- **Live prices** — buy/sell prices from all major cities via the [Albion Online Data Project](https://www.albion-online-data.com/)
- **Best city highlight** — instantly shows the cheapest city to buy and the best city to sell
- **Tier & enchantment selector** — switch T4/T5/T6... and .1/.2/.3 enchantments with one click, prices refresh automatically
- **System tray** — lives in the tray when not in use; double-click or use the hotkey to bring it back
- **Auto-update** — detects new GitHub releases on startup and installs updates automatically

## Installation

Download from the [latest release](https://github.com/EstebanLemes/AlbionPricesOverlay/releases/latest):

| File | Description |
|---|---|
| `AlbionPrices-Setup-x.x.x.exe` | Installer (recommended) |
| `AlbionPrices-x.x.x.zip` | Portable — extract and run `AlbionPrices.exe` |

**Requirements:** Windows 10 x64 (build 17763) or later. No additional runtime needed — the app is self-contained.

## Usage

1. Launch `AlbionPrices.exe` — it starts minimized in the system tray
2. Press **Ctrl+D** (or double-click the tray icon) to open the overlay
3. Type an item name and press **Enter** or click **Check**
4. The overlay shows buy/sell prices per city and highlights the best options
5. For tiered items, use the **Tier** and **Enchant** buttons to switch variants — prices refresh instantly
6. Click anywhere outside the overlay to hide it

### Keyboard shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl+D` | Show / hide overlay |
| `Enter` | Search item |
| Click title bar | Drag window |
| `_` button | Minimize to tray |

## Auto-update

When a new version is available, a banner appears at the top of the overlay. Click it to download and install the update — the app closes, installs, and relaunches automatically.

## Building from source

**Requirements:** [.NET 10 SDK](https://dotnet.microsoft.com/download), [Inno Setup 6](https://jrsoftware.org/isinfo.php) (optional, for installer), [GitHub CLI](https://cli.github.com/) (optional, for releases)

```powershell
# Build + ZIP only (auto-increments patch version)
.\build.ps1

# Build + ZIP + installer + publish GitHub release
.\build.ps1 -GitHubOwner YourGitHubUsername

# Build a specific version
.\build.ps1 -GitHubOwner YourGitHubUsername -Version 1.2.0
```

The script automatically:
1. Increments the patch version in the `.csproj`
2. Compiles a self-contained `win-x64` binary
3. Packages `Releases/AlbionPrices-{version}.zip`
4. Builds the installer via Inno Setup (outputs to `../Installer/`)
5. Creates a GitHub release with both assets attached

### Build parameters

| Parameter | Description | Default |
|---|---|---|
| `-GitHubOwner` | GitHub username for the release | (none) |
| `-GitHubRepo` | Repository name | `AlbionPricesOverlay` |
| `-Version` | Force a specific version | (auto-increment) |

## Data sources

| Data | Source |
|---|---|
| Live prices | [albion-online-data.com](https://west.albion-online-data.com/api/v2/stats/prices/) |
| Item database | [ao-data/ao-bin-dumps](https://github.com/ao-data/ao-bin-dumps) |

Prices are fetched live on each search. The item database (~17 MB) is downloaded from GitHub on first launch and kept in memory for the session.

## Tech stack

- **Framework:** WPF on .NET 10, self-contained `win-x64`
- **Prices API:** Albion Online Data Project v2
- **Item DB:** ao-data/ao-bin-dumps (JSON, downloaded at runtime)
- **Installer:** Inno Setup 6

## License

MIT
