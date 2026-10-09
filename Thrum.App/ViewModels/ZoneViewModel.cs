using System.Windows.Threading;
using Thrum.Core.Models;

namespace Thrum.App.ViewModels;

public sealed class ZoneViewModel : BaseViewModel
{
    private readonly Zone _model;
    private bool _isSelected;
    private bool _isFlashing;
    private bool _isRecording;
    private float _lastConfidence;
    private readonly DispatcherTimer _flashTimer;

    public Zone Model => _model;

    public string Id => _model.Id;

    public string Name
    {
        get => _model.Name;
        set
        {
            if (_model.Name != value)
            {
                _model.Name = value;
                OnPropertyChanged();
            }
        }
    }

    public string Color
    {
        get => _model.Color;
        set
        {
            if (_model.Color != value)
            {
                _model.Color = value;
                OnPropertyChanged();
            }
        }
    }

    public double X
    {
        get => _model.X;
        set
        {
            if (Math.Abs(_model.X - value) > 0.0001)
            {
                _model.X = Math.Clamp(value, 0.05, 0.95);
                OnPropertyChanged();
            }
        }
    }

    public double Y
    {
        get => _model.Y;
        set
        {
            if (Math.Abs(_model.Y - value) > 0.0001)
            {
                _model.Y = Math.Clamp(value, 0.05, 0.95);
                OnPropertyChanged();
            }
        }
    }

    public bool IsIgnore
    {
        get => _model.IsIgnore;
        set
        {
            if (_model.IsIgnore != value)
            {
                _model.IsIgnore = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActionSummary));
            }
        }
    }

    public int SampleCount
    {
        get => _model.SampleCount;
        set
        {
            if (_model.SampleCount != value)
            {
                _model.SampleCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasEnoughSamples));
            }
        }
    }

    public bool HasEnoughSamples => SampleCount >= 10;

    public ActionType ActionType
    {
        get => _model.Action.Type;
        set
        {
            if (_model.Action.Type != value)
            {
                _model.Action.Type = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActionSummary));
            }
        }
    }

    public string ActionKey
    {
        get => _model.Action.Key;
        set
        {
            if (_model.Action.Key != value)
            {
                _model.Action.Key = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActionSummary));
            }
        }
    }

    public bool HasCtrl
    {
        get => _model.Action.HasCtrl;
        set
        {
            if (value) _model.Action.Modifiers |= 1;
            else _model.Action.Modifiers &= ~1;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActionSummary));
        }
    }

    public bool HasAlt
    {
        get => _model.Action.HasAlt;
        set
        {
            if (value) _model.Action.Modifiers |= 2;
            else _model.Action.Modifiers &= ~2;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActionSummary));
        }
    }

    public bool HasShift
    {
        get => _model.Action.HasShift;
        set
        {
            if (value) _model.Action.Modifiers |= 4;
            else _model.Action.Modifiers &= ~4;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActionSummary));
        }
    }

    public bool HasWin
    {
        get => _model.Action.HasWin;
        set
        {
            if (value) _model.Action.Modifiers |= 8;
            else _model.Action.Modifiers &= ~8;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActionSummary));
        }
    }

    public string TargetPath
    {
        get => _model.Action.TargetPath;
        set
        {
            if (_model.Action.TargetPath != value)
            {
                _model.Action.TargetPath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActionSummary));
            }
        }
    }

    public int CooldownMs
    {
        get => _model.Action.CooldownMs;
        set
        {
            if (_model.Action.CooldownMs != value)
            {
                _model.Action.CooldownMs = value;
                OnPropertyChanged();
            }
        }
    }

    public string ActionSummary => IsIgnore ? "Ignore Noise / Clicks" : _model.Action.ToString();

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsFlashing
    {
        get => _isFlashing;
        set => SetProperty(ref _isFlashing, value);
    }

    public bool IsRecording
    {
        get => _isRecording;
        set => SetProperty(ref _isRecording, value);
    }

    public float LastConfidence
    {
        get => _lastConfidence;
        set => SetProperty(ref _lastConfidence, value);
    }

    public ZoneViewModel(Zone model)
    {
        _model = model;
        _flashTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _flashTimer.Tick += (_, _) =>
        {
            _flashTimer.Stop();
            IsFlashing = false;
        };
    }

    public void Flash(float confidence = 1.0f)
    {
        LastConfidence = confidence;
        IsFlashing = true;
        _flashTimer.Stop();
        _flashTimer.Start();
    }
}
