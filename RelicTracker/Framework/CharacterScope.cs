namespace RelicTracker.Framework;

internal static class CharacterScope
{
    public static ulong CurrentContentId =>
        Svc.PlayerState.IsLoaded ? Svc.PlayerState.ContentId : 0;
}
