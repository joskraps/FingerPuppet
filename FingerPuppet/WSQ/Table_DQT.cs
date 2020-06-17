namespace FingerPuppet.WSQ
{
    public class Table_DQT
    {
        public const int MAX_SUBBANDS = 64;
        public float binCenter;
        public int dqtDef;
        public float[] qBin = new float[MAX_SUBBANDS];
        public float[] zBin = new float[MAX_SUBBANDS];
    }
}