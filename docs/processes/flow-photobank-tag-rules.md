---
process: flow-photobank-tag-rules
kind: workflow
module: photobank
summary: Admin maintains folder-path → tag rules for the photobank and re-applies them, which deletes and recomputes every Rule-sourced photo tag in PhotoTags while leaving Manual and AI tags untouched.
owns:
  - backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/AddRule/**
  - backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/UpdateRule/**
  - backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/DeleteRule/**
  - backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/GetRules/**
  - backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/ReapplyRules/**
  - backend/src/Anela.Heblo.Application/Features/Photobank/Validators/AddRuleRequestValidator.cs
  - backend/src/Anela.Heblo.Application/Features/Photobank/Validators/UpdateRuleRequestValidator.cs
  - backend/src/Anela.Heblo.Domain/Features/Photobank/TagRule.cs
  - backend/src/Anela.Heblo.Domain/Features/Photobank/TagRuleMatcher.cs
  - backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankTagRuleRepository.cs
  - backend/src/Anela.Heblo.Persistence/Photobank/PhotobankTagRuleRepository.cs
  - backend/src/Anela.Heblo.Persistence/Photobank/TagRuleConfiguration.cs
verified_at: "5e993f9e2"
related:
  - sync-photobank-index
  - job-photobank-auto-tag
---

# Photobank tag rules and re-apply

## Purpose
Lets marketing tag whole folders at once: "everything under `PROFI_FOCENI/Produkty/` gets tag
`produkty`". Rules are kept on the photobank settings page, tab "Tag Rules"
(`/marketing/photobank/settings`, `Marketing_Photobank` Admin). The resulting tags have source
**Rule** and show in the gallery `/marketing/photobank` like any other tag.

## Trigger
User-driven, Admin only:
1. **Add / edit / delete a rule** — `POST`, `PUT /{id}`, `DELETE /{id}` on
   `api/photobank/settings/rules`. Only the rule row changes; **no photo is retagged yet**.
2. **Re-apply all rules** — button "Re-aplikovat pravidla" → `POST
   api/photobank/settings/rules/reapply`. Synchronous; the page shows "Pravidla aplikována na N
   fotek".
3. **Re-apply one rule** — `POST api/photobank/settings/rules/{id}/reapply`. API only; the UI
   does not call it.

Independently, `sync-photobank-index` applies the current active rules to every photo the
nightly SharePoint delta reports as new or changed.

## Data flow
Rule edit → `public."PhotobankTagRules"`.

Re-apply (`ReapplyRulesHandler`):
1. Read all rules; for a single-rule run, scope = that rule's tag name (error
   `PhotobankRuleNotFound` if the id does not exist).
2. Delete `public."PhotoTags"` rows with `Source = Rule` — all of them, or only those whose tag
   has the scoped name — and **commit**.
3. If no active rule (in scope) remains: stop, return 0.
4. Load the (PhotoId, TagId) pairs that already exist as Manual or AI; get-or-create the rule tag
   names in `public."PhotobankTags"`.
5. Page through all of `public."Photos"` by `Id` (2,000 per page), match `FolderPath/FileName`
   against the active rules, collect new `Rule` rows (skip pairs that exist as Manual/AI).
6. Insert all collected rows in one save, invalidate the tag-count cache
   `Photobank:Tags:WithCounts`, return the number of photos that received at least one Rule tag.

## Logic & formulas
- **Matching** (`TagRuleMatcher`): virtual path `FolderPath + "/" + FileName` (no leading slash;
  `FolderPath` is relative to the drive root, e.g. `Grafika_interní/PROFI_FOCENI/Produkty`).
  `PathPattern` is a .NET regular expression, case-insensitive, culture-invariant, matched
  anywhere in the path unless anchored with `^`. Example from the feature doc:
  `^PROFI_FOCENI/Produkty/[^/]+(/|$)`.
- **All** matching active rules apply (not only the first); `SortOrder` only orders evaluation.
  Several rules may produce the same tag.
- Tag names are trimmed and lowercased on save; tags are created on first use.
- Validation: `PathPattern` required, ≤ 500 characters, must compile as a .NET regex;
  `TagName` required, ≤ 100 characters.
- Inactive rules (`IsActive = false`, only via edit) are ignored by both re-apply and the index.
- Manual and AI tags are never deleted by re-apply; a Rule tag is not created where the photo
  already has that tag as Manual/AI.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `PhotobankTagRules` rows | none (DB) | The rules themselves |
| `Photobank:TagsCache:TtlSeconds` | 60 | Tag-count cache TTL (invalidated by re-apply anyway) |

## Runtime facts
None.

## Known quirks
- **Rule changes do nothing until re-apply.** Adding, editing, deactivating or deleting a rule
  leaves existing Rule tags as they were; only photos the nightly index sees as changed pick up
  the new rule set. Press "Re-aplikovat pravidla" after editing rules.
- **Single-rule re-apply is scoped by tag name, not by rule.** It recomputes every Rule tag
  with that name, including matches of other active rules that produce the same tag.
- **Not atomic.** Step 2 commits the deletion before new tags are inserted; if the request fails
  or times out after that (large library, browser closed), photos are left without Rule tags
  until the next successful re-apply.
- **Runs inside the HTTP request** over the whole library (all photos in memory pages of 2,000,
  all new tags in one insert). Fine for the stated 1,000–20,000 photos; nothing protects against
  two admins re-applying at once.
- **A manually removed Rule tag comes back** at the next re-apply or index touch.
- **Gallery regex search is a different engine**: the gallery's regex search runs in PostgreSQL
  (`Regex.IsMatch` translated to a case-insensitive Postgres regex), while rules use .NET regex;
  a pattern can behave slightly differently in the two places.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/ReapplyRules/ReapplyRulesHandler.cs` — delete-then-recompute
- `backend/src/Anela.Heblo.Domain/Features/Photobank/TagRuleMatcher.cs` — matching rules
- `backend/src/Anela.Heblo.Persistence/Photobank/PhotobankPhotoTagRepository.cs` — `RemoveRuleTagsAsync`, `GetOccupiedTagPairsAsync`
- `backend/src/Anela.Heblo.Application/Features/Photobank/Validators/AddRuleRequestValidator.cs` — pattern validation
- `frontend/src/components/marketing/photobank/settings/TagRulesTab.tsx` — rules UI and re-apply button
