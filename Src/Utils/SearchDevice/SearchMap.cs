namespace OneWirePassiveAdapter.Utils.SearchDevice
{
    internal class SearchMap
    {
        private ulong _lasSearchDeviceId = 0;
        private readonly bool[] _filter = null!;
        internal int LastZeroIndex { get; private set; } = -1;
        internal int LastCollisionIndex { get; private set; } = -1;       

        internal SearchMap(bool[] filter = null!)
        {
            _filter = filter;
        }

        internal bool? GetBitDevice(bool masterBit, bool additionalBit, int bitIndex)
        {
            if (bitIndex < 0)
                return null;
            //Если у всех устройств значение бита серийного номера в порядке совпадает
            if (masterBit != additionalBit)
            {
                if (bitIndex < (_filter?.Length ?? -1) && _filter![bitIndex] != masterBit)
                    return null;
               
                return masterBit;

                
            }
            else if (masterBit == additionalBit && !masterBit) //значит есть устройства с разными номерами
            {
                if(bitIndex > LastCollisionIndex)
                {
                    //Запоминаем позицию последнего нулевого бита по коллизии
                    LastZeroIndex = bitIndex;
                    //Идем по левой ветке
                    masterBit = false;
                }
                else if(bitIndex < LastCollisionIndex) 
                {
                    if (((_lasSearchDeviceId >> bitIndex) & 1) == 1)
                    {
                        masterBit = true;
                    }
                    else
                    {
                        LastZeroIndex = bitIndex;
                        masterBit = false;
                    }
                        
                }
                else
                {
                    //выбираем ветку справа, то есть берём устройство с битом равным единице так как по последней
                    //коллизии шли влево, то есть по устройствам с нулевым битом в данной позиции 
                    masterBit = true;
                }

                if (bitIndex < (_filter?.Length ?? -1) && _filter![bitIndex] != masterBit)
                    return null;
                else
                    return masterBit;
            }

            return null;
        }

        internal void SetLastSearchDeviceId(ulong lasSearchDeviceId)
        {
            _lasSearchDeviceId = lasSearchDeviceId;
            LastCollisionIndex = LastZeroIndex;
            LastZeroIndex = -1;
        }
    }
}
