using System.Diagnostics;

namespace HpBacklightProbe;

public enum LightingEffectMode
{
    Static,
    Breathing,
    Pulse,
    TypingPulse,
    SlowFade,
    NotificationFlash
}

/// <summary>
/// Background animation engine managing dynamic lighting effects for HP keyboard backlights.
/// </summary>
public sealed class LightingEffectsEngine : IDisposable
{
    private readonly HpBacklightController _controller;
    private readonly GlobalKeyboardHook _keyboardHook;

    private CancellationTokenSource? _cts;
    private Task? _effectTask;
    private readonly object _lock = new();

    // Effect Parameters
    public LightingEffectMode CurrentMode { get; private set; } = LightingEffectMode.Static;
    public double SpeedSeconds { get; set; } = 2.5; // Cycle duration in seconds (0.5s to 6.0s)
    public int MinBrightnessPercent { get; set; } = 5;
    public int MaxBrightnessPercent { get; set; } = 100;
    public int StaticBrightnessPercent { get; set; } = 50;
    public int FlashCount { get; set; } = 3;
    public int FlashSpeedMs { get; set; } = 140;

    // Typing pulse tracking
    private long _lastKeyPressTimestamp;
    private readonly double _typingDecayMs = 450.0;

    // Slow fade tracking
    private int _fadeCurrentPercent = 50;
    private int _fadeTargetPercent = 100;
    private double _fadeDurationSeconds = 3.0;

    // Callbacks for UI updates (dispatched from engine tick)
    public event Action<int, LightingEffectMode>? BrightnessUpdated;
    public event Action<string>? LogMessage;
    public event Action? EffectCompleted;

    public LightingEffectsEngine(HpBacklightController controller)
    {
        _controller = controller;
        _keyboardHook = new GlobalKeyboardHook();
        _keyboardHook.KeyActivityDetected += OnKeyActivity;
    }

    private void OnKeyActivity()
    {
        Interlocked.Exchange(ref _lastKeyPressTimestamp, Stopwatch.GetTimestamp());
    }

