# Implementation: move-download-into-handler

## What was implemented
DownloadFromUrlHandler now owns GET + UploadAsync via private DownloadAndStoreAsync (named client, ResponseHeadersRead, EnsureSuccessStatusCode, BlobNaming, redacted URL logging, rethrow). DownloadFromUrlHandlerTests and FileStorageValidationPipelineTests reworked to mock UploadAsync and a stub HttpMessageHandler; ported named-client, non-2xx, cancellation, blob-name, content-type and no-secret-in-logs scenarios from AzureBlobStorageServiceTests.

## How to verify
`dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~FileStorage"`

## PR Summary
DownloadFromUrlHandler now owns GET + UploadAsync via private DownloadAndStoreAsync (named client, ResponseHeadersRead, EnsureSuccessStatusCode, BlobNaming, redacted URL logging, rethrow). DownloadFromUrlHandlerTests and FileStorageValidationPipelineTests reworked to mock UploadAsync and a stub HttpMessageHandler; ported named-client, non-2xx, cancellation, blob-name, content-type and no-secret-in-logs scenarios from AzureBlobStorageServiceTests.

## Status
DONE
