using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace HpBacklightProbe;

public partial class MainWindow : Window
{
    private HpBacklightController? _controller;
    private LightingEffectsEngine? _effectsEngine;
    private bool _isUpdatingUi = false;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        bool isAdmin = HpBacklightController.IsAdministrator();
        var glassGreenBorder = Color.FromArgb(0x66, 0x34, 0xD3, 0x99);
        var glassRoseBorder = Color.FromArgb(0x66, 0xFB, 0x71, 0x85);

        if (isAdmin)
        {
            BadgeAdmin.BorderBrush = new SolidColorBrush(glassGreenBorder);
            TxtAdminStatus.Text = "🛡️ Admin: Active";
            TxtAdminStatus.Foreground = (Brush)FindResource("SuccessGreen");
            BannerAdminWarning.Visibility = Visibility.Collapsed;
        }
        else
        {
            BadgeAdmin.BorderBrush = new SolidColorBrush(glassRoseBorder);
            TxtAdminStatus.Text = "⚠️ Non-Admin (Read Only)";
            TxtAdminStatus.Foreground = (Brush)FindResource("ErrorRed");
            BannerAdminWarning.Visibility = Visibility.Visible;
        }

        InitializeController();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _effectsEngine?.Dispose();
        _controller?.Dispose();
    }

    private void InitializeController()
    {
        try
        {
            _controller = HpBacklightController.Create();
            _effectsEngine = new LightingEffectsEngine(_controller);
            _effectsEngine.BrightnessUpdated += EffectsEngine_BrightnessUpdated;
            _effectsEngine.LogMessage += Log;
            _effectsEngine.EffectCompleted += EffectsEngine_EffectCompleted;

            Log("Connected to HP BIOS WMI (root\\wmi -> hpqBIntM).");
            UpdateEffectButtonStyles("Static");
            RefreshState();
        }
        catch (Exception ex)
        {
            Log($"[Error] WMI connection failed: {ex.Message}");
            TxtHardwareSummary.Text = "Could not connect to HP WMI interface.";
            TxtFooterStatus.Text = "WMI interface offline. Please ensure you are running as Administrator.";
            DisableControls();
        }
    }

    private void RefreshState()
    {
        if (_controller is null) return;

        try
        {
            var info = _controller.GetInfo();
            if (!string.IsNullOrWhiteSpace(info.Model) && info.Model != "(unknown)")
            {
                TxtTitleHeader.Text = $"{info.Model} Backlight";
                Title = $"{info.Model} - Backlight Controller";
            }
            else
            {
                TxtTitleHeader.Text = "HP Keyboard Backlight Controller";
                Title = "HP Keyboard Backlight Controller";
            }

            TxtHardwareSummary.Text = $"Device: {info.Model} | BIOS: {info.BiosVersion} | Board: {info.BoardProduct}";
            TxtKbdType.Text = info.KeyboardTypeDescription;
            TxtLightingSupport.Text = info.IsLightingSupported ? $"Supported (0x{info.CapabilitiesFlags:X2})" : "Not Supported";
            TxtRawState.Text = $"0x{info.CurrentStateMode:X2}";
            TxtRawBrightness.Text = $"0x{info.CurrentBrightness:X2} ({info.CurrentBrightness})";

            UpdatePowerAndBrightnessUi(info.CurrentBrightness);
            TxtFooterStatus.Text = $"Updated at {DateTime.Now:HH:mm:ss} - Status: {(info.IsOn ? "ON" : "OFF")} ({info.BrightnessPercent}%)";
        }
        catch (Exception ex)
        {
            Log($"[Error] Refresh failed: {ex.Message}");
        }
    }

    private void UpdatePowerAndBrightnessUi(byte brightness)
    {
        _isUpdatingUi = true;
        try
        {
            int percent = (int)Math.Round((brightness / 255.0) * 100.0);
            SliderBrightness.Value = percent;
            TxtSliderPercent.Text = $"{percent}%";
            TxtPreviewPercent.Text = percent > 0 ? $"{percent}% BRIGHTNESS" : "BACKLIGHT OFF";
            
            // Visualizer glowing effect
            GlowRect.Opacity = Math.Max(0.05, percent / 100.0);
            if (percent > 0)
            {
                TxtPowerStateSubtitle.Text = $"Backlight is ON ({percent}%)";
                BtnMasterPower.Content = "TURN OFF";
                BtnMasterPower.Background = (Brush)FindResource("RoseGradient");
                BtnMasterPower.Effect = (Effect)FindResource("GlowRose");
            }
            else
            {
                TxtPowerStateSubtitle.Text = "Backlight is OFF";
                BtnMasterPower.Content = "TURN ON";
                BtnMasterPower.Background = (Brush)FindResource("AccentGradient");
                BtnMasterPower.Effect = (Effect)FindResource("GlowViolet");
            }
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    private void ApplyBrightness(int percent)
    {
        if (_controller is null) return;

        try
        {
            if (_effectsEngine != null && _effectsEngine.CurrentMode != LightingEffectMode.Static)
            {
                _effectsEngine.SetMode(LightingEffectMode.Static);
                UpdateEffectButtonStyles("Static");
                TxtActiveEffectBadge.Text = "Mode: Static";
            }

            bool success = _controller.SetBrightnessPercent(percent);
            byte raw = (byte)Math.Round((percent / 100.0) * 255.0);
            if (success)
            {
                Log($"[Success] Set brightness to {percent}% (raw 0x{raw:X2}).");
                RefreshState();
            }
            else
            {
                Log($"[Warning] Set brightness to {percent}% returned non-success from firmware.");
            }
        }
        catch (Exception ex)
        {
            Log($"[Error] Failed to set brightness: {ex.Message}");
        }
    }

    private void SliderBrightness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingUi) return;
        int percent = (int)Math.Round(SliderBrightness.Value);
        TxtSliderPercent.Text = $"{percent}%";
        TxtPreviewPercent.Text = percent > 0 ? $"{percent}% BRIGHTNESS" : "BACKLIGHT OFF";
        GlowRect.Opacity = Math.Max(0.05, percent / 100.0);
        ApplyBrightness(percent);
    }

    private void BtnPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.Tag is string tagStr && int.TryParse(tagStr, out int targetPercent))
        {
            SliderBrightness.Value = targetPercent;
            ApplyBrightness(targetPercent);
        }
    }

    private void BtnMasterPower_Click(object sender, RoutedEventArgs e)
    {
        if (_controller is null) return;

        try
        {
            if (_effectsEngine != null && _effectsEngine.CurrentMode != LightingEffectMode.Static)
            {
                _effectsEngine.SetMode(LightingEffectMode.Static);
                UpdateEffectButtonStyles("Static");
                TxtActiveEffectBadge.Text = "Mode: Static";
            }

            bool currentlyOn = _controller.IsBacklightOn();
            bool success;
            if (currentlyOn)
            {
                success = _controller.TurnOff();
                Log(success
                    ? "[Success] Turned backlight OFF."
                    : "[Warning] Turn OFF returned non-success from firmware.");
            }
            else
            {
                success = _controller.TurnOn();
                Log(success
                    ? "[Success] Turned backlight ON."
                    : "[Warning] Turn ON returned non-success from firmware.");
            }
            RefreshState();
        }
        catch (Exception ex)
        {
            Log($"[Error] Power toggle failed: {ex.Message}");
            Log("Make sure you are running this app as Administrator - HP's BIOS WMI writes require it.");
        }
    }

    #region Lighting Effects Handlers

    private void BtnEffectMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && _effectsEngine != null)
        {
            if (Enum.TryParse<LightingEffectMode>(tag, out var mode))
            {
                UpdateEffectButtonStyles(tag);
                TxtActiveEffectBadge.Text = $"Mode: {tag}";

                // Adapt parameter visibility based on effect
                PanelFlashActions.Visibility = mode == LightingEffectMode.NotificationFlash ? Visibility.Visible : Visibility.Collapsed;
                PanelSlowFadeActions.Visibility = mode == LightingEffectMode.SlowFade ? Visibility.Visible : Visibility.Collapsed;
                TxtTypingPulseHint.Visibility = mode == LightingEffectMode.TypingPulse ? Visibility.Visible : Visibility.Collapsed;

                bool isSpeedRelevant = mode is LightingEffectMode.Breathing or LightingEffectMode.Pulse or LightingEffectMode.SlowFade;
                PanelSpeedControl.Visibility = isSpeedRelevant ? Visibility.Visible : Visibility.Collapsed;

                bool isRangeRelevant = mode is LightingEffectMode.Breathing or LightingEffectMode.Pulse or LightingEffectMode.TypingPulse;
                PanelIntensityRange.Visibility = isRangeRelevant ? Visibility.Visible : Visibility.Collapsed;

                if (mode == LightingEffectMode.Static)
                {
                    _effectsEngine.StaticBrightnessPercent = (int)SliderBrightness.Value;
                }

                _effectsEngine.SetMode(mode);
            }
        }
    }

    private void SliderSpeed_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_effectsEngine != null && SliderSpeed != null && TxtSpeedVal != null)
        {
            _effectsEngine.SpeedSeconds = SliderSpeed.Value;
            TxtSpeedVal.Text = $"{SliderSpeed.Value:F1}s cycle";
        }
    }

    private void SliderMinBrightness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_effectsEngine != null && SliderMinBrightness != null && TxtMinVal != null)
        {
            int val = (int)Math.Round(SliderMinBrightness.Value);
            _effectsEngine.MinBrightnessPercent = val;
            TxtMinVal.Text = $"{val}%";
        }
    }

    private void SliderMaxBrightness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_effectsEngine != null && SliderMaxBrightness != null && TxtMaxVal != null)
        {
            int val = (int)Math.Round(SliderMaxBrightness.Value);
            _effectsEngine.MaxBrightnessPercent = val;
            TxtMaxVal.Text = $"{val}%";
        }
    }

    private void BtnFlashCount_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int count) && _effectsEngine != null)
        {
            _effectsEngine.FlashCount = count;
            Log($"[Effect] Set flash count to {count}x.");
        }
    }

    private void BtnTriggerFlash_Click(object sender, RoutedEventArgs e)
    {
        if (_effectsEngine != null)
        {
            UpdateEffectButtonStyles("NotificationFlash");
            TxtActiveEffectBadge.Text = "Mode: Flash";
            _effectsEngine.TriggerFlash(_effectsEngine.FlashCount);
        }
    }

    private void BtnSlowFadeTarget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int target) && _effectsEngine != null)
        {
            UpdateEffectButtonStyles("SlowFade");
            TxtActiveEffectBadge.Text = $"Mode: SlowFade ({target}%)";
            _effectsEngine.StartSlowFade(target, _effectsEngine.SpeedSeconds);
        }
    }

    private void EffectsEngine_BrightnessUpdated(int brightness, LightingEffectMode mode)
    {
        Dispatcher.InvokeAsync(() =>
        {
            _isUpdatingUi = true;
            try
            {
                TxtPreviewPercent.Text = brightness > 0 ? $"{brightness}% BRIGHTNESS" : "BACKLIGHT OFF";
                GlowRect.Opacity = Math.Max(0.05, brightness / 100.0);
                TxtFooterStatus.Text = $"Effect [{mode}]: {brightness}% ({DateTime.Now:HH:mm:ss})";
            }
            finally
            {
                _isUpdatingUi = false;
            }
        });
    }

    private void EffectsEngine_EffectCompleted()
    {
        Dispatcher.InvokeAsync(() =>
        {
            RefreshState();
        });
    }

    private void UpdateEffectButtonStyles(string activeTag)
    {
        var buttons = new[] { BtnEffectStatic, BtnEffectBreathing, BtnEffectPulse, BtnEffectTyping, BtnEffectSlowFade, BtnEffectFlash };
        var goldBrush = (Brush)FindResource("AccentCyan");
        var activeFill = (Brush)FindResource("GlassFillStrong");
        var idleFill = (Brush)FindResource("GlassFill");
        var subtleBorder = (Brush)FindResource("GlassBorderSubtle");

        foreach (var btn in buttons)
        {
            if (btn == null) continue;
            bool isActive = (btn.Tag as string) == activeTag;
            btn.BorderBrush = isActive ? goldBrush : subtleBorder;
            btn.Background = isActive ? activeFill : idleFill;
            btn.Foreground = isActive ? (Brush)FindResource("TextPrimary") : (Brush)FindResource("TextSecondary");
        }
    }

    #endregion

    private void BtnToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_controller is null) return;

        try
        {
            if (_effectsEngine != null && _effectsEngine.CurrentMode != LightingEffectMode.Static)
            {
                _effectsEngine.SetMode(LightingEffectMode.Static);
                UpdateEffectButtonStyles("Static");
                TxtActiveEffectBadge.Text = "Mode: Static";
            }

            bool success = _controller.Toggle();
            Log(success
                ? "[Success] Toggled backlight."
                : "[Warning] Toggle returned non-success from firmware.");
            RefreshState();
        }
        catch (Exception ex)
        {
            Log($"[Error] Toggle failed: {ex.Message}");
            Log("Make sure you are running this app as Administrator - HP's BIOS WMI writes require it.");
        }
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        Log("[Action] Refreshing firmware state...");
        RefreshState();
    }

    private void BtnRunProbe_Click(object sender, RoutedEventArgs e)
    {
        if (_controller is null) return;

        Log("=== Running Firmware Diagnostic Probe ===");
        try
        {
            RunAndLogQuery("Get keyboard type", 0x20008, 0x2B);
            RunAndLogQuery("Is lighting supported", 0x20009, 0x01);
            RunAndLogQuery("Get color/state table", 0x20009, 0x02);
            RunAndLogQuery("Get brightness", 0x20009, 0x04);
            Log("=== Probe Complete ===");
        }
        catch (Exception ex)
        {
            Log($"[Error] Probe execution failed: {ex.Message}");
        }
    }

    private void RunAndLogQuery(string label, uint command, uint commandType)
    {
        if (_controller is null) return;
        var res = _controller.WmiClient.Query(command, commandType);
        string hex = res.Data.Length > 0 ? string.Join(" ", res.Data.Take(16).Select(b => b.ToString("X2"))) : "(empty)";
        Log($"[{label}] (0x{command:X}/0x{commandType:X}) -> ReturnCode: 0x{res.ReturnCode:X2} ({HpWmiReturnCodes.Describe(res.ReturnCode)}) | Data: {hex}");
    }

    private void BtnCopyStatus_Click(object sender, RoutedEventArgs e)
    {
        if (_controller is null) return;
        var info = _controller.GetInfo();
        string headerModel = !string.IsNullOrWhiteSpace(info.Model) && info.Model != "(unknown)" ? info.Model : "HP Device";
        string text = $"{headerModel} Backlight Info:\n" +
                      $"Model: {info.Model}\n" +
                      $"BIOS: {info.BiosVersion}\n" +
                      $"Keyboard Type: {info.KeyboardTypeDescription} (0x{info.RawKeyboardType:X2})\n" +
                      $"Lighting Supported: {info.IsLightingSupported} (Flags: 0x{info.CapabilitiesFlags:X2})\n" +
                      $"Current State: 0x{info.CurrentStateMode:X2}\n" +
                      $"Current Brightness: {info.BrightnessPercent}% (0x{info.CurrentBrightness:X2})\n" +
                      $"Power State: {(info.IsOn ? "ON" : "OFF")}";
        Clipboard.SetText(text);
        TxtFooterStatus.Text = "Status copied to clipboard!";
        Log("[Action] Copied hardware status report to clipboard.");
    }

    private void BtnClearLog_Click(object sender, RoutedEventArgs e)
    {
        TxtLog.Clear();
    }

    private void DisableControls()
    {
        BtnMasterPower.IsEnabled = false;
        SliderBrightness.IsEnabled = false;
    }

    private void Log(string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        TxtLog.AppendText($"[{timestamp}] {message}\n");
        TxtLog.ScrollToEnd();
    }
}

