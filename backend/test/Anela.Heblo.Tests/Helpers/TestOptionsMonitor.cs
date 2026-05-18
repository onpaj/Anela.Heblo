using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Tests.Helpers;

public sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
{
    private T _current;
    private readonly List<Action<T, string?>> _listeners = new();

    public TestOptionsMonitor(T initial)
    {
        _current = initial;
    }

    public T CurrentValue => _current;

    public T Get(string? name) => _current;

    public IDisposable OnChange(Action<T, string?> listener)
    {
        _listeners.Add(listener);
        return new Subscription(() => _listeners.Remove(listener));
    }

    public void Set(T next)
    {
        _current = next;
        foreach (var l in _listeners.ToArray())
        {
            l(next, null);
        }
    }

    public void SetNull()
    {
        foreach (var l in _listeners.ToArray())
        {
            l(default(T)!, null);
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Action _dispose;

        public Subscription(Action d)
        {
            _dispose = d;
        }

        public void Dispose() => _dispose();
    }
}
