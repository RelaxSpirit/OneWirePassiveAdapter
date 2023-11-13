using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Ports;
using OneWirePassiveAdapter.Helpers;
using OneWirePassiveAdapter.Utils.SearchDevice;

namespace OneWirePassiveAdapter.Uart
{
    /// <summary>
    /// Пассивный UART адаптер
    /// </summary>
    public class OwpAdapter : IDisposable
    {
        const int OW_RESET_SPEED = 9600;
        const int OW_TRANSFER_SPEED = 115200;
        const byte UART_OW_RESET = 0xF0;
        const byte UART_OW_W0 = 0x00;
        const byte UART_OW_W1 = 0xFF;
        const byte MAX_DEVICE_ON_ONE_WIRE_BUS = 248;

        #region ROM Commands

        public const byte SEARCH_ROM = 0xF0;
        public const byte READ_ROM = 0x33;
        public const byte MATCH_ROM = 0x55;
        public const byte SKIP_ROM = 0xCC;
        public const byte ALARM_SEARCH = 0xEC;

        #endregion

        private readonly Dictionary<bool, byte[]> _tx_bitsData = new()
        {
            {false, new byte[] { UART_OW_W0 } },
            {true, new byte[] { UART_OW_W1} }
        };

        private readonly byte[] _tx_resetData = new byte[] { UART_OW_RESET };

#pragma warning disable IDE0230 // Использовать строковый литерал UTF-8
        private readonly byte[] _tx_readRom = new byte[] { READ_ROM };
        private readonly byte[] _tx_matchRom = new byte[] { MATCH_ROM };
#pragma warning restore IDE0230 // Использовать строковый литерал UTF-8
        private readonly byte[] _tx_searchRom = new byte[] { SEARCH_ROM };
        private readonly byte[] _tx_alarmSearchRom = new byte[] { ALARM_SEARCH };
        private readonly byte[] _tx_skipRom = new byte[] { SKIP_ROM };



        private bool _disposedValue;
        private readonly SerialPort _serialPort;
        private readonly SemaphoreSlim _resetLock = new(1, 1);
        private readonly ConcurrentDictionary<SerialError, DateTime> _serialErrorFrameReceived = new();

        public OwpAdapter(string portName)
        {

            _serialPort = new SerialPort(portName, OW_TRANSFER_SPEED)
            {
                Handshake = Handshake.None,
                DataBits = 8,
                StopBits = StopBits.One,
                ReceivedBytesThreshold = 1,
                ReadTimeout = 500,
            };
            _serialPort.ErrorReceived += SerialPort_ErrorReceived;
        }

        private void SerialPort_ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            Debug.WriteLine($"Serial error '{e.EventType}'");
            if (e.EventType == SerialError.Frame)
                _serialErrorFrameReceived.AddOrUpdate(e.EventType, DateTime.Now, (et, oldDt) => DateTime.UtcNow);
        }

        private bool ResetAndGetPresencePulse()
        {
            try
            {
                _serialPort.BaudRate = OW_RESET_SPEED;
                _serialPort.Write(_tx_resetData, 0, 1);
                byte pulse = (byte)_serialPort.ReadByte();
                return pulse != UART_OW_RESET;
            }
            catch (Exception e) when (e is TaskCanceledException || e is OperationCanceledException)
            {
                return false;
            }
            finally
            {
                _serialPort.BaudRate = OW_TRANSFER_SPEED;
            }
        }

        internal async Task<ulong[]> SearchDevisesOnBus(TimeSpan searchTimeLimit, CancellationToken cancellationToken, bool alarmFlag = false, bool[] filterBits = null!)
        {
            CancellationTokenSource cancellationTokenSource = new(searchTimeLimit);

            List<ulong> searchDevices = new();

            var searchMap = new SearchMap(filterBits);

            while (!cancellationTokenSource.IsCancellationRequested && searchDevices.Count < MAX_DEVICE_ON_ONE_WIRE_BUS)
            {
                try
                {
                    var newSN = await RunUartCommunication(() =>
                    {
                        return SearchOneDevise(searchMap, alarmFlag, cancellationTokenSource.Token);

                    }, cancellationToken);

                    if (newSN > 0 && !searchDevices.Contains(newSN))
                        searchDevices.Add(newSN);

                    if (searchMap.LastCollisionIndex < 0)
                        break;
                }
                catch (Exception e) when (e is TaskCanceledException || e is OperationCanceledException)
                {
                    break;
                }

            }
            return searchDevices.ToArray();

        }

        internal async Task<ulong> FindOneDevice(bool[] deviceFilter, CancellationToken cancellationToken, bool alarmFlag = false)
        {
            return await RunUartCommunication(() =>
            {
                var searchMap = new SearchMap(deviceFilter);

                return SearchOneDevise(searchMap, alarmFlag, cancellationToken);
            }, cancellationToken);
        }

        internal async Task<ulong> ReadRom(CancellationToken cancellationToken)
        {
            return await RunUartCommunication(() =>
            {
                TransmitData(_tx_readRom, cancellationToken);

                return BitConverter.ToUInt64(ReceiveData(8, cancellationToken));

            }, cancellationToken);
        }

