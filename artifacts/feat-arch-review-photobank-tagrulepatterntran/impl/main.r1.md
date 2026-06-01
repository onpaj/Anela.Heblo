The session has no write permissions granted for any file operations — every attempt to write, edit, or delete files requires user approval that isn't coming through. Based on the git log, previous agent phases (arch-review, task-plan creation) ran with different permissions.

Here's the complete implementation summary:

---

## Implementation: Remove TagRulePatternTranslator + Fix Photobank Docs

### Pre-verification (completed)
All pre-conditions from spec.r3.md and arch-review.r1.md were verified:
- Zero production references: `grep -rn "TagRulePatternTranslator" backend/src/` returns only the class definition itself
- Both target files confirmed at expected paths
- `docs/features/photobank.md` Tag Rules section contains stale glob-style docs (lines 80–88)
- No DI registration, no Program.cs references, no csproj `<Compile Include>` entries

### Changes needed

**1. Delete two files:**
```bash
git rm backend/src/Anela.Heblo.Domain/Features/Photobank/TagRulePatternTranslator.cs
git rm backend/test/Anela.Heblo.Tests/Features/Photobank/TagRulePatternTranslatorTests.cs
```

**2. In `docs/features/photobank.md`, replace line 84:**

*Old:*
```
- **Vzor cesty** — např. `/PROFI_FOCENI/Produkty/*` (hvězdička nahrazuje jeden segment cesty)
```

*New:*
```
- **Vzor cesty** — regulární výraz kompatibilní s .NET (ne glob). Vzor se porovnává s cestou ve formátu `složka/soubor.jpg` (bez úvodního lomítka). Neplatný regulární výraz je odmítnut při uložení pravidla.
  - Příklad: `^PROFI_FOCENI/Produkty/[^/]+(/|$)` — `^` kotví na začátek cesty, `[^/]+` odpovídá právě jednomu segmentu (bez lomítek), `(/|$)` zajistí hranici segmentu.
```

**3. Verify:**
```bash
grep -rn "TagRulePatternTranslator" backend/   # expect zero results
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Photobank" --no-build
```

**4. Commit:**
```bash
git add -A
git commit -m "refactor(photobank): remove dead TagRulePatternTranslator and fix docs

The translator was a one-time migration utility (glob→regex) with zero
production callers. Removes the class and its test suite.

Updates docs/features/photobank.md Tag Rules section to describe the
actual .NET regex contract instead of the stale glob-with-asterisk example."
```

---

## Status: BLOCKED

The agent session does not have write/delete permissions for existing tracked files. Every `git rm`, `Edit`, and `Write` attempt triggered an approval prompt that was denied (or not responded to). The implementation is ready to execute but requires the user to either run the commands above directly, or grant file modification permissions to this agent session.