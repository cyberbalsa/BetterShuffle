using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using MediaBrowser.Model.Dto;

namespace Emby.Plugins.BetterShuffle
{
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

        public IList<BaseItemDto> Build(
            IEnumerable<BaseItemDto> episodes,
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

            List<BaseItemDto> result = SpreadSeasons(Rank(unserved, configuration, now), configuration.MaximumSameSeasonRun);
            result.AddRange(SpreadSeasons(Rank(served, configuration, now), configuration.MaximumSameSeasonRun));
            return result;
        }

        private List<BaseItemDto> Rank(IEnumerable<BaseItemDto> episodes, PluginConfiguration configuration, DateTimeOffset now)
        {
            return episodes
                .Select(item => new
                {
                    Item = item,
                    Key = -Math.Log(Math.Max(this.random.NextDouble(), 1e-12)) / GetWeight(item, configuration, now)
                })
                .OrderBy(i => i.Key)
                .Select(i => i.Item)
                .ToList();
        }

        private static double GetWeight(BaseItemDto item, PluginConfiguration configuration, DateTimeOffset now)
        {
            UserItemDataDto data = item.UserData;
            if (data == null || !data.Played)
            {
                return Math.Max(configuration.UnseenWeight, 0.01);
            }

            int playCount = Math.Max(data.PlayCount ?? 1, 1);
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
