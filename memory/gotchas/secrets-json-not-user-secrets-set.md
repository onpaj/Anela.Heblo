# Gotcha: Edit secrets.json Directly, Never Use dotnet user-secrets set

**Problem:** Using `dotnet user-secrets set` commands to configure secrets produces incorrect output or fails silently when run in a monorepo with multiple projects.

**Fix:** Always open and edit `secrets.json` directly in the correct project's user secrets directory:
```bash
# Find the path:
cat backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj | grep UserSecretsId
# Then edit:
# ~/.microsoft/usersecrets/{UserSecretsId}/secrets.json
```

**Why this matters:** `dotnet user-secrets set` picks the wrong project in some contexts, silently writing to the wrong `secrets.json`.
