namespace Anela.Heblo.Application.Common.Cache.Abstractions;

public enum CacheFailureMode
{
    KeepStale,
    ThrowException,
    ReturnNull
}