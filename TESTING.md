# BetterShuffle integration test

Tested on 2026-09-19 with an isolated Emby Server 4.10.0.40 data directory and
a test media share mounted read-only. The production Emby instance was not
changed during integration testing.

The request shape was taken from the stock Emby Android TV client: an
authenticated recursive item query with a Series or Season `ParentId` and
`SortBy=Random`. Tests sent `X-Emby-Client: AndroidTv`; no custom endpoint was
used to generate a queue.

## Results

- Emby loaded both plugin assemblies and `/emby/BetterShuffle/Status` reported
  `PatchActive: true`.
- Stock Series and Season shuffle requests both returned all 11 test episodes.
- A control request without Android TV client identity did not invoke the
  replacement.
- Five episodes were marked watched and six unwatched. Across 250 stock shuffle
  requests, an unwatched episode appeared first 241 times (96.4%).
- A real `/Sessions/Playing` playback-start report removed the started episode
  from the persisted coverage bag. It then appeared at position 11 of 11 in the
  next queue.
- Exhausting the bag produced 11 distinct first-play selections across 11
  playback starts. The following shuffle refilled the bag with all 11 episodes.
- The final Release build completed with zero warnings and zero errors.

## Fail-closed startup test

Version 0.1.1 was also started with an intentionally invalid `0Harmony.dll`.
BetterShuffle detected the SHA-256 mismatch, returned normally from its server
entry point, and reported `PatchActive: false` with a `FailureReason`. Emby's
ping and authenticated APIs remained available and its stock shuffle stayed
active. Restoring the validated dependency and restarting returned the status
to `PatchActive: true`.

## Compatibility boundary

Emby's reserved `Random` sort is implemented inside the server and has no
documented plugin override. BetterShuffle therefore patches the private
`Emby.Api.UserLibrary.ItemsService.GetItems` method. Repeat this integration
test before deploying the plugin after an Emby Server upgrade.
