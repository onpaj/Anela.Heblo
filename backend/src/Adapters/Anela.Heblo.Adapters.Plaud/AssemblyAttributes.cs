using System.Runtime.CompilerServices;

// Expose internal test seams (e.g. PlaudCliClient / PlaudTokenRefresher constructors that accept an
// overridable tokens-file path) to the adapter test projects.
[assembly: InternalsVisibleTo("Anela.Heblo.Adapters.Plaud.Tests")]
[assembly: InternalsVisibleTo("Anela.Heblo.Tests")]
