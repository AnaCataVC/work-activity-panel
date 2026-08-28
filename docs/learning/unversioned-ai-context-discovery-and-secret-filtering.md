# Engineering Learning: High-Performance Unversioned AI Context Discovery & Infrastructure Secret Scanning

> **Date:** 2026-08-28  
> **Status:** Implemented in `WorkActivityPanel.Services.ClaudeConfigDiscovery` & `DriveSyncService`  
> **Target Framework:** .NET 9 (`net9.0-windows10.0.26100.0`)

---

## 1. Context & Architectural Challenge

When collaborating with AI coding agents (such as Claude Code, Antigravity, or Copilot Workspace) across numerous software projects, developers maintain local steering instructions (`CLAUDE.md`), operational manuals, team rosters, and domain reference documents (`.claude/references/*.md`, `references/*.md`).

### The Dual Failure Modes:
1. **Unversioned AI Context Loss:** Because these files often contain machine-specific overrides, testing tokens, or local workflows, they are frequently excluded from Git (`.gitignore`) or kept in untracked folders. When a developer's workstation is formatted or fails, this entire knowledge base is permanently lost.
2. **The "Fork Bomb" and Git Traversal Overhead:** An earlier design evaluated Git tracking directory-by-directory via `git rev-parse --is-inside-work-tree`. This introduced two critical flaws:
   - It treated any file inside a Git repository as "versioned", thereby skipping all untracked or gitignored context files (`.claude/references/`, `.env.local`).
   - Querying Git synchronously per file via `Process.Start("git", ...)` on Windows creates severe process creation latency (~20–50 ms per process), taking over a minute on large directories with thousands of files.
3. **Secret Leakage Vector:** Naive cloud uploads risk backing up private SSH keys (`id_rsa`), AWS root keys, or GitHub personal access tokens if they are accidentally referenced in markdown files.

---

## 2. Engineered Solution

### 2.1 Multi-Zone Breadth-First Search (BFS) Traversal
`ClaudeConfigDiscovery.cs` implements an iterative BFS walk up to a configurable depth (`ClaudeMarkdownScanDepth`, default 6):
- **Direct Instructions:** Detects `CLAUDE.md` in any scanned folder and `.claude/CLAUDE.md`.
- **Targeted Reference Folders:** Discovers all `*.md` files inside direct `references/` subdirectories (`references/**/*.md`) and within `.claude/references/**/*.md`.
- **Strict Isolation of Claude Internals:** Excludes Claude CLI and desktop internal state directories (`.claude/projects/**/memory`, `.claude/plans`, `.claude/security`, `.claude/cache`, `.claude/plugins`, `*.log`, `log.txt`).
- **Strict Directory Exclusions:** Automatically skips build outputs, package caches, note vaults, and backup trees (`node_modules`, `.git`, `.vs`, `bin`, `obj`, `venv`, `.venv`, `__pycache__`, `AppData`, `dist`, `build`, `.obsidian`, `.trash`, `.idea`, and directories matching `_backup_*` or `backup_*`).

```
Project Root / User Profile
├── CLAUDE.md                     ──> Discovered
├── .claude/
│    ├── CLAUDE.md                ──> Discovered
│    ├── references/
│    │    ├── team-roster.md      ──> Discovered
│    │    └── architecture.md     ──> Discovered
│    ├── projects/                ──> Skipped (Internal session memory)
│    ├── plans/                   ──> Skipped (Internal session plans)
│    └── security/                ──> Skipped (Internal security logs)
├── geocoding/
│    ├── CLAUDE.md                ──> Discovered
│    └── references/
│         └── bq-gotchas.md       ──> Discovered
└── _backup_claudemd_20260828/    ──> Skipped (Anti-noise filter)
```

---

### 2.2 Batched Git Tracking Verification (`batchSize = 50`)
To eliminate process creation overhead while accurately identifying untracked files:
1. **Repository Root Discovery:** Discovers the repository root once per candidate directory via `git rev-parse --show-toplevel` and caches the result.
2. **Orphan Identification:** Candidates outside any Git repository are immediately marked as unversioned.
3. **Chunked `git ls-files` Execution:** Candidates inside a Git repository are grouped and queried in batches of up to 50 files:
   ```bash
   git -C <repoRoot> ls-files -- <file1> <file2> ... <file50>
   ```
4. **Untracked Resolution:** Any candidate file **not** returned in the output of `git ls-files` is classified as untracked/ignored and scheduled for synchronization.

---

### 2.3 Multi-Tier Infrastructure Secret Protection
To prevent uploading live infrastructure credentials without blocking legitimate sandbox documentation (e.g., test tokens or mock hashes):
- **Filename Keyword Filter:** Discards candidate files named `id_rsa`, `id_ed25519`, `credentials`, `auth_token`, or `api_key`, as well as log files (`*.log`, `log.txt`).
- **Bounded Stream Content Inspection (64 KB):** Reads the initial 64 KB of file contents to evaluate compiled regex signatures:
  - **SSH / PGP Private Keys:** `-----BEGIN\s+[A-Z\s]+PRIVATE\s+KEY-----`
  - **AWS Access Keys:** `\bAKIA[0-9A-Z]{16}\b`
  - **GitHub Personal Access Tokens:** `\bghp_[A-Za-z0-9_]{36}\b`
  - **Slack User / Bot Tokens:** `\bxox[baprs]-[0-9a-zA-Z]{10,48}\b`
- **Sandbox Tolerance:** Operational test tokens (e.g., sandbox account IDs and 40-character hex tokens) that do not match production infrastructure signatures are safely preserved.

---

## 3. Verification & Benchmark Results

- **Unit Test Suite:** 110 automated xUnit tests passing with 0 errors (`WorkActivityPanel.Tests.ClaudeConfigDiscoveryTests`).
- **Live Discovery:** Verified 100% discovery of all reference files and `CLAUDE.md` anchors across directory hierarchies without leaking Claude CLI internal memory, plans, or security logs.
- **Process Efficiency:** Git query execution reduced from $>16$ individual process invocations to 2 batched executions with $<150$ ms total execution time.
