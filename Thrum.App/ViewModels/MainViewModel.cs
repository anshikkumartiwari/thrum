using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Thrum.App.Services;
using Thrum.Core.Actions;
using Thrum.Core.Capture;
using Thrum.Core.Classification;
using Thrum.Core.Dsp;
using Thrum.Core.Logging;
using Thrum.Core.Models;
using Thrum.Core.Storage;

namespace Thrum.App.ViewModels;

public sealed class MainViewModel : BaseViewModel, IDisposable
{
    private readonly ILogger _logger;
    private readonly ProfileStore _profileStore;
    private readonly AudioPipeline _audioPipeline;
    private readonly ActionExecutor _actionExecutor;
    private readonly Win32ActionRunner _actionRunner;

    private Profile _activeProfile;
    private ZoneViewModel? _selectedZone;
    private int _selectedTabIndex; // 0 = Zones/Live, 1 = Settings

    private bool _isListening;
    private float _audioLevel;
    private float _noiseFloor;
    private string _statusMessage = "Ready. Select a zone to calibrate or turn on Live Mode.";
    private string _lastDetectedZoneName = string.Empty;
    private float _lastDetectedConfidence;

    // Recording State
    private bool _isRecording;
    private string _recordingPrompt = string.Empty;
    private string _recordingProgress = string.Empty;

    // Training State
    private TrainingResult? _latestTrainingResult;
    private bool _showTrainingResultDialog;

    // Settings
    private ObservableCollection<AudioDeviceInfo> _devices = new();
    private AudioDeviceInfo? _selectedDevice;
    private bool _startWithWindows;
    private bool _micAccessDenied;
    private bool _showPhysicsCaveat = true;
    private bool _showGuidedFlow = false;

    // Color Swatches
    public static readonly string[] PaletteColors = {
        "#3B82F6", // Blue
        "#10B981", // Emerald
        "#8B5CF6", // Purple
        "#F59E0B", // Amber
        "#EF4444", // Red
        "#06B6D4", // Cyan
        "#EC4899", // Pink
        "#64748B"  // Slate / Ignore
    };

    public ObservableCollection<Profile> ProfilesList { get; } = new();
    public ObservableCollection<ZoneViewModel> Zones { get; } = new();

    public Profile ActiveProfile
    {
        get => _activeProfile;
        set
        {
            if (SetProperty(ref _activeProfile, value))
            {
                OnProfileChanged();
            }
        }
    }

    public ZoneViewModel? SelectedZone
    {
        get => _selectedZone;
        set
        {
            if (_selectedZone != null) _selectedZone.IsSelected = false;
            if (SetProperty(ref _selectedZone, value))
            {
                if (_selectedZone != null) _selectedZone.IsSelected = true;
                OnPropertyChanged(nameof(HasSelectedZone));
            }
        }
    }

    public bool HasSelectedZone => SelectedZone != null;

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    public bool IsListening
    {
        get => _isListening;
        set
        {
            if (SetProperty(ref _isListening, value))
            {
                if (value) StartListening();
                else StopListening();
            }
        }
    }

    public float AudioLevel
    {
        get => _audioLevel;
        private set => SetProperty(ref _audioLevel, value);
    }

    public float NoiseFloor
    {
        get => _noiseFloor;
        private set => SetProperty(ref _noiseFloor, value);
    }

