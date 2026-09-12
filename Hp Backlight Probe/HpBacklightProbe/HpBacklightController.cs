using System.Management;
using System.Security.Principal;

namespace HpBacklightProbe;

/// <summary>
/// Keyboard lighting capabilities and current state report.
/// </summary>
public record KeyboardInfo
{
    public string Model { get; init; } = "(unknown)";
    public string BiosVersion { get; init; } = "(unknown)";
    public string BoardProduct { get; init; } = "(unknown)";
    public string KeyboardTypeDescription { get; init; } = "Single-zone / Monochrome";
    public byte RawKeyboardType { get; init; }
    public bool IsLightingSupported { get; init; }
    public byte CapabilitiesFlags { get; init; }
    public byte CurrentStateMode { get; init; }
    public byte CurrentBrightness { get; init; }
    public int BrightnessPercent => (int)Math.Round((CurrentBrightness / 255.0) * 100.0);
    public bool IsOn => CurrentBrightness > 0;
}

public record MethodTestResult(string MethodName, uint Command, uint CommandType, int ReturnCode, string StatusDescription);

/// <summary>
/// High-level controller for HP laptop keyboard backlights.
/// </summary>
public sealed class HpBacklightController : IDisposable
{
    public const uint CmdGeneral = 0x20008;
    public const uint CmdBacklight = 0x20009;

    public const uint TypeGetKeyboardType = 0x2B;
    public const uint TypeIsLightingSupported = 0x01;
    public const uint TypeGetColorStateTable = 0x02;
    public const uint TypeSetColorStateTable = 0x03;
    public const uint TypeGetBrightness = 0x04;
    public const uint TypeSetBrightness = 0x05;
    public const uint TypeGmBacklight = 0x22;

    private readonly HpWmiClient _wmiClient;
    private byte _lastNonZeroBrightness = 0xFF; // default to 100% (255)

    public HpBacklightController(HpWmiClient wmiClient)
    {
        _wmiClient = wmiClient;
    }

    /// <summary>
    /// Checks if the current Windows process is elevated with Administrator privileges.
    /// </summary>
    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Creates and connects a new HpBacklightController instance.
    /// </summary>
    public static HpBacklightController Create()
    {
        var client = HpWmiClient.Connect();
        return new HpBacklightController(client);
    }

    /// <summary>
    /// Retrieves comprehensive hardware and backlight state details.
    /// </summary>
    public KeyboardInfo GetInfo()
    {
        string model = "(unknown)";
        string bios = "(unknown)";
        string board = "(unknown)";

        try
        {
            using var csSearcher = new ManagementObjectSearcher("SELECT Model FROM Win32_ComputerSystem");
            using var biosSearcher = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion FROM Win32_BIOS");
            using var boardSearcher = new ManagementObjectSearcher("SELECT Product FROM Win32_BaseBoard");

            foreach (ManagementObject obj in csSearcher.Get())
                model = obj["Model"]?.ToString() ?? model;
            foreach (ManagementObject obj in biosSearcher.Get())
                bios = obj["SMBIOSBIOSVersion"]?.ToString() ?? bios;
            foreach (ManagementObject obj in boardSearcher.Get())
                board = obj["Product"]?.ToString() ?? board;
        }
        catch
        {
            // System info query fallback
        }

        // Query 1: Keyboard Type
        var kbdTypeResult = _wmiClient.Query(CmdGeneral, TypeGetKeyboardType);
        byte rawKbdType = kbdTypeResult.Data.Length > 0 ? kbdTypeResult.Data[0] : (byte)0;
        string kbdDesc = rawKbdType switch
        {
            0x00 => "Single-Zone White Backlight (Victus / Standard)",
            0x01 => "4-Zone RGB Backlight (OMEN)",
            0x02 => "Per-Key RGB Backlight (OMEN)",
            _ => $"Custom / Type 0x{rawKbdType:X2}"
        };

        // Query 2: Lighting Supported
        var supportedResult = _wmiClient.Query(CmdBacklight, TypeIsLightingSupported);
        bool isSupported = supportedResult.Success && supportedResult.Data.Length > 0;
        byte capFlags = supportedResult.Data.Length > 0 ? supportedResult.Data[0] : (byte)0;

        // Query 3: Color/State Table
        var stateResult = _wmiClient.Query(CmdBacklight, TypeGetColorStateTable);
        byte stateMode = stateResult.Data.Length > 0 ? stateResult.Data[0] : (byte)0;

        // Query 4: Brightness
        byte brightness = GetBrightness();

        return new KeyboardInfo
        {
            Model = model,
            BiosVersion = bios,
            BoardProduct = board,
            KeyboardTypeDescription = kbdDesc,
            RawKeyboardType = rawKbdType,
            IsLightingSupported = isSupported,
            CapabilitiesFlags = capFlags,
            CurrentStateMode = stateMode,
            CurrentBrightness = brightness
        };
    }

