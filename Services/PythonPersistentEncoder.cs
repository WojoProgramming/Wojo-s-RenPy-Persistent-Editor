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
    public static class PythonPersistentEncoder
    {
        public static async Task<PersistentEncodeResult> EncodeAsync(
            string sourcePath,
            string outputPath,
            List<PersistentVariable> variables
        )
        {
            string scriptPath = Path.Combine(
                AppContext.BaseDirectory,
                "Scripts",
                "Encoding.py"
            );


            string changesPath = Path.Combine(
                Path.GetTempPath(),
                "WojoPersistentEditor-" +
                Guid.NewGuid().ToString("N") +
                ".json"
            );

            try
            {
                await Task.Run(() =>
                    WriteChangesFileAsync(
                        changesPath,
                        variables
                    )
                ).ConfigureAwait(false);

                List<PythonCommand> commands =
                    GetPythonCommands(scriptPath);
                
                if (commands.Count == 0)
                {
                    throw new FileNotFoundException(
                        "Neither the bundled encoder nor Encoding.py was found."
                    );
                }
                
                List<string> unavailableCommands = new List<string>();
                foreach (PythonCommand command in commands)
                {
                    try
                    {
                        return await RunEncoderAsync(
                            command,
                            sourcePath,
                            outputPath,
                            changesPath
                        ).ConfigureAwait(false);
                    }
                    catch (Win32Exception)
                    {
                        unavailableCommands.Add(command.FileName);
                    }
                }

                throw new InvalidOperationException(
                    "Python 3 could not be started. Install Python 3 and " +
                    "make sure it is available in PATH. Tried: " +
                    string.Join(", ", unavailableCommands) + "."
                );
            }
            finally
            {
                DeleteFileIfExists(changesPath);
            }
        }

        private static async Task WriteChangesFileAsync(
            string changesPath,
            List<PersistentVariable> variables
        )
        {
            PersistentChangesPayload payload =
                new PersistentChangesPayload
                {
                    Changes = variables
                };

            await using FileStream stream = new FileStream(
                changesPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                true
            );

            await JsonSerializer.SerializeAsync(
                stream,
                payload
            ).ConfigureAwait(false);
        }

        private static async Task<PersistentEncodeResult> RunEncoderAsync(
            PythonCommand command,
            string scriptPath,
            string sourcePath,
            string outputPath,
            string changesPath
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

            processInfo.ArgumentList.Add(sourcePath);
            processInfo.ArgumentList.Add(outputPath);
            processInfo.ArgumentList.Add(changesPath);
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
                    "Encoding.py did not return JSON.\n" + errorOutput
                );
            }

            PersistentEncodeResult? result;

            try
            {
                result = await Task.Run(() =>
                    JsonSerializer.Deserialize<PersistentEncodeResult>(
                        output
                    )
                ).ConfigureAwait(false);
            }
            catch (JsonException error)
            {
                throw new InvalidOperationException(
                    "Encoding.py returned invalid JSON.\n" + errorOutput,
                    error
                );
            }

            if (result == null)
            {
                throw new InvalidOperationException(
                    "Encoding.py returned an empty result."
                );
            }

            if (!result.Success)
            {
                string errorMessage =
                    "The persistent file could not be encoded.";

                if (result.Error != null)
                {
                    errorMessage =
                        result.Error.Type + ": " +
                        result.Error.Message;
                }

                throw new InvalidOperationException(errorMessage);
            }

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "Encoding.py finished with code " +
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
            ? "Encoding.exe"
            : "Encoding";

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

        private static void DeleteFileIfExists(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            try
            {
                File.Delete(filePath);
            }
            catch
            {
            }
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
