using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CoreEngine.Sim.Compile
{
    public enum DiagnosticSeverity
    {
        Note,
        Warning,
        Error,
    }

    /// <summary>One GCC message mapped back to the user's sketch file (docs/05 §8.3).</summary>
    public sealed class CompilerDiagnostic
    {
        public CompilerDiagnostic(string file, int line, int column, DiagnosticSeverity severity, string message)
        {
            File = file;
            Line = line;
            Column = column;
            Severity = severity;
            Message = message;
        }

        public string File { get; }
        public int Line { get; }
        public int Column { get; }
        public DiagnosticSeverity Severity { get; }
        public string Message { get; }

        public override string ToString() => $"{Path.GetFileName(File)}:{Line}:{Column}: {Severity.ToString().ToLowerInvariant()}: {Message}";
    }

    public sealed class CompileResult
    {
        public CompileResult(bool success, int exitCode, string? hexPath, string? elfPath,
            IReadOnlyList<CompilerDiagnostic> diagnostics, string output, TimeSpan duration)
        {
            Success = success;
            ExitCode = exitCode;
            HexPath = hexPath;
            ElfPath = elfPath;
            Diagnostics = diagnostics;
            Output = output;
            Duration = duration;
        }

        public bool Success { get; }
        public int ExitCode { get; }
        public string? HexPath { get; }
        public string? ElfPath { get; }
        public IReadOnlyList<CompilerDiagnostic> Diagnostics { get; }
        public string Output { get; }
        public TimeSpan Duration { get; }
    }

    /// <summary>
    /// Compiles sketches with the bundled arduino-cli, run as a separate process
    /// (ADR-0003: arduino-cli is GPLv3 and is never linked into the game).
    /// </summary>
    public sealed class ArduinoCliCompiler
    {
        static readonly Regex DiagnosticPattern = new Regex(
            @"^(?<file>.+?):(?<line>\d+):(?<col>\d+):\s+(?<sev>fatal error|error|warning|note):\s+(?<msg>.*)$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        public ArduinoCliCompiler(string cliPath, string configPath, IProcessRunner? runner = null)
        {
            CliPath = cliPath;
            ConfigPath = configPath;
            Runner = runner ?? ProcessRunner.Default;
        }

        public string CliPath { get; }
        public string ConfigPath { get; }

        /// <summary>How arduino-cli is started; the Win32 runner by default on Windows, which also works under IL2CPP.</summary>
        public IProcessRunner Runner { get; }

        /// <summary>
        /// Looks for tools/arduino/bin/arduino-cli.exe and tools/arduino/arduino-cli.yaml
        /// in <paramref name="startDirectory"/> and its parents (created by tools/fetch-toolchain.ps1).
        /// </summary>
        public static ArduinoCliCompiler? FindBundled(string startDirectory)
        {
            var dir = new DirectoryInfo(startDirectory);
            while (dir != null)
            {
                string baseDir = Path.Combine(dir.FullName, "tools", "arduino");
                string cli = Path.Combine(baseDir, "bin", "arduino-cli.exe");
                string config = Path.Combine(baseDir, "arduino-cli.yaml");
                if (File.Exists(cli) && File.Exists(config)) return new ArduinoCliCompiler(cli, config);
                dir = dir.Parent;
            }
            return null;
        }

        public CompileResult Compile(string sketchDirectory, string fqbn, string buildDirectory, string outputDirectory, TimeSpan? timeout = null)
        {
            string sketchDir = Path.GetFullPath(sketchDirectory);
            string sketchName = Path.GetFileName(sketchDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            Directory.CreateDirectory(buildDirectory);
            Directory.CreateDirectory(outputDirectory);

            var arguments = new[]
            {
                "compile", "--fqbn", fqbn, "--config-file", ConfigPath,
                "--build-path", Path.GetFullPath(buildDirectory), "--output-dir", Path.GetFullPath(outputDirectory),
                "--warnings", "default", sketchDir,
            };
            var run = Runner.Run(CliPath, arguments, timeout ?? TimeSpan.FromMinutes(2));
            string text = run.Output;
            string hex = Path.Combine(outputDirectory, sketchName + ".ino.hex");
            string elf = Path.Combine(outputDirectory, sketchName + ".ino.elf");
            bool success = run.ExitCode == 0 && File.Exists(hex);
            return new CompileResult(success, run.ExitCode, success ? hex : null, File.Exists(elf) ? elf : null,
                ParseDiagnostics(text), text, run.Duration);
        }

        /// <summary>Extracts GCC-style "file:line:col: severity: message" lines.</summary>
        public static IReadOnlyList<CompilerDiagnostic> ParseDiagnostics(string output)
        {
            var list = new List<CompilerDiagnostic>();
            foreach (Match m in DiagnosticPattern.Matches(output))
            {
                var severity = m.Groups["sev"].Value switch
                {
                    "note" => DiagnosticSeverity.Note,
                    "warning" => DiagnosticSeverity.Warning,
                    _ => DiagnosticSeverity.Error,
                };
                list.Add(new CompilerDiagnostic(
                    m.Groups["file"].Value.Trim(),
                    int.Parse(m.Groups["line"].Value),
                    int.Parse(m.Groups["col"].Value),
                    severity,
                    m.Groups["msg"].Value.TrimEnd('\r')));
            }
            return list;
        }
    }
}
