using System.Management;

namespace HpBacklightProbe;

/// <summary>
/// Result of a single call into the HP BIOS WMI command channel.
/// </summary>
public sealed class HpWmiResult
{
    /// <summary>Return code from the firmware. 0 == success. See HpWmiReturnCodes.</summary>
    public int ReturnCode { get; init; }

    /// <summary>Raw 128-byte (or shorter) response buffer from the firmware.</summary>
    public byte[] Data { get; init; } = Array.Empty<byte>();

    public bool Success => ReturnCode == 0;
}

/// <summary>
/// Known return codes for the HP BIOS WMI command channel.
/// Sourced from the public Linux hp-wmi driver (drivers/platform/x86/hp/hp-wmi.c).
/// </summary>
public static class HpWmiReturnCodes
{
    public const int Success = 0x00;
    public const int WrongSignature = 0x02;
    public const int UnknownCommand = 0x03;
    public const int UnknownCommandType = 0x04;
    public const int InvalidParameters = 0x05;

    public static string Describe(int code) => code switch
    {
        Success => "Success",
        WrongSignature => "Wrong signature (our Sign value didn't match what firmware expects)",
        UnknownCommand => "Unknown command (this 'command' value isn't recognized by firmware)",
        UnknownCommandType => "Unknown command type (this sub-query isn't recognized)",
        InvalidParameters => "Invalid parameters (payload size/content rejected)",
        _ => $"Unrecognized/undocumented return code (0x{code:X})"
    };
}

/// <summary>
/// Thin wrapper around the HP "hpqBIntM" WMI class in the root\wmi namespace.
///
/// This mirrors the pattern used by HP's own LightStudioHelper.exe (as decompiled by
/// Rishit Bansal, see https://dev.to/rishit/reverse-engineering-keyboard-driver-part-2-decompiling-net-applications-44l2)
/// and by the open-source omen-cli project (https://github.com/thebongy/omen-cli).
///
/// This talks to root\wmi, not root\cimv2 - HP's BIOS command interface lives there.
/// </summary>
public sealed class HpWmiClient : IDisposable
{
    // This is the "Sign" / signature value the Linux hp-wmi driver embeds in every
    // BIOS command it sends ("SECU" = 0x55434553 in little-endian byte order).
    // In WMI hpqBDataIn schema, 'Sign' is defined as UInt8Array (byte[]).
    private static readonly byte[] Signature = { (byte)'S', (byte)'E', (byte)'C', (byte)'U' };

    private readonly ManagementObject _biosInterface;

    private HpWmiClient(ManagementObject biosInterface)
    {
        _biosInterface = biosInterface;
    }

    /// <summary>
    /// Connects to the HP BIOS WMI interface. Throws ManagementException with a
    /// descriptive message if the class/instance isn't present on this machine.
    /// </summary>
    public static HpWmiClient Connect()
    {
        var scope = new ManagementScope(@"root\wmi");
        scope.Connect();

        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM hpqBIntM"));
        var results = searcher.Get();

        ManagementObject? found = null;
        foreach (ManagementObject mo in results)
        {
            found = mo;
            break; // there should only be one instance
        }

        if (found is null)
        {
            throw new ManagementException(
                "No instance of 'hpqBIntM' found under root\\wmi. " +
                "This machine may not expose the HP BIOS WMI interface, or it may be named " +
                "differently on this firmware revision. Try: " +
                "Get-CimClass -Namespace root\\wmi | Where-Object CimClassName -like 'hpq*' " +
                "in PowerShell to look for the correct class name.");
        }

        return new HpWmiClient(found);
    }

    /// <summary>
    /// Sends one BIOS command and returns the firmware's response.
    /// </summary>
    /// <param name="command">Top-level command domain, e.g. 0x20008 (GM) or 0x20009 (Backlight).</param>
    /// <param name="commandType">Sub-query within that domain, e.g. 0x2B (get keyboard type).</param>
    /// <param name="inputData">Optional payload (max 128 bytes). Pass null/empty for read-only queries.</param>
    public HpWmiResult Query(uint command, uint commandType, byte[]? inputData = null)
    {
        inputData ??= Array.Empty<byte>();
        if (inputData.Length > 128)
            throw new ArgumentException("Input payload cannot exceed 128 bytes.", nameof(inputData));

        // Pad to 128 bytes - the firmware buffer is a fixed 128-byte array.
        var paddedInput = new byte[128];
        Array.Copy(inputData, paddedInput, inputData.Length);

        using var inDataClass = new ManagementClass(@"root\wmi", "hpqBDataIn", null);
        using ManagementObject inData = inDataClass.CreateInstance()
            ?? throw new InvalidOperationException("Failed to create hpqBDataIn instance.");

        inData["Sign"] = Signature;
        inData["Command"] = command;
        inData["CommandType"] = commandType;
        inData["Size"] = (uint)inputData.Length;
        inData["hpqBData"] = paddedInput;

        using ManagementBaseObject methodParams = _biosInterface.GetMethodParameters("hpqBIOSInt128");
        methodParams["InData"] = inData;

        using ManagementBaseObject outParams = _biosInterface.InvokeMethod(
            "hpqBIOSInt128", methodParams, new InvokeMethodOptions { Timeout = TimeSpan.FromSeconds(5) });

        var outData = (ManagementBaseObject)outParams["OutData"];
        var data = outData["Data"] as byte[] ?? Array.Empty<byte>();
        var returnCode = Convert.ToInt32(outData["rwReturnCode"]);

        return new HpWmiResult { ReturnCode = returnCode, Data = data };
    }

    public void Dispose()
    {
        _biosInterface.Dispose();
    }
}
