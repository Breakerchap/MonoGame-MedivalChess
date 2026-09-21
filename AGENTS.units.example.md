# Unit implementation instructions

## Unit source of truth

- `units_codex.json` is the authoritative current unit roster/specification.
- Use `unit_id` as the stable global identifier.
- Never assume `abbreviation` is globally unique.
- Prefer data-driven definitions for static unit stats.
- Reuse shared game systems and reusable ability hooks/components instead of duplicating mechanics.
- Never invent unspecified unit values; record a blocker and continue with other work.
- When unit behavior changes, update or add tests and keep `docs/unit-implementation-audit.md` current.
