using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MediaBrowser.Controller.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;

namespace Emby.Plugins.BetterShuffle
{
    internal static class BetterShuffleRuntime
    {
        private static readonly object SyncRoot = new object();
        private static readonly WeightedQueueBuilder QueueBuilder = new WeightedQueueBuilder();
        private static ShuffleStateStore stateStore;
        private static ILogger logger;

        public static bool PatchActive { get; set; }

        public static string FailureReason { get; private set; }

        public static int BagCount => stateStore == null ? 0 : stateStore.BagCount;

        public static void Initialize(ShuffleStateStore store, ILogger runtimeLogger)
        {
            stateStore = store;
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

                lock (SyncRoot)
                {
                    HashSet<string> remaining = stateStore.GetRemaining(
                        userGuid.ToString("N"),
                        ShuffleStateStore.NormalizeId(parentId),
                        episodes.Select(i => i.Id));
                    IList<BaseItemDto> orderedEpisodes = QueueBuilder.Build(episodes, remaining, configuration, DateTimeOffset.UtcNow);
                    Queue<BaseItemDto> queue = new Queue<BaseItemDto>(orderedEpisodes);

                    for (int index = 0; index < items.Length; index++)
                    {
                        if (string.Equals(items[index].Type, "Episode", StringComparison.OrdinalIgnoreCase))
                        {
                            items[index] = queue.Dequeue();
                        }
                    }

                    logger.Debug(
                        "Replaced stock Android TV shuffle for {0}: {1} episodes, {2} remaining in coverage cycle",
                        parent.Name,
                        episodes.Count,
                        remaining.Count);
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
