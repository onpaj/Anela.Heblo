## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs:53` — the literal "1.0.0" is duplicated from the handler's private `DefaultVersion`; acceptable since the const is private, no change required.
