# Design
Single file `GraphFolderResolverTests.cs`, namespace `Anela.Heblo.Tests.Shared.Rag.OneDrive`.
- `RecordingHandler : HttpMessageHandler` with `Func<HttpRequestMessage, HttpResponseMessage>` responder and `List<(HttpMethod Method, string Url, string? Body)> Calls` (read body before returning).
- Helper `Json(id)` -> 200/201 with `{"id":"..."}`; helper `Status(code)`.
- Helper `Create(handler, out cache)` -> `new GraphFolderResolver(new HttpClient(handler), "tok", cache, NullLogger.Instance)`.
- Responder routing by method + URL suffix (`EndsWith("/root")`, `Contains("/root:/a/b:")`, `EndsWith("/children")`) with a counter for POST ids.
Tests map 1:1 to FR-1..FR-8 (one `[Fact]`, `[Theory]` for 500/403 in FR-5).
