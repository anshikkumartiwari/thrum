# Thrum ⚡

> **Lightweight Windows 10/11 Acoustic Tap Tray Application**  
> Tap the laptop chassis (palm rest, desk edge, keyboard bezel, anywhere) and the built-in microphone classifies *which* spot was tapped, triggering an instant mapped action.

[![.NET 8](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-0078D4.svg)](https://microsoft.com/windows)
[![Offline & Private](https://img.shields.io/badge/Privacy-100%25%20Offline%20%7C%20No%20Audio%20Stored-green.svg)](#privacy-guarantee)

---

## ⚠️ Physics Caveat (Read First)

> **Single built-in microphone = no true spatial localization.**  
> Thrum does *not* triangulate positions using time-of-flight (which requires multi-mic arrays). Instead, it learns **per-zone acoustic resonance signatures** (the structural frequency response, transfer function, and damping characteristics of your specific laptop chassis).  
>
> **Important implications:**
> - Accuracy is specific to your exact laptop chassis, desk surface, and laptop lid angle.
> - If you move from a wood desk to your lap, or change surfaces, recalibrate your profile.
> - Visual marker placement on the screen outline is a visual label and configuration handle; the machine learning model classifies structural timbre.

---

## Architecture Overview

Thrum is written in modern **C# / .NET 8** with **WPF**, zero cloud connectivity, zero Python dependencies, and zero heavy external ML frameworks.

```
┌────────────────────────────────────────────────────────────────────────┐
│                               THRUM APP                                │
│                                                                        │
│   WPF UI (Modern Dark Cards)  │  System Tray (H.NotifyIcon)            │
│   - Laptop Chassis Canvas     │  - Background listening                │
│   - Draggable Zone Markers    │  - Win32 SendInput Action Execution    │
│   - Live VU Meter & Rejection │  - Process.Start (Apps & URLs)         │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                              THRUM CORE                                │
│                                                                        │
│  [NAudio WASAPI Shared Capture] ──► Mono Downmix & 16kHz Resampler     │
│                                           │                            │
│                                           ▼                            │
│                                [Audio Ring Buffer] (Producer)          │
│                                           │                            │
│                                           ▼                            │
│                            [DSP Processing Thread] (Consumer)          │
│                                           │                            │
│  ┌────────────────────────────────────────┴─────────────────────────┐  │
│  │ 1. Adaptive Noise Floor (EMA updated only during quiet frames)   │  │
│  │ 2. Onset Detector (Low-pass filtered energy with refractory gate)│  │
│  │ 3. Impulse Gate (Duration, Crest factor, SNR, Late/Early ratio)  │  │
│  │ 4. Feature Extractor (Time-domain + 20-band FFT Log Energies)    │  │
│  │ 5. Tap Classifier (Z-Score + Softmax Ridge Regression)           │  │
│  │ 6. Outlier Detector (Mahalanobis Distance Rejection)             │  │
│  └──────────────────────────────────────────────────────────────────┘  │
│                                           │                            │
│                         No Raw Audio Disk Persistence                  │
│                     %AppData%/Thrum/profile.json (Features)            │
└────────────────────────────────────────────────────────────────────────┘
```

### Signal Processing & Machine Learning Pipeline

1. **Producer-Consumer Audio Capture:**
   - Uses NAudio WASAPI Shared Mode capture.
   - Converts arbitrary sample rates (44.1 kHz, 48 kHz, 96 kHz) and bit formats (16/24/32-bit PCM and IEEE Float) into a uniform mono 16 kHz stream.
   - Pushes into a lock-free circular `AudioRingBuffer`. The UI thread is never blocked.
2. **Adaptive Noise Floor:**
   - Tracks ambient environmental noise floor using an Exponential Moving Average (EMA).
   - Updates *only* during quiet frames, ensuring loud taps or continuous speech never artificially inflate the baseline noise floor.
3. **Onset & Impulse Gate:**
   - Onset triggers when low-pass-filtered envelope energy exceeds `noiseFloor × threshold`.
   - Captures ~30 ms pre-roll and ~90 ms post-onset window (120 ms event total).
   - 140 ms refractory period eliminates duplicate triggers from chassis reverberation.
   - **Impulse Gate:** Rejects sustained environmental sounds (speech, singing, computer fans, prolonged keypresses) by evaluating effective duration (<55 ms), crest factor (>2.8), and late/early energy ratio (<0.48). Real-time rejection reasons ("too quiet", "too long") are displayed instantly in the UI.
4. **Feature Extraction (27 Dimensions):**
   - **Time-Domain (5):** Peak amplitude, RMS, crest factor, decay time (ms), early-energy ratio (first 25 ms).
   - **Spectral Log-Bands (20):** Hann-windowed 2048-point Radix-2 FFT normalized by peak amplitude, partitioned into 20 logarithmically spaced filter banks from 60 Hz to 7500 Hz.
   - **Spectral Statistics (2):** Spectral centroid and 85% energy spectral rolloff.
5. **Classifier & Rejection Rule:**
   - Z-score normalized against training sample statistics.
   - Ridge-regularized multinomial logistic regression (Softmax with $L_2$ penalty).
   - **Rejection Guarantee:** If `max_probability < threshold` OR `Mahalanobis distance > threshold`, the event is classified as an outlier and **NO action is taken**.

---

## 🔒 Privacy Guarantee

- **Zero raw audio is ever written to disk.**
- Audio samples exist only in volatile RAM within the circular ring buffer for the duration of the 120 ms onset window.
- Profile persistence (`%AppData%/Thrum/profile.json`) stores only mathematical feature vectors, model weights, and zone configuration.
- Application logs (`%AppData%/Thrum/logs/`) record only structured lifecycle events (tap detection status, confidence score, action executed).

---

## ⚙️ Calibration Steps (Guided Flow)

1. **Add Zones:**
   - Click **"+ Add Zone"** on the laptop diagram.
   - Drag the marker to the physical spot you intend to tap (e.g., Left Palm Rest, Right Palm Rest, Left Bezel).
2. **Record Tap Calibration:**
   - Select the zone marker in the list.
   - Click **"Record ~15 Taps"**.
   - Tap the chosen spot with your fingertip ~15 times.
   - The VU meter and tap counter update in real time. Accepted taps flash the marker; unclear taps are rejected with real-time feedback.
3. **Repeat for Other Zones:**
   - Record at least 2 distinct zones with ≥ 10 accepted taps each.
   - *(Optional)* Add an "Ignore" zone and record typing on the keyboard or clicks to teach the classifier negative examples.
4. **Train Model:**
   - Click **"Train Model"**.
   - The engine performs an automated 30% held-out test split, fits the model, and displays a training evaluation report showing accuracy %, confusion matrix, rejected outliers, and median latency.
5. **Map Actions & Enjoy:**
   - Configure the desired action for each zone:
     - Keyboard shortcut (e.g. `Ctrl+Shift+M` to mute Discord/Teams)
     - Single key (e.g. `Space` or `F5`)
     - Media Play / Pause
     - Volume Up, Volume Down, Volume Mute
     - Launch Application (`.exe` path)
     - Open Web URL (`https://...`)
   - Close the window to minimize to the Windows system tray.

---

## 🎙️ Microphone Permissions & Audio Enhancements

### Microphone Access Denied
If Thrum indicates that microphone access is denied:
1. Open Windows **Settings** ➔ **Privacy & Security** ➔ **Microphone** (or click the button in Thrum's Settings tab).
2. Turn ON **"Microphone access"** and **"Let desktop apps access your microphone"**.

### Disable Windows Audio Enhancements
Many laptops (especially Dell, Lenovo, HP, ASUS) enable proprietary audio post-processing (MaxxAudio, Realtek Audio Effects, Dolby Voice, AI Noise Cancellation).
These effects actively cancel and distort chassis vibrations:
1. Press `Win + R`, type `mmsys.cpl`, and hit Enter (or click "Open Sound Properties" in Thrum Settings).
2. Double-click your active microphone under the **Recording** tab.
3. In the **Advanced** or **Enhancements** tab:
   - **Uncheck "Enable audio enhancements"** or **Check "Disable all sound effects"**.
4. Click Apply.

---

## 🛠️ Build and Run

### Prerequisites
- Windows 10 (1903+) or Windows 11 (x64 / ARM64)
- .NET 8.0 SDK

### Building from Source

```powershell
# Clone the repository
git clone https://github.com/your-username/thrum.git
cd thrum

# Build the entire solution
dotnet build Thrum.sln

# Run unit tests
dotnet test Thrum.sln

# Launch Thrum desktop application
dotnet run --project Thrum.App/Thrum.App.csproj
```

---

## 🧪 Unit Tests

Run the test suite with:

```powershell
dotnet test Thrum.Tests/Thrum.Tests.csproj
```

The test suite exercises:
- **`AdaptiveNoiseFloor`**: Adapts during quiet frames; freezes during loud sounds.
- **`OnsetDetector`**: Accurately triggers on synthetic chassis thumps.
- **`ImpulseGate`**: Accepts crisp decaying thumps and rejects sustained tones, hums, and low-crest audio.
- **`Refractory Period`**: Suppresses rapid duplicate triggerings within 140 ms.
- **`FeatureExtractor`**: Validates 27-dimensional scale-invariant feature extraction.
- **`TapClassifier`**: Separates distinct synthetic acoustic resonances with held-out validation.
- **`OutlierDetector`**: Rejects out-of-distribution sounds via Mahalanobis distance.
- **`ActionExecutor`**: Enforces per-zone cooldown and global debounce.
