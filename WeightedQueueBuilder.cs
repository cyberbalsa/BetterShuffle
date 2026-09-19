using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using MediaBrowser.Model.Dto;

namespace Emby.Plugins.BetterShuffle
{
    internal sealed class EpisodePlaybackData
    {
        public bool Played { get; set; }

        public int PlayCount { get; set; }

        public DateTimeOffset? LastPlayedDate { get; set; }
    }

    internal sealed class WeightedQueueResult
    {
        public IList<BaseItemDto> Items { get; set; }

        public int WatchedCount { get; set; }

        public int UnwatchedCount { get; set; }

        public int FirstTierCount { get; set; }

        public int FirstTierWatchedCount { get; set; }

        public int FirstTierUnwatchedCount { get; set; }

        public double FirstTierUnwatchedProbability { get; set; }
    }

    internal sealed class WeightedQueueBuilder
    {
        private readonly Random random;

        public WeightedQueueBuilder()
        {
            byte[] seedBytes = new byte[4];
            using (RandomNumberGenerator generator = RandomNumberGenerator.Create())
            {
                generator.GetBytes(seedBytes);
            }

            this.random = new Random(BitConverter.ToInt32(seedBytes, 0));
        }

        internal WeightedQueueBuilder(int seed)
        {
            this.random = new Random(seed);
        }

        public WeightedQueueResult Build(
            IEnumerable<BaseItemDto> episodes,
            IReadOnlyDictionary<string, EpisodePlaybackData> playbackData,
            HashSet<string> remainingIds,
            PluginConfiguration configuration,
            DateTimeOffset now)
        {
            List<BaseItemDto> all = episodes.ToList();
            List<BaseItemDto> unserved;
            List<BaseItemDto> served;

            if (configuration.CoverageBagEnabled)
            {
                unserved = all.Where(i => remainingIds.Contains(ShuffleStateStore.NormalizeId(i.Id))).ToList();
                served = all.Where(i => !remainingIds.Contains(ShuffleStateStore.NormalizeId(i.Id))).ToList();
            }
            else
            {
                unserved = all;
                served = new List<BaseItemDto>();
            }

            List<BaseItemDto> firstTier = unserved.Count > 0 ? unserved : served;
            double firstTierUnwatchedWeight = firstTier
                .Where(i => !GetPlaybackData(i, playbackData).Played)
                .Sum(i => GetWeight(GetPlaybackData(i, playbackData), configuration, now));
            double firstTierTotalWeight = firstTier
                .Sum(i => GetWeight(GetPlaybackData(i, playbackData), configuration, now));

            List<BaseItemDto> result = SpreadSeasons(
                Rank(unserved, playbackData, configuration, now),
                configuration.MaximumSameSeasonRun);
            result.AddRange(SpreadSeasons(
                Rank(served, playbackData, configuration, now),
                configuration.MaximumSameSeasonRun));

            return new WeightedQueueResult
            {
                Items = result,
                WatchedCount = all.Count(i => GetPlaybackData(i, playbackData).Played),
                UnwatchedCount = all.Count(i => !GetPlaybackData(i, playbackData).Played),
                FirstTierCount = firstTier.Count,
                FirstTierWatchedCount = firstTier.Count(i => GetPlaybackData(i, playbackData).Played),
                FirstTierUnwatchedCount = firstTier.Count(i => !GetPlaybackData(i, playbackData).Played),
                FirstTierUnwatchedProbability = firstTierTotalWeight > 0
                    ? firstTierUnwatchedWeight / firstTierTotalWeight
                    : 0
            };
        }

        private List<BaseItemDto> Rank(
            IEnumerable<BaseItemDto> episodes,
            IReadOnlyDictionary<string, EpisodePlaybackData> playbackData,
            PluginConfiguration configuration,
            DateTimeOffset now)
        {
            return episodes
                .Select(item => new
                {
                    Item = item,
                    Key = -Math.Log(Math.Max(this.random.NextDouble(), 1e-12))
                        / GetWeight(GetPlaybackData(item, playbackData), configuration, now)
                })
                .OrderBy(i => i.Key)
                .Select(i => i.Item)
                .ToList();
        }

        private static EpisodePlaybackData GetPlaybackData(
            BaseItemDto item,
            IReadOnlyDictionary<string, EpisodePlaybackData> playbackData)
        {
            EpisodePlaybackData data;
            if (!playbackData.TryGetValue(ShuffleStateStore.NormalizeId(item.Id), out data))
            {
                throw new InvalidOperationException($"Authoritative user data was not loaded for episode {item.Id}.");
            }

            return data;
        }

        private static double GetWeight(
            EpisodePlaybackData data,
            PluginConfiguration configuration,
            DateTimeOffset now)
        {
            if (!data.Played)
            {
                return Math.Max(configuration.UnseenWeight, 0.01);
            }

            int playCount = Math.Max(data.PlayCount, 1);
            double playCountWeight = 1.0 / Math.Pow(1.0 + playCount, Math.Max(configuration.PlayCountExponent, 0.0));
            double recoveryDays = Math.Max(configuration.RecencyRecoveryDays, 1.0);
            double daysSincePlayed = data.LastPlayedDate.HasValue
                ? Math.Max(0.0, (now - data.LastPlayedDate.Value).TotalDays)
                : recoveryDays;
            double recovery = Math.Min(daysSincePlayed / recoveryDays, 1.0);
            double minimum = Math.Min(Math.Max(configuration.MinimumRecencyWeight, 0.01), 1.0);
            double recencyWeight = minimum + ((1.0 - minimum) * recovery);
            return Math.Max(playCountWeight * recencyWeight, 0.001);
        }

        private static List<BaseItemDto> SpreadSeasons(List<BaseItemDto> ranked, int maximumSameSeasonRun)
        {
            if (maximumSameSeasonRun < 1 || ranked.Count < 2)
            {
                return ranked;
            }

            List<BaseItemDto> pending = new List<BaseItemDto>(ranked);
            List<BaseItemDto> result = new List<BaseItemDto>(ranked.Count);
            int? currentSeason = null;
            int currentRun = 0;

            while (pending.Count > 0)
            {
                int index = 0;
                if (currentSeason.HasValue && currentRun >= maximumSameSeasonRun)
                {
                    int alternative = pending.FindIndex(i => i.ParentIndexNumber != currentSeason);
                    if (alternative >= 0)
                    {
                        index = alternative;
                    }
                }

                BaseItemDto selected = pending[index];
                pending.RemoveAt(index);
                if (selected.ParentIndexNumber == currentSeason)
                {
                    currentRun++;
                }
                else
                {
                    currentSeason = selected.ParentIndexNumber;
                    currentRun = 1;
                }

                result.Add(selected);
            }

            return result;
        }
    }
}
