using DigitalThermometer.OneWire;
using OneWirePassiveAdapter.Helpers;
using OneWirePassiveAdapter.Uart;

namespace OneWirePassiveAdapter.OneWire
{
    public class OwBusMaster
    {
        private readonly OwpAdapter _oneWirePassiveAdapter;
        public OwBusMaster(OwBusMasterSettings settings, IOwpAdapterFactory oneWirePassiveAdapterFactory)
        {
            _oneWirePassiveAdapter = oneWirePassiveAdapterFactory.GetAdapter(settings.SerialPortName);
        }

        public async Task<IList<ulong>> SearchDevicesOnBusAsync(CancellationToken cancellationToken, TimeSpan? searchTimeLimit = null, bool checkCrc = true,
            bool alarmFlag = false, byte[] deviceSnFilter = null!)
        {
            if (!searchTimeLimit.HasValue)
                searchTimeLimit = TimeSpan.FromSeconds(1);

            bool[]? filter = null;
            if (deviceSnFilter != null)
                filter = deviceSnFilter.ReadBitsCore(cancellationToken).ToArray();

            var searchedDevices = await _oneWirePassiveAdapter.SearchDevisesOnBus(searchTimeLimit.Value, cancellationToken,
                alarmFlag, filter!);

            return (checkCrc ? searchedDevices?.Where(sn => BitConverter.GetBytes(sn).CalculateCrc8() == 0).ToList() : searchedDevices?.ToList()) ?? Array.Empty<ulong>().ToList();
        }

        public async Task<ulong> FindOneDeviceAsync(ulong deviceId, CancellationToken cancellationToken, bool alarmFlag = false)
            => await _oneWirePassiveAdapter.FindOneDevice(BitConverter.GetBytes(deviceId).ReadBitsCore(cancellationToken).ToArray(), cancellationToken, alarmFlag);

        protected async Task<ulong> ReadRomAsync(CancellationToken cancellationToken, bool checkCrc = true)
        {
            var deviceSn = await _oneWirePassiveAdapter.ReadRom(cancellationToken);
            if (checkCrc)
            {
                if (BitConverter.GetBytes(deviceSn).CalculateCrc8() == 0)
                    return deviceSn;
                else
                    return 0ul;
            }
            return deviceSn;
        }

        protected async Task SkipRomAsync(byte[] functionCommand, Func<bool, bool> funcReceiveBitCallback, Func<byte[]> funcGetAdditionalData, CancellationToken cancellationToken)
            => await _oneWirePassiveAdapter.SkipRom(functionCommand, funcReceiveBitCallback, funcGetAdditionalData, cancellationToken);

        protected async Task MatchRomAsync(ulong deviceId, byte[] functionCommand, Func<bool, bool> funcReceiveBitCallback, Func<byte[]> funcGetAdditionalData, CancellationToken cancellationToken)
            => await _oneWirePassiveAdapter.MatchRom(deviceId, functionCommand, funcReceiveBitCallback, funcGetAdditionalData, cancellationToken);

    }
}
