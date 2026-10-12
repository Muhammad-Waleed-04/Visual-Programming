# LAN Game Hub

A C# Windows Forms desktop application. Players on the same Wi-Fi / LAN find each other automatically, chat, invite each other and play games.

## Features

- Automatic player discovery on the same Wi-Fi / WLAN / LAN (no IP typing)
- Player list shows everyone on the same Wi-Fi, with a **Refresh** button to scan again
- Works even when the router blocks broadcast (the app also scans every address of the subnet)
- Private chat with any online player
- Game invitations (the other player can accept or decline)
- 4 games: Tic Tac Toe, Connect Four, Chess, Card Duel
- Same application is installed on every PC

## Requirements

- Windows 10 / 11
- .NET 8 SDK (https://dotnet.microsoft.com/download)
- VS Code with the "C# Dev Kit" (or "C#") extension

## How to run (VS Code)

1. Open the `LanGameHub` folder in VS Code.
2. Open the terminal and run:

```
dotnet run
```

3. Enter your player name when asked.

## Test on one PC

Run `dotnet run` in two terminals. Both windows will see each other. This is enough to test chat and all games.

## Install on another PC

1. Build a release version:

```
dotnet publish -c Release -r win-x64 --self-contained true -o publish
```

2. Copy the `publish` folder to the other PC (USB or any share).
3. Run `LanGameHub.exe` on both PCs. Both PCs must be on the same Wi-Fi / network.
4. When Windows Firewall asks, click **Allow access** (private networks). Without this, players will not see each other.

## How to play

1. Wait until the other player appears in the list.
2. Select the player. You can now chat.
3. Choose a game in the box below the list and click **Invite to play**.
4. The other player clicks **Yes**. Both game windows open. The one who invited moves first.

## How it works (for viva)

- **Discovery:** every 2 seconds each app sends a UDP broadcast (port 45454) with its id, name and TCP port. Every app listens on that port and builds the player list. A player who is silent for 8 seconds is removed.
- **Communication:** apps connect to each other with TCP. Each message is one text line: `TYPE|data`.
  - `CHAT` text message
  - `INVITE`, `ACCEPT`, `DECLINE` game invitation
  - `MOVE` a game move (example for Tic Tac Toe: cell number)
  - `QUIT` a player closed the game
- **Games:** every game is a window that inherits from `GameForm`. It sends its own moves with `Send()` and receives the opponent's moves in `ReceiveMove()`. Each side checks the rules itself, so the two windows always show the same board.

## Project files

| File | Purpose |
|---|---|
| `Program.cs` | Starts the app |
| `NetworkService.cs` | UDP discovery and TCP messaging |
| `MainForm.cs` | Player list, chat, invitations |
| `GameForm.cs` | Base class for all game windows |
| `TicTacToeForm.cs` | Game 1 |
| `ConnectFourForm.cs` | Game 2 |
| `ChessForm.cs` | Game 3 (no castling / en passant) |
| `CardDuelForm.cs` | Game 4 |

## Troubleshooting

- **Players not visible:** allow the app in Windows Firewall, and make sure both PCs are on the same network. Some public Wi-Fi networks block device-to-device traffic (client isolation); use a home router or a mobile hotspot instead.
- **Unicode chess pieces look like emoji:** this is only a font matter, the game still works.
