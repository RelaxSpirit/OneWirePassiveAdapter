using System.Diagnostics;
using System.Text;

namespace OneWirePassiveAdapter.OneWire.DS18B20
{
    public sealed class DS18B20Device
    {
        public const double MinTemperature = -55.0;

        public const double MaxTemperature = +125.0;

        #region Memory map offsets

        private const int MemoryMapOffsetTemperatureLsb = 0;

        private const int MemoryMapOffsetTemperatureMsb = 1;

        private const int MemoryMapOffsetThRegister = 2;

        private const int MemoryMapOffsetTlRegister = 3;

        private const int MemoryMapOffsetConfigurationRegister = 4;

        private const int MemoryMapOffsetReserved1 = 5;

        private const int MemoryMapOffsetReserved2 = 6;

        private const int MemoryMapOffsetReserved3 = 7;

        private const int MemoryMapOffsetCrc = 8;

        #endregion

        private byte[] _scratchpad = null!;
        private readonly DS18B20BusMaster _master;
        private readonly TimeSpan _internalOperationLimitTime = TimeSpan.FromSeconds(2);
        private int _measureTemperatureInterLocked = 0;
        private double? _lastMeasureTemperature;
        private readonly Stopwatch _requestTemperatureWatch = new();

        DS18B20Device(ulong id, DS18B20BusMaster master)
        {
            Id = id;
            _master = master;
        }

        internal async Task Initialize(CancellationToken cancellationToken)
        {
            _scratchpad = await _master.ReadScratchpadAsync(cancellationToken, 9, this);
            ParasitePowerMode = !(await _master.ReadPowerSupplyAsync(cancellationToken, this));
            var configurationRegister = _scratchpad[MemoryMapOffsetConfigurationRegister];
            var bits56 = (configurationRegister & 0x60) >> 5;
            TemperatureResolution = bits56 switch
            {
                0x00 => TResolution.Resolution9bit,
                0x01 => TResolution.Resolution10bit,
                0x02 => TResolution.Resolution11bit,
                _ => TResolution.Resolution12bit,
            };
            HighAlarmTemperature = _scratchpad[MemoryMapOffsetThRegister];
            LowAlarmTemperature = _scratchpad[MemoryMapOffsetTlRegister];

            var temperatureRaw = BitConverter.ToUInt16(_scratchpad.Take(2).ToArray(), 0);
            _lastMeasureTemperature = temperatureRaw / 16.0;
            _requestTemperatureWatch.Restart();
        }

