namespace FingerPuppet.WSQ
{
    public class TableDHT
    {
        private const int MAX_HUFFBITS = 16; /*DO NOT CHANGE THIS CONSTANT!! */
        private const int MAX_HUFFCOUNTS_WSQ = 256; /* Length of code table: change as needed */
        public int[] huffbits = new int[MAX_HUFFBITS];
        public int[] huffvalues = new int[MAX_HUFFCOUNTS_WSQ + 1];
        public byte tabdef;
    }
}