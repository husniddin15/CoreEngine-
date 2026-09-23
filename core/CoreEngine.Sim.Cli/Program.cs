using System.Diagnostics;
using System.Globalization;
using System.Text;
using CoreEngine.Sim.Avr;
using CoreEngine.Sim.Compile;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
return SimCli.Execute(args);

/// <summary>simcli: headless runner for the simulation core (docs/04-technical-design.md §3).</summary>
static class SimCli
{
    public static int Execute(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        try
        {
            return args[0] switch
            {
                "run" => Run(new Options(args[1..]), benchmark: false),
                "bench" => Run(new Options(args[1..]), benchmark: true),
                "compile" => Compile(new Options(args[1..])),
                _ => Fail($"Unknown command '{args[0]}'. Try: simcli --help"),
            };
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException or FormatException or ArgumentException or TimeoutException)
        {
            return Fail(e.Message);
        }
    }

    static void PrintUsage()
    {
        Console.WriteLine("""
            simcli - CoreEngine simulation core, headless

              simcli run <program.hex> [--seconds 5] [--pins led|all|none] [--no-serial]
                  Runs an Arduino Uno (ATmega328P) program and prints pin changes and Serial output.

              simcli bench <program.hex> [--seconds 10]
                  Measures emulation speed in clock cycles per second.

              simcli compile <sketch-folder> [--fqbn arduino:avr:uno] [--run <seconds>]
                  Compiles a sketch with the bundled arduino-cli (tools/fetch-toolchain.ps1),
                  prints diagnostics, and optionally runs the result.
            """);
    }

    static int Fail(string message)
    {
        Console.Error.WriteLine("error: " + message);
        return 1;
    }

    // ------------------------------------------------------------------ run / bench

    static int Run(Options options, bool benchmark)
    {
        string hexPath = options.Positional(0, "program.hex");
        double seconds = options.Double("--seconds", benchmark ? 10 : 5);
        return RunHex(hexPath, seconds, benchmark, options.Value("--pins", "led"), !options.Flag("--no-serial"));
    }

    static int RunHex(string hexPath, double seconds, bool benchmark, string pins, bool serial)
    {
        var mcu = new Atmega328P();
        mcu.LoadHex(File.ReadAllText(hexPath));
        Console.WriteLine($"Loaded {Path.GetFileName(hexPath)}: {mcu.ProgramSize} bytes of flash");

        var diagnostics = new HashSet<string>();
        mcu.Cpu.Diagnostic += message =>
        {
            if (diagnostics.Add(message)) Console.WriteLine("  ! " + message);
        };

        if (benchmark)
        {
            mcu.RunSeconds(1); // warm up the JIT
            long startCycles = mcu.Cpu.Cycles;
            var watch = Stopwatch.StartNew();
            mcu.RunSeconds(seconds);
            watch.Stop();
            long cycles = mcu.Cpu.Cycles - startCycles;
            double rate = cycles / watch.Elapsed.TotalSeconds;
            Console.WriteLine($"Emulated {seconds:F1} s ({cycles:N0} cycles) in {watch.Elapsed.TotalSeconds:F3} s");
            Console.WriteLine($"Speed: {rate / 1e6:F1} M cycles/s = {rate / Atmega328P.ClockHz:F1} x real time (target: 110 M cycles/s)");
            return 0;
        }

        if (pins != "none")
        {
            PinDriveChangedHandler print = (port, bit, drive, cycle) =>
            {
                string name = Atmega328P.ArduinoPinName(port.Name, bit);
                Console.WriteLine($"  {cycle / (double)Atmega328P.ClockHz,12:F6} s  pin {name,-4} (P{port.Name}{bit})  {drive}");
            };
            if (pins == "led")
            {
                mcu.PortB.PinDriveChanged += (port, bit, drive, cycle) => { if (bit == 5) print(port, bit, drive, cycle); };
            }
            else
            {
                mcu.PortB.PinDriveChanged += print;
                mcu.PortC.PinDriveChanged += print;
                mcu.PortD.PinDriveChanged += print;
            }
        }

        long serialBytes = 0;
        if (serial)
        {
            var line = new StringBuilder();
            long lineStart = -1;
            mcu.Usart0.ByteTransmitted += (value, start, end) =>
            {
                serialBytes++;
                if (lineStart < 0) lineStart = start;
                if (value == '\n')
                {
                    Console.WriteLine($"  {end / (double)Atmega328P.ClockHz,12:F6} s  serial: {line.ToString().TrimEnd('\r')}");
                    line.Clear();
                    lineStart = -1;
                }
                else
                {
                    line.Append(value >= 32 && value < 127 || value == '\r' ? (char)value : '?');
                }
            };
        }

        var run = Stopwatch.StartNew();
        mcu.RunSeconds(seconds);
        run.Stop();

        double speed = mcu.Cpu.Cycles / run.Elapsed.TotalSeconds;
        Console.WriteLine();
        Console.WriteLine($"Emulated {mcu.Seconds:F3} s ({mcu.Cpu.Cycles:N0} cycles, {mcu.Cpu.InterruptsServiced:N0} interrupts) " +
                          $"in {run.Elapsed.TotalSeconds:F3} s real time ({speed / Atmega328P.ClockHz:F1} x real time)");
        if (serialBytes > 0)
            Console.WriteLine($"USART0: {serialBytes} bytes at {mcu.Usart0.ActualBaud(Atmega328P.ClockHz):F0} baud actual, {mcu.Usart0.FrameBits} bits per frame");
        return 0;
    }

