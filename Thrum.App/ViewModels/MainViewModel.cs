using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
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

    // Guided Countdown Recording State
    private bool _isRecording;
    private int _currentTapNumber = 1;
    private int _targetTapCount = 15;
    private int _countdownNumber = 3;
    private string _countdownText = "3";
    private string _countdownColor = "#DC2626";
    private string _countdownStateLabel = "GET READY";
    private string _recordingPrompt = string.Empty;
    private string _recordingProgress = string.Empty;
    private string _diagnosticText = string.Empty;
    private bool _isCalibrationPaused = false;
    private int _calibrationStep = 3;
    private int _tapWindowRemainingTicks = 0;
    private readonly System.Windows.Threading.DispatcherTimer _calibrationTimer;

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

    // Clean High-Contrast Palette
    public static readonly string[] PaletteColors = {
        "#2563EB", // Royal Blue
        "#16A34A", // Emerald Green
        "#D97706", // Amber
        "#DC2626", // Crimson
        "#9333EA", // Purple
        "#0D9488", // Teal
        "#EA580C", // Orange
        "#4F46E5"  // Indigo
    };

    public ObservableCollection<Profile> ProfilesList { get; } = new();
    public ObservableCollection<ZoneViewModel> Zones { get; } = new();

    public string MicStatusText => (_isListening && !_micAccessDenied && _selectedDevice != null)
        ? $"MIC ACTIVE: {_selectedDevice.Name}"
        : (_micAccessDenied ? "MIC ACCESS DENIED" : "MIC PAUSED");

    public bool IsMicActive => _isListening && !_micAccessDenied;

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

    public int CurrentTapNumber
    {
        get => _currentTapNumber;
        set => SetProperty(ref _currentTapNumber, value);
    }

    public int TargetTapCount
    {
        get => _targetTapCount;
        set => SetProperty(ref _targetTapCount, value);
    }

    public int CountdownNumber
    {
        get => _countdownNumber;
        set => SetProperty(ref _countdownNumber, value);
    }

    public string CountdownText
    {
        get => _countdownText;
        set => SetProperty(ref _countdownText, value);
    }

    public string CountdownColor
    {
        get => _countdownColor;
        set => SetProperty(ref _countdownColor, value);
    }

    public string CountdownStateLabel
    {
        get => _countdownStateLabel;
        set => SetProperty(ref _countdownStateLabel, value);
    }

    public string DiagnosticText
    {
        get => _diagnosticText;
        set => SetProperty(ref _diagnosticText, value);
    }

    public bool IsCalibrationPaused
    {
        get => _isCalibrationPaused;
        set => SetProperty(ref _isCalibrationPaused, value);
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

    public bool CanTrain => Zones.Count(z => z.SampleCount >= 10) >= 1;

    public string TrainButtonToolTip
    {
        get
        {
            int ready = Zones.Count(z => z.SampleCount >= 10);
            if (ready == 0) return "Record ≥ 10 taps on at least 1 zone to train";
            if (ready == 1) return "Train single-zone acoustic template (add more zones to discriminate between spots)";
            return $"Train acoustic classifier across {ready} zones";
        }
    }

    // Commands
    public RelayCommand AddZoneCommand { get; }
    public RelayCommand DeleteZoneCommand { get; }
    public RelayCommand ClearZoneTapsCommand { get; }
    public RelayCommand RecordZoneCommand { get; }
    public RelayCommand StopRecordingCommand { get; }
    public RelayCommand RedoLastTapCommand { get; }
    public RelayCommand TogglePauseCalibrationCommand { get; }
    public RelayCommand OpenRecordingsFolderCommand { get; }
    public RelayCommand ExportTapDataCommand { get; }
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

        _calibrationTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _calibrationTimer.Tick += OnCalibrationTimerTick;

        // Wire Audio Pipeline Events
        _audioPipeline.AudioLevelUpdated += (level, floor) =>
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                AudioLevel = Math.Clamp(level * 2.5f, 0f, 1f);
                NoiseFloor = Math.Clamp(floor * 2.5f, 0f, 1f);
            });
        };

        _audioPipeline.TapRejected += (reason, message, result) =>
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                StatusMessage = $"Tap rejected: {message}";
                if (IsRecording && SelectedZone != null)
                {
                    CountdownColor = "#DC2626";
                    CountdownText = "REJECTED";
                    CountdownStateLabel = reason.ToString().ToUpperInvariant();
                    RecordingPrompt = $"✕ {message}. Retrying Tap {CurrentTapNumber}...";
                    DiagnosticText = $"Peak: {result.PeakAmplitude:0.003} | Crest: {result.CrestFactor:0.1} | Dur: {result.EffectiveDurationMs:0}ms";

                    TapDataExporter.AppendRejectedSample(_activeProfile.Name, SelectedZone.Name, result);
                    _audioPipeline.IsAwaitingRecordingTap = false;
                    _calibrationStep = 4; // Restart countdown 3, 2, 1
                }
            });
        };

        _audioPipeline.TapAccepted += (feats, result) =>
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                if (IsRecording && SelectedZone != null)
                {
                    var sample = new TapFeatureSample(
                        SelectedZone.Id,
                        SelectedZone.Name,
                        CurrentTapNumber,
                        feats,
                        result.PeakAmplitude,
                        result.Rms,
                        result.CrestFactor,
                        result.LateEarlyEnergyRatio,
                        result.EffectiveDurationMs,
                        result.SnrRatio);

                    _activeProfile.TrainingSamples.Add(sample);
                    TapDataExporter.AppendAcceptedSample(_activeProfile.Name, sample);

                    SelectedZone.SampleCount = CurrentTapNumber;
                    SelectedZone.Flash();

                    CountdownColor = "#16A34A";
                    CountdownText = "RECORDED!";
                    CountdownStateLabel = $"TAP {CurrentTapNumber} SAVED";
                    RecordingPrompt = $"✓ Tap {CurrentTapNumber} captured! Peak: {result.PeakAmplitude:0.003}, Crest: {result.CrestFactor:0.1}";
                    DiagnosticText = $"Peak: {result.PeakAmplitude:0.003} | Crest: {result.CrestFactor:0.1} | Dur: {result.EffectiveDurationMs:0}ms | SNR: {result.SnrRatio:0.1}x";
                    StatusMessage = $"Recorded tap {CurrentTapNumber} of {TargetTapCount} for '{SelectedZone.Name}'.";

                    if (CurrentTapNumber >= TargetTapCount)
                    {
                        _calibrationTimer.Stop();
                        _audioPipeline.IsAwaitingRecordingTap = false;
                        _audioPipeline.StartLiveMode();
                        IsRecording = false;
                        SelectedZone.IsRecording = false;
                        _profileStore.SaveActiveProfile(_activeProfile);
                        TapDataExporter.ExportProfileTapsToCsv(_activeProfile, TapDataExporter.GetDefaultCsvPath(_activeProfile.Name));
                        StatusMessage = $"Calibration complete for '{SelectedZone.Name}' (15 taps recorded). Ready to Train!";
                        OnPropertyChanged(nameof(CanTrain));
                        OnPropertyChanged(nameof(TrainButtonToolTip));
                        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                    }
                    else
                    {
                        CurrentTapNumber++;
                        RecordingProgress = $"Tap {CurrentTapNumber} of {TargetTapCount}";
                        OnPropertyChanged(nameof(CanTrain));
                        OnPropertyChanged(nameof(TrainButtonToolTip));
                        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                        _calibrationStep = 4;
                    }
                }
                else if (!IsRecording && SelectedZone != null)
                {
                    SelectedZone.Flash();
                    StatusMessage = $"Accepted tap! Peak: {result.PeakAmplitude:0.003}, Crest: {result.CrestFactor:0.1}";
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
        RedoLastTapCommand = new RelayCommand(RedoLastTap, () => IsRecording && CurrentTapNumber > 1);
        TogglePauseCalibrationCommand = new RelayCommand(TogglePauseCalibration, () => IsRecording);
        OpenRecordingsFolderCommand = new RelayCommand(OpenRecordingsFolder);
        ExportTapDataCommand = new RelayCommand(ExportTapData);
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
        finally
        {
            OnPropertyChanged(nameof(MicStatusText));
            OnPropertyChanged(nameof(IsMicActive));
        }
    }

    public void StopListening()
    {
        _audioPipeline.StopCapture();
        _audioPipeline.SetModeIdle();
        AudioLevel = 0f;
        StatusMessage = "Listening paused.";
        OnPropertyChanged(nameof(MicStatusText));
        OnPropertyChanged(nameof(IsMicActive));
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

        // Clear previous taps for this zone for a fresh clean calibration
        _activeProfile.TrainingSamples.RemoveAll(s => s.ZoneId == SelectedZone.Id);
        SelectedZone.SampleCount = 0;

        CurrentTapNumber = 1;
        TargetTapCount = 15;
        IsRecording = true;
        SelectedZone.IsRecording = true;
        IsCalibrationPaused = false;

        RecordingProgress = $"Tap 1 of {TargetTapCount}";
        CountdownNumber = 3;
        CountdownText = "3";
        CountdownColor = "#DC2626";
        CountdownStateLabel = "GET READY...";
        RecordingPrompt = $"Get ready to tap '{SelectedZone.Name}'...";
        DiagnosticText = "Waiting for countdown to reach green...";

        _audioPipeline.IsAwaitingRecordingTap = false;
        _calibrationStep = 3;

        if (!_audioPipeline.IsCapturing)
        {
            _audioPipeline.StartCapture(SelectedDevice?.Id);
        }
        _audioPipeline.StartRecording(SelectedZone.Id, TargetTapCount);

        _calibrationTimer.Start();
        StatusMessage = $"Calibration started for '{SelectedZone.Name}'. Tap on GREEN!";
    }

    private void OnCalibrationTimerTick(object? sender, EventArgs e)
    {
        if (!_isRecording || _isCalibrationPaused || SelectedZone == null) return;

        if (_calibrationStep == 4) // Inter-tap transition delay
        {
            _calibrationStep = 3;
            CountdownNumber = 3;
            CountdownText = "3";
            CountdownColor = "#DC2626"; // Bright Red
            CountdownStateLabel = "GET READY...";
            RecordingPrompt = $"Get ready to tap '{SelectedZone.Name}'...";
            _audioPipeline.IsAwaitingRecordingTap = false;
            return;
        }

        if (_calibrationStep == 3)
        {
            _calibrationStep = 2;
            CountdownNumber = 2;
            CountdownText = "2";
            CountdownColor = "#DC2626"; // Bright Red
            CountdownStateLabel = "GET READY...";
            RecordingPrompt = $"Get ready to tap '{SelectedZone.Name}'...";
            _audioPipeline.IsAwaitingRecordingTap = false;
            return;
        }

        if (_calibrationStep == 2)
        {
            _calibrationStep = 1;
            CountdownNumber = 1;
            CountdownText = "1";
            CountdownColor = "#EA580C"; // Deep Amber/Orange
            CountdownStateLabel = "READY...";
            RecordingPrompt = $"Strike '{SelectedZone.Name}' when it turns green!";
            _audioPipeline.IsAwaitingRecordingTap = false;
            return;
        }

        if (_calibrationStep == 1)
        {
            // Turn GREEN!
            _calibrationStep = 0;
            CountdownNumber = 0;
            CountdownText = "TAP NOW!";
            CountdownColor = "#16A34A"; // Vibrant Green
            CountdownStateLabel = "● STRIKE FIRMLY NOW";
            RecordingPrompt = $"TAP '{SelectedZone.Name}' SHARPLY NOW!";
            _tapWindowRemainingTicks = 3; // 3 seconds listening window
            _audioPipeline.IsAwaitingRecordingTap = true;
            return;
        }

        if (_calibrationStep == 0)
        {
            // Still waiting for tap in the green window
            _tapWindowRemainingTicks--;
            if (_tapWindowRemainingTicks <= 0)
            {
                // Timeout without tap
                _audioPipeline.IsAwaitingRecordingTap = false;
                CountdownColor = "#DC2626";
                CountdownText = "MISSED";
                CountdownStateLabel = "NO TAP DETECTED";
                RecordingPrompt = $"Tap window missed. Retrying Tap {CurrentTapNumber}...";
                _calibrationStep = 4; // Restart countdown next tick
            }
        }
    }

    public void StopRecording()
    {
        _calibrationTimer.Stop();
        IsRecording = false;
        if (SelectedZone != null) SelectedZone.IsRecording = false;
        _audioPipeline.IsAwaitingRecordingTap = false;
        _audioPipeline.StartLiveMode();
        StatusMessage = "Calibration stopped.";
    }

    private void RedoLastTap()
    {
        if (!IsRecording || SelectedZone == null || CurrentTapNumber <= 1) return;

        // Discard the last sample for this zone
        int lastIdx = _activeProfile.TrainingSamples.FindLastIndex(s => s.ZoneId == SelectedZone.Id);
        if (lastIdx >= 0)
        {
            _activeProfile.TrainingSamples.RemoveAt(lastIdx);
        }

        CurrentTapNumber--;
        SelectedZone.SampleCount = CurrentTapNumber - 1;
        RecordingProgress = $"Tap {CurrentTapNumber} of {TargetTapCount}";
        StatusMessage = $"Redoing Tap {CurrentTapNumber}...";

        _audioPipeline.IsAwaitingRecordingTap = false;
        _calibrationStep = 4;
    }

    private void TogglePauseCalibration()
    {
        if (!IsRecording) return;
        IsCalibrationPaused = !IsCalibrationPaused;
        if (IsCalibrationPaused)
        {
            _audioPipeline.IsAwaitingRecordingTap = false;
            CountdownText = "PAUSED";
            CountdownColor = "#64748B";
            CountdownStateLabel = "CALIBRATION PAUSED";
            RecordingPrompt = "Calibration paused. Click Resume to continue.";
        }
        else
        {
            _calibrationStep = 4;
            RecordingPrompt = "Resuming countdown...";
        }
    }

    private void OpenRecordingsFolder()
    {
        try
        {
            string dir = TapDataExporter.GetRecordingsDirectory();
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error opening recordings folder: {ex.Message}";
        }
    }

    private void ExportTapData()
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export Tap Recordings Dataset",
                Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                FileName = $"{_activeProfile.Name}_taps.csv"
            };

            if (dialog.ShowDialog() == true)
            {
                TapDataExporter.ExportProfileTapsToCsv(_activeProfile, dialog.FileName);
                StatusMessage = $"Exported tap dataset to '{Path.GetFileName(dialog.FileName)}'.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    private void TrainModel()
    {
        if (!CanTrain)
        {
            StatusMessage = "Need ≥ 10 recorded taps on at least 1 zone to train.";
            return;
        }

        // Include all zones that have at least 10 samples
        var trainedZones = Zones.Where(z => z.SampleCount >= 10).Select(z => z.Model).ToList();
        var samples = _activeProfile.TrainingSamples;

        StatusMessage = trainedZones.Count == 1
            ? "Calibrating single-zone acoustic template..."
            : $"Training acoustic classifier across {trainedZones.Count} zones...";

        var classifier = new TapClassifier();
        var result = classifier.Train(trainedZones, samples);

        LatestTrainingResult = result;
        ShowTrainingResultDialog = true;

        if (result.Success)
        {
            _audioPipeline.Classifier = classifier;
            _activeProfile.SaveClassifier(classifier, result.AccuracyPercent);
            _profileStore.SaveActiveProfile(_activeProfile);

            _logger.LogInformation("Model trained successfully across {ZoneCount} zones with {Accuracy:0.0}% accuracy. Median latency: {Latency:0.00}ms.",
                trainedZones.Count, result.AccuracyPercent, result.MedianLatencyMs);

            StatusMessage = $"Model trained! Accuracy: {result.AccuracyPercent:0.0}% across {trainedZones.Count} zones.";
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
        _calibrationTimer.Stop();
        _audioPipeline.Dispose();
    }
}
