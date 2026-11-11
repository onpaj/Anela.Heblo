## Module
Marketing

## Finding
`MarketingFolderType` in `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingFolderType.cs` defines values named for folder organizational scope (`General`, `Seasonal`, `ProductLine`, `Campaign`), but the frontend repurposes them as media asset categories with unrelated labels:

```csharp
// Domain enum (original intent: folder organizational scope)
General = 0
Seasonal = 1
ProductLine = 2
Campaign = 3
Other = 99
```

```tsx
// frontend/src/components/marketing/detail/MarketingActionModal.tsx:29-35
const FOLDER_TYPE_OPTIONS = [
  { value: MarketingFolderType.General,     label: "Obrázky" },   // Images
  { value: MarketingFolderType.Seasonal,    label: "Texty" },     // Texts
  { value: MarketingFolderType.ProductLine, label: "Videa" },     // Videos
  { value: MarketingFolderType.Campaign,    label: "Grafika" },   // Graphics
  { value: MarketingFolderType.Other,       label: "Ostatní" },
];
```

A developer reading the database sees the value `Seasonal` stored for a folder link; there is no way to know from the code that it means "Texty / Texts". Similarly, adding a new folder type requires guessing which organizational-scope name to repurpose.

## Why it matters
The domain model's enum values no longer describe what they represent — a new contributor or a debugging session must cross-reference the frontend label map to understand what any stored value means. This violates the principle of self-documenting domain types and will cause confusion when extending the type list.

## Suggested fix
Rename the enum values to match their actual usage (media asset categories), keeping the same integer values to avoid a migration:

```csharp
public enum MarketingFolderType
{
    Images = 0,      // was General
    Texts = 1,       // was Seasonal
    Videos = 2,      // was ProductLine
    Graphics = 3,    // was Campaign
    Other = 99,
}
```

Update the frontend `FOLDER_TYPE_OPTIONS` accordingly (values stay, only the C# name changes; the TypeScript generated enum names update with the next build). The DB stores integer values so no data migration is needed.

---
_Filed by daily arch-review routine on 2026-10-09._
