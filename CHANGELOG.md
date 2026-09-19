# Changelog

## 0.1.2

- Read play count and last-played time from Emby's authoritative user-data
  repository instead of the incomplete recursive-list DTO.
- Add opt-in per-plugin debug logging without requiring Emby's global debug log.
- Expose structured statistics for the latest shuffle through the status API.

## 0.1.1

- Add fail-closed startup with exact Emby signature and version checks.
- Verify the Harmony version, SHA-256, and registered patch before activation.
- Roll back partial initialization without propagating errors to Emby.
- Expose inactive-state diagnostics through `FailureReason`.
- Handle Emby's distinct internal and client-facing episode IDs in coverage state.

## 0.1.0

- Initial weighted, coverage-aware Android TV episode shuffle.
