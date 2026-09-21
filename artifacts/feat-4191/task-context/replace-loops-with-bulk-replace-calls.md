### task: replace-loops-with-bulk-replace-calls

Replaces the two per-item loops in `CreateMarketingActionHandler.Handle` with the two bulk-replace calls, matching `UpdateMarketingActionHandler` exactly.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs` (lines 59–65)

1. Open `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs`. Lines 59–65 currently read:

   ```csharp
            if (request.AssociatedProducts?.Any() == true)
                foreach (var product in request.AssociatedProducts.Distinct())
                    action.AssociateWithProduct(product, now);

            if (request.FolderLinks?.Any() == true)
                foreach (var link in request.FolderLinks)
                    action.LinkToFolder(link.FolderKey.Trim(), link.FolderType, now);
   ```

   Replace exactly this block with:

   ```csharp
            action.ReplaceProductAssociations(request.AssociatedProducts, now);
            action.ReplaceFolderLinks(
                request.FolderLinks?.Select(l => (l.FolderKey, l.FolderType)),
                now);
   ```

   No other line in the file changes — the surrounding `action` construction (lines 49–57) and the Outlook sync block (line 67 onward) are untouched. Do not add a `using System.Linq;` line: `ImplicitUsings` is enabled for this project (confirmed in `Anela.Heblo.Application.csproj`), so `.Select` resolves the same way `UpdateMarketingActionHandler.cs` already relies on it without an explicit `using`.

2. Build the Application project and confirm it succeeds:

   ```bash
   cd /home/user/worktrees/feature-4191-Arch-Review-Marketing-Createmarketingactionhandler
   dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
   ```

   Expected output: `Build succeeded.` with `0 Error(s)`.

3. Confirm no remaining calls to the per-item methods inside this handler:

   ```bash
   grep -n "AssociateWithProduct\|LinkToFolder" backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs
   ```

   Expected output: no matches (empty output). (These methods remain defined on `MarketingAction` itself and are out of scope — this grep only checks the handler file.)

4. Stage and commit:

   ```bash
   cd /home/user/worktrees/feature-4191-Arch-Review-Marketing-Createmarketingactionhandler
   git add backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs
   git commit -m "Align CreateMarketingActionHandler with Update's bulk-replace domain calls"
   ```

   Expected output: a new commit containing exactly this one file.

---