    /// <summary>
    /// Gets the current backlight brightness raw byte (0 to 255).
    /// </summary>
    public byte GetBrightness()
    {
        var result = _wmiClient.Query(CmdBacklight, TypeGetBrightness);
        if (result.Success && result.Data.Length > 0)
        {
            byte val = result.Data[0];
            if (val > 0)
            {
                _lastNonZeroBrightness = val;
            }
            return val;
        }
        return 0;
    }

    /// <summary>
    /// Gets the current backlight brightness as a percentage (0% to 100%).
    /// </summary>
    public int GetBrightnessPercent()
    {
        byte val = GetBrightness();
        return (int)Math.Round((val / 255.0) * 100.0);
    }

    /// <summary>
    /// Checks if the backlight is currently turned on.
    /// </summary>
    public bool IsBacklightOn()
    {
        return GetBrightness() > 0;
    }

    /// <summary>
    /// Method 1: HP Standard 128-byte Color/Zone Table (Command 0x20009, Type 0x03).
    /// Used by OMEN Hub and Linux hp-wmi driver for backlight & color zones.
    /// </summary>
    public HpWmiResult SetColorTable(byte brightnessLevel)
    {
        var buffer = new byte[128];
        // Byte 0: Zone count (0x04 for 4-zone or 0x01 / 0x03 for single-zone)
        buffer[0] = brightnessLevel > 0 ? (byte)0x04 : (byte)0x00;
        
        // Bytes 25..36: 4 zones x 3 bytes RGB
        // For monochrome/white backlights, set all RGB channels to brightnessLevel
        for (int i = 25; i < 25 + 12; i++)
        {
            buffer[i] = brightnessLevel;
        }

        return _wmiClient.Query(CmdBacklight, TypeSetColorStateTable, buffer);
    }

    /// <summary>
    /// Method 2: HP Brightness Register (Command 0x20009, Type 0x05).
    /// </summary>
    public HpWmiResult SetBrightnessRaw(byte brightnessLevel)
    {
        var buffer = new byte[128];
        buffer[0] = brightnessLevel;
        return _wmiClient.Query(CmdBacklight, TypeSetBrightness, buffer);
    }

    /// <summary>
    /// Method 3: HP GM Toggle Register (Command 0x20008, Type 0x22).
    /// Sourced from OmenMon / OmenFlow: 0xE4 = On, 0x64 = Off.
    ///
    /// SAFETY: 0x20008 is the same undocumented "GM" domain that caused a full-system
    /// freeze during probing (see Program.cs RunComprehensiveProbe comment). This method
    /// is kept ONLY for the manual, opt-in `test-methods` CLI command where you are
    /// actively watching the keyboard and can hard-reboot if needed. It must never be
    /// called from the automatic SetBrightness()/TurnOn()/TurnOff()/Toggle() path that
    /// runs on every ordinary use of the app.
    /// </summary>
    public HpWmiResult SetGmToggle(bool on)
    {
        var buffer = new byte[128];
        buffer[0] = on ? (byte)0xE4 : (byte)0x64;
        return _wmiClient.Query(CmdGeneral, TypeGmBacklight, buffer);
    }

    /// <summary>
    /// Method 4: HP State Mode (Command 0x20009, Type 0x03 with State Table byte).
    /// </summary>
    public HpWmiResult SetStateMode(byte state)
    {
        var buffer = new byte[128];
        buffer[0] = state; // e.g. 0x03 (On) or 0x00 (Off)
        return _wmiClient.Query(CmdBacklight, TypeSetColorStateTable, buffer);
    }

