# Implementation: remove-interface-method-and-adapter-http

## What was implemented
Removed DownloadFromUrlAsync from IBlobStorageService; AzureBlobStorageService dropped IHttpClientFactory, HTTP download and content-type helpers (ctor now BlobServiceClient+ILogger). Removed download tests/helpers from AzureBlobStorageServiceTests, MockBlobStorageService (and its now-unused content type helper); SimpleFileStorageTest now uses UploadAsync.

## How to verify
`dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~FileStorage"`

## PR Summary
Removed DownloadFromUrlAsync from IBlobStorageService; AzureBlobStorageService dropped IHttpClientFactory, HTTP download and content-type helpers (ctor now BlobServiceClient+ILogger). Removed download tests/helpers from AzureBlobStorageServiceTests, MockBlobStorageService (and its now-unused content type helper); SimpleFileStorageTest now uses UploadAsync.

## Status
DONE
