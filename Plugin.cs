using System;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;

namespace Emby.Plugins.BetterShuffle
{
    public sealed class Plugin : BasePlugin<PluginConfiguration>
    {
        private static readonly Guid PluginId = new Guid("fd01c2a9-d6cf-49f0-bbf3-0f23cb9f63a1");

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogManager logManager)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            Logger = logManager.GetLogger(Name);
            Logger.Info("BetterShuffle plugin assembly loaded");
        }

        public static Plugin Instance { get; private set; }

        internal static ILogger Logger { get; private set; }

        public override string Name => "BetterShuffle";

        public override string Description => "Coverage-aware weighted episode shuffle for the stock Emby Android TV client.";

        public override Guid Id => PluginId;
    }
}
