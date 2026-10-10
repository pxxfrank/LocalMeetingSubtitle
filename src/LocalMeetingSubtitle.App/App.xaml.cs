using System.IO;
using System.Windows;
using System.Windows.Threading;
using LocalMeetingSubtitle.App.Infrastructure;
using LocalMeetingSubtitle.App.ViewModels;
using LocalMeetingSubtitle.Asr;
using LocalMeetingSubtitle.ModelDownloads;
using LocalMeetingSubtitle.Audio;
using LocalMeetingSubtitle.Core.Abstractions;
using LocalMeetingSubtitle.Core.Hotwords;
using LocalMeetingSubtitle.Core.Models;
using LocalMeetingSubtitle.Core.Speakers;
using LocalMeetingSubtitle.Core.Transcription;
using LocalMeetingSubtitle.Diagnostics;
using LocalMeetingSubtitle.Export;
using LocalMeetingSubtitle.Media;
using LocalMeetingSubtitle.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace LocalMeetingSubtitle.App;

/// <summary>
/// Composition root. Builds the DI container, enforces single-instance, wires global exception
/// handling and owns the shell (windows + tray icon). All transcript work lives in the
/// view-models; this class only assembles and supervises them.
/// </summary>
public partial class App : Application
{
    private const string MutexName = "Local\\字幕君.SingleInstance.v1";

    private SingleInstanceGuard? _singleInstance;
    private ServiceProvider? _services;
    private FileLogger? _logger;
    private ShellService? _shell;
    private TrayIconController? _tray;
    private MainViewModel? _mainViewModel;
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var migratedEntries = 0;
        try
        {
            // Move data left behind by the pre-rename folder (%LOCALAPPDATA%\LocalMeetingSubtitle).
            migratedEntries = LocalDataPaths.MigrateLegacyFolderIfNeeded();
            LocalDataPaths.EnsureAllDirectories();
        }
        catch
        {
            // Never fail startup over directory creation; components fall back to temp paths.
        }

        _logger = new FileLogger(LocalDataPaths.EnsureLogsDirectory());
        if (migratedEntries > 0)
        {
            _logger.Info($"Migrated {migratedEntries} item(s) from a legacy data folder to '{LocalDataPaths.Root}'.");
        }

        RegisterExceptionHandlers();

