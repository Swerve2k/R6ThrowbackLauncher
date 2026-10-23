# Changelog

All notable changes to R6 Throwback Launcher are documented here.

---

## [3.0.0] – 2026-10-23

A full redesign with a black, grey and white interface, a rebuilt download system, Steam-linked profiles and a new updater.
The launcher now calls itself Swerve2k Downloader.

### Interface
- New monochrome design with dark and light themes, switchable live from Settings
- Native window chrome: Windows snap layouts, rounded corners, proper maximize behavior
- Line icons throughout; no emoji in the interface
- Season art is in color by default (or greyscale until hover, or always greyscale)
- Picture cards zoom in slightly on hover
- Interface size (80 to 125 percent) and reduce motion
- Themed dialogs replace the standard Windows message boxes

### Library and seasons
- New season page with Overview and Manage tabs
- Overview lists release date, new operators and gadgets, maps, highlights, in-build events and requirements
- Manage tab: renderer choice (DirectX or Vulkan, DirectX 12 or 11), launch options, per-install in-game name, notes, game settings editor
- Browse page with filters for installed, not installed and Heated Metal
- Library filters for favorites, Heated Metal and incomplete downloads, plus grid and list views
- Added Y10S4 Tenfold Pursuit, Y11S1 Silent Hunt and Y11S2 System Override
- Build numbers and download sizes for every season

### Downloads
- Persistent download queue with reordering, real pause and resume, and cancel
- Free space check before downloading
- Multiple library folders with a default folder and a picker per download
- Verify files from the season page or the queue
- Free up space: remove intro videos, event playlist data or high texture tiers from an install
- Russian language files are optional; a failure there no longer fails the whole download
- Approving sign-in in the Steam Mobile app no longer triggers a code prompt
- Detection of incomplete downloads and of folders from older launcher versions

### Profiles, friends and leaderboard
- Sign in through Steam: profiles, friends and stats are tied to your Steam account, so typing someone's name no longer gets you their stats
- Stats from before 3.0 carry over: your old profile is linked to your Steam account after a Steam login proves the account name is yours, and each old profile can be linked only once
- Old friendships are restored once both people have linked their Steam accounts
- Display name setting, shown in the sidebar, on your profile and on the leaderboard; the Steam login name is never shown
- New Friends page with friend requests, player search by name or Steam profile link, and chat
- Player profile pages with stats, leaderboard rank, installed seasons and bio; open one by clicking a name on the leaderboard
- Leaderboard search, online status, and old profiles waiting to be linked (old login names are partly hidden)
- The title bar shows how many players are online right now
- Profile pictures and banners were removed; everyone uses the default person icon

### Updates
- Update banner across the top of every page and a count of available updates on the Updates tab
- Popup when a new version is out, with release notes and one-click install
- Full-screen update progress with download speed, time left, verify, install and restart steps
- "What's new" popup after updating
- Announcements published from the repository (`broadcast.json`) pop up when the launcher starts
- The updater runs the installer silently and the launcher restarts when it finishes

### Accounts and security
- Saved Steam password is encrypted with Windows DPAPI; existing plaintext passwords are migrated
- Sign out clears DepotDownloader's saved Steam session
- In-game names are validated so they can't break loader config files

### Storage
- Storage page shows drive usage per library and finds unrecognized folders you can relink or remove

### Removed
- AI Help, News, Party, Feedback, bug reporter, Guide, Credits and Tools
- Window outline, background image and keyboard shortcut list settings

---

## [2.1.0] – 2026-04-13

### Added

#### Leaderboard Tab
- New **Leaderboard** tab in the sidebar (🏆) showing community rankings pulled live from Firebase
- Three sort modes: **Total Playtime**, **Sessions Played**, and **Versions Installed**
- Rank cards display username, stat value, favourite season, and banner colour accent
- Refresh button to force re-fetch leaderboard data

#### Friends Slideout Panel
- Quick-access friends panel slides in from the right when clicking the Friends button in the sidebar
- Shows online friends with their current status at a glance
- "Open Full Friends Panel" footer button opens the full `FriendsWindow` for chat

#### Live Chat Polling
- Active chat conversations now poll Firebase every 4 seconds for new messages
- Only new messages are appended — no full re-render flicker
- Polling stops automatically when closing the chat or the window

#### Widget Visibility Toggles
- **⚙ WIDGETS** button added to the library header toolbar
- Toggle **Hero Banner** and **Recently Played** section on/off from a quick popup
- Visibility preferences are saved to user settings and persist between sessions

#### System Tray
- Right-clicking the tray icon shows a context menu with **Open** (bold default) and **Exit** options

#### Window Edge Resize
- The launcher window can now be resized by dragging any edge or corner
- Implemented via a native `WndProc` hook handling `WM_NCHITTEST`

#### Intro Splash Screen
- Short branded splash screen shown on launch while the app initialises
- Displays loading status phases: manifest load → install scan → ready
- Fades out smoothly when the main window is ready

### Fixed

- **Y9S2 Heated Metal not launching** — HM files from the bundled archive are now automatically extracted into the install directory during post-processing. No verification step required.
- **Removed Heated Metal Verification** — The ownership-gate system has been removed entirely. Y9S2 Heated Metal and Y5S4 Heated Metal download and launch without any verification.
- **Y5S4 Heated Metal artwork** — Season Y5S4 Heated Metal now displays the standard Neon Dawn artwork instead of the Heated Metal logo.
- **Taskbar visibility when maximised** — Maximising the window no longer covers the Windows taskbar. The window is constrained to `SystemParameters.WorkArea`.
- **Remember Password** — The "Remember Password" checkbox now correctly saves and restores the Steam password across sessions.
- **Prerequisites auto-download** — Before each download, the launcher silently checks for and downloads Plazas bypass files and Quality filelists if missing (mirrors the `idk.bat` prerequisite check from the batch downloader).
- **Auto-updater** — Fixed: uses `Environment.ProcessPath`, adds retry loop, and correctly launches the new version after update.

### Removed

- **Heated Metal Early Access system** — All HM detection, classification, ownership-gating UI cards, badges, and code paths have been removed. HM builds are now standard downloadable entries.

---

## [2.0.0] – Prior release

Initial public release with library, download manager, profiles, friends, Discord RPC, wallpaper, and theming.
