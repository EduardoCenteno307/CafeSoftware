# CafeSoftware

A coin-operated timer for a Windows PC, like a pisonet. The PC stays locked until someone inserts a coin. The coin buys time, and when the time runs out the PC restarts or shuts down for the next customer.

> **Status: early and experimental.** It has not been tested in a real shop yet. The Windows setup changes user accounts, automatic sign-in and registry settings, so **try it on a spare PC or a virtual machine first.**

## How it works

```
coin acceptor -> PC817 isolation module -> ESP8266 (or Arduino Mega) -> USB -> CafeSoftware (Windows)
```

- The **coin acceptor** recognizes the coin and sends a burst of pulses. Each coin type sends a different number of pulses.
- The **microcontroller** counts the pulses and sends the count to the PC over USB serial.
- **CafeSoftware** looks the count up in a coin table (pulses -> pesos + bonus seconds) and adds the time.

## Features

- Full-screen lock screen with shop name, price list, clock and coin slot status
- Countdown window with sound and pop-up alerts (first alert and a blinking final countdown), configurable
- Coin table with a bonus in seconds per coin
- When time ends: show the lock screen, restart the PC, or shut it down
- Idle shutdown when nobody uses the PC
- Admin settings (Ctrl+Shift+A) in tabs, protected by a password stored as a salted hash
- Automatic reconnect when the coin slot USB cable is unplugged and plugged back in
- Daily log file of coins, sessions and connection problems
- Windows setup tab: creates a restricted "Customer" account, automatic sign-in and restrictions (needs administrator permission)
- Keyboard lock on the lock screen (Windows key, Alt+Tab, Ctrl+Esc) and covers for extra monitors

## What you need

- A Windows 10 or 11 PC
- A coin acceptor with a pulse output (developed with an Allan universal 12 V acceptor)
- A PC817 optocoupler isolation module
- An ESP8266 board (NodeMCU or Wemos D1 mini) or an Arduino Mega
- A USB data cable

The ESP8266 inputs are **3.3 V only**. Never connect 5 V or 12 V to an ESP8266 pin. See `Firmware/` for the sketch and wiring notes.

## Build

You need the .NET 8 SDK and a recent Visual Studio (or the `dotnet` command line).

1. Clone this repository.
2. Open `CafeSoftware.slnx` in Visual Studio and build, or run `dotnet build CafeSoftware/CafeSoftware.csproj`.
3. NuGet restores the packages (`CommunityToolkit.Mvvm` and `System.IO.Ports`) automatically.

To make a copy to install on a shop PC:

```
dotnet publish CafeSoftware/CafeSoftware.csproj -c Release -r win-x64 --self-contained true -o publish
```

## Firmware

The sketch is in `Firmware/CoinSlot/`. Open it in the Arduino IDE. It runs on the ESP8266 and on the Arduino Mega and picks the board automatically. It sends:

| Message | Meaning |
|---|---|
| `READY` | the board has started |
| `PULSES,<seq>,<count>` | a coin sent `<count>` pulses (repeated until acknowledged) |
| `PONG` | reply to `PING` |

CafeSoftware replies `ACK,<seq>` to each coin, so a coin is not lost and not counted twice.

## First run

1. Start the app. It starts in **test mode**, with test coin buttons on the lock screen.
2. Press **Ctrl+Shift+A** and log in with the default password `admin123`. The app asks you to choose a new one. **Do this**, because the default password is public.
3. Go through the Settings tabs: pricing, coin slot (COM port, coin table), session and general.
4. Insert each coin and add it to the coin table.
5. On the shop PC, use the **Windows setup** tab to create the Customer account.
6. Turn **test mode off** when you are done.

## Security notes

- The restrictions stop ordinary users, not a determined one. Ctrl+Alt+Del cannot be blocked by software.
- Run customers on a standard (non-administrator) Windows account, which is what the setup creates.
- The settings file is `C:\ProgramData\CafeSoftware\CafeSoftware.ini`. If you forget the admin password, delete it. That also resets every setting.

## Known limitations

- The remaining time is not saved. If the PC restarts, the time is lost.
- Sales totals are not recorded yet.
- Only tested on a bench so far, not in a real shop.

## License

MIT. See `LICENSE`.