    /// <summary>
    /// Sets the backlight brightness using the documented backlight-domain (0x20009)
    /// write formats. Does NOT touch the 0x20008 "GM" domain - see the safety note on
    /// SetGmToggle() for why that one is opt-in/diagnostic-only, not automatic.
    /// </summary>
    public bool SetBrightness(byte level)
    {
        bool anySuccess = false;

        // 1. Send ColorTable format (HP standard 4-zone / single-zone RGB table)
        var res1 = SetColorTable(level);
        if (res1.Success) anySuccess = true;

        // 2. Send Brightness level format
        var res2 = SetBrightnessRaw(level);
        if (res2.Success) anySuccess = true;

        // 3. Send State Mode format (0x03 On / 0x00 Off)
        var res4 = SetStateMode(level > 0 ? (byte)0x03 : (byte)0x00);
        if (res4.Success) anySuccess = true;

        if (level > 0)
        {
            _lastNonZeroBrightness = level;
        }

        return anySuccess;
    }

    /// <summary>
    /// Sets the backlight brightness using a percentage (0% to 100%).
    /// </summary>
    public bool SetBrightnessPercent(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        byte rawLevel = (byte)Math.Round((percent / 100.0) * 255.0);
        return SetBrightness(rawLevel);
    }

    /// <summary>
    /// Turns the keyboard backlight ON.
    /// </summary>
    public bool TurnOn()
    {
        byte target = _lastNonZeroBrightness > 0 ? _lastNonZeroBrightness : (byte)0xFF;
        return SetBrightness(target);
    }

    /// <summary>
    /// Turns the keyboard backlight OFF.
    /// </summary>
    public bool TurnOff()
    {
        return SetBrightness(0);
    }

    /// <summary>
    /// Toggles the keyboard backlight state between ON and OFF.
    /// </summary>
    public bool Toggle()
    {
        if (IsBacklightOn())
        {
            return TurnOff();
        }
        else
        {
            return TurnOn();
        }
    }

    /// <summary>
    /// Runs a benchmark testing all known write command variants to find the active channel.
    /// </summary>
    public List<MethodTestResult> TestAllMethods(bool turnOn)
    {
        var results = new List<MethodTestResult>();
        byte level = turnOn ? (byte)0xFF : (byte)0x00;

        // Test 1: ColorTable (0x20009 / 0x03)
        var res1 = SetColorTable(level);
        results.Add(new MethodTestResult("1. ColorTable (0x20009/0x03 with RGB buffer)", CmdBacklight, TypeSetColorStateTable, res1.ReturnCode, HpWmiReturnCodes.Describe(res1.ReturnCode)));

        // Test 2: Brightness (0x20009 / 0x05)
        var res2 = SetBrightnessRaw(level);
        results.Add(new MethodTestResult("2. Brightness (0x20009/0x05 byte level)", CmdBacklight, TypeSetBrightness, res2.ReturnCode, HpWmiReturnCodes.Describe(res2.ReturnCode)));

        // Test 3: GM Toggle (0x20008 / 0x22 with 0xE4/0x64)
        var res3 = SetGmToggle(turnOn);
        results.Add(new MethodTestResult("3. GM Toggle (0x20008/0x22 flag 0xE4/0x64)", CmdGeneral, TypeGmBacklight, res3.ReturnCode, HpWmiReturnCodes.Describe(res3.ReturnCode)));

        // Test 4: State Mode (0x20009 / 0x03 byte mode)
        var res4 = SetStateMode(turnOn ? (byte)0x03 : (byte)0x00);
        results.Add(new MethodTestResult("4. State Mode (0x20009/0x03 byte 0x03/0x00)", CmdBacklight, TypeSetColorStateTable, res4.ReturnCode, HpWmiReturnCodes.Describe(res4.ReturnCode)));

        return results;
    }

    /// <summary>
    /// Accesses the underlying WMI client to run diagnostic queries.
    /// </summary>
    public HpWmiClient WmiClient => _wmiClient;

    public void Dispose()
    {
        _wmiClient.Dispose();
    }
}
