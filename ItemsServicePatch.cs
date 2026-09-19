namespace Emby.Plugins.BetterShuffle
{
    internal static class ItemsServicePatch
    {
        public static void Postfix(object __instance, object request, object __result)
        {
            BetterShuffleRuntime.Reorder(__instance, request, __result);
        }
    }
}
