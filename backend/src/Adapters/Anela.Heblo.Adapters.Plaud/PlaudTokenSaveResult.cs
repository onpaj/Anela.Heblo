namespace Anela.Heblo.Adapters.Plaud;

public sealed record PlaudTokenSaveResult(bool KeyVaultWriteFailed, Exception? KeyVaultError);
