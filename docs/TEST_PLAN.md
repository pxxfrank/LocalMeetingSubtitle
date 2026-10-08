# Test Plan

The solution uses **xunit 2.5.3** with three test projects. All commands below are
**Windows PowerShell 5.1** syntax (`;` chaining). If the .NET 8 SDK is not on `PATH`, prefix a
session with:

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
```

## Test categories

| Category | Project | What it covers |
| --- | --- | --- |
| **Unit** | `tests/LocalMeetingSubtitle.UnitTests` | Pure logic: `StreamingResampler`, `ChannelConverter`, `BoundedAudioQueue`, `SubtitleAccumulator`, hotword parsing/validation, text-correction engine, SQLite repositories, export formatters |
| **Integration** | `tests/LocalMeetingSubtitle.IntegrationTests` | Cross-layer wiring: pipeline integration, hotword wiring, export round-trip, and a real-model decode against `test_wavs/0.wav` (dynamically skipped when the model is absent) |
| **Performance / long-run** | `tests/LocalMeetingSubtitle.PerformanceTests` | `[Trait("Category","Performance")]`: bounded-queue stress, resampler throughput (60 s stereo 48k→16k), 10-minute bounded/monotonic/exception-free pipeline run |
| **Manual soak (NOT executed)** | `tests/LocalMeetingSubtitle.PerformanceTests` | `ThreeHourSoak` — a documented placeholder, `[Fact(Skip=…)]` / `[Trait("Category","Soak")]` |

> Exception handling is exercised throughout (retry-on-persist, capture errors, model-missing paths)
> rather than as a separate project. There is currently no dedicated "exception" test project.

## Exact commands

```powershell
# Unit tests
dotnet test tests/LocalMeetingSubtitle.UnitTests/LocalMeetingSubtitle.UnitTests.csproj -c Debug

# Integration tests
dotnet test tests/LocalMeetingSubtitle.IntegrationTests/LocalMeetingSubtitle.IntegrationTests.csproj -c Debug

# Performance / long-run tests ONLY (by trait)
dotnet test tests/LocalMeetingSubtitle.PerformanceTests/LocalMeetingSubtitle.PerformanceTests.csproj -c Debug --filter "Category=Performance"

# The whole solution at once
dotnet test LocalMeetingSubtitle.sln -c Debug
```

### The `--filter "Category=Performance"` note

`PerformanceTests` mixes quick scenarios with long ones. Tagging the long scenarios with
`[Trait("Category","Performance")]` lets a normal `dotnet test` run skip them (default filter runs
everything, but the long ones are relatively cheap except the soak). To run **only** the long/performance
scenarios, use `--filter "Category=Performance"` as shown above.

### ThreeHourSoak (manual — never executed)

`ThreeHourSoak` is `Skip`-ed by design because it needs ~3 hours of wall-clock time. To run it,
remove the `Skip` argument on the `[Fact]` first, then:

```powershell
dotnet test tests/LocalMeetingSubtitle.PerformanceTests/LocalMeetingSubtitle.PerformanceTests.csproj -c Debug --filter "FullyQualifiedName~ThreeHourSoak"
```

## Last run results (development host)

| Project | Result |
| --- | --- |
| `UnitTests` | **112 passed / 0 failed** |
| `IntegrationTests` | **8 passed / 0 failed** |
| `PerformanceTests` | **3 passed / 1 skipped** (the skipped one is `ThreeHourSoak`, explicitly NOT executed) |
| Full solution `dotnet build` | **0 errors** (a few benign `CS0067` warnings in test fakes) |

Environment: Windows 10 Pro 19045, Intel Xeon Gold 6230N (36 cores/36 threads), 64 GB RAM,
one virtual render audio device, no Intel Arc, no NPU, interactive session.

> These are development-host results. No test was executed on the target hardware
> (Windows 11 + Core Ultra 7 155H) — those items are BLOCKED / NOT_TESTED.
> See [`RELEASE_CHECKLIST.md`](RELEASE_CHECKLIST.md) and [`PERFORMANCE.md`](PERFORMANCE.md).
