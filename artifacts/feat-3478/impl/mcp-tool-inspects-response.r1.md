# Implementation: mcp-tool-inspects-response

## What was implemented
`LeafletTools.GenerateLeaflet` no longer catches `EmptyRetrievalException` (that exception is never thrown by the handler anymore — the handler returns a failed `GenerateLeafletResponse` instead). The MCP tool now inspects `response.Success` after calling `_mediator.Send(...)` and throws `McpException` before serializing when the call failed, so the MCP boundary never silently returns a `success: false` JSON payload as if it were a normal result.

- If `response.Success` is `false` and `response.ErrorCode == ErrorCodes.LeafletEmptyRetrieval`, the tool throws `McpException("Knowledge Base does not yet cover this topic; try a broader phrasing")`.
- For any other failed response, it throws `McpException("Leaflet generation failed. Please try again.")`.
- Only on `response.Success == true` does it return `JsonSerializer.Serialize(response)`.
- The outer `catch (McpException) { throw; }` and `catch (Exception ex) { ...log...; throw new McpException("Leaflet generation failed. Please try again."); }` blocks are unchanged — they still translate genuinely unexpected exceptions (not business-logic failures) at the MCP protocol boundary.
- Added `using Anela.Heblo.Application.Shared;` to both the implementation and test file (for `ErrorCodes`).

## Files created/modified
- `backend/src/Anela.Heblo.API/MCP/Tools/LeafletTools.cs` — removed the `catch (EmptyRetrievalException ex)` block; added a `response.Success` check after `_mediator.Send(...)` that throws `McpException` with the appropriate message before returning the serialized response.
- `backend/test/Anela.Heblo.Tests/MCP/Tools/LeafletToolsTests.cs` — replaced `GenerateLeaflet_wraps_EmptyRetrievalException_as_McpException` (which mocked the mediator to throw `EmptyRetrievalException`) with `GenerateLeaflet_throws_McpException_on_LeafletEmptyRetrieval_response` (which mocks the mediator to return a `GenerateLeafletResponse` constructed with `ErrorCodes.LeafletEmptyRetrieval`), asserting the thrown `McpException.Message` equals the expected user-facing message.

## Tests
- `backend/test/Anela.Heblo.Tests/MCP/Tools/LeafletToolsTests.cs`:
  - `GenerateLeaflet_returns_serialized_response_on_success` — unchanged, still covers the happy path.
  - `GenerateLeaflet_throws_McpException_on_invalid_audience` / `_on_invalid_length` — unchanged, cover input validation.
  - `GenerateLeaflet_throws_McpException_on_LeafletEmptyRetrieval_response` — new/replaced test covering the `response.Success == false` + `ErrorCode == ErrorCodes.LeafletEmptyRetrieval` path now that the handler returns a failed response instead of throwing.
  - `GenerateLeaflet_wraps_unexpected_exception_with_generic_message` — unchanged, still covers the generic `catch (Exception ex)` path and logger verification.

## How to verify
- `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj` — succeeds with 0 errors (160 pre-existing warnings unrelated to this change, plus a pre-existing non-fatal post-build AccessMatrixGen tool warning that does not affect compilation).
- `grep -n EmptyRetrievalException backend/src/Anela.Heblo.API/MCP/Tools/LeafletTools.cs` — no matches (confirmed dead code fully removed).
- Manual code read of `LeafletToolsTests.cs` confirms the new test's mock setup (`ReturnsAsync(errorResponse)` where `errorResponse = new GenerateLeafletResponse(ErrorCodes.LeafletEmptyRetrieval, ...)`) matches `GenerateLeafletResponse`'s actual constructor signature (`GenerateLeafletResponse(ErrorCodes errorCode, Dictionary<string, string>? details = null)`) and `BaseResponse.Success` (set to `false` by that constructor), so the assertion path exercises the new `if (!response.Success)` branch correctly.

## Notes
- Per the task's known-issue callout, `Anela.Heblo.Tests` cannot currently be built/run via `dotnet test` because of a pre-existing, unrelated compile error in `GetConfigurationHandlerTests.cs` (missing `ConfigurationConstants.APP_VERSION`), confirmed present on `origin/main`. This is not addressed here. Test correctness for `LeafletToolsTests.cs` was instead verified by careful manual reading against the real `GenerateLeafletResponse`/`BaseResponse` source (confirmed `Success`/`ErrorCode` properties and the two-arg error constructor exist exactly as used).
- No other references to `EmptyRetrievalException` remain in `LeafletTools.cs`; the `using Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;` was kept since `GenerateLeafletRequest`/`GenerateLeafletResponse` still live there and are still used.

## Status
DONE
