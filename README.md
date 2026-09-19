# BetterShuffle

BetterShuffle is an Emby Server plugin that replaces the stock episode shuffle
response used by the native Android TV client. It keeps the existing Android TV
Shuffle button and requires no modified APK.

The plugin intercepts only authenticated `SortBy=Random` item queries whose
parent is a Series or Season. By default it also requires the client header to
identify `AndroidTv`.

Episode ordering combines:

- an 8x boost for unwatched episodes;
- lower weight for higher play counts;
- a 90-day recovery curve that suppresses recently watched episodes;
- weighted sampling without replacement, so the queue has no duplicates;
- a persistent per-user, per-series/season coverage bag;
- a maximum run of two episodes from the same season when alternatives exist.

BetterShuffle reads watched state, play count, and last-played time from Emby's
authoritative user-data repository. It does not rely on the recursive item DTO,
which omits play count and last-played time on Emby 4.10.0.40.

An episode leaves the coverage bag on Emby's playback-start event. Consequently,
stopping a queue and pressing Shuffle again keeps unserved episodes ahead of
episodes already played during the current coverage cycle.

## Compatibility note

Emby does not expose a supported hook for replacing its reserved `Random` sort.
BetterShuffle therefore applies a narrowly scoped Harmony postfix to the private
`Emby.Api.UserLibrary.ItemsService.GetItems` method. The patch is validated for
Emby Server 4.10.0.40 and should be retested after every Emby upgrade.

The patch changes only the returned order. Emby's normal authentication,
library permissions, item filtering, and 1,000-item Android TV queue limit are
left in place. Queries from other clients keep the stock random ordering unless
`RestrictToAndroidTv` is explicitly disabled.

Startup is fail-closed. The plugin verifies the exact Emby method signature,
the validated server version, Harmony version and SHA-256, and Harmony's active
patch registry. Any mismatch rolls back a partial patch, leaves Emby's stock
shuffle active, records `FailureReason` in the status response, and returns from
the entry point without propagating the initialization error to Emby.

## Build

```sh
dotnet publish BetterShuffle.csproj -c Release
```

Install `Emby.Plugins.BetterShuffle.dll` and `0Harmony.dll` in Emby's plugin
directory, restart Emby, and check authenticated endpoint
`/emby/BetterShuffle/Status`.

Do not copy the other `MediaBrowser.*` or `Emby.*` build dependencies into the
plugin directory; Emby supplies those assemblies itself.

## Configuration and state

The defaults are defined in `PluginConfiguration.cs`. Emby persists them through
the standard plugin configuration endpoint for plugin ID
`fd01c2a9-d6cf-49f0-bbf3-0f23cb9f63a1`. Coverage state is stored separately as
`bettershuffle-state.xml` in Emby's plugin configurations directory.

Set `EnableDebugLogging` to `true` in the plugin configuration to emit one
compact info-level diagnostic line per intercepted shuffle. The line includes
candidate watched/unwatched counts, coverage-tier size, modeled first-unwatched
probability, the selected first episode's play history, and user-data lookup
time. The same structured statistics are returned as `LastShuffle` by
`/emby/BetterShuffle/Status`.

See `TESTING.md` for the isolated integration test performed against Emby
4.10.0.40 and a read-only media mount.
