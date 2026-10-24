# Implementation: remove-constants-from-domain

## What was implemented
Deleted Domain FileStorageConstants; FileStorageModule.FileDownloadClientName is now the literal "FileDownload". No remaining references.

## How to verify
`dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~FileStorage"`

## PR Summary
Deleted Domain FileStorageConstants; FileStorageModule.FileDownloadClientName is now the literal "FileDownload". No remaining references.

## Status
DONE