        internal async Task SkipRom(byte[] functionCommand, Func<bool, bool> funcReceiveBitCallback, Func<byte[]> funcGetAdditionalData, CancellationToken cancellationToken)
        {
            _= await RunUartCommunication(() =>
            {
                InternalRom(_tx_skipRom, null, functionCommand, funcReceiveBitCallback, funcGetAdditionalData, cancellationToken);
                return 0;
            }
            , cancellationToken);
        }

        internal async Task MatchRom(ulong deviceId, byte[] functionCommand, Func<bool, bool> funcReceiveBitCallback, Func<byte[]> funcGetAdditionalData, CancellationToken cancellationToken)
        {
            _= await RunUartCommunication(() =>
            {
                InternalRom(_tx_matchRom, deviceId, functionCommand, funcReceiveBitCallback, funcGetAdditionalData, cancellationToken);
                return 0;
            }, cancellationToken);
        }

        private void InternalRom(byte[] tx_rom, ulong? deviceId, byte[] functionCommand, Func<bool, bool> funcReceiveBitCallback, Func<byte[]> funcGetAdditionalData, CancellationToken cancellationToken)
        {
            TransmitData(tx_rom, cancellationToken);
            if (deviceId.HasValue)
                TransmitData(BitConverter.GetBytes(deviceId.Value), cancellationToken);
            TransmitData(functionCommand, cancellationToken);
            var additionalData = funcGetAdditionalData?.Invoke();
            if (additionalData?.Any() ?? false)
                TransmitData(additionalData, cancellationToken);

            while (!cancellationToken.IsCancellationRequested && !(funcReceiveBitCallback?.Invoke(ReadOneBit()) ?? true)) ;
        }

        private ulong SearchOneDevise(SearchMap searchMap, bool alarmFlag, CancellationToken cancellationToken)
        {
            var snBitIndex = 0;
            ulong sn = 0;

            TransmitData(alarmFlag ? _tx_alarmSearchRom : _tx_searchRom, cancellationToken);

            while (!cancellationToken.IsCancellationRequested && snBitIndex < 64)
            {
                var masterBit = ReadOneBit();

                var additionalBit = ReadOneBit();

                var actualBit = searchMap.GetBitDevice(masterBit, additionalBit, snBitIndex);

                if (actualBit.HasValue)
                {
                    //Отправляем бит обратно в шину, ждем следующие биты номера
                    WriteAndReadOneBit(actualBit.Value);

                    sn |= (actualBit.Value ? 1ul : 0ul) << snBitIndex;

                    snBitIndex++;
                }
                else
                {
                    sn = 0ul;
                    break;
                }
            }
            
            searchMap.SetLastSearchDeviceId(sn);

            return sn;

        }

        private void TransmitData(byte[] data, CancellationToken cancellationToken)
        {
            foreach (bool bit in data.ReadBitsCore(cancellationToken))
            {
                var waitedBit = WriteAndReadOneBit(bit);
                if (waitedBit != bit)
                    throw new IOException("The procedure to write one bit of information to the 1-Wire bus failed");
            }
        }

        private byte[] ReceiveData(int dataBytesCount, CancellationToken cancellationToken)
        {
            List<byte> result = new(dataBytesCount);

            while (dataBytesCount > 0 && !cancellationToken.IsCancellationRequested)
            {
                byte oneByte = 0;
                for (byte bitOffset = 0; bitOffset < 8; bitOffset++)
                {
                    oneByte ^= oneByte.GetByteMask(ReadOneBit(), bitOffset);
                }

                result.Add(oneByte);
                dataBytesCount--;
            }

            return result.ToArray();
        }

        private bool WriteAndReadOneBit(bool bit)
        {
            _serialPort.Write(_tx_bitsData[bit], 0, 1);
            var readingByte = (byte)_serialPort.ReadByte();
            return readingByte == UART_OW_W1;
        }

        private bool ReadOneBit()
        {
            return WriteAndReadOneBit(true);
        }

        private void CheckUartOneWireBridge()
        {
            if (_serialErrorFrameReceived.TryRemove(SerialError.Frame, out DateTime dateTime))
                throw new IOException($"There is a framing error from {dateTime}");
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _resetLock.Dispose();

                    lock (_serialPort)
                    {
                        if (_serialPort.IsOpen)
                            _serialPort.Close();
                        _serialPort.Dispose();
                    }
                }
                _disposedValue = true;
            }
        }

        private async Task<TResult> RunUartCommunication<TResult>(Func<TResult> action, CancellationToken cancellationToken)
        {
            CheckUartOneWireBridge();

            try
            {
                await _resetLock.WaitAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                _serialPort.Open();
                if (ResetAndGetPresencePulse())
                    return action.Invoke();
                else
                    throw new InvalidOperationException("There is no pulse on the bus. No devices available");

            }
            catch (Exception e) when (e is TaskCanceledException || e is OperationCanceledException)
            {
                throw new TimeoutException("Operation canceled by timeout", e);
            }
            finally
            {
                if (_resetLock.CurrentCount < 1)
                    _resetLock.Release();

                _serialPort.Close();
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
