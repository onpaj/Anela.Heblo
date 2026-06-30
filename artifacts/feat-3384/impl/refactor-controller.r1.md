# Task: refactor-controller (r1)

## Summary
Replaced BackgroundRefreshController's embedded business logic with pure MediatR dispatch.

## Changes

### Modified
- `backend/src/Anela.Heblo.API/Controllers/BackgroundRefreshController.cs` — replaced all inline logic (registry calls, mapping, orchestration) with `_mediator.Send()` calls. Controller now only: (1) receives HTTP request, (2) sends MediatR command/query, (3) returns HTTP response.

## Before
Controller directly injected `IBackgroundRefreshTaskRegistry` and contained mapping, filtering, and orchestration logic in action methods.

## After
Controller injects only `IMediator`. Each action sends one request and returns the response. All business logic lives in the respective MediatR handlers.

## Status: completed
