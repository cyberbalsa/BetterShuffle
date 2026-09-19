using System;
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

        public bool DebugLoggingEnabled { get; set; }

        public BetterShuffleLastShuffleStatus LastShuffle { get; set; }
    }

    public sealed class BetterShuffleLastShuffleStatus
    {
        public DateTimeOffset TimestampUtc { get; set; }

        public string ScopeName { get; set; }

        public int EpisodeCount { get; set; }

        public int WatchedCount { get; set; }

        public int UnwatchedCount { get; set; }

        public int CoverageRemaining { get; set; }

        public int FirstTierCount { get; set; }

        public double FirstTierUnwatchedProbabilityPercent { get; set; }

        public string FirstEpisodeName { get; set; }

        public bool FirstEpisodePlayed { get; set; }

        public int FirstEpisodePlayCount { get; set; }

        public DateTimeOffset? FirstEpisodeLastPlayedDate { get; set; }

        public long UserDataLookupMilliseconds { get; set; }
    }

    public sealed class BetterShuffleStatusService : IService
    {
        public object Get(GetBetterShuffleStatus request)
        {
            PluginConfiguration configuration = Plugin.Instance.Configuration;
            ShuffleDiagnostics diagnostics = BetterShuffleRuntime.LastShuffle;
            return new BetterShuffleStatus
            {
                Enabled = configuration.Enabled,
                PatchActive = BetterShuffleRuntime.PatchActive,
                RestrictToAndroidTv = configuration.RestrictToAndroidTv,
                BagCount = BetterShuffleRuntime.BagCount,
                Version = Plugin.Instance.Version?.ToString() ?? "unknown",
                FailureReason = BetterShuffleRuntime.FailureReason,
                SupportedServerVersion = "4.10.0.40",
                DebugLoggingEnabled = configuration.EnableDebugLogging,
                LastShuffle = diagnostics == null
                    ? null
                    : new BetterShuffleLastShuffleStatus
                    {
                        TimestampUtc = diagnostics.TimestampUtc,
                        ScopeName = diagnostics.ScopeName,
                        EpisodeCount = diagnostics.EpisodeCount,
                        WatchedCount = diagnostics.WatchedCount,
                        UnwatchedCount = diagnostics.UnwatchedCount,
                        CoverageRemaining = diagnostics.CoverageRemaining,
                        FirstTierCount = diagnostics.FirstTierCount,
                        FirstTierUnwatchedProbabilityPercent = diagnostics.FirstTierUnwatchedProbability * 100.0,
                        FirstEpisodeName = diagnostics.FirstEpisodeName,
                        FirstEpisodePlayed = diagnostics.FirstEpisodePlayed,
                        FirstEpisodePlayCount = diagnostics.FirstEpisodePlayCount,
                        FirstEpisodeLastPlayedDate = diagnostics.FirstEpisodeLastPlayedDate,
                        UserDataLookupMilliseconds = diagnostics.UserDataLookupMilliseconds
                    }
            };
        }
    }
}