        public ulong Id { get; set; }
        public bool ParasitePowerMode { get; private  set; }
        public TResolution TemperatureResolution { get; private set; }
        public byte HighAlarmTemperature { get; private set; }
        public byte LowAlarmTemperature { get; private set; }
        public double? Temperature
        {
            get
            {
                if (Interlocked.Exchange(ref _measureTemperatureInterLocked, 1) < 1)
                {
                    try
                    {
                        if (_requestTemperatureWatch.Elapsed > (_internalOperationLimitTime / (int)TemperatureResolution))
                        {
                            using CancellationTokenSource cancellationTokenSource = new(_internalOperationLimitTime);

                            Task.Run(async () =>
                            {
                                await _master.ConvertTAsync(cancellationTokenSource.Token, this);
                                
                                //Если датчик запитан паразитным питанием, оставляем шину в покое что бы подтягивающий резистор
                                //подтянул уровень до высокого
                                if(ParasitePowerMode)
                                    await Task.Delay(750 / (int)TemperatureResolution, cancellationTokenSource.Token);
                                
                                var temperatureRaw = BitConverter.ToUInt16(await _master.ReadScratchpadAsync(cancellationTokenSource.Token, 2, this), 0);

                                _lastMeasureTemperature = temperatureRaw / 16.0;

                                if (_lastMeasureTemperature >= MaxTemperature || _lastMeasureTemperature <= MinTemperature)
                                    _lastMeasureTemperature = null;
                                
                                _requestTemperatureWatch.Restart();

                            }).ConfigureAwait(false).GetAwaiter().GetResult();
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e);
                        _lastMeasureTemperature = null;
                    }
                    finally
                    {
                        
                        Interlocked.Exchange(ref _measureTemperatureInterLocked, 0);
                    }
                }
                return _lastMeasureTemperature;
            }
        }
        public async Task SetConfigurationAsync(TResolution temperatureResolution, byte? highAlarmTemperature = null, byte? lowAlarmTemperature = null)
        {
            if (TemperatureResolution != temperatureResolution || HighAlarmTemperature != highAlarmTemperature || LowAlarmTemperature != lowAlarmTemperature)
            {
                using CancellationTokenSource cancellationTokenSource = new(_internalOperationLimitTime);

                byte configurationByte = GetConfigurationRegister(temperatureResolution);
                await _master.WriteScratchpadAsync(
                    highAlarmTemperature ?? HighAlarmTemperature,
                    lowAlarmTemperature ?? LowAlarmTemperature,
                    configurationByte,
                    CancellationToken.None, this);

                if (highAlarmTemperature.HasValue)
                {
                    HighAlarmTemperature = highAlarmTemperature.Value;
                    _scratchpad[MemoryMapOffsetThRegister] = HighAlarmTemperature;
                }
                if (lowAlarmTemperature.HasValue)
                {
                    LowAlarmTemperature = lowAlarmTemperature.Value;
                    _scratchpad[MemoryMapOffsetTlRegister] = LowAlarmTemperature;
                }

                TemperatureResolution = temperatureResolution;

                _scratchpad[MemoryMapOffsetConfigurationRegister] = configurationByte;
            }
        }
        public async Task SaveConfigurationAsync()
        {
            using CancellationTokenSource cancellationTokenSource = new(_internalOperationLimitTime);
            await _master.CopyScratchpadAsync(cancellationTokenSource.Token, this);
        }
        public async Task ResetConfigurationAsync()
        {
            using CancellationTokenSource cancellationTokenSource = new(_internalOperationLimitTime);
            await _master.RecallE2Async(cancellationTokenSource.Token, this);
            await Initialize(cancellationTokenSource.Token);
        }
        private static byte GetConfigurationRegister(TResolution resolution)
        {
            return resolution switch
            {
                TResolution.Resolution9bit => 0b_0_00_11111,
                TResolution.Resolution10bit => 0b_0_01_11111,
                TResolution.Resolution11bit => 0b_0_10_11111,
                _ => 0b_0_11_11111,
            };
        }
        private static string GetTResolutionString(TResolution resolution)
        {
            return resolution switch
            {
                TResolution.Resolution9bit => "9-bit",
                TResolution.Resolution10bit => "10-bit",
                TResolution.Resolution11bit => "11-bit",
                _ => "12-bit",
            };
        }
        private static string GetTemperatureCelsiusString(double? temperature)
        {
            if (!temperature.HasValue)
                return "--";

            return $"{(temperature.Value != 0 ? (temperature.Value > 0 ? "+" : "-") : string.Empty)}{temperature.Value}°C";
        }

        public override string ToString()
        {
            StringBuilder sb = new();
            sb.Append("DS18B20 - ");
            sb.Append(BitConverter.ToUInt64(BitConverter.GetBytes(Id).Reverse().ToArray(), 0).ToString("X16"));
            sb.Append(" (");
            sb.Append(GetTemperatureCelsiusString(Temperature));
            sb.Append("), ");
            sb.Append("Resolution ");
            sb.Append(GetTResolutionString(TemperatureResolution));
            sb.Append(", ParasitePowerMode-");
            sb.Append(ParasitePowerMode ? "yes" : "no");

            return sb.ToString();
        }

        public enum TResolution
        {
            Resolution9bit = 8,

            Resolution10bit = 4,

            Resolution11bit = 2,

            Resolution12bit = 1,
        }

        internal static DS18B20Device GetDS18B20Device(ulong Id, DS18B20BusMaster master)
        {
            return new DS18B20Device(Id, master);
        }
    }
}
