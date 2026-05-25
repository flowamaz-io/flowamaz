---
title: Install Community Edition
updated: 2026-05-24
readingTime: "4 min read"
plans: "All plans"
---

# Install Community Edition

Community Edition is free and runs entirely on your own machine or server. It includes
the full workflow engine, the `fmz` CLI, the visual canvas, and Git-native versioning.

## Prerequisites

You need **either** of:

- **Docker** (Docker Desktop on Mac/Windows, or Docker Engine on Linux), or
- **Node 20+** if you prefer the binary install.

## Method 1 — Docker Compose (recommended, one command)

```bash
curl -fsSL https://get.flowamaz.io/community | sh
```

This downloads a Docker Compose bundle and starts every service (engine, database,
Redis, web UI). When it finishes, open `http://localhost:3000`.

## Method 2 — Homebrew (macOS / Linux)

```bash
brew install flowamaz/tap/fmz
fmz start
```

`fmz start` launches the local stack and prints the URL once it is ready.

## Method 3 — Manual / VPS

1. Download the binary for your platform from the releases page.
2. Make it executable and move it onto your PATH:
   ```bash
   chmod +x fmz
   sudo mv fmz /usr/local/bin/fmz
   ```
3. Run it as a systemd service so it survives reboots:
   ```ini
   # /etc/systemd/system/flowamaz.service
   [Unit]
   Description=Flowamaz Community Edition
   After=network.target

   [Service]
   ExecStart=/usr/local/bin/fmz start --port 3000
   Restart=always
   User=flowamaz

   [Install]
   WantedBy=multi-user.target
   ```
   Then enable it:
   ```bash
   sudo systemctl enable --now flowamaz
   ```

## After install

1. Open `http://localhost:3000`.
2. Create your organisation (name and a short URL slug).
3. Sign in with the first-user credentials you set during registration.
4. You now have one workspace ready — start describing your first workflow.

## Troubleshooting

- **"Port 3000 already in use."** Another app is on that port. Start on a different one:
  `fmz start --port 3100`, then open `http://localhost:3100`.
- **"Cannot connect to the Docker daemon."** Docker isn't running. Start Docker Desktop
  (Mac/Windows) or run `sudo systemctl start docker` (Linux), then retry the installer.

## Community Edition limits

Community runs 5 workflows, 500 runs per month, 1 workspace, and 1 user, with 7-day run
history and Claude Haiku via your own API key. When you need teams, SSO, or more
capacity, see [Migrate Community to Cloud](migrate-community-to-cloud).

---

Was this helpful? 👍 👎

[Edit on GitHub ↗](https://github.com/flowamaz-io/docs/blob/main/docs/getting-started/install-community-edition.md)
