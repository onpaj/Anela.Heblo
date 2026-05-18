---
name: baleni-module-touch-design
description: Balení module is touch-first and landscape-oriented — binding UI constraints for all work in frontend/src/components/baleni/
metadata:
  type: project
---

All UI in `frontend/src/components/baleni/` must be:

- **Touch-first**: interactive elements ≥44px, tiles ≥160px tall, no hover-only affordances
- **Landscape-oriented**: wide containers (`max-w-5xl`), multi-column grids (`grid-cols-3`), landscape PWA manifest (`orientation: landscape`)
- **PWA scope**: `/baleni` served by `manifest.baleni.json`

**Why:** This module targets a landscape touch PC at the packing station, not a portrait handheld like the terminal module.

**How to apply:** Every component added under this path must honor these constraints. Never apply portrait/narrow layouts or small touch targets here.
