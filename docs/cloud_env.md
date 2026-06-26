# Cloud environment setup scripts

How a fresh cloud box (Claude Code on the web, Codespaces, CI, dev containers) is
provisioned to build and run the Anela Heblo backend + frontend.

Three scripts under `scripts/` cooperate. They are **not duplicates** — they run at
different points in the session lifecycle and have different scopes. Every step is
**idempotent**, so re-running is always safe.

## The three scripts

| Script | Role | Runs | Repo available? | Wired up via |
|---|---|---|---|---|
| `cloud-env-bootstrap.sh` | Pre-launch system toolchain (a cached subset) | Before Claude Code launches | ❌ No | Web UI **"Setup script"** field (paste contents) |
| `cloud-session-setup.sh` | Cloud-only guard + delegator | SessionStart (after launch) | ✅ Yes | `.claude/settings.json` SessionStart hook |
| `setup-cloud-env.sh` | The full, standalone provisioner (the real worker) | Invoked by the hook, or manually | ✅ Yes | Delegated to / run by hand |

## Lifecycle

```
┌─────────────────────────┐
│ cloud-env-bootstrap.sh  │  Pre-launch. Web UI "Setup script" field.
│                         │  No repo yet → repo-INDEPENDENT installs only.
│  • base apt + Skia libs │  Output is CACHED by the environment, so the slow
│  • .NET 8 SDK (/usr/local) │  toolchain installs persist across sessions.
│  • GitHub CLI           │
└────────────┬────────────┘
             │  Claude Code launches, repo checked out at $CLAUDE_PROJECT_DIR
             ▼
┌─────────────────────────┐
│ cloud-session-setup.sh  │  SessionStart hook (.claude/settings.json).
│  (thin guard)           │  No-op unless CLAUDE_CODE_REMOTE=true (keeps local fast).
│                         │  Then: SKIP_PLAYWRIGHT=1 exec setup-cloud-env.sh
└────────────┬────────────┘
             ▼
┌─────────────────────────┐
│ setup-cloud-env.sh      │  The full worker. Repo-DEPENDENT steps now possible.
│  • base apt + Skia libs │  (toolchain steps are no-ops if bootstrap already ran)
│  • .NET 8 SDK           │
│  • Node.js 20           │
│  • GitHub CLI           │
│  • dotnet restore       │  ← needs the repo
│  • npm install          │  ← needs the repo
│  • AgentHarness (pip)   │
│  • AgentHarness link    │  ← needs the repo (symlink mode)
│  • Playwright (optional)│
└─────────────────────────┘
```

## What each script does

### `cloud-env-bootstrap.sh` — pre-launch toolchain
Repo-*independent* system installs only. It must **not** reference repo files because
it runs before the repository is checked out. Paste its contents into the cloud
environment's **"Setup script"** field in the web UI.

- Base apt libs: `curl`, `ca-certificates`, `gnupg`, plus SkiaSharp natives
  (`libfontconfig1`, `libfreetype6`) for PDF generation.
- .NET 8 SDK → installed to `/usr/local` and symlinked to `/usr/local/bin/dotnet`
  so `dotnet` is on PATH for the hook, Claude, and shells without sourcing a profile.
- GitHub CLI from the official apt repo.

Its output is cached by the environment, so these installs persist across new sessions
without re-downloading.

### `cloud-session-setup.sh` — SessionStart hook entrypoint
A thin wrapper, wired up in `.claude/settings.json` under `SessionStart`
(matcher `startup|resume`, 600 s timeout).

- **Exits immediately (no-op) unless `CLAUDE_CODE_REMOTE=true`** — that env var is set
  only in Claude Code on the web, so local SessionStart stays fast.
- Resolves the repo root from `$CLAUDE_PROJECT_DIR` (falls back to the script's own
  location for manual runs).
- `exec`s `setup-cloud-env.sh` with `SKIP_PLAYWRIGHT=1` (Playwright browsers are heavy
  and only needed for E2E sessions).

**Why it exists:** the web "Setup script" field runs *before* the repo is reliably
checked out. Pointing that field at a repo path fails with exit 127, and a non-zero
setup script makes the whole session fail to start. Repo-dependent setup therefore
belongs in a SessionStart hook, which runs after launch with the repo present.

### `setup-cloud-env.sh` — the full provisioner
The complete, standalone, idempotent setup. A **superset** of bootstrap. Can run on its
own on any Debian/Ubuntu box (Codespaces, CI, dev container). Targets Debian/Ubuntu —
warns and exits on machines without `apt-get` (use the `start-*-dev.sh` scripts + brew
on macOS).

Steps (`main`), each skipping work already done:

1. `install_base` — apt libs + `python3`/`python3-pip` (AgentHarness needs pip).
2. `install_dotnet` — .NET 8 SDK → `$HOME/.dotnet`, appends `DOTNET_ROOT`/`PATH` to `~/.bashrc`.
3. `install_node` — Node.js 20 via NodeSource (matches the Dockerfile).
4. `install_gh` — GitHub CLI.
5. `install_agentharness` — `pip install --upgrade` from `github.com/onpaj/harness@master`
   (`--break-system-packages` for PEP 668). Repo-independent, so it runs *before*
   `require_repo`; `--upgrade` pulls the latest master each session.
6. `require_repo` — fails fast if `Anela.Heblo.sln` isn't found (i.e. it was wrongly
   used as a pre-launch setup field).
7. `init_agentharness` — `agentharness init --symlink --force` links each shipped agent/skill
   from the installed package and records them in a managed `.gitignore` block, so the
   `--upgrade` above keeps the repo current with nothing to re-commit; `--force` converts
   any leftover committed copies in place. Owner/repo/token auto-detected (gh auth + git
   remote); blank lines guard the `.env` prompt under `set -e`. Needs the repo, so it runs
   after `require_repo`.
8. `restore_backend` — `dotnet restore Anela.Heblo.sln`.
9. `restore_frontend` — `npm install --legacy-peer-deps` in `frontend/` (matches Dockerfile).
10. `install_playwright` — `npx playwright install --with-deps chromium`, unless
   `SKIP_PLAYWRIGHT=1`.

Usage:

```bash
./scripts/setup-cloud-env.sh                    # full setup
SKIP_PLAYWRIGHT=1 ./scripts/setup-cloud-env.sh  # skip Playwright browser download
```

## Overlap is deliberate

bootstrap and `setup-cloud-env.sh` share three install routines (base apt, .NET, gh).
That is intentional:

- **bootstrap** runs the slow, repo-independent toolchain installs *once*, pre-launch,
  where the output is **cached** across sessions.
- **`setup-cloud-env.sh`** re-checks the same tools, but each install early-returns when
  the tool is already present — so after bootstrap those steps are no-ops, and only the
  repo-dependent work (Node, restore/install, AgentHarness, Playwright) actually runs.

One key difference: bootstrap installs .NET to `/usr/local` (globally on PATH without a
profile), while standalone `setup-cloud-env.sh` installs to `$HOME/.dotnet` and writes
to `~/.bashrc` — because it is also meant to run **standalone** on a generic box where
no bootstrap ever ran.

## Mental model

- **bootstrap** prepares the *machine* before the repo exists (cached toolchain slice).
- **session-setup** is the cloud-only trigger that fires once the repo is checked out.
- **setup-cloud-env** is the complete provisioner — the full version bootstrap front-loads
  a slice of, and the only one that touches repo-dependent dependencies.

## Verify the toolchain

```bash
dotnet --version    node -v    npm -v    gh --version
```