    /// <summary>
    /// Switches to a specific lighting effect mode.
    /// </summary>
    public void SetMode(LightingEffectMode mode)
    {
        lock (_lock)
        {
            StopActiveEffect();
            CurrentMode = mode;

            if (mode == LightingEffectMode.TypingPulse)
            {
                _keyboardHook.Start();
            }
            else
            {
                _keyboardHook.Stop();
            }

            if (mode == LightingEffectMode.Static)
            {
                _controller.SetBrightnessPercent(StaticBrightnessPercent);
                BrightnessUpdated?.Invoke(StaticBrightnessPercent, mode);
                LogMessage?.Invoke($"[Effect] Switched to Static mode ({StaticBrightnessPercent}%).");
                return;
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            LogMessage?.Invoke($"[Effect] Started {mode} effect (Speed: {SpeedSeconds:F1}s, Range: {MinBrightnessPercent}%-{MaxBrightnessPercent}%).");
            _effectTask = Task.Run(() => RunEffectLoopAsync(mode, token), token);
        }
    }

    /// <summary>
    /// Manually triggers a Notification Flash sequence without interrupting previous static state.
    /// </summary>
    public void TriggerFlash(int count, int speedMs = 140)
    {
        lock (_lock)
        {
            StopActiveEffect();
            CurrentMode = LightingEffectMode.NotificationFlash;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            FlashCount = count;
            FlashSpeedMs = speedMs;

            LogMessage?.Invoke($"[Effect] Triggered Notification Flash ({count} flashes at {speedMs}ms interval).");
            _effectTask = Task.Run(() => RunFlashSequenceAsync(count, speedMs, token), token);
        }
    }

    /// <summary>
    /// Initiates a smooth slow fade to a specific target brightness percentage.
    /// </summary>
    public void StartSlowFade(int targetPercent, double durationSeconds = 2.5)
    {
        lock (_lock)
        {
            StopActiveEffect();
            CurrentMode = LightingEffectMode.SlowFade;
            _fadeCurrentPercent = _controller.GetBrightnessPercent();
            _fadeTargetPercent = Math.Clamp(targetPercent, 0, 100);
            _fadeDurationSeconds = Math.Max(0.5, durationSeconds);

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            LogMessage?.Invoke($"[Effect] Slow Fade initiated: {_fadeCurrentPercent}% -> {_fadeTargetPercent}% over {_fadeDurationSeconds:F1}s.");
            _effectTask = Task.Run(() => RunSlowFadeLoopAsync(token), token);
        }
    }

    private async Task RunEffectLoopAsync(LightingEffectMode mode, CancellationToken token)
    {
        const int tickIntervalMs = 60; // ~16 FPS: smooth for human eye and safe for WMI/SMI bus
        var stopwatch = Stopwatch.StartNew();
        int lastSentBrightness = -1;

        try
        {
            while (!token.IsCancellationRequested)
            {
                double elapsedSec = stopwatch.Elapsed.TotalSeconds;
                int brightness = 0;

                switch (mode)
                {
                    case LightingEffectMode.Breathing:
                        // Sine wave oscillation between MinBrightness and MaxBrightness
                        double period = Math.Max(0.5, SpeedSeconds);
                        double sine = Math.Sin(2.0 * Math.PI * (elapsedSec / period));
                        double normalizedSine = (sine + 1.0) / 2.0; // 0.0 to 1.0
                        brightness = (int)Math.Round(MinBrightnessPercent + normalizedSine * (MaxBrightnessPercent - MinBrightnessPercent));
                        break;

                    case LightingEffectMode.Pulse:
                        // Heartbeat / pulse wave: sharp spike up followed by exponential decay
                        double pulsePeriod = Math.Max(0.4, SpeedSeconds);
                        double phase = (elapsedSec % pulsePeriod) / pulsePeriod; // 0.0 to 1.0
                        double pulseIntensity;
                        if (phase < 0.25)
                        {
                            // Sharp linear rise (0 -> 1) in first 25% of cycle
                            pulseIntensity = phase / 0.25;
                        }
                        else
                        {
                            // Exponential decay in remaining 75%
                            double decayPhase = (phase - 0.25) / 0.75;
                            pulseIntensity = Math.Exp(-3.5 * decayPhase);
                        }
                        brightness = (int)Math.Round(MinBrightnessPercent + pulseIntensity * (MaxBrightnessPercent - MinBrightnessPercent));
                        break;

                    case LightingEffectMode.TypingPulse:
                        // Base brightness + spike on keypress with decay
                        long lastKey = Interlocked.Read(ref _lastKeyPressTimestamp);
                        double msSinceKey = lastKey > 0
                            ? (Stopwatch.GetTimestamp() - lastKey) * 1000.0 / Stopwatch.Frequency
                            : 999999.0;

                        if (msSinceKey <= _typingDecayMs)
                        {
                            double decay = 1.0 - (msSinceKey / _typingDecayMs);
                            brightness = (int)Math.Round(MinBrightnessPercent + decay * (MaxBrightnessPercent - MinBrightnessPercent));
                        }
                        else
                        {
                            brightness = MinBrightnessPercent;
                        }
                        break;
                }

                brightness = Math.Clamp(brightness, 0, 100);

                if (brightness != lastSentBrightness)
                {
                    _controller.SetBrightnessPercent(brightness);
                    lastSentBrightness = brightness;
                    BrightnessUpdated?.Invoke(brightness, mode);
                }

                await Task.Delay(tickIntervalMs, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on effect stop
        }
    }

    private async Task RunSlowFadeLoopAsync(CancellationToken token)
    {
        const int tickIntervalMs = 50;
        var sw = Stopwatch.StartNew();
        int startVal = _fadeCurrentPercent;
        int endVal = _fadeTargetPercent;
        double duration = _fadeDurationSeconds;

        try
        {
            while (!token.IsCancellationRequested)
            {
                double elapsed = sw.Elapsed.TotalSeconds;
                double progress = Math.Clamp(elapsed / duration, 0.0, 1.0);

                // Smooth cubic ease-in-out curve
                double ease = progress < 0.5
                    ? 4.0 * progress * progress * progress
                    : 1.0 - Math.Pow(-2.0 * progress + 2.0, 3.0) / 2.0;

                int current = (int)Math.Round(startVal + (endVal - startVal) * ease);
                current = Math.Clamp(current, 0, 100);

                _controller.SetBrightnessPercent(current);
                BrightnessUpdated?.Invoke(current, LightingEffectMode.SlowFade);

                if (progress >= 1.0)
                {
                    LogMessage?.Invoke($"[Effect] Slow Fade completed at {endVal}%.");
                    EffectCompleted?.Invoke();
                    break;
                }

                await Task.Delay(tickIntervalMs, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on cancel
        }
    }

    private async Task RunFlashSequenceAsync(int count, int speedMs, CancellationToken token)
    {
        try
        {
            for (int i = 0; i < count; i++)
            {
                token.ThrowIfCancellationRequested();

                // Flash ON (100%)
                _controller.SetBrightnessPercent(100);
                BrightnessUpdated?.Invoke(100, LightingEffectMode.NotificationFlash);
                await Task.Delay(speedMs, token).ConfigureAwait(false);

                token.ThrowIfCancellationRequested();

                // Flash OFF (0%)
                _controller.SetBrightnessPercent(0);
                BrightnessUpdated?.Invoke(0, LightingEffectMode.NotificationFlash);
                await Task.Delay(speedMs, token).ConfigureAwait(false);
            }

            LogMessage?.Invoke("[Effect] Notification Flash sequence complete.");
            EffectCompleted?.Invoke();
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
    }

    public void StopActiveEffect()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }

        _keyboardHook.Stop();
    }

    public void Dispose()
    {
        StopActiveEffect();
        _keyboardHook.Dispose();
    }
}
