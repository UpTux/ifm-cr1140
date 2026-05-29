# Licensing

Copyright (C) 2026 Patrick Dahlke.

This workspace is **dual-licensed**. There are two ways to use it:

1. **Open source — free of charge — under the GNU GPL v3.**
   If your project complies with the GPLv3 (i.e. it is itself open source under a
   GPL-compatible license), you may use these crates at no cost under the terms in
   each crate's `LICENSE` file.

2. **Commercial — paid — for closed-source / proprietary use.**
   If you cannot or do not wish to comply with the GPLv3 (for example, you ship a
   closed-source product), you need a commercial license. **Contact
   <me@patrickdahlke.com>** to arrange one.

## Per-crate status

| Crate | License | Notes |
|-------|---------|-------|
| `cr1140-hal` | **GPL-3.0-only OR Commercial** | The hardware abstraction layer. Dual-licensed as above. |
| `cr1140-sdk` | **GPL-3.0-only OR Commercial** | App conveniences (LED, telemetry, device info). Dual-licensed as above. |
| `cr1140-slint` | **GPL-3.0-only** | Links [Slint](https://slint.dev) under its GPLv3 option; must be GPLv3. |
| `cr1140-slint-demo` | **GPL-3.0-only** | Binary that links Slint; must be GPLv3. |

The `license` field in each `Cargo.toml` records `GPL-3.0-only` because that is
the license actually distributed in this repository; the commercial option is
granted separately by agreement (this file is the offer).

## Important: Slint is licensed separately

`cr1140-slint` and `cr1140-slint-demo` depend on **Slint**, which is itself
dual-licensed by SixtyFPS GmbH (GPLv3 / royalty-free / paid commercial). A
commercial license for *this* workspace from Patrick Dahlke does **not** cover
Slint. A closed-source product that links Slint must **also** obtain an
appropriate Slint license from <https://slint.dev>.

## Contributions

Unless stated otherwise, contributions are accepted under the GPLv3. To keep the
commercial-licensing option viable, the maintainer may ask contributors to sign a
Contributor License Agreement (CLA) assigning or licensing their contribution so
it can be offered under both the GPLv3 and the commercial license.
