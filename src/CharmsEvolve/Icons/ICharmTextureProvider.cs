namespace CharmsEvolve.Icons
{
    public interface ICharmTextureProvider
    {
        bool TryGetIcon(string charmKey, int originalCharmId, out IconHandle icon);
    }
}
