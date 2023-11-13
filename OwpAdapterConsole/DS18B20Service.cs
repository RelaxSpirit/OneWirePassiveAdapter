using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OneWirePassiveAdapter.OneWire.DS18B20;

namespace OwpAdapterConsole
{
    internal sealed class DS18B20Service : IHostedService, IDisposable
    {
        private CancellationTokenSource _cancellationTokenSource = null!;
        private readonly DS18B20BusMaster _dS18B20BusMaster;
        private readonly ILogger _logger;
        private Task _mainTask = null!;
        private bool disposedValue;

        public DS18B20Service(DS18B20BusMaster dS18B20BusMaster, ILogger<DS18B20BusMaster> logger) 
        {
            _logger = logger;
            _dS18B20BusMaster = dS18B20BusMaster;
        }
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _mainTask = Task.Factory.StartNew(async () =>
            {
                try
                {
                    var devices = await _dS18B20BusMaster.SearchDS18B20DevicesAsync(_cancellationTokenSource.Token);

                    while (!_cancellationTokenSource.IsCancellationRequested)
                    {
                        foreach (var device in devices)
                        {
                            var deviceInfo = device.ToString();
                            _logger.LogInformation("{deviceInfo}", deviceInfo);
                        }

                        await Task.Delay(TimeSpan.FromSeconds(1), _cancellationTokenSource.Token);
                    }
                }
                catch(Exception ex) when ((ex is OperationCanceledException) || (ex is TaskCanceledException))
                {
                    _logger.LogInformation("End main task");
                }
                catch(Exception ex)
                {
                    _logger.LogError(ex, "Error main task!");
                }

            }, _cancellationTokenSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (!(_cancellationTokenSource?.IsCancellationRequested ?? true))
                _cancellationTokenSource.Cancel();

            await _mainTask;
        }

        private void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    if (!(_cancellationTokenSource?.IsCancellationRequested ?? true))
                        _cancellationTokenSource.Cancel();

                    _cancellationTokenSource?.Dispose();

                    _mainTask?.Dispose();
                }
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Не изменяйте этот код. Разместите код очистки в методе "Dispose(bool disposing)".
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
