using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using MediaBrowser.Controller.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;

namespace Emby.Plugins.BetterShuffle
{
    internal sealed class ShuffleDiagnostics
    {
        public DateTimeOffset TimestampUtc { get; set; }

        public string ScopeName { get; set; }

        public int EpisodeCount { get; set; }

        public int WatchedCount { get; set; }

        public int UnwatchedCount { get; set; }

        public int CoverageRemaining { get; set; }

        public int FirstTierCount { get; set; }

        public double FirstTierUnwatchedProbability { get; set; }

        public string FirstEpisodeName { get; set; }

        public bool FirstEpisodePlayed { get; set; }

        public int FirstEpisodePlayCount { get; set; }

        public DateTimeOffset? FirstEpisodeLastPlayedDate { get; set; }

        public long UserDataLookupMilliseconds { get; set; }
    }

    internal static class BetterShuffleRuntime
    {
        private static readonly object SyncRoot = new object();
        private static readonly WeightedQueueBuilder QueueBuilder = new WeightedQueueBuilder();
        private static ShuffleStateStore stateStore;
        private static IUserDataManager userDataManager;
        private static ILogger logger;

        public static bool PatchActive { get; set; }

        public static string FailureReason { get; private set; }

        public static int BagCount => stateStore == null ? 0 : stateStore.BagCount;

        public static ShuffleDiagnostics LastShuffle { get; private set; }

        public static void Initialize(
            ShuffleStateStore store,
            IUserDataManager runtimeUserDataManager,
            ILogger runtimeLogger)
        {
            stateStore = store;
            userDataManager = runtimeUserDataManager;
            logger = runtimeLogger;
        }

        public static void MarkActive()
        {
            FailureReason = null;
            PatchActive = true;
        }

        public static void Disable(string reason)
        {
            PatchActive = false;
            FailureReason = string.IsNullOrWhiteSpace(reason)
                ? "BetterShuffle is inactive."
                : reason.Substring(0, Math.Min(reason.Length, 500));
        }

