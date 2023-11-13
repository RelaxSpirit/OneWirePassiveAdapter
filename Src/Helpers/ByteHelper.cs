using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OneWirePassiveAdapter.Helpers
{
    internal static class ByteHelper
    {
        private readonly static Dictionary<bool, byte> s_rx_bitsData = new()
        {
            {false, 0 },
            {true, 1 }
        };

        internal static byte GetByteMask(this byte sourceByte, bool bitValue, byte bitOffset)
        {
            return (byte)((-s_rx_bitsData[bitValue] ^ sourceByte) & (1 << bitOffset));
        }

        internal static IEnumerable<bool> ReadBitsCore(this byte[] input, CancellationToken cancellationToken)
        {
            if(input is null) throw new ArgumentNullException(nameof(input));

            foreach (var readByte in input)
            {
                for (int i = 0; i <= 7 && !cancellationToken.IsCancellationRequested; i++)
                    yield return ((readByte >> i) & 1) == 1;

                if (cancellationToken.IsCancellationRequested)
                    break;
            }
        }
    }
}
