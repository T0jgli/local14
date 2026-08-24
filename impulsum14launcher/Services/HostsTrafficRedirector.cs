using System.Diagnostics;
using System.IO;
using System.Net;

namespace ImpulsumLauncher14.Services;

public sealed class HostsTrafficRedirector : IDisposable
{
    private const string Marker = "# Impulsum14 launcher";
    private static readonly string[] ServiceHosts =
    [
        "gosredirector.ea.com",
    ];

    private bool _enabled;

    public void Enable()
    {
        if (_enabled) return;

        var hostsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"drivers\etc\hosts");
        var lines = File.Exists(hostsPath)
            ? File.ReadAllLines(hostsPath).ToList()
            : new List<string>();

        lines.RemoveAll(line => line.Contains(Marker, StringComparison.Ordinal));
        foreach (var host in ServiceHosts)
        {
            if (!ContainsHost(lines, host))
                lines.Add($"{IPAddress.Loopback} {host} {Marker}");
        }

        File.WriteAllLines(hostsPath, lines);
        FlushDnsCache();
        _enabled = true;
    }

    public void Disable()
    {
        if (!_enabled) return;

        var hostsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"drivers\etc\hosts");
        if (File.Exists(hostsPath))
        {
            var lines = File.ReadAllLines(hostsPath).ToList();
            lines.RemoveAll(line => line.Contains(Marker, StringComparison.Ordinal));
            File.WriteAllLines(hostsPath, lines);
            FlushDnsCache();
        }

        _enabled = false;
    }

    private static void FlushDnsCache()
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "ipconfig.exe",
            Arguments = "/flushdns",
            CreateNoWindow = true,
            UseShellExecute = false,
        });
        process?.WaitForExit(5000);
    }

    private static bool ContainsHost(IEnumerable<string> lines, string host)
    {
        return lines.Any(line =>
        {
            var content = line.Split('#', 2)[0];
            var fields = content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return fields.Skip(1).Any(field =>
                string.Equals(field, host, StringComparison.OrdinalIgnoreCase));
        });
    }

    public void Dispose() => Disable();
}