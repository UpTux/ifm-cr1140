# Domain docs

How the engineering skills consume domain documentation in this repo.

## Layout

This repo uses **multiple contexts** (a Rust cargo workspace with several crates).

- `CONTEXT-MAP.md` at the repo root is the index — it points to each crate's `CONTEXT.md`.
- Each crate has its own `CONTEXT.md` describing that crate's domain language and key concepts:
  - `cr1140-hal/CONTEXT.md`
  - `cr1140-sdk/CONTEXT.md`
  - `cr1140-slint/CONTEXT.md`
  - `cr1140-slint-demo/CONTEXT.md`
- `docs/adr/` holds Architecture Decision Records (one file per decision), shared across the workspace.

## Consumer rules

Skills that read these docs (`improve-codebase-architecture`, `diagnose`, `tdd`, …):

1. **Read `CONTEXT-MAP.md` first** to find the right context for the crate you're working in.
2. **Then read that crate's `CONTEXT.md`** to pick up its domain language before starting work.
3. **Check `docs/adr/`** for past decisions before proposing architectural changes.
4. **Treat ADRs as immutable history** — supersede with a new ADR, don't edit old ones.
5. **Use the domain language** from the relevant `CONTEXT.md` in code, comments, and issues.
6. If the relevant `CONTEXT.md` is missing or stale, flag it — don't guess silently.
