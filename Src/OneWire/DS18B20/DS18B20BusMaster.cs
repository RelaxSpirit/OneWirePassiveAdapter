using System.Runtime.CompilerServices;
using OneWirePassiveAdapter.Uart;

namespace OneWirePassiveAdapter.OneWire.DS18B20
{
    public class DS18B20BusMaster : OwBusMaster
    {
        public const byte DEVICE_FAMILY_CODE = 0x28;

        #region DS18B20 FUNCTION COMMANDS

        public const byte CONVERT_T = 0x44;

        public const byte WRITE_SCRATCHPAD = 0x4E;

        public const byte READ_SCRATCHPAD = 0xBE;

        public const byte COPY_SCRATCHPAD = 0x48;

        public const byte RECALL_E2 = 0xB8;

        public const byte READ_POWER_SUPPLY = 0xB4;

        #endregion

#pragma warning disable IDE0230 // Использовать строковый литерал UTF-8
        private readonly byte[] _filterSearch = new byte[] { DEVICE_FAMILY_CODE };
        private readonly byte[] _tx_convertT = new byte[] { CONVERT_T };
        private readonly byte[] _tx_writeScratchpad = new byte[] { WRITE_SCRATCHPAD };
        private readonly byte[] _tx_copyScratchpad = new byte[] { COPY_SCRATCHPAD };
#pragma warning restore IDE0230 // Использовать строковый литерал UTF-8
        private readonly byte[] _tx_readScratchpad = new byte[] { READ_SCRATCHPAD };
        private readonly byte[] _tx_readPowerSupply = new byte[] { READ_POWER_SUPPLY };
        private readonly byte[] _tx_recallE2 = new byte[] { RECALL_E2 };

        public DS18B20BusMaster(DS18B20BusMasterSettings settings, IOwpAdapterFactory oneWirePassiveAdapterFactory) 
            : base(settings, oneWirePassiveAdapterFactory)
        {
        }

        public async Task<IList<DS18B20Device>> SearchDS18B20DevicesAsync(CancellationToken cancellationToken, TimeSpan? searchTimeLimit = null, bool alarmFlag = false)
        {
            var result = (await SearchDevicesOnBusAsync(cancellationToken, searchTimeLimit, true, alarmFlag, _filterSearch))
                .Select(sn => DS18B20Device.GetDS18B20Device(sn, this)).ToList();

            foreach (DS18B20Device device in result)
            {
                using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromSeconds(5));
                await device.Initialize(cancellationTokenSource.Token);
            }

            return result;
                
        }

        public async Task ConvertTAsync(CancellationToken cancellationToken, DS18B20Device ds18B20Device = null!)
        {
            bool IsNeedExitRead(bool sourceBit)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return sourceBit;
            }

            if (ds18B20Device == null)
                await SkipRomAsync(_tx_convertT!, null!, null!, cancellationToken);
            else
                await MatchRomAsync(ds18B20Device.Id, _tx_convertT!, (ds18B20Device.ParasitePowerMode ? null : IsNeedExitRead)!, null!, cancellationToken);
        }

        public async Task<byte[]> ReadScratchpadAsync(CancellationToken cancellationToken, int bytesCountNeedToRead = 9, DS18B20Device ds18B20Device = null!)
        {
            if (bytesCountNeedToRead < 1 || bytesCountNeedToRead > 9) 
                throw new IndexOutOfRangeException($"Parameter '{nameof(bytesCountNeedToRead)}' must be greater than 0 and less than 10");

            int maxBitInResponse = bytesCountNeedToRead * 8;

            var resultBytes = new byte[bytesCountNeedToRead];
            var responseBitPosition = -1;
            

            if (ds18B20Device == null)
                await SkipRomAsync(_tx_readScratchpad, IsNeedExitRead, null!, cancellationToken);
            else
                await MatchRomAsync(ds18B20Device.Id, _tx_readScratchpad, IsNeedExitRead, null!, cancellationToken);

            bool IsNeedExitRead(bool sourceBit)
            {
                cancellationToken.ThrowIfCancellationRequested();

                responseBitPosition++;

                if (responseBitPosition < maxBitInResponse)
                {
                    var bytePosition = responseBitPosition / 8;
                    var bitPosition = responseBitPosition - bytePosition * 8;

                    if (bytePosition < resultBytes!.Length)
                    {
                        resultBytes[bytePosition] |= (byte)((sourceBit ? 1 : 0) << bitPosition);
                    }
                    else
                        return true;
                }

                return (maxBitInResponse - responseBitPosition) <= 1;
            }

            return resultBytes;
        }

        public async Task WriteScratchpadAsync(byte Th, byte Tl, byte configurationByte, CancellationToken cancellationToken, DS18B20Device ds18B20Device = null!)
        {
            var configurationData = new byte[3];
            configurationData[0] = Th;
            configurationData[1] = Tl; 
            configurationData[2] = configurationByte;
            if (ds18B20Device == null)
                await SkipRomAsync(_tx_writeScratchpad, null!, ()  => configurationData, cancellationToken);
            else
                await MatchRomAsync(ds18B20Device.Id, _tx_writeScratchpad, null!, () => configurationData, cancellationToken);
        }

        public async Task CopyScratchpadAsync(CancellationToken cancellationToken, DS18B20Device ds18B20Device = null!)
        {
            if (ds18B20Device == null)
                await SkipRomAsync(_tx_copyScratchpad, null!, null!, cancellationToken);
            else
                await MatchRomAsync(ds18B20Device.Id, _tx_copyScratchpad, null!, null!, cancellationToken);
        }

        public async Task RecallE2Async(CancellationToken cancellationToken, DS18B20Device ds18B20Device = null!)
        {
             bool IsNeedExitRead(bool sourceBit)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return sourceBit;
            }

            if (ds18B20Device == null)
                await SkipRomAsync(_tx_recallE2!, IsNeedExitRead, null!, cancellationToken);
            else
                await MatchRomAsync(ds18B20Device.Id, _tx_recallE2!, IsNeedExitRead, null!, cancellationToken);
        }

        public async Task<bool> ReadPowerSupplyAsync(CancellationToken cancellationToken, DS18B20Device ds18B20Device = null!)
        {
            bool result = false;

            if (ds18B20Device == null)
                await SkipRomAsync(_tx_readPowerSupply!, IsNeedExitRead, null!, cancellationToken);
            else
                await MatchRomAsync(ds18B20Device.Id, _tx_readPowerSupply!, IsNeedExitRead, null!, cancellationToken);
            
            bool IsNeedExitRead(bool sourceBit)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result = sourceBit;
                return true;
            }
            return result;
        }
    }
}
