using System;

namespace FingerPuppet.WSQ
{
    public class Token
    {
        public byte[] buffer;
        public int pointer;
        public QuantTree[] qtree;
        public TableDHT[] tableDHT;
        public Table_DQT tableDQT;
        public TableDTT tableDTT;
        public WavletTree[] wtree;

        public Token(Span<byte> buffer)
        {
            this.buffer = buffer.ToArray();
            pointer = 0;
        }

        public void Initialize()
        {
            tableDTT = new TableDTT();
            tableDQT = new Table_DQT();

            /* Init DHT Tables to 0. */
            tableDHT = new TableDHT[WsqHelper.MAX_DHT_TABLES];
            for (var i = 0; i < WsqHelper.MAX_DHT_TABLES; i++)
                tableDHT[i] = new TableDHT
                {
                    tabdef = 0
                };
        }

        public long ReadInt()
        {
            var byte1 = buffer[pointer++];
            var byte2 = buffer[pointer++];
            var byte3 = buffer[pointer++];
            var byte4 = buffer[pointer++];

            return ((0xffL & byte1) << 24) | ((0xffL & byte2) << 16) | ((0xffL & byte3) << 8) | (0xffL & byte4);
        }

        public int ReadShort()
        {
            var byte1 = buffer[pointer++];
            var byte2 = buffer[pointer++];

            return ((0xff & byte1) << 8) | (0xff & byte2);
        }

        public int ReadByte()
        {
            return 0xff & buffer[pointer++];
        }

        public Span<byte> ReadBytes(int size)
        {
            var bytes = new byte[size];

            for (var i = 0; i < size; i++)
                bytes[i] = buffer[pointer++];

            return bytes;
        }
    }
}