        public static void Reorder(object service, object request, object result)
        {
            try
            {
                PluginConfiguration configuration = Plugin.Instance?.Configuration;
                if (configuration == null || !configuration.Enabled || request == null || result == null)
                {
                    return;
                }

                PropertyInfo sortByProperty = request.GetType().GetProperty("SortBy");
                string sortBy = sortByProperty?.GetValue(request, null) as string;
                if (string.IsNullOrWhiteSpace(sortBy)
                    || !sortBy.Split(',').Any(i => string.Equals(i.Trim(), "Random", StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }

                BaseApiService apiService = service as BaseApiService;
                IRequest httpRequest = (service as IRequiresRequest)?.Request;
                if (apiService == null || httpRequest == null)
                {
                    return;
                }

                if (configuration.RestrictToAndroidTv && !IsAndroidTv(httpRequest))
                {
                    return;
                }

                string parentId = request.GetType().GetProperty("ParentId")?.GetValue(request, null)?.ToString();
                long parentInternalId;
                if (string.IsNullOrWhiteSpace(parentId) || !long.TryParse(parentId, out parentInternalId))
                {
                    return;
                }

                BaseItem parent = apiService.LibraryManager.GetItemById(parentInternalId);
                if (!(parent is Series) && !(parent is Season))
                {
                    return;
                }

                PropertyInfo itemsProperty = result.GetType().GetProperty("Items");
                BaseItemDto[] items = itemsProperty?.GetValue(result, null) as BaseItemDto[];
                if (items == null || items.Length < 2)
                {
                    return;
                }

                List<BaseItemDto> episodes = items.Where(i => string.Equals(i.Type, "Episode", StringComparison.OrdinalIgnoreCase)).ToList();
                if (episodes.Count < 2)
                {
                    return;
                }

                Guid userGuid = GetGuidProperty(request, "UserId");
                if (userGuid == Guid.Empty || stateStore == null)
                {
                    return;
                }

                User user = apiService.UserManager.GetUserById(userGuid);
                if (user == null || userDataManager == null)
                {
                    return;
                }

                Stopwatch userDataTimer = Stopwatch.StartNew();
                Dictionary<string, EpisodePlaybackData> playbackData = new Dictionary<string, EpisodePlaybackData>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (BaseItemDto episode in episodes)
                {
                    long episodeInternalId;
                    if (!long.TryParse(episode.Id, out episodeInternalId))
                    {
                        throw new InvalidOperationException($"Episode {episode.Id} does not have an internal numeric ID.");
                    }

                    UserItemData data = userDataManager.GetUserData(user, episodeInternalId);
                    playbackData[ShuffleStateStore.NormalizeId(episode.Id)] = new EpisodePlaybackData
                    {
                        Played = data?.Played ?? false,
                        PlayCount = data?.PlayCount ?? 0,
                        LastPlayedDate = data?.LastPlayedDate
                    };
                }

                userDataTimer.Stop();

                lock (SyncRoot)
                {
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    HashSet<string> remaining = stateStore.GetRemaining(
                        userGuid.ToString("N"),
                        ShuffleStateStore.NormalizeId(parentId),
                        episodes.Select(i => i.Id));
                    WeightedQueueResult build = QueueBuilder.Build(
                        episodes,
                        playbackData,
                        remaining,
                        configuration,
                        now);
                    Queue<BaseItemDto> queue = new Queue<BaseItemDto>(build.Items);

                    for (int index = 0; index < items.Length; index++)
                    {
                        if (string.Equals(items[index].Type, "Episode", StringComparison.OrdinalIgnoreCase))
                        {
                            items[index] = queue.Dequeue();
                        }
                    }

                    BaseItemDto first = build.Items[0];
                    EpisodePlaybackData firstData = playbackData[ShuffleStateStore.NormalizeId(first.Id)];
                    LastShuffle = new ShuffleDiagnostics
                    {
                        TimestampUtc = now,
                        ScopeName = parent.Name,
                        EpisodeCount = episodes.Count,
                        WatchedCount = build.WatchedCount,
                        UnwatchedCount = build.UnwatchedCount,
                        CoverageRemaining = remaining.Count,
                        FirstTierCount = build.FirstTierCount,
                        FirstTierUnwatchedProbability = build.FirstTierUnwatchedProbability,
                        FirstEpisodeName = first.Name,
                        FirstEpisodePlayed = firstData.Played,
                        FirstEpisodePlayCount = firstData.PlayCount,
                        FirstEpisodeLastPlayedDate = firstData.LastPlayedDate,
                        UserDataLookupMilliseconds = userDataTimer.ElapsedMilliseconds
                    };

                    Action<string, object[]> log = configuration.EnableDebugLogging
                        ? new Action<string, object[]>((message, args) => logger.Info(message, args))
                        : new Action<string, object[]>((message, args) => logger.Debug(message, args));
                    log(
                        "BetterShuffle debug: scope={0}; episodes={1}; watched={2}; unwatched={3}; coverageRemaining={4}; firstTier={5}; firstTierUnwatchedChance={6:F2}%; first={7}; firstPlayed={8}; firstPlayCount={9}; firstLastPlayed={10}; userDataMs={11}",
                        new object[]
                        {
                            parent.Name,
                            episodes.Count,
                            build.WatchedCount,
                            build.UnwatchedCount,
                            remaining.Count,
                            build.FirstTierCount,
                            build.FirstTierUnwatchedProbability * 100.0,
                            first.Name,
                            firstData.Played,
                            firstData.PlayCount,
                            firstData.LastPlayedDate?.ToString("O") ?? "never",
                            userDataTimer.ElapsedMilliseconds
                        });
                }
            }
            catch (Exception ex)
            {
                logger?.ErrorException("BetterShuffle could not reorder an Items response; stock order was preserved", ex);
            }
        }

        public static void MarkPlaybackStarted(string userId, string episodeId)
        {
            stateStore?.MarkPlaybackStarted(userId, episodeId);
        }

        private static Guid GetGuidProperty(object instance, string propertyName)
        {
            object value = instance.GetType().GetProperty(propertyName)?.GetValue(instance, null);
            if (value is Guid)
            {
                return (Guid)value;
            }

            Guid parsed;
            return value != null && Guid.TryParse(value.ToString(), out parsed) ? parsed : Guid.Empty;
        }

        private static bool IsAndroidTv(IRequest request)
        {
            string client = request.Headers["X-Emby-Client"] ?? string.Empty;
            string userAgent = request.UserAgent ?? string.Empty;
            return client.IndexOf("AndroidTv", StringComparison.OrdinalIgnoreCase) >= 0
                || client.IndexOf("Android TV", StringComparison.OrdinalIgnoreCase) >= 0
                || (userAgent.IndexOf("Android", StringComparison.OrdinalIgnoreCase) >= 0
                    && userAgent.IndexOf("TV", StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
