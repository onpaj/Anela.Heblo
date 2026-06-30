# FR-3 Implementation: GetExtensionFromContentType — All Switch Arms

## Status: DONE

## What was implemented

Added a `[Theory]` test `DownloadFromUrlAsync_NoFilenameUrl_ContentTypeToExtension_AllArms` to:

`backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs`

## Test cases (9 InlineData arms)

| Content-Type           | Expected Extension |
|------------------------|--------------------|
| `image/jpeg`           | `.jpg`             |
| `image/png`            | `.png`             |
| `image/gif`            | `.gif`             |
| `image/webp`           | `.webp`            |
| `application/pdf`      | `.pdf`             |
| `text/plain`           | `.txt`             |
| `application/json`     | `.json`            |
| `application/xml`      | `.xml`             |
| `application/x-unknown`| `.bin` (default)   |

## Approach

The test exercises `GetExtensionFromContentType` indirectly by triggering the "no filename in URL" branch of `DownloadFromUrlAsync`. A URL ending with `/` causes `Path.GetFileName` to return an empty string, which forces the code to generate a blob name of the form `downloaded-file-{guid}{ext}`. The test asserts that the generated blob name ends with the expected extension, covering every arm of the private switch including the default `.bin` fallback.

The shared `SetupContainerAndBlobClient` helper captures blob names via a `Callback` on `GetBlobClient`, and `Assert.Single` + `Assert.EndsWith` validate the result.

## Test results

- Failed: 0
- Passed: 9
- Total: 9

## Commit

`test(filestorage): FR-3 GetExtensionFromContentType all switch arms`