        _singleInstance = new SingleInstanceGuard(MutexName);
        if (!_singleInstance.IsPrimary)
        {
            _logger.Warn("A second instance attempted to start; exiting.");
            MessageBox.Show(
                "字幕君 已在运行。\n字幕君 is already running.",
                "字幕君", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _logger.Info($"=== 字幕君 starting (v{GetType().Assembly.GetName().Version}) ===");

        try
        {
            _services = BuildServices(_logger);
        }
        catch (Exception ex)
        {
            _logger.Error("Building the service container failed", ex);
            MessageBox.Show("初始化失败 / Initialization failed:\n" + ex.Message,
                "字幕君", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        // Apply the persisted theme before the first window is shown so there is no light flash.
        ApplyStartupTheme();

        _logger.Info("Services built; creating shell.");
        _shell = new ShellService(_logger, CreateSettingsViewModel, CreateSpeakerManagementViewModel, RequestExit,
            (title, message) => _tray?.ShowBalloon(title, message));

        _logger.Info("Shell created; creating view-model.");
        _mainViewModel = new MainViewModel(
            _shell,
            _logger,
            _services.GetRequiredService<ISubtitleRepository>(),
            _services.GetRequiredService<IHotwordService>(),
            _services.GetRequiredService<ISettingsRepository>(),
            _services.GetRequiredService<IModelManager>(),
            _services.GetRequiredService<ISubtitleExportService>(),
            _services.GetRequiredService<IPerformanceMonitor>(),
            () => _services.GetRequiredService<WasapiLoopbackCaptureService>(),
            () => _services.GetRequiredService<Func<IAsrEngine>>()(),
            _services.GetRequiredService<ISpeakerRepository>(),
            _services.GetRequiredService<ISpeakerDiarizationService>(),
            _services.GetRequiredService<IRecordingService>(),
            _services.GetRequiredService<IAudioAssetRepository>(),
            _services.GetRequiredService<LocalAudioAssetStore>());
        _shell.Attach(_mainViewModel);

        _logger.Info("View-model created; creating tray icon.");
        _tray = new TrayIconController(_mainViewModel, _shell, _logger);
        _logger.Info("Tray icon created.");

        // Show the window immediately (in its "Preparing…" state) so the UI is never blank.
        try
        {
            _shell.ShowMainWindow();
            MainWindow = _shell.MainWindow;
            _logger.Info("Main window shown.");
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to create/show the main window", ex);
            MessageBox.Show("无法创建主窗口 / Failed to create the main window:\n" + ex.Message,
                "字幕君", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        _ = StartInitializationAsync();
    }

    private async Task StartInitializationAsync()
    {
        try
        {
            // Bring the schema up to date before any repository read/write.
            await _services!.GetRequiredService<SqliteDatabase>().InitializeAsync().ConfigureAwait(true);

            // A job left running by a previous session is marked interrupted so it is visible and
            // resumable instead of silently stuck.
            var recovered = await _services.GetRequiredService<ITranscriptionJobService>()
                .RecoverUnfinishedAsync().ConfigureAwait(true);
            if (recovered > 0)
            {
                _logger?.Warn($"{recovered} file transcription job(s) were interrupted by a previous session.");
            }

            await _mainViewModel!.InitializeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger?.Error("Startup initialization failed", ex);
            MessageBox.Show("初始化失败 / Initialization failed:\n" + ex.Message,
                "字幕君", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Reads the persisted <see cref="Core.Models.AppSettings.Theme"/> (bringing the schema up first)
    /// and applies it. Falls back to Light on any failure.
    /// </summary>
    private void ApplyStartupTheme()
    {
        try
        {
            // The settings row lives in SQLite; ensure the schema exists before reading it.
            _services!.GetRequiredService<SqliteDatabase>().InitializeAsync().GetAwaiter().GetResult();
            var settings = _services!.GetRequiredService<ISettingsRepository>().LoadAsync().GetAwaiter().GetResult();
            ThemeManager.Apply(settings.Theme);
        }
        catch (Exception ex)
        {
            _logger?.Warn($"Applying the persisted theme failed; using Light. {ex.Message}");
            ThemeManager.Apply(ThemeManager.Light);
        }
    }

    private SettingsViewModel CreateSettingsViewModel()
    {
        var services = _services!;
        return new SettingsViewModel(
            services.GetRequiredService<ISettingsRepository>(),
            services.GetRequiredService<IHotwordRepository>(),
            _mainViewModel!,
            _logger!);
    }

    private SpeakerManagementViewModel CreateSpeakerManagementViewModel()
    {
        var services = _services!;
        return new SpeakerManagementViewModel(
            services.GetRequiredService<ISpeakerRepository>(),
            services.GetRequiredService<ISubtitleRepository>(),
            _mainViewModel!,
            _logger!);
    }

    /// <summary>
    /// Resolves the directory that holds ASR models. A portable ZIP ships its models in
    /// <c>&lt;app&gt;\models</c>, so that location wins when present; otherwise the per-user
    /// <c>%LOCALAPPDATA%</c> models directory is used (and is where downloads are written).
    /// </summary>
    private static string ResolveModelsRoot(FileLogger logger)
    {
        var portable = Path.Combine(AppContext.BaseDirectory, "models");
        if (Directory.Exists(portable) && Directory.EnumerateDirectories(portable).Any())
        {
            logger.Info($"Using portable models directory: {portable}");
            return portable;
        }

        var user = LocalDataPaths.EnsureModelsDirectory();
        logger.Info($"Using user models directory: {user}");
        return user;
    }

    private static ServiceProvider BuildServices(FileLogger logger)
    {
        var services = new ServiceCollection();

        services.AddSingleton<IAppLogger>(logger);
        services.AddSingleton(new SqliteDatabase(LocalDataPaths.DatabaseFile));
        services.AddSingleton<ISubtitleRepository>(sp => new SqliteSubtitleRepository(sp.GetRequiredService<SqliteDatabase>()));
        services.AddSingleton<IHotwordRepository>(sp => new SqliteHotwordRepository(sp.GetRequiredService<SqliteDatabase>()));
        services.AddSingleton<ISettingsRepository>(sp => new SqliteSettingsRepository(sp.GetRequiredService<SqliteDatabase>()));

        services.AddSingleton<IHotwordService>(sp => new DefaultHotwordService(
            sp.GetRequiredService<IHotwordRepository>(), engine: null, sp.GetRequiredService<IAppLogger>()));

        services.AddSingleton<WasapiLoopbackCaptureService>(sp => new WasapiLoopbackCaptureService(sp.GetRequiredService<IAppLogger>()));
        services.AddSingleton<IAudioCaptureService>(sp => sp.GetRequiredService<WasapiLoopbackCaptureService>());
        services.AddSingleton<IPerformanceMonitor>(_ => new ProcessPerformanceMonitor());
        services.AddSingleton<ISubtitleExportService>(_ => new SubtitleExportService());

        // V0.5 media decode: local audio/video probing + PCM extraction through the bundled FFmpeg.
        // Resolved lazily so a deployment without FFmpeg still starts (probing/decoding then fails
        // with MediaErrorKind.ToolMissing).
        services.AddSingleton<IMediaDecodeService>(sp =>
        {
            var log = sp.GetRequiredService<IAppLogger>();
            var tools = FFmpegLocator.Resolve();
            log.Info($"Media decode toolchain ready: ffmpeg='{tools.FfmpegPath}', ffprobe='{tools.FfprobePath}'.");
            return new FFmpegMediaDecodeService(tools, log);
        });

        // The model manager is used offline (installed-model checks); recognition itself never
        // touches the network.
        services.AddSingleton<IModelManager>(_ => new HttpModelManager(ResolveModelsRoot(logger)));

        // Lazy factory: each call constructs a fresh recognition engine.
        services.AddSingleton<Func<IAsrEngine>>(sp => () => new SherpaOnnxAsrEngine(sp.GetRequiredService<IAppLogger>()));

        // ---- Offline speaker diarization (post-meeting) -------------------
        services.AddSingleton<ISpeakerRepository>(sp => new SqliteSpeakerRepository(sp.GetRequiredService<SqliteDatabase>()));
        services.AddSingleton<IAudioAssetRepository>(sp => new SqliteAudioAssetRepository(sp.GetRequiredService<SqliteDatabase>()));
        services.AddSingleton<IAudioFileLoader>(_ => new NaudioAudioFileLoader());
        services.AddSingleton<ISpeakerAlignmentService>(_ => new SpeakerAlignmentService());

        // Opt-in post-meeting recording (default: off) and the on-disk asset store that cleans it up.
        services.AddSingleton<IRecordingService>(sp => new WaveRecordingService(sp.GetRequiredService<IAppLogger>()));
        services.AddSingleton(sp => new LocalAudioAssetStore(LocalDataPaths.RecordingsDirectory, sp.GetRequiredService<IAppLogger>()));

        // A second, keyed model manager so diarization descriptors never enter the ASR catalog.
        services.AddKeyedSingleton<IModelManager>("diarization",
            (_, _) => new HttpModelManager(ResolveModelsRoot(logger), DiarizationModelCatalog.All));

        services.AddSingleton(sp =>
        {
            var keyed = sp.GetRequiredKeyedService<IModelManager>("diarization");
            var segmentation = DiarizationModelCatalog.Segmentation;
            var embedding = DiarizationModelCatalog.Embedding;
            return new DiarizationEngineOptions
            {
                SegmentationModelPath = Path.Combine(keyed.GetModelDirectory(segmentation), segmentation.Files[0].RelativePath),
                EmbeddingModelPath = Path.Combine(keyed.GetModelDirectory(embedding), embedding.Files[0].RelativePath),
                Provider = "cpu",
                ComputeConfidence = true
            };
        });

        services.AddSingleton<ISpeakerDiarizationService>(sp => new SpeakerDiarizationService(
            () => new SherpaOfflineSpeakerDiarizer(sp.GetRequiredService<IAppLogger>()),
            sp.GetRequiredService<IAudioFileLoader>(),
            sp.GetRequiredService<ISpeakerAlignmentService>(),
            sp.GetRequiredService<ISpeakerRepository>(),
            sp.GetRequiredService<ISubtitleRepository>(),
            sp.GetRequiredService<DiarizationEngineOptions>(),
            sp.GetRequiredService<IAudioAssetRepository>(),
            sp.GetRequiredService<IAppLogger>()));

        // ---- V0.5 file transcription (Phase 3: role-tagged dialogue) -------
        // The transcript is persisted into the existing sessions/segments tables and diarized by the
        // same service the live path uses; this seam only turns the result into dialogue turns.
        services.AddSingleton<ITranscriptAlignmentService>(_ => new TranscriptAlignmentService());
        services.AddSingleton<IFileTranscriptionService>(sp => new FileTranscriptionService(
            sp.GetRequiredService<IMediaDecodeService>(),
            sp.GetRequiredService<ISpeakerDiarizationService>(),
            sp.GetRequiredService<ISubtitleRepository>(),
            sp.GetRequiredService<ISpeakerRepository>(),
            sp.GetRequiredService<ITranscriptAlignmentService>(),
            // The job's temporary WAV lives under the recordings folder so the existing orphan
            // sweep cleans it up if the process dies mid-job.
            LocalDataPaths.RecordingsDirectory,
            sp.GetRequiredService<IAppLogger>()));

        // ---- V0.5 file transcription (Phase 4: job queue + checkpoint/resume) ----
        services.AddSingleton<IMediaFileRepository>(sp => new SqliteMediaFileRepository(sp.GetRequiredService<SqliteDatabase>()));
        services.AddSingleton<ITranscriptionJobRepository>(sp => new SqliteTranscriptionJobRepository(sp.GetRequiredService<SqliteDatabase>()));
        services.AddSingleton<ITranscriptionJobService>(sp => new TranscriptionJobService(
            sp.GetRequiredService<IFileTranscriptionService>(),
            sp.GetRequiredService<ITranscriptionJobRepository>(),
            sp.GetRequiredService<IMediaFileRepository>(),
            sp.GetRequiredService<ISubtitleRepository>(),
            (job, cancellationToken) => ResolveJobRequestAsync(sp, job, cancellationToken),
            sp.GetRequiredService<IAppLogger>()));

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Rebuilds the runnable request for a queued or resumed job: resolves the mode, creates a fresh
    /// recognizer for this attempt (the job service disposes it) and points the run at the job's
    /// existing session so it continues from the last committed segment.
    /// </summary>
    private static async Task<FileTranscriptionRequest> ResolveJobRequestAsync(
        IServiceProvider services,
        TranscriptionJob job,
        CancellationToken cancellationToken)
    {
        var mediaFiles = services.GetRequiredService<IMediaFileRepository>();
        var media = await mediaFiles.GetAsync(job.MediaFileId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Media file {job.MediaFileId} for job {job.JobId} is missing.");

        string? hotwordsFile = null;
        try
        {
            hotwordsFile = services.GetRequiredService<IHotwordService>()
                .WriteModelHotwordFile(ModelHotwordFile.DefaultTempPath);
        }
        catch (Exception ex)
        {
            services.GetRequiredService<IAppLogger>().Warn($"No hotwords file for job {job.JobId}: {ex.Message}");
        }

        var resolved = TranscriptionModeCatalog.Resolve(job.Mode, services.GetRequiredService<IModelManager>(), hotwordsFile);
        if (!resolved.IsAvailable)
        {
            throw new InvalidOperationException(resolved.UnavailableReason ?? "The model for this job is not installed.");
        }

        var engine = services.GetRequiredService<Func<IAsrEngine>>()();
        var init = await engine.InitializeAsync(resolved.EngineOptions, cancellationToken).ConfigureAwait(false);
        if (!init.Ok)
        {
            engine.Dispose();
            throw new InvalidOperationException(init.Message);
        }

        return new FileTranscriptionRequest(
            media.Path,
            engine,
            resolved.TranscriptionOptions,
            job.AudioStreamIndex,
            job.RunDiarization,
            job.DiarizationCountMode,
            job.ManualSpeakerCount,
            job.ClusteringThreshold,
            Title: job.Title,
            SessionId: job.SessionId);
    }

    private void RegisterExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _logger?.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.Error("Unhandled UI-thread exception", e.Exception);
        try
        {
            MessageBox.Show("发生未处理错误 / Unhandled error:\n" + e.Exception.Message,
                "字幕君", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            // Ignore failures while reporting.
        }
        e.Handled = true; // Keep the app alive rather than dying silently.
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        _logger?.Error("Unhandled application exception", ex);
        try
        {
            MessageBox.Show("发生严重错误 / Fatal error:\n" + (ex?.Message ?? e.ExceptionObject?.ToString()),
                "字幕君", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            // Ignore failures while reporting.
        }
    }

    /// <summary>Graceful exit: stop transcription, then shut down on the UI thread.</summary>
    private void RequestExit()
    {
        if (_exiting) return;
        _exiting = true;

        _ = ExitAsync();
    }

    private async Task ExitAsync()
    {
        try
        {
            if (_mainViewModel is not null)
            {
                await _mainViewModel.StopIfRunningAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            _logger?.Error("Error while stopping before exit", ex);
        }

        try
        {
            Dispatcher.Invoke(() =>
            {
                // The main window cancels its Closing event (normal close = hide-to-tray), so we
                // must explicitly allow it to close during an intentional shutdown.
                if (MainWindow is LocalMeetingSubtitle.App.MainWindow main) main.AllowClose();
                Shutdown();
            });
        }
        catch
        {
            // If the dispatcher is already gone, nothing left to do.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Info("字幕君 exiting.");

        try { _tray?.Dispose(); } catch { /* ignore */ }
        try { _services?.Dispose(); } catch { /* ignore */ }
        try { _singleInstance?.Dispose(); } catch { /* ignore */ }

        base.OnExit(e);
    }
}
