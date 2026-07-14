using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using WojoPersistentEditor.Models;

namespace WojoPersistentEditor.Services
{
    public static class PythonPersistentDecoder
    {
        public static async Task<PersistentDecodeResult> DecodeAsync(
            string persistentPath
        )
        {
            string scriptPath = Path.Combine(
                AppContext.BaseDirectory,
                "Scripts",
                "Decoding.py"
            );
            
            List<PythonCommand> commands =
                GetPythonCommands(scriptPath);
            
            if (commands.Count == 0)
            {
                throw new FileNotFoundException(
                    "Neither the bundled decoder nor Decoding.py was found."
                );
            }
            
            List<string> unavailableCommands = new List<string>();

            foreach (PythonCommand command in commands)
            {
                try
                {
                    return await RunDecoderAsync(
                        command,
                        persistentPath
                    );
                }
                catch (Win32Exception)
                {
                    unavailableCommands.Add(command.FileName);
                }
            }

            throw new InvalidOperationException(
                "Python 3 could not be started. Install Python 3 and make " +
                "sure it is available in PATH. Tried: " +
                string.Join(", ", unavailableCommands) + "."
            );
        }

        private static async Task<PersistentDecodeResult> RunDecoderAsync(
            PythonCommand command,
            string persistentPath
        )
        {
            ProcessStartInfo processInfo = new ProcessStartInfo
            {
                FileName = command.FileName,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
                UseShellExecute = false
            };

            foreach (string prefixArgument in command.PrefixArguments)
            {
                processInfo.ArgumentList.Add(prefixArgument);
            }

            processInfo.ArgumentList.Add(persistentPath);
            processInfo.Environment["PYTHONIOENCODING"] = "utf-8";

            using Process process = new Process
            {
                StartInfo = processInfo
            };

            process.Start();

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync().ConfigureAwait(false);

            string output = await outputTask.ConfigureAwait(false);
            string errorOutput = await errorTask.ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(output))
            {
                throw new InvalidOperationException(
                    "Decoding.py did not return JSON.\n" + errorOutput
                );
            }

            PersistentDecodeResult? result;

            try
            {
                result = await Task.Run(() =>
                    JsonSerializer.Deserialize<PersistentDecodeResult>(
                        output
                    )
                ).ConfigureAwait(false);
            }
            catch (JsonException error)
            {
                throw new InvalidOperationException(
                    "Decoding.py returned invalid JSON.\n" + errorOutput,
                    error
                );
            }

            if (result == null)
            {
                throw new InvalidOperationException(
                    "Decoding.py returned an empty result."
                );
            }

            if (!result.Success)
            {
                string errorMessage =
                    "The persistent file could not be decoded.";

                if (result.Error != null)
                {
                    errorMessage =
                        result.Error.Type + ": " + result.Error.Message;
                }

                throw new InvalidOperationException(errorMessage);
            }

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "Decoding.py finished with code " +
                    process.ExitCode + ".\n" + errorOutput
                );
            }

            return result;
        }

        private static List<PythonCommand> GetPythonCommands(
    string scriptPath
)
{
    List<PythonCommand> commands = new List<PythonCommand>();

    string toolFileName =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "Decoding.exe"
            : "Decoding";

    string toolPath = Path.Combine(
        AppContext.BaseDirectory,
        "Tools",
        toolFileName
    );

    if (File.Exists(toolPath))
    {
        commands.Add(new PythonCommand(toolPath));
    }

    if (!File.Exists(scriptPath))
    {
        return commands;
    }

    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        commands.Add(
            new PythonCommand("py", "-3", scriptPath)
        );
        commands.Add(
            new PythonCommand("python", scriptPath)
        );
        commands.Add(
            new PythonCommand("python3", scriptPath)
        );
    }
    else
    {
        commands.Add(
            new PythonCommand("python3", scriptPath)
        );
        commands.Add(
            new PythonCommand("python", scriptPath)
            );
        }
    
        return commands;
    }
        private sealed class PythonCommand
        {
            public string FileName { get; }
            public IReadOnlyList<string> PrefixArguments { get; }

            public PythonCommand(
                string fileName,
                params string[] prefixArguments
            )
            {
                FileName = fileName;
                PrefixArguments = prefixArguments;
            }
        }
    }
}
