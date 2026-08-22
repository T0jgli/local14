using System.Diagnostics;
using System.IO;

namespace ImpulsumLauncher14.Services;

public class ServerService
{
    private static readonly string[] ServerProcessNames = { "Impulsum14", "FIFAServer14" };
    private Process? _serverProcess;
    private readonly string _logPath;

    public bool IsRunning => _serverProcess is { HasExited: false } || HasRunningServer();

    public event Action<bool>? StatusChanged;
    public event Action<string>? LogReceived;

    public ServerService()
    {
        _logPath = Path.Combine(AppContext.BaseDirectory, "server-logs");
        Directory.CreateDirectory(_logPath);
    }

    public string FindServerPath()
    {
        var baseDir = AppContext.BaseDirectory;

        var probePaths = new[]
        {
            Path.Combine(baseDir, "Server", "Impulsum14.exe"),
            Path.Combine(baseDir, "Server", "FIFAServer14.exe"),
        };

        foreach (var p in probePaths)
        {
            var full = Path.GetFullPath(p);
            if (File.Exists(full)) return full;
        }

        return string.Empty;
    }

    public async Task<ProcessResult> StartAsync(string? serverExePath = null, CancellationToken cancellationToken = default)
    {
        var result = new ProcessResult();

        if (IsRunning)
        {
            result.Success = false;
            result.ErrorMessage = "Server is already running.";
            return result;
        }

        var exePath = string.IsNullOrWhiteSpace(serverExePath) ? FindServerPath() : serverExePath;
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            result.Success = false;
            result.ErrorMessage = "Impulsum14.exe not found. Build the server project first.";
            return result;
        }

        try
        {
            var serverDir = Path.GetDirectoryName(exePath)!;
            var logFile = Path.Combine(_logPath, $"server-{DateTime.Now:yyyyMMdd-HHmmss}.log");

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = serverDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            var process = new Process { StartInfo = psi };
            _serverProcess = process;
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    File.AppendAllText(logFile, e.Data + Environment.NewLine);
                    LogReceived?.Invoke(e.Data);
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    File.AppendAllText(logFile, "[ERR] " + e.Data + Environment.NewLine);
                    LogReceived?.Invoke($"[ERR] {e.Data}");
                }
            };
            process.Exited += (_, _) =>
            {
                StatusChanged?.Invoke(false);
                LogReceived?.Invoke("[SERVER] Process exited.");
            };
            process.EnableRaisingEvents = true;

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await Task.Delay(500);

            if (cancellationToken.IsCancellationRequested)
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                if (ReferenceEquals(_serverProcess, process))
                    _serverProcess = null;
                return result;
            }

            if (process.HasExited)
            {
                result.Success = false;
                result.ErrorMessage = "Server process exited prematurely.";
                if (ReferenceEquals(_serverProcess, process))
                    _serverProcess = null;
                return result;
            }

            result.Success = true;
            result.ProcessId = process.Id;
            StatusChanged?.Invoke(true);
            LogReceived?.Invoke($"[SERVER] Started (PID: {process.Id})");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            _serverProcess = null;
        }

        return result;
    }

    public void Stop()
    {
        if (_serverProcess is { HasExited: false })
        {
            _serverProcess.Kill(entireProcessTree: true);
            _serverProcess = null;
            StatusChanged?.Invoke(false);
            LogReceived?.Invoke("[SERVER] Stopped.");
        }

        foreach (var process in GetRunningServers())
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch { }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static bool HasRunningServer()
    {
        var running = false;
        foreach (var process in GetRunningServers())
        {
            try
            {
                running |= !process.HasExited;
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return running;
    }

    private static IEnumerable<Process> GetRunningServers()
    {
        foreach (var processName in ServerProcessNames)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(processName);
            }
            catch
            {
                continue;
            }

            foreach (var process in processes)
                yield return process;
        }
    }

    public async Task<bool> UpdateAndBuildServerAsync(ImpulsumLauncher14.Models.LauncherConfig config, Action<string> statusCallback)
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var serverDir = Path.Combine(baseDir, "Server");
            
            using var client = new System.Net.Http.HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "ImpulsumLauncher14");

            statusCallback("Checking for server updates...");
            
            var apiUrl = "https://api.github.com/repos/MarvelcoCode/Impulsum14/commits/main";
            var response = await client.GetAsync(apiUrl);
            
            string currentHash = string.Empty;
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var node = System.Text.Json.Nodes.JsonNode.Parse(json);
                currentHash = node?["sha"]?.GetValue<string>() ?? string.Empty;
            }
            else
            {
                LogReceived?.Invoke($"[SERVER] GitHub API returned {response.StatusCode}. Cannot check updates if private.");
            }

            var serverExe = FindServerPath();
            bool isMissing = string.IsNullOrEmpty(serverExe) || !File.Exists(serverExe);
            bool needsUpdate = !string.IsNullOrEmpty(currentHash) && config.ServerCommitHash != currentHash;

            if (isMissing || needsUpdate)
            {
                var sourceDir = Path.Combine(baseDir, "ServerSource");
                if (needsUpdate || !Directory.Exists(sourceDir))
                {
                    statusCallback("Downloading server source...");
                    var zipUrl = "https://api.github.com/repos/MarvelcoCode/Impulsum14/zipball/main";
                    var zipBytes = await client.GetByteArrayAsync(zipUrl);
                    
                    if (Directory.Exists(sourceDir)) Directory.Delete(sourceDir, true);
                    Directory.CreateDirectory(sourceDir);
                    
                    var zipPath = Path.Combine(baseDir, "source.zip");
                    await File.WriteAllBytesAsync(zipPath, zipBytes);
                    
                    System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, sourceDir);
                    File.Delete(zipPath);
                    
                    var extractedDirs = Directory.GetDirectories(sourceDir);
                    if (extractedDirs.Length == 1)
                    {
                        var rootDir = extractedDirs[0];
                        foreach (var d in Directory.GetDirectories(rootDir))
                            Directory.Move(d, Path.Combine(sourceDir, Path.GetFileName(d)));
                        foreach (var f in Directory.GetFiles(rootDir))
                            File.Move(f, Path.Combine(sourceDir, Path.GetFileName(f)));
                        Directory.Delete(rootDir);
                    }
                }

                statusCallback("Compiling server...");
                
                var jsonBackups = new System.Collections.Generic.Dictionary<string, string>();
                if (Directory.Exists(serverDir))
                {
                    foreach (var file in Directory.GetFiles(serverDir, "*.json"))
                    {
                        jsonBackups[Path.GetFileName(file)] = File.ReadAllText(file);
                    }
                }

                var publishPsi = new ProcessStartInfo("dotnet", $"publish -c Release -o \"{serverDir}\"") { WorkingDirectory = sourceDir, CreateNoWindow = true, UseShellExecute = false };
                var publishProc = Process.Start(publishPsi);
                if (publishProc != null) await publishProc.WaitForExitAsync();

                foreach (var kvp in jsonBackups)
                {
                    File.WriteAllText(Path.Combine(serverDir, kvp.Key), kvp.Value);
                }

                if (!string.IsNullOrEmpty(currentHash))
                {
                    config.ServerCommitHash = currentHash;
                    config.Save();
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            LogReceived?.Invoke($"[ERR] UpdateAndBuild failed: {ex.Message}");
            var serverExe = FindServerPath();
            if (!string.IsNullOrEmpty(serverExe) && File.Exists(serverExe))
            {
                LogReceived?.Invoke("[SERVER] Using existing server build as fallback.");
                return true;
            }
            return false;
        }
    }
}
