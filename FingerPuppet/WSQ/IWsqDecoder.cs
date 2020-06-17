namespace FingerPuppet.WSQ
{
    public interface IWsqDecoder
    {
        byte[] Decode(byte[] wsq);
    }
}