    // ------------------------------------------------------------------ compile

    static int Compile(Options options)
    {
        string sketch = Path.GetFullPath(options.Positional(0, "sketch-folder"));
        if (!Directory.Exists(sketch)) throw new DirectoryNotFoundException($"Sketch folder not found: {sketch}");
        string fqbn = options.Value("--fqbn", "arduino:avr:uno");

        var compiler = ArduinoCliCompiler.FindBundled(AppContext.BaseDirectory)
                       ?? ArduinoCliCompiler.FindBundled(Directory.GetCurrentDirectory())
                       ?? throw new FileNotFoundException("Bundled arduino-cli not found. Run tools\\fetch-toolchain.ps1 first.");

        string name = Path.GetFileName(sketch);
        string work = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(compiler.CliPath)!)!, "builds", "simcli-" + name);
        Console.WriteLine($"Compiling {name} for {fqbn} ...");
        var result = compiler.Compile(sketch, fqbn, Path.Combine(work, "build"), Path.Combine(work, "out"));

        foreach (var d in result.Diagnostics) Console.WriteLine("  " + d);
        Console.WriteLine($"{(result.Success ? "Success" : "FAILED")} in {result.Duration.TotalSeconds:F2} s (exit code {result.ExitCode})");
        if (!result.Success)
        {
            if (result.Diagnostics.Count == 0) Console.WriteLine(result.Output);
            return 1;
        }

        Console.WriteLine($"  {result.HexPath}");
        double runSeconds = options.Double("--run", 0);
        return runSeconds > 0 ? RunHex(result.HexPath!, runSeconds, false, "led", true) : 0;
    }

    // ------------------------------------------------------------------ arguments

    sealed class Options
    {
        readonly List<string> positional = new();
        readonly Dictionary<string, string?> named = new();

        public Options(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("--", StringComparison.Ordinal))
                {
                    bool hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
                    named[args[i]] = hasValue ? args[++i] : null;
                }
                else
                {
                    positional.Add(args[i]);
                }
            }
        }

        public string Positional(int index, string name) =>
            index < positional.Count ? positional[index] : throw new ArgumentException($"Missing <{name}>. Try: simcli --help");

        public bool Flag(string name) => named.ContainsKey(name);

        public string Value(string name, string fallback) =>
            named.TryGetValue(name, out var v) && v != null ? v : fallback;

        public double Double(string name, double fallback) =>
            named.TryGetValue(name, out var v) && v != null ? double.Parse(v, CultureInfo.InvariantCulture) : fallback;
    }
}
