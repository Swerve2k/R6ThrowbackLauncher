# Remote popups

The launcher (3.0.0 and later) shows popups to everyone when they open it. There are two kinds.

## 1. New version popup (automatic)

Publish a GitHub release on `Swerve2k/R6ThrowbackLauncher` with a version tag higher than the one people have
(for example `v3.2.0`) and attach the installer as `OperationThrowback_Setup_v3.2.0.exe`.

On the next launch everyone gets:

- an "Update available" popup with your release notes and an **Update now** button,
- a banner across the top of every page,
- a number on the **Updates** tab.

The release notes (the release description on GitHub) are what people read, so write them for players.
Draft and pre-release releases are ignored.

## 2. Announcements (`broadcast.json`)

Put a file named `broadcast.json` in the **root of the `main` branch** of the repository. The launcher reads

```
https://raw.githubusercontent.com/Swerve2k/R6ThrowbackLauncher/main/broadcast.json
```

on every start. GitHub can take up to about five minutes to serve a changed file.

Start from the empty template in this folder and add messages:

```json
{
  "messages": [
    {
      "id": "servers-maintenance-2026-11-01",
      "type": "warning",
      "title": "Maintenance tonight",
      "message": "Friends and the leaderboard will be offline from 22:00 to 23:00 UTC.",
      "expires": "2026-11-02T00:00:00Z"
    },
    {
      "id": "please-update-3.2",
      "type": "update",
      "title": "Please update",
      "message": "Version 3.2.0 fixes downloads for Y8 seasons. Update now to keep downloading.",
      "maxVersion": "3.1.9"
    },
    {
      "id": "discord-invite",
      "type": "info",
      "title": "Join the Discord",
      "message": "Get help and find people to play with.",
      "buttonText": "Open Discord",
      "url": "https://discord.gg/your-invite"
    }
  ]
}
```

| Field | Required | Meaning |
|---|---|---|
| `id` | yes | Unique name. A message is shown once per person; change the id to show it again. |
| `type` | no | `info` (default), `update` or `warning`. Changes the icon and label. |
| `title` | yes* | Heading. |
| `message` | yes* | Body text. Plain text; line breaks with `\n`. |
| `buttonText` | no | Label for the main button. |
| `url` | no | `https://` link opened by the main button. Other schemes are ignored. |
| `minVersion` | no | Only show to launchers at this version or newer. |
| `maxVersion` | no | Only show to launchers at this version or older. Use it to nag old versions to update. |
| `expires` | no | Date/time (UTC) after which the message is no longer shown. |
| `showEveryLaunch` | no | `true` to show on every start instead of once. Use sparingly. |

\* At least one of `title` or `message`.

An `update` message with no `url` gets an **Update now** button when a newer release is available,
which runs the in-app updater.

To stop showing everything, set `"messages": []`. Launchers older than 3.0.0 don't read this file.
