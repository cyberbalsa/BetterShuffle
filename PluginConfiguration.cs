using MediaBrowser.Model.Plugins;

namespace Emby.Plugins.BetterShuffle
{
    public sealed class PluginConfiguration : BasePluginConfiguration
    {
        public bool Enabled { get; set; } = true;

        public bool RestrictToAndroidTv { get; set; } = true;

        public double UnseenWeight { get; set; } = 8.0;

        public double PlayCountExponent { get; set; } = 0.75;

        public double MinimumRecencyWeight { get; set; } = 0.10;

        public double RecencyRecoveryDays { get; set; } = 90.0;

        public int MaximumSameSeasonRun { get; set; } = 2;

        public bool CoverageBagEnabled { get; set; } = true;
    }
}
