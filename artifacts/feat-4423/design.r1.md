# Design: Configuration version fallback

## Component Design
`GetConfigurationHandler`
- Public ctor `(IConfiguration, ILogger<GetConfigurationHandler>)` -> chains to seam ctor with `Assembly.GetEntryAssembly`.
- Seam ctor `(IConfiguration, ILogger<...>, Func<Assembly?> entryAssemblyProvider)`.
- `GetVersionFromSources()`: config -> `var assembly = _entryAssemblyProvider() ?? Assembly.GetExecutingAssembly();` -> informational version -> `assembly.GetName().Version` -> null.
- Update the priority comment in `Handle` to say "entry assembly (falls back to executing assembly)".

Tests (`GetConfigurationHandlerTests`)
- Entry assembly provider returning a test assembly with a known `AssemblyInformationalVersion` (or a dynamically built/other loaded assembly with distinguishable version) -> response uses that version.
- Provider returning null -> version equals the Application assembly's informational version.
- Existing tests unchanged.

## Data Schemas
None; no API or DB changes.
