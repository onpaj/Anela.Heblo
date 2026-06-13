namespace Anela.Heblo.Adapters.Plaud;

internal sealed record PlaudTokenSaveResult(bool KeyVaultWriteFailed, Exception? KeyVaultError);
