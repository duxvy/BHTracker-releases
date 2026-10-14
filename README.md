# Installing BrawlScout

BrawlScout shows your Brawlhalla opponents' stats on the loading screen, by itself. It reads the names off your screen
and looks them up with the official Brawlhalla API. It never touches the game.

You need Windows 10 or 11 (64-bit). You don't need admin rights or anything else installed.

## 1. Download

Get **BrawlScout-Setup.exe** from the latest release: **Releases** on this page, then **BrawlScout-Setup.exe** under
*Assets*. It's about 145 MB.

Your browser may say the file "isn't commonly downloaded". Choose **Keep** (in Edge: **…** → **Keep** → **Show more** →
**Keep anyway**).

## 2. Run it

Double-click **BrawlScout-Setup.exe**. Windows will probably show a blue box:

> **Windows protected your PC**
> Microsoft Defender SmartScreen prevented an unrecognized app from starting.

That's because BrawlScout is new and not signed with a paid certificate, not because anything is wrong. Click
**More info**, then **Run anyway**.

Setup takes a few seconds. It installs BrawlScout for your Windows account (about 550 MB on disk) and adds it to the
Start menu, then starts it.

## 3. First start

A short setup opens:

1. **Find yourself**: type your in-game name and pick your account, so your own card is skipped and BrawlScout knows
   your Elo. You can skip this.
2. Pick your **region** and a **card style**.
3. **Display check**: set Brawlhalla to **windowed** or **borderless** (in Brawlhalla: Settings → Display → Fullscreen
   **off**). In true fullscreen, Windows hides the game from BrawlScout.
4. Optionally add a **desktop shortcut**, and have BrawlScout **start with Windows** (it waits quietly in the tray).

Then play. When a match's loading screen shows up, the cards appear by themselves. **F8** scans whenever you want,
**Esc** hides the cards, and **F9** shows every legend. You can change all of this in **Settings**.

BrawlScout reads names with Windows' built-in text recognition for English. An English Windows already has it. If
yours is in another language and BrawlScout says it can't read names, add **English** in Windows Settings → Time &
language → Language & region → **Add a language**.

## Updates

BrawlScout updates itself. New versions download in the background, never while Brawlhalla is in front, and install
the next time you start BrawlScout. When one is ready, BrawlScout says so, and **Restart now** installs it at once.
Your settings stay as they are. **Settings → About** shows your version and what's new.

## Uninstalling

Open Windows Settings → Apps → **Installed apps** → **BrawlScout** → **Uninstall**. BrawlScout then asks whether to
delete your settings too. **No** keeps them, so a reinstall picks up where you left off.

## Something wrong?

- **No cards on the loading screen**: Brawlhalla has to be windowed or borderless, not fullscreen. Run
  **Diagnostics** from the BrawlScout window.
- **F8 does nothing**: another app may own F8 (GameClipper does). Pick another key in **Settings**.
- **Your antivirus complains**: new, unsigned apps sometimes get flagged. BrawlScout only takes screenshots of the
  game window and reads them.
- **Anything else**: **Settings → Help → Create bug report** makes a zip to send. Your name is in it, but your
  Brawlhalla ID isn't.

BrawlScout is not affiliated with Blue Mammoth Games or Ubisoft.
