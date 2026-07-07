### task: response-error-constructor

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletResponse.cs`

Give `GenerateLeafletResponse` the same two-constructor shape `SubmitLeafletFeedbackResponse` already has, so the handler can construct an error response in the next task. Adding constructors to a DTO with no behavior of its own isn't independently unit-testable in a meaningful way (there is no logic branch to assert on beyond "the base class sets these properties," which is already covered by existing `BaseResponse` behavior) — this task is verified by the handler test in the next task, which is the first real caller of the new constructor.

- [ ] Step 1: Open `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletResponse.cs`. Current content:
  ```csharp
  using System.Text.Json.Serialization;
  using Anela.Heblo.Application.Shared;

  namespace Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;

  public class GenerateLeafletResponse : BaseResponse
  {
      [JsonPropertyName("content")]
      public string Content { get; set; } = string.Empty;

      [JsonPropertyName("id")]
      public Guid? Id { get; set; }

      [JsonPropertyName("kbSourceCount")]
      public int KbSourceCount { get; set; }

      [JsonPropertyName("leafletSourceCount")]
      public int LeafletSourceCount { get; set; }
  }
  ```
- [ ] Step 2: Add the two constructors (mirroring `SubmitLeafletFeedbackResponse` in `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/SubmitLeafletFeedback/SubmitLeafletFeedbackRequest.cs:23-26`):
  ```csharp
  using System.Text.Json.Serialization;
  using Anela.Heblo.Application.Shared;

  namespace Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;

  public class GenerateLeafletResponse : BaseResponse
  {
      public GenerateLeafletResponse() { }

      public GenerateLeafletResponse(ErrorCodes errorCode, Dictionary<string, string>? details = null)
          : base(errorCode, details) { }

      [JsonPropertyName("content")]
      public string Content { get; set; } = string.Empty;

      [JsonPropertyName("id")]
      public Guid? Id { get; set; }

      [JsonPropertyName("kbSourceCount")]
      public int KbSourceCount { get; set; }

      [JsonPropertyName("leafletSourceCount")]
      public int LeafletSourceCount { get; set; }
  }
  ```
- [ ] Step 3: Build the backend — confirm the existing success-path usage (`new GenerateLeafletResponse { Content = ..., KbSourceCount = ..., LeafletSourceCount = ... }` in `GenerateLeafletHandler.cs:134-139`) still compiles unchanged:
  ```bash
  dotnet build Anela.Heblo.sln
  ```
- [ ] Step 4: Run the full backend test suite to confirm nothing broke:
  ```bash
  dotnet test Anela.Heblo.sln
  ```
- [ ] Step 5: Commit.
  ```bash
  git add backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletResponse.cs
  git commit -m "#3478: add error-constructing constructor to GenerateLeafletResponse"
  ```

---
