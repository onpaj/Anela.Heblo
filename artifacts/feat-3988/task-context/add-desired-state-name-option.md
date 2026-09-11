### task: add-desired-state-name-option

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListOptions.cs`
- Modify: `backend/src/Anela.Heblo.API/appsettings.json:540`

- [ ] **Step 1: Add the `DesiredStateName` property to `PrintPickingListOptions`**

Open `backend/src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListOptions.cs`. It currently reads:

```csharp
namespace Anela.Heblo.Application.Features.ExpeditionList;

public class PrintPickingListOptions
{
    public const string ConfigurationKey = "ExpeditionList";

    public string EmailSender { get; set; } = string.Empty;
    public string PrintQueueFolder { get; set; } = string.Empty;
    public List<string> DefaultEmailRecipients { get; set; } = new();
    public int SourceStateId { get; set; } = -2;
    public int FixSourceStateId { get; set; } = 73;
    public int DesiredStateId { get; set; } = 26;
    public int NoteStateId { get; set; } = 35;
    public bool SendToPrinterByDefault { get; set; } = false;
    public bool ChangeOrderStateByDefault { get; set; } = true;
    public string PrintSink { get; set; } = "FileSystem"; // "FileSystem" | "AzureBlob" | "Cups"
    public string BlobConnectionString { get; set; } = string.Empty;
    public string BlobContainerName { get; set; } = "expedition-lists";
}
```

Add `DesiredStateName` immediately after `DesiredStateId` so the ID/name pair sits together:

```csharp
namespace Anela.Heblo.Application.Features.ExpeditionList;

public class PrintPickingListOptions
{
    public const string ConfigurationKey = "ExpeditionList";

    public string EmailSender { get; set; } = string.Empty;
    public string PrintQueueFolder { get; set; } = string.Empty;
    public List<string> DefaultEmailRecipients { get; set; } = new();
    public int SourceStateId { get; set; } = -2;
    public int FixSourceStateId { get; set; } = 73;
    public int DesiredStateId { get; set; } = 26;
    public string DesiredStateName { get; set; } = "Balí se";
    public int NoteStateId { get; set; } = 35;
    public bool SendToPrinterByDefault { get; set; } = false;
    public bool ChangeOrderStateByDefault { get; set; } = true;
    public string PrintSink { get; set; } = "FileSystem"; // "FileSystem" | "AzureBlob" | "Cups"
    public string BlobConnectionString { get; set; } = string.Empty;
    public string BlobContainerName { get; set; } = "expedition-lists";
}
```

- [ ] **Step 2: Pair `DesiredStateName` next to `DesiredStateId` in `appsettings.json`**

Open `backend/src/Anela.Heblo.API/appsettings.json`. The `"ExpeditionList"` section (around line 535) currently reads:

```jsonc
  "ExpeditionList": {
    "EmailSender": "heblo@anela.cz",
    "PrintQueueFolder": "PDFPrints",
    "SourceStateId": -2, // Vyrizuje se
    "FixSourceStateId": 73, // Oprava robot
    "DesiredStateId": 26, // Bali se
    "NoteStateId": 35, // Poznamka (neuplna adresa)
    "SendToPrinterByDefault": true,
    "ChangeOrderStateByDefault": true,
    "PrintSink": "AzureBlob",
    "BlobConnectionString": "DefaultEndpointsProtocol=https;AccountName=youraccountname;AccountKey=youraccountkey;EndpointSuffix=core.windows.net",
```

Add `"DesiredStateName"` immediately after `"DesiredStateId"`:

```jsonc
  "ExpeditionList": {
    "EmailSender": "heblo@anela.cz",
    "PrintQueueFolder": "PDFPrints",
    "SourceStateId": -2, // Vyrizuje se
    "FixSourceStateId": 73, // Oprava robot
    "DesiredStateId": 26, // Bali se
    "DesiredStateName": "Balí se",
    "NoteStateId": 35, // Poznamka (neuplna adresa)
    "SendToPrinterByDefault": true,
    "ChangeOrderStateByDefault": true,
    "PrintSink": "AzureBlob",
    "BlobConnectionString": "DefaultEndpointsProtocol=https;AccountName=youraccountname;AccountKey=youraccountkey;EndpointSuffix=core.windows.net",
```

This entry is documentary/symmetry-only — the C# default on `PrintPickingListOptions.DesiredStateName` already equals `"Balí se"`, so runtime behavior is unchanged whether or not this JSON key is present. Do **not** add this key to any Key Vault secret — it is not a secret, plain `appsettings.json` is the correct location per `CLAUDE.md`'s Key Vault rule (which only applies to actual secrets).

- [ ] **Step 3: Build to confirm the option compiles**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: `Build succeeded.` with no new warnings or errors.

- [ ] **Step 4: Commit**

```bash
cd backend
git add src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListOptions.cs src/Anela.Heblo.API/appsettings.json
git commit -m "feat(expedition-list): add DesiredStateName option paired with DesiredStateId"
```

---