    public float Sensitivity
    {
        get => _audioPipeline.Sensitivity;
        set
        {
            _audioPipeline.Sensitivity = value;
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string LastDetectedZoneName
    {
        get => _lastDetectedZoneName;
        set => SetProperty(ref _lastDetectedZoneName, value);
    }

    public float LastDetectedConfidence
    {
        get => _lastDetectedConfidence;
        set => SetProperty(ref _lastDetectedConfidence, value);
    }

    public bool IsRecording
    {
        get => _isRecording;
        set => SetProperty(ref _isRecording, value);
    }

    public string RecordingPrompt
    {
        get => _recordingPrompt;
        set => SetProperty(ref _recordingPrompt, value);
    }

    public string RecordingProgress
    {
        get => _recordingProgress;
        set => SetProperty(ref _recordingProgress, value);
    }

    public TrainingResult? LatestTrainingResult
    {
        get => _latestTrainingResult;
        set => SetProperty(ref _latestTrainingResult, value);
    }

    public bool ShowTrainingResultDialog
    {
        get => _showTrainingResultDialog;
        set => SetProperty(ref _showTrainingResultDialog, value);
    }

    public ObservableCollection<AudioDeviceInfo> Devices => _devices;

    public AudioDeviceInfo? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                if (_isListening)
                {
                    _audioPipeline.StartCapture(value?.Id);
                }
            }
        }
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (SetProperty(ref _startWithWindows, value))
            {
                SetStartupRegistry(value);
            }
        }
    }

    public bool MicAccessDenied
    {
        get => _micAccessDenied;
        set => SetProperty(ref _micAccessDenied, value);
    }

    public bool ShowPhysicsCaveat
    {
        get => _showPhysicsCaveat;
        set => SetProperty(ref _showPhysicsCaveat, value);
    }

    public bool ShowGuidedFlow
    {
        get => _showGuidedFlow;
        set => SetProperty(ref _showGuidedFlow, value);
    }

    public bool CanTrain => Zones.Count(z => z.SampleCount >= 10) >= 2;

    // Commands
    public RelayCommand AddZoneCommand { get; }
    public RelayCommand DeleteZoneCommand { get; }
    public RelayCommand ClearZoneTapsCommand { get; }
    public RelayCommand RecordZoneCommand { get; }
    public RelayCommand StopRecordingCommand { get; }
    public RelayCommand TrainModelCommand { get; }
    public RelayCommand ToggleListeningCommand { get; }
    public RelayCommand OpenMicSettingsCommand { get; }
    public RelayCommand OpenSoundControlPanelCommand { get; }
    public RelayCommand DismissCaveatCommand { get; }
    public RelayCommand DismissGuidedFlowCommand { get; }
    public RelayCommand CreateProfileCommand { get; }
    public RelayCommand DeleteProfileCommand { get; }

    public MainViewModel()
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new FileLoggerProvider());
        });
        _logger = loggerFactory.CreateLogger("Thrum.App");
        _logger.LogInformation("Thrum starting up. Initializing audio and profile subsystems.");

        _profileStore = new ProfileStore();
        _activeProfile = _profileStore.LoadActiveProfile();

        _actionRunner = new Win32ActionRunner();
        _actionExecutor = new ActionExecutor(_actionRunner);
        _audioPipeline = new AudioPipeline();

        // Wire Audio Pipeline Events
        _audioPipeline.AudioLevelUpdated += (level, floor) =>
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                AudioLevel = Math.Clamp(level * 2.5f, 0f, 1f);
                NoiseFloor = Math.Clamp(floor * 2.5f, 0f, 1f);
            });
        };

        _audioPipeline.TapRejected += (reason, message) =>
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                StatusMessage = $"Tap rejected: {message}";
            });
        };

        _audioPipeline.TapAccepted += (feats, result) =>
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                if (IsRecording && SelectedZone != null)
                {
                    SelectedZone.Flash();
                    StatusMessage = $"Accepted tap! Peak: {result.PeakAmplitude:0.00}, Crest: {result.CrestFactor:0.1}";
                }
            });
        };

        _audioPipeline.SampleRecorded += (zoneId, current, target) =>
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                var zoneVm = Zones.FirstOrDefault(z => z.Id == zoneId);
                if (zoneVm != null)
                {
                    zoneVm.SampleCount = current;
                    OnPropertyChanged(nameof(CanTrain));
                }

                RecordingProgress = $"Taps recorded: {current} / {target}";

                if (current >= target)
                {
                    StopRecording();
                    StatusMessage = $"Recording complete for '{zoneVm?.Name}' ({target} taps).";
                    _profileStore.SaveActiveProfile(_activeProfile);
                }
            });
        };

        _audioPipeline.TapClassified += OnTapClassified;

        // Initialize Commands
        AddZoneCommand = new RelayCommand(AddZone);
        DeleteZoneCommand = new RelayCommand(DeleteSelectedZone, () => HasSelectedZone);
        ClearZoneTapsCommand = new RelayCommand(ClearSelectedZoneTaps, () => HasSelectedZone);
        RecordZoneCommand = new RelayCommand(StartRecordingSelectedZone, () => HasSelectedZone && !IsRecording);
        StopRecordingCommand = new RelayCommand(StopRecording, () => IsRecording);
        TrainModelCommand = new RelayCommand(TrainModel, () => CanTrain);
        ToggleListeningCommand = new RelayCommand(() => IsListening = !IsListening);
        OpenMicSettingsCommand = new RelayCommand(OpenMicSettings);
        OpenSoundControlPanelCommand = new RelayCommand(OpenSoundControlPanel);
        DismissCaveatCommand = new RelayCommand(() => ShowPhysicsCaveat = false);
        DismissGuidedFlowCommand = new RelayCommand(() => ShowGuidedFlow = false);
        CreateProfileCommand = new RelayCommand(CreateProfile);
        DeleteProfileCommand = new RelayCommand(DeleteProfile);

        // Load Profiles & Initial State
        RefreshProfilesList();
        RefreshInputDevices();
        CheckStartupRegistry();

        // Restore model if trained
        var restoredClassifier = _activeProfile.RestoreClassifier();
        if (restoredClassifier != null)
        {
            _audioPipeline.Classifier = restoredClassifier;
            StatusMessage = $"Loaded trained model ({_activeProfile.TrainingAccuracy:0.0}% accuracy).";
        }

        // Show guided flow on first run if no training data
        if (_activeProfile.TrainingSamples.Count == 0 && _activeProfile.LastTrainedAt == null)
        {
            ShowGuidedFlow = true;
        }

        // Auto-start capture for live monitoring
        StartListening();
    }

    private void RefreshProfilesList()
    {
        ProfilesList.Clear();
        foreach (var p in _profileStore.LoadAllProfiles())
        {
            ProfilesList.Add(p);
        }
    }

    private void OnProfileChanged()
    {
        Zones.Clear();
        foreach (var z in _activeProfile.Zones)
        {
            z.SampleCount = _activeProfile.TrainingSamples.Count(s => s.ZoneId == z.Id);
            Zones.Add(new ZoneViewModel(z));
        }

        SelectedZone = Zones.FirstOrDefault();
        OnPropertyChanged(nameof(CanTrain));

        var restored = _activeProfile.RestoreClassifier();
        if (restored != null)
        {
            _audioPipeline.Classifier = restored;
            StatusMessage = $"Profile '{_activeProfile.Name}' loaded with trained model.";
        }
        else
        {
            _audioPipeline.Classifier = new TapClassifier();
            StatusMessage = $"Profile '{_activeProfile.Name}' active (untrained).";
        }
    }

    private void RefreshInputDevices()
    {
        Devices.Clear();
        try
        {
            var deviceList = _audioPipeline.GetInputDevices();
            foreach (var dev in deviceList)
            {
                Devices.Add(dev);
            }
            SelectedDevice = Devices.FirstOrDefault(d => d.IsDefault) ?? Devices.FirstOrDefault();
            MicAccessDenied = false;
        }
        catch (Exception)
        {
            MicAccessDenied = true;
            StatusMessage = "Microphone access is unavailable or denied in Windows Settings.";
        }
    }

    public void StartListening()
    {
        try
        {
            _audioPipeline.StartCapture(SelectedDevice?.Id);
            _audioPipeline.StartLiveMode();
            StatusMessage = "Listening for chassis taps...";
            MicAccessDenied = false;
        }
        catch (Exception ex)
        {
            _isListening = false;
            OnPropertyChanged(nameof(IsListening));
            MicAccessDenied = true;
            StatusMessage = $"Microphone error: {ex.Message}";
        }
    }

    public void StopListening()
    {
        _audioPipeline.StopCapture();
        _audioPipeline.SetModeIdle();
        AudioLevel = 0f;
        StatusMessage = "Listening paused.";
    }

    private void AddZone()
    {
        int nextIndex = Zones.Count + 1;
        string color = PaletteColors[(nextIndex - 1) % PaletteColors.Length];

        // Position staggered across laptop outline
        double x = 0.2 + ((nextIndex % 4) * 0.2);
        double y = 0.5 + ((nextIndex % 3) * 0.15);

        var newZone = new Zone
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = $"Area {nextIndex}",
            Color = color,
            X = Math.Clamp(x, 0.15, 0.85),
            Y = Math.Clamp(y, 0.35, 0.85),
            Action = new ZoneAction
            {
                Type = ActionType.SingleKey,
                Key = "Space",
                CooldownMs = 500
            }
        };

        _activeProfile.Zones.Add(newZone);
        _profileStore.SaveActiveProfile(_activeProfile);

        var vm = new ZoneViewModel(newZone);
        Zones.Add(vm);
        SelectedZone = vm;
        StatusMessage = $"Added new zone '{vm.Name}'. Drag marker to position.";
    }

    private void DeleteSelectedZone()
    {
        if (SelectedZone == null) return;

        var toRemove = SelectedZone;
        _activeProfile.Zones.Remove(toRemove.Model);
        _activeProfile.TrainingSamples.RemoveAll(s => s.ZoneId == toRemove.Id);
        _profileStore.SaveActiveProfile(_activeProfile);

        Zones.Remove(toRemove);
        SelectedZone = Zones.FirstOrDefault();
        OnPropertyChanged(nameof(CanTrain));
        StatusMessage = $"Deleted zone '{toRemove.Name}'.";
    }

    private void ClearSelectedZoneTaps()
    {
        if (SelectedZone == null) return;

        _activeProfile.TrainingSamples.RemoveAll(s => s.ZoneId == SelectedZone.Id);
        SelectedZone.SampleCount = 0;
        _profileStore.SaveActiveProfile(_activeProfile);
        OnPropertyChanged(nameof(CanTrain));
        StatusMessage = $"Cleared recorded taps for '{SelectedZone.Name}'.";
    }

    private void StartRecordingSelectedZone()
    {
        if (SelectedZone == null) return;

        IsRecording = true;
        SelectedZone.IsRecording = true;
        RecordingPrompt = $"Tap '{SelectedZone.Name}' ~15 times.";
        RecordingProgress = $"Taps recorded: {SelectedZone.SampleCount} / 15";

        // Listen for new taps
        Action<float[], ImpulseGateResult>? tapHandler = null;
        tapHandler = (feats, _) =>
        {
            _activeProfile.TrainingSamples.Add(new TapFeatureSample(SelectedZone.Id, feats));
        };

        _audioPipeline.TapAccepted += tapHandler;

        // When recording finishes or cancels, detach handler
        void CleanUpRecording()
        {
            _audioPipeline.TapAccepted -= tapHandler;
            if (SelectedZone != null) SelectedZone.IsRecording = false;
            IsRecording = false;
        }

        _audioPipeline.SampleRecorded += (zid, cur, tgt) =>
        {
            if (cur >= tgt)
            {
                CleanUpRecording();
            }
        };

        _audioPipeline.StartCapture(SelectedDevice?.Id);
        _audioPipeline.StartRecording(SelectedZone.Id, 15);
        StatusMessage = $"Recording mode active. {RecordingPrompt}";
    }

    public void StopRecording()
    {
        IsRecording = false;
        if (SelectedZone != null) SelectedZone.IsRecording = false;
        _audioPipeline.StartLiveMode();
        StatusMessage = "Recording cancelled.";
    }

    private void TrainModel()
    {
        if (!CanTrain)
        {
            StatusMessage = "Need ≥ 2 zones with ≥ 10 recorded taps each.";
            return;
        }

        StatusMessage = "Training acoustic model...";
        var activeZones = Zones.Select(z => z.Model).ToList();
        var samples = _activeProfile.TrainingSamples;

        var classifier = new TapClassifier();
        var result = classifier.Train(activeZones, samples);

        LatestTrainingResult = result;
        ShowTrainingResultDialog = true;

        if (result.Success)
        {
            _audioPipeline.Classifier = classifier;
            _activeProfile.SaveClassifier(classifier, result.AccuracyPercent);
            _profileStore.SaveActiveProfile(_activeProfile);

            _logger.LogInformation("Model trained successfully across {ZoneCount} zones with {Accuracy:0.0}% accuracy. Median latency: {Latency:0.00}ms.",
                activeZones.Count, result.AccuracyPercent, result.MedianLatencyMs);

            StatusMessage = $"Model trained! Accuracy: {result.AccuracyPercent:0.0}% across {activeZones.Count} zones.";
        }
        else
        {
            _logger.LogWarning("Model training failed: {Message}", result.Message);
            StatusMessage = $"Training failed: {result.Message}";
        }
    }

    private void OnTapClassified(ClassificationResult result)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (result.IsAccepted && result.MatchedZoneId != null)
            {
                var matchedVm = Zones.FirstOrDefault(z => z.Id == result.MatchedZoneId);
                if (matchedVm != null)
                {
                    matchedVm.Flash(result.Confidence);
                    LastDetectedZoneName = matchedVm.Name;
                    LastDetectedConfidence = result.Confidence;

                    if (!result.IsIgnoreZone)
                    {
                        bool executed = _actionExecutor.TryExecute(matchedVm.Model);
                        _logger.LogInformation("Tap detected on zone '{Zone}' (confidence: {Confidence:0.00}, distance: {Dist:0.1}). Action executed: {Executed}.",
                            matchedVm.Name, result.Confidence, result.MahalanobisDistance, executed);

                        StatusMessage = executed
                            ? $"Triggered {matchedVm.Name} (Conf: {result.Confidence * 100:0.0}%) -> {matchedVm.ActionSummary}"
                            : $"Cooldown blocked {matchedVm.Name}";
                    }
                    else
                    {
                        _logger.LogInformation("Environmental noise classified into ignore zone '{Zone}'. No action taken.", matchedVm.Name);
                        StatusMessage = $"Ignored environmental noise ({matchedVm.Name}).";
                    }
                }
            }
            else
            {
                _logger.LogDebug("Impulse rejected: {Reason}", result.RejectionReason);
                StatusMessage = $"Tap rejected: {result.RejectionReason}";
            }
        });
    }

    private void CreateProfile()
    {
        string name = $"Profile {ProfilesList.Count + 1}";
        var newProfile = ProfileStore.CreateDefaultProfile(name);
        _profileStore.SaveProfile(newProfile);
        RefreshProfilesList();
        ActiveProfile = newProfile;
        StatusMessage = $"Created and switched to '{name}'.";
    }

    private void DeleteProfile()
    {
        if (ProfilesList.Count <= 1)
        {
            StatusMessage = "Cannot delete the only profile.";
            return;
        }

        string toDeleteId = ActiveProfile.Id;
        _profileStore.DeleteProfile(toDeleteId);
        RefreshProfilesList();
        ActiveProfile = ProfilesList.First();
        StatusMessage = "Profile deleted.";
    }

    private static void OpenMicSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:privacy-microphone") { UseShellExecute = true });
        }
        catch { }
    }

    private static void OpenSoundControlPanel()
    {
        try
        {
            Process.Start(new ProcessStartInfo("control.exe", "mmsys.cpl,,recording") { UseShellExecute = true });
        }
        catch { }
    }

    private void CheckStartupRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
            _startWithWindows = key?.GetValue("Thrum") != null;
            OnPropertyChanged(nameof(StartWithWindows));
        }
        catch { }
    }

    private static void SetStartupRegistry(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;

            string exePath = Environment.ProcessPath ?? string.Empty;
            if (enable && !string.IsNullOrEmpty(exePath))
            {
                key.SetValue("Thrum", $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue("Thrum", false);
            }
        }
        catch { }
    }

    public void Dispose()
    {
        _audioPipeline.Dispose();
    }
}
