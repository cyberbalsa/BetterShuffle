using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace Emby.Plugins.BetterShuffle
{
    [Route("/BetterShuffle/Status", "GET", Summary = "Gets BetterShuffle status")]
    [Authenticated]
    public sealed class GetBetterShuffleStatus : IReturn<BetterShuffleStatus>
    {
    }

    public sealed class BetterShuffleStatus
    {
        public bool Enabled { get; set; }

        public bool PatchActive { get; set; }

        public bool RestrictToAndroidTv { get; set; }

        public int BagCount { get; set; }

        public string Version { get; set; }

        public string FailureReason { get; set; }

        public string SupportedServerVersion { get; set; }
    }

    public sealed class BetterShuffleStatusService : IService
    {
        public object Get(GetBetterShuffleStatus request)
        {
            PluginConfiguration configuration = Plugin.Instance.Configuration;
            return new BetterShuffleStatus
            {
                Enabled = configuration.Enabled,
                PatchActive = BetterShuffleRuntime.PatchActive,
                RestrictToAndroidTv = configuration.RestrictToAndroidTv,
                BagCount = BetterShuffleRuntime.BagCount,
                Version = Plugin.Instance.Version?.ToString() ?? "unknown",
                FailureReason = BetterShuffleRuntime.FailureReason,
                SupportedServerVersion = "4.10.0.40"
            };
        }
    }
}
