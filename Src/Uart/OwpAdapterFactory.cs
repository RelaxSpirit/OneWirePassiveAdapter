using System;
using System.Collections.Concurrent;

namespace OneWirePassiveAdapter.Uart
{
    public sealed class OwpAdapterFactory : IOwpAdapterFactory
    {
        private readonly ConcurrentDictionary<string, OwpAdapter> _adapters = new();

        public void Dispose()
        {
            foreach (var adapter in _adapters.Values)
                adapter.Dispose();
        }

        OwpAdapter IOwpAdapterFactory.GetAdapter(string serialPortName)
        {
            return _adapters.GetOrAdd(serialPortName, (name) => new OwpAdapter(name));
        }
    }

    public interface IOwpAdapterFactory : IDisposable
    {
        public OwpAdapter GetAdapter(string serialPortName);
    }
}
