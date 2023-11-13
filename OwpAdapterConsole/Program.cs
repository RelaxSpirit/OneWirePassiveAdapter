// See https://aka.ms/new-console-template for more information
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OneWirePassiveAdapter.OneWire.DS18B20;
using OneWirePassiveAdapter.Uart;
using OwpAdapterConsole;
using System.IO.Ports;

var ports = SerialPort.GetPortNames();
if (ports?.Any() ?? false)
{
    Console.WriteLine(string.Join(Environment.NewLine, ports));
    Console.WriteLine("Select and enter serial port name:");
    var serialPortName = Console.ReadLine();

    if (string.IsNullOrEmpty(serialPortName))
        Console.WriteLine("Serial port name can not be empty!");
    else
    {
        await new HostBuilder()
            .ConfigureAppConfiguration(option =>
            {
                option.AddJsonFile("appSettings.json");
            })
            .ConfigureServices((hostContext, services) =>
            {
                services.AddLogging(configure => configure.AddConsole());
                services.AddSingleton<IOwpAdapterFactory, OwpAdapterFactory>();
                services.AddTransient(_ => new DS18B20BusMasterSettings() { SerialPortName = serialPortName });
                services.AddSingleton<DS18B20BusMaster>();
                services.AddHostedService<DS18B20Service>();
            })
            .RunConsoleAsync();
    }
}
else
{
    Console.WriteLine("No serial ports");
}
