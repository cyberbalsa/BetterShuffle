using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;

namespace Emby.Plugins.BetterShuffle
{
    public sealed class ShuffleStateDocument
    {
        public List<ShuffleBagState> Bags { get; set; } = new List<ShuffleBagState>();
    }

    public sealed class ShuffleBagState
    {
        public string UserId { get; set; }

        public string ScopeId { get; set; }

        public List<string> EpisodeIds { get; set; } = new List<string>();

        public List<string> RemainingEpisodeIds { get; set; } = new List<string>();
    }

    internal sealed class ShuffleStateStore
    {
        private readonly object syncRoot = new object();
        private readonly IXmlSerializer serializer;
        private readonly ILogger logger;
        private readonly string path;
        private ShuffleStateDocument document;

        public ShuffleStateStore(IXmlSerializer serializer, ILogger logger, string path)
        {
            this.serializer = serializer;
            this.logger = logger;
            this.path = path;
            this.document = Load();
        }

        public HashSet<string> GetRemaining(string userId, string scopeId, IEnumerable<string> currentEpisodeIds)
        {
            lock (this.syncRoot)
            {
                string normalizedUser = NormalizeId(userId);
                string normalizedScope = NormalizeId(scopeId);
                HashSet<string> current = new HashSet<string>(currentEpisodeIds.Select(NormalizeId), StringComparer.OrdinalIgnoreCase);
                ShuffleBagState bag = this.document.Bags.FirstOrDefault(i =>
                    string.Equals(i.UserId, normalizedUser, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(i.ScopeId, normalizedScope, StringComparison.OrdinalIgnoreCase));

                bool changed = false;
                if (bag == null)
                {
                    bag = new ShuffleBagState
                    {
                        UserId = normalizedUser,
                        ScopeId = normalizedScope,
                        EpisodeIds = current.ToList(),
                        RemainingEpisodeIds = current.ToList()
                    };
                    this.document.Bags.Add(bag);
                    changed = true;
                }
                else
                {
                    HashSet<string> known = new HashSet<string>(bag.EpisodeIds.Select(NormalizeId), StringComparer.OrdinalIgnoreCase);
                    HashSet<string> remaining = new HashSet<string>(bag.RemainingEpisodeIds.Select(NormalizeId), StringComparer.OrdinalIgnoreCase);

                    if (remaining.RemoveWhere(id => !current.Contains(id)) > 0)
                    {
                        changed = true;
                    }

                    foreach (string id in current)
                    {
                        if (!known.Contains(id))
                        {
                            remaining.Add(id);
                            changed = true;
                        }
                    }

                    if (remaining.Count == 0 && current.Count > 0)
                    {
                        remaining.UnionWith(current);
                        changed = true;
                    }

                    if (!known.SetEquals(current))
                    {
                        changed = true;
                    }

                    bag.EpisodeIds = current.ToList();
                    bag.RemainingEpisodeIds = remaining.ToList();
                }

                if (changed)
                {
                    Save();
                }

                return new HashSet<string>(bag.RemainingEpisodeIds, StringComparer.OrdinalIgnoreCase);
            }
        }

        public void MarkPlaybackStarted(string userId, string episodeId)
        {
            lock (this.syncRoot)
            {
                string normalizedUser = NormalizeId(userId);
                string normalizedEpisode = NormalizeId(episodeId);
                bool changed = false;

                foreach (ShuffleBagState bag in this.document.Bags.Where(i =>
                    string.Equals(i.UserId, normalizedUser, StringComparison.OrdinalIgnoreCase)))
                {
                    int removed = bag.RemainingEpisodeIds.RemoveAll(i =>
                        string.Equals(NormalizeId(i), normalizedEpisode, StringComparison.OrdinalIgnoreCase));
                    changed |= removed > 0;
                }

                if (changed)
                {
                    Save();
                }
            }
        }

        public int BagCount
        {
            get
            {
                lock (this.syncRoot)
                {
                    return this.document.Bags.Count;
                }
            }
        }

        private ShuffleStateDocument Load()
        {
            try
            {
                if (File.Exists(this.path))
                {
                    return (ShuffleStateDocument)this.serializer.DeserializeFromFile(typeof(ShuffleStateDocument), this.path);
                }
            }
            catch (Exception ex)
            {
                this.logger.ErrorException("Unable to load BetterShuffle state; starting with empty state", ex);
            }

            return new ShuffleStateDocument();
        }

        private void Save()
        {
            string directory = Path.GetDirectoryName(this.path);
            string temporaryPath = this.path + ".tmp";

            try
            {
                Directory.CreateDirectory(directory);
                this.serializer.SerializeToFile(this.document, temporaryPath);
                if (File.Exists(this.path))
                {
                    File.Replace(temporaryPath, this.path, null);
                }
                else
                {
                    File.Move(temporaryPath, this.path);
                }
            }
            catch (Exception ex)
            {
                this.logger.ErrorException("Unable to save BetterShuffle state", ex);
            }
        }

        internal static string NormalizeId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            Guid guid;
            return Guid.TryParse(value, out guid)
                ? guid.ToString("N")
                : value.Trim().Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
