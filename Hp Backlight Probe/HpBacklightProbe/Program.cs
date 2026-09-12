using System.Management;
using HpBacklightProbe;

namespace HpBacklightProbe;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // If no arguments or explicitly requesting GUI, launch WPF Desktop UI
        if (args.Length == 0 || (args.Length == 1 && (args[0].Equals("gui", StringComparison.OrdinalIgnoreCase) || args[0].Equals("--gui", StringComparison.OrdinalIgnoreCase))))
        {
            var app = new App();
            app.InitializeComponent();
            return app.Run(new MainWindow());
        }

        string cmd = args[0].ToLowerInvariant().TrimStart('-');

        // Help Menu
        if (cmd is "help" or "h" or "?")
        {
            PrintHelp();
            return 0;
        }

        // Check for Administrator privileges
        if (!HpBacklightController.IsAdministrator())
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[Warning] Not running as Administrator. Hardware WMI commands may fail.");
            Console.ResetColor();
        }

        using var controller = HpBacklightController.Create();

        switch (cmd)
        {
            case "status":
            case "info":
                var info = controller.GetInfo();
                Console.WriteLine("=== HP Keyboard Backlight Status ===");
                Console.WriteLine($"Model:            {info.Model}");
                Console.WriteLine($"BIOS Version:     {info.BiosVersion}");
                Console.WriteLine($"Board:            {info.BoardProduct}");
                Console.WriteLine($"Keyboard Type:    {info.KeyboardTypeDescription}");
                Console.WriteLine($"Lighting Support: {(info.IsLightingSupported ? "Yes" : "No")} (Flags: 0x{info.CapabilitiesFlags:X2})");
                Console.WriteLine($"State Mode:       0x{info.CurrentStateMode:X2}");
                Console.WriteLine($"Brightness:       {info.BrightnessPercent}% (raw: 0x{info.CurrentBrightness:X2})");
                Console.WriteLine($"Backlight Power:  {(info.IsOn ? "ON" : "OFF")}");
                return 0;

            case "on":
                Console.WriteLine("Turning keyboard backlight ON...");
                if (controller.TurnOn())
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("[Success] Backlight turned ON.");
                    Console.ResetColor();
                    return 0;
                }
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[Fail] Failed to turn backlight ON.");
                Console.ResetColor();
                return 1;

            case "off":
                Console.WriteLine("Turning keyboard backlight OFF...");
                if (controller.TurnOff())
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("[Success] Backlight turned OFF.");
                    Console.ResetColor();
                    return 0;
                }
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[Fail] Failed to turn backlight OFF.");
                Console.ResetColor();
                return 1;

            case "toggle":
                Console.WriteLine("Toggling keyboard backlight...");
                if (controller.Toggle())
                {
                    bool isNowOn = controller.IsBacklightOn();
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"[Success] Backlight toggled -> Now {(isNowOn ? "ON" : "OFF")}.");
                    Console.ResetColor();
                    return 0;
                }
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[Fail] Failed to toggle backlight.");
                Console.ResetColor();
                return 1;

            case "set":
                if (args.Length < 2 || !int.TryParse(args[1], out int percent))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("[Error] Please specify a brightness level from 0 to 100. (e.g., dotnet run -- set 75)");
                    Console.ResetColor();
                    return 1;
                }
                percent = Math.Clamp(percent, 0, 100);
                Console.WriteLine($"Setting keyboard brightness to {percent}%...");
                if (controller.SetBrightnessPercent(percent))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"[Success] Brightness set to {percent}%.");
                    Console.ResetColor();
                    return 0;
                }
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[Fail] Failed to set brightness.");
                Console.ResetColor();
                return 1;

            case "test":
            case "test-methods":
                bool turnOn = args.Length > 1 ? args[1].Equals("on", StringComparison.OrdinalIgnoreCase) : true;
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[Note] Method 3 below (GM Toggle) uses the same undocumented 0x20008");
                Console.WriteLine("domain that previously caused a full-system freeze. Save your work");
                Console.WriteLine("before running this, in case you need to hard-reboot.");
                Console.ResetColor();
                Console.WriteLine($"=== Testing All Write Protocols (Target State: {(turnOn ? "ON" : "OFF")}) ===");
                var testResults = controller.TestAllMethods(turnOn);
                foreach (var tr in testResults)
                {
                    var color = tr.ReturnCode == 0 ? ConsoleColor.Green : ConsoleColor.Yellow;
                    Console.ForegroundColor = color;
                    Console.WriteLine($"  {tr.MethodName}");
                    Console.WriteLine($"    Command=0x{tr.Command:X} CommandType=0x{tr.CommandType:X} -> ReturnCode: 0x{tr.ReturnCode:X2} ({tr.StatusDescription})");
                    Console.ResetColor();
                }
                Console.WriteLine();
                Console.WriteLine("Check your keyboard to see if the backlight changed.");
                return 0;

            case "probe":
            case "scan":
                RunComprehensiveProbe(controller.WmiClient);
                return 0;

            default:
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Error] Unknown command '{args[0]}'.");
                Console.ResetColor();
                PrintHelp();
                return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("=== HP Victus / OMEN Backlight Controller ===");
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run                  Launch the Desktop GUI Application");
        Console.WriteLine("  dotnet run -- status        Display current backlight state and hardware info");
        Console.WriteLine("  dotnet run -- on            Turn backlight ON");
        Console.WriteLine("  dotnet run -- off           Turn backlight OFF");
        Console.WriteLine("  dotnet run -- toggle        Toggle backlight ON / OFF");
        Console.WriteLine("  dotnet run -- set <0-100>   Set backlight brightness percentage");
        Console.WriteLine("  dotnet run -- probe         Run low-level firmware diagnostic hex dump");
        Console.WriteLine("  dotnet run -- help          Show this help message");
    }

    // SAFETY NOTE:
    // hpqBIOSInt128 is not a normal Windows API - each call is a real SMI (System
    // Management Interrupt) into BIOS firmware. While an SMI handler runs, it can
    // block every CPU core on the machine at the hardware level, below Windows
    // itself. A .NET-side timeout (see HpWmiClient.Query) cannot interrupt that -
    // if firmware spins waiting on an EC handshake for a command it doesn't
    // recognize, the whole PC hard-freezes and needs a forced power cycle.
    //
    // This probe therefore ONLY sends command/commandType pairs that are either
    // documented in the README or already proven safe elsewhere in this app
    // (GetInfo() calls these same registers on every refresh). Do NOT add
    // speculative/guessed registers here (e.g. undocumented "GM" domain 0x20008
    // sub-commands like thermal profile/policy) - that is what caused past
    // full-system hangs. If you need to explore unknown registers, do it on
    // a machine you can afford to hard-reboot, one register at a time, saving
    // work first.
    private static void RunComprehensiveProbe(HpWmiClient client)
    {
        Console.WriteLine("=== Running HP BIOS WMI Probe (safe/documented registers only) ===");

        Console.WriteLine("[Domain 0x20008 (HP GM) - verified-safe registers]");
        RunQuery(client, "GM: Keyboard Type (0x2B)", 0x20008, 0x2B);

        Console.WriteLine("[Domain 0x20009 (HP Backlight / Lighting)]");
        RunQuery(client, "Backlight: Is Supported (0x01)", 0x20009, 0x01);
        RunQuery(client, "Backlight: Color/State Table (0x02)", 0x20009, 0x02);
        RunQuery(client, "Backlight: Brightness (0x04)", 0x20009, 0x04);

        Console.WriteLine("=== Probe Finished ===");
        Console.WriteLine();
        Console.WriteLine("Note: undocumented/speculative registers (old 0x0D, 0x1A, 0x22, 0x27,");
        Console.WriteLine("0x2C, 0x30 and the 0x01 domain reads) were removed from this probe -");
        Console.WriteLine("they are what caused the previous full-system freeze. See the comment");
        Console.WriteLine("above RunComprehensiveProbe() if you need to re-add exploration later.");
    }

    private static void RunQuery(HpWmiClient client, string label, uint command, uint commandType)
    {
        Console.WriteLine($"--- {label} ---");
        Console.WriteLine($"    command=0x{command:X} commandType=0x{commandType:X}");
        try
        {
            HpWmiResult result = client.Query(command, commandType);
            Console.WriteLine($"    returnCode = 0x{result.ReturnCode:X2} ({HpWmiReturnCodes.Describe(result.ReturnCode)})");
            Console.WriteLine($"    data ({result.Data.Length} bytes):");
            Console.WriteLine("    " + HexDump(result.Data));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    [error] {ex.GetType().Name}: {ex.Message}");
        }
        Console.WriteLine();
    }

    private static string HexDump(byte[] data)
    {
        if (data.Length == 0) return "(empty)";
        int shown = Math.Min(data.Length, 32);
        var parts = new string[shown];
        for (int i = 0; i < shown; i++)
            parts[i] = data[i].ToString("X2");

        string line = string.Join(" ", parts);
        if (data.Length > shown)
            line += $" ... ({data.Length - shown} more bytes)";
        return line;
    }
}
