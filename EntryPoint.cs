using System;
using System.IO;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;

namespace Emby.Plugins.BetterShuffle
{
    public sealed class EntryPoint : IServerEntryPoint
    {
        private const string HarmonyId = "dev.bettershuffle.emby";
        private const string SupportedServerVersion = "4.10.0.40";
        private const string ExpectedHarmonyVersion = "2.4.2.0";
        private const string ExpectedHarmonySha256 = "2b0496067bda368ff35c383d80421401c57a3acc091dcb3e5a8f15636104987f";
        private readonly ISessionManager sessionManager;
        private readonly IApplicationPaths applicationPaths;
        private readonly IXmlSerializer xmlSerializer;
        private readonly ILogger logger;
        private object harmony;
        private Type harmonyType;
        private bool playbackSubscribed;

        public EntryPoint(
            ISessionManager sessionManager,
            IApplicationPaths applicationPaths,
            IXmlSerializer xmlSerializer,
            ILogManager logManager)
        {
            this.sessionManager = sessionManager;
            this.applicationPaths = applicationPaths;
            this.xmlSerializer = xmlSerializer;
            this.logger = logManager.GetLogger("BetterShuffle");
        }

        public void Run()
        {
            BetterShuffleRuntime.Disable("Initialization has not completed.");

            try
            {
                string harmonyPath = Path.Combine(this.applicationPaths.PluginsPath, "0Harmony.dll");
                VerifyFileHash(harmonyPath, ExpectedHarmonySha256);

                Assembly harmonyAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(i => string.Equals(i.GetName().Name, "0Harmony", StringComparison.OrdinalIgnoreCase));
                if (harmonyAssembly == null)
                {
                    harmonyAssembly = Assembly.LoadFrom(harmonyPath);
                }

                if (!string.Equals(harmonyAssembly.GetName().Version?.ToString(), ExpectedHarmonyVersion, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Harmony {ExpectedHarmonyVersion} is required; loaded {harmonyAssembly.GetName().Version}.");
                }

                if (!string.IsNullOrWhiteSpace(harmonyAssembly.Location))
                {
                    VerifyFileHash(harmonyAssembly.Location, ExpectedHarmonySha256);
                }
                else
                {
                    this.logger.Warn(
                        "Emby loaded Harmony from memory; accepting the exact assembly version after validating the on-disk DLL hash");
                }

                this.harmonyType = harmonyAssembly.GetType("HarmonyLib.Harmony", throwOnError: false);
                Type harmonyMethodType = harmonyAssembly.GetType("HarmonyLib.HarmonyMethod", throwOnError: false);
                if (this.harmonyType == null || harmonyMethodType == null)
                {
                    throw new InvalidOperationException("The required Harmony types were not found.");
                }

                Type itemsServiceType = Type.GetType("Emby.Api.UserLibrary.ItemsService, Emby.Api", throwOnError: false);
                if (itemsServiceType == null)
                {
                    throw new InvalidOperationException("Emby.Api.UserLibrary.ItemsService was not found.");
                }

                string actualServerVersion = itemsServiceType.Assembly.GetName().Version?.ToString() ?? "unknown";
                if (!string.Equals(actualServerVersion, SupportedServerVersion, StringComparison.Ordinal))
                {
                    throw new NotSupportedException(
                        $"Emby Server {actualServerVersion} has not been validated; expected {SupportedServerVersion}.");
                }

                MethodInfo[] targets = itemsServiceType
                    .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                    .Where(i => i.Name == "GetItems"
                        && i.GetParameters().Length == 1
                        && string.Equals(
                            i.GetParameters()[0].ParameterType.FullName,
                            "Emby.Api.UserLibrary.GetItems",
                            StringComparison.Ordinal))
                    .ToArray();
                if (targets.Length != 1)
                {
                    throw new MissingMethodException(
                        $"Expected one compatible ItemsService.GetItems method, found {targets.Length}.");
                }

                MethodInfo target = targets[0];
                if (!target.ReturnType.IsGenericType
                    || !string.Equals(
                        target.ReturnType.GetGenericTypeDefinition().FullName,
                        "MediaBrowser.Model.Querying.QueryResult`1",
                        StringComparison.Ordinal)
                    || !string.Equals(
                        target.ReturnType.GetGenericArguments()[0].FullName,
                        "MediaBrowser.Model.Dto.BaseItemDto",
                        StringComparison.Ordinal))
                {
                    throw new MissingMethodException("ItemsService.GetItems has an unexpected return type.");
                }

                MethodInfo postfix = typeof(ItemsServicePatch).GetMethod(
                    nameof(ItemsServicePatch.Postfix),
                    BindingFlags.Public | BindingFlags.Static);
                if (postfix == null || postfix.GetParameters().Length != 3)
                {
                    throw new MissingMethodException("The BetterShuffle postfix is invalid.");
                }

                string statePath = Path.Combine(this.applicationPaths.PluginConfigurationsPath, "bettershuffle-state.xml");
                ShuffleStateStore store = new ShuffleStateStore(this.xmlSerializer, this.logger, statePath);
                BetterShuffleRuntime.Initialize(store, this.logger);

                object harmonyPostfix = Activator.CreateInstance(harmonyMethodType, new object[] { postfix });
                this.harmony = Activator.CreateInstance(this.harmonyType, new object[] { HarmonyId });
                MethodInfo patch = this.harmonyType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .SingleOrDefault(i => i.Name == "Patch" && i.GetParameters().Length == 5);
                if (patch == null)
                {
                    throw new MissingMethodException("The compatible Harmony patch method was not found.");
                }

                patch.Invoke(this.harmony, new[] { target, null, harmonyPostfix, null, null });
                MethodInfo getPatchedMethods = this.harmonyType.GetMethod(
                    "GetPatchedMethods",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    Type.EmptyTypes,
                    null);
                IEnumerable patchedMethods = getPatchedMethods?.Invoke(this.harmony, null) as IEnumerable;
                bool patchVerified = patchedMethods != null
                    && patchedMethods.Cast<object>().OfType<MethodBase>().Any(i => i.Equals(target));
                if (!patchVerified)
                {
                    throw new InvalidOperationException("Harmony did not report the ItemsService patch as active.");
                }

                this.sessionManager.PlaybackStart += OnPlaybackStart;
                this.playbackSubscribed = true;
                BetterShuffleRuntime.MarkActive();
                this.logger.Info("BetterShuffle safely replaced the stock ItemsService shuffle path ({0})", target);
            }
            catch (Exception ex)
            {
                this.SafeUnsubscribe();
                this.SafeUnpatch();
                Exception root = ex.GetBaseException();
                BetterShuffleRuntime.Disable(root.Message);
                this.logger.ErrorException(
                    "BetterShuffle initialization failed and the plugin disabled itself; Emby's stock shuffle remains active",
                    ex);
            }
        }

        public void Dispose()
        {
            try
            {
                this.SafeUnsubscribe();
                this.SafeUnpatch();
            }
            catch (Exception ex)
            {
                this.logger.ErrorException("BetterShuffle cleanup failed; server shutdown will continue", ex);
            }

            BetterShuffleRuntime.Disable("The plugin has stopped.");
        }

        private void OnPlaybackStart(object sender, PlaybackProgressEventArgs eventArgs)
        {
            try
            {
                if (!(eventArgs?.Item is Episode))
                {
                    return;
                }

                HashSet<string> userIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (eventArgs.Users != null)
                {
                    foreach (var user in eventArgs.Users)
                    {
                        userIds.Add(user.IdString);
                    }
                }

                if (!string.IsNullOrWhiteSpace(eventArgs.Session?.UserId))
                {
                    userIds.Add(eventArgs.Session.UserId);
                }

                foreach (string userId in userIds)
                {
                    BetterShuffleRuntime.MarkPlaybackStarted(userId, eventArgs.Item.GetClientId());
                }

                this.logger.Debug(
                    "Recorded playback start for episode {0} across {1} user(s)",
                    eventArgs.Item.GetClientId(),
                    userIds.Count);
            }
            catch (Exception ex)
            {
                this.logger.ErrorException("BetterShuffle ignored a playback event after an internal error", ex);
            }
        }

        private void SafeUnsubscribe()
        {
            if (!this.playbackSubscribed)
            {
                return;
            }

            try
            {
                this.sessionManager.PlaybackStart -= OnPlaybackStart;
            }
            catch (Exception ex)
            {
                this.logger.ErrorException("BetterShuffle could not remove its playback event handler", ex);
            }
            finally
            {
                this.playbackSubscribed = false;
            }
        }

        private void SafeUnpatch()
        {
            if (this.harmony == null || this.harmonyType == null)
            {
                return;
            }

            try
            {
                MethodInfo unpatchAll = this.harmonyType.GetMethod(
                    "UnpatchAll",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new[] { typeof(string) },
                    null);
                if (unpatchAll == null)
                {
                    throw new MissingMethodException("Harmony.UnpatchAll was not found.");
                }

                unpatchAll.Invoke(this.harmony, new object[] { HarmonyId });
            }
            catch (Exception ex)
            {
                this.logger.ErrorException("BetterShuffle could not remove its Harmony patch", ex);
            }
            finally
            {
                this.harmony = null;
            }
        }

        private static void VerifyFileHash(string path, string expectedSha256)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                throw new FileNotFoundException("A required BetterShuffle dependency is missing.", path);
            }

            string actual;
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha256 = SHA256.Create())
            {
                actual = string.Concat(sha256.ComputeHash(stream).Select(i => i.ToString("x2")));
            }

            if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Dependency integrity validation failed for {Path.GetFileName(path)}.");
            }
        }
    }
}
