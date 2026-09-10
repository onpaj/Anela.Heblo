### task: update-upload-document-tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/UploadDocumentHandlerTests.cs`

This task depends on `migrate-upload-document-request-to-bytes` being done first (the test file will not compile against the old `UploadDocumentRequest` shape once that task lands, and this task fixes exactly that).

- [ ] **Step 1: Update `Handle_NewDocument_IndexesAndReturnsIndexedStatus`**

Replace:
```csharp
        var request = new UploadDocumentRequest
        {
            FileStream = new MemoryStream("pdf content"u8.ToArray()),
            Filename = "guide.pdf",
            ContentType = "application/pdf",
        };
```
with:
```csharp
        var request = new UploadDocumentRequest
        {
            Content = "pdf content"u8.ToArray(),
            Filename = "guide.pdf",
            ContentType = "application/pdf",
        };
```

- [ ] **Step 2: Update `Handle_OctetStreamWithTxtExtension_ResolvesToTextPlainAndIndexes`**

Replace:
```csharp
        var request = new UploadDocumentRequest
        {
            FileStream = new MemoryStream("plain text"u8.ToArray()),
            Filename = "readme.txt",
            ContentType = "application/octet-stream",
        };
```
with:
```csharp
        var request = new UploadDocumentRequest
        {
            Content = "plain text"u8.ToArray(),
            Filename = "readme.txt",
            ContentType = "application/octet-stream",
        };
```

- [ ] **Step 3: Update `Handle_OctetStreamWithDocxExtension_ResolvesToDocxContentType`**

Replace:
```csharp
        var request = new UploadDocumentRequest
        {
            FileStream = new MemoryStream("docx bytes"u8.ToArray()),
            Filename = "document.docx",
            ContentType = "application/octet-stream",
        };
```
with:
```csharp
        var request = new UploadDocumentRequest
        {
            Content = "docx bytes"u8.ToArray(),
            Filename = "document.docx",
            ContentType = "application/octet-stream",
        };
```

- [ ] **Step 4: Update `Handle_UnsupportedFileType_ReturnsUnsupportedFileTypeErrorWithoutThrowing`**

Replace:
```csharp
        var request = new UploadDocumentRequest
        {
            FileStream = new MemoryStream("binary"u8.ToArray()),
            Filename = "archive.zip",
            ContentType = "application/zip",
        };
```
with:
```csharp
        var request = new UploadDocumentRequest
        {
            Content = "binary"u8.ToArray(),
            Filename = "archive.zip",
            ContentType = "application/zip",
        };
```

- [ ] **Step 5: Update `Handle_IndexDocumentRequest_ContainsUploadSourcePath`**

Replace:
```csharp
        var request = new UploadDocumentRequest
        {
            FileStream = new MemoryStream("content"u8.ToArray()),
            Filename = "doc.pdf",
            ContentType = "application/pdf",
        };
```
with:
```csharp
        var request = new UploadDocumentRequest
        {
            Content = "content"u8.ToArray(),
            Filename = "doc.pdf",
            ContentType = "application/pdf",
        };
```

- [ ] **Step 6: Confirm no remaining `FileStream` reference in this test file**

Run:
```bash
grep -n "FileStream" backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/UploadDocumentHandlerTests.cs
```
Expected: no output (no matches).

- [ ] **Step 7: Run the updated test file and confirm all five tests pass**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UploadDocumentHandlerTests"
```
Expected: `Passed! - Failed: 0, Passed: 5, Skipped: 0`.

- [ ] **Step 8: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/UploadDocumentHandlerTests.cs
git commit -m "test(knowledgebase): update UploadDocumentRequest construction to byte[] Content"
```

