using System.Diagnostics;
using Xunit;

namespace Weather.Tests;

public sealed class WeatherImportCliTests
{
    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public async Task HelpSucceedsWithoutConfiguration(string flag)
    {
        var result = await RunAsync([flag]);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--macAddress", result.Output);
        Assert.DoesNotContain("Importing", result.Output);
        Assert.Empty(result.Error);
    }

    [Theory]
    [InlineData("ConnectionStrings__sql")]
    [InlineData("Ambient__ApiKey")]
    [InlineData("Ambient__ApplicationKey")]
    [InlineData("Ambient__DeviceMacAddress")]
    public async Task MissingConfigurationFailsBeforeImporting(string missing)
    {
        var result = await RunAsync([], missing);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Configure ConnectionStrings:sql", result.Error);
        Assert.DoesNotContain("Importing", result.Output);
        Assert.DoesNotContain("secret", result.Error);
    }

    [Fact]
    public async Task ImportFailureReturnsNonzeroAndSanitizesDiagnostics()
    {
        var result = await RunAsync([]);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Importing Ambient Weather history", result.Output);
        Assert.Contains("Import failed.", result.Error);
        Assert.Contains("No partial import was committed.", result.Error);
        Assert.DoesNotContain("secret", result.Output + result.Error);
    }

    [Fact]
    public async Task WebJobShutdownFileCancelsBeforeSqlOrAmbientAccess()
    {
        var shutdownFile = Path.GetTempFileName();
        try
        {
            var result = await RunAsync([], shutdownFile: shutdownFile);
            Assert.Equal(130, result.ExitCode);
            Assert.Contains("Import canceled.", result.Error);
            Assert.DoesNotContain("Importing", result.Output);
            Assert.DoesNotContain("secret", result.Output + result.Error);
        }
        finally { File.Delete(shutdownFile); }
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(string[] args, string? missing = null, string? shutdownFile = null)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var dll = Path.Combine(root, "src", "Weather.Import", "bin", configuration, "net10.0", "Weather.Import.dll");
        var info = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        info.ArgumentList.Add(dll);
        foreach (var arg in args) info.ArgumentList.Add(arg);
        info.Environment["DOTNET_ENVIRONMENT"] = "Production";
        // Override local settings and inherited credentials. This invalid connection
        // string fails parsing before SQL or Ambient can be contacted.
        info.Environment["ConnectionStrings__sql"] = "invalid-secret-connection";
        info.Environment["Ambient__ApiKey"] = "secret-api";
        info.Environment["Ambient__ApplicationKey"] = "secret-app";
        info.Environment["Ambient__DeviceMacAddress"] = "test-station";
        info.Environment.Remove("WEBJOBS_SHUTDOWN_FILE");
        if (shutdownFile is not null) info.Environment["WEBJOBS_SHUTDOWN_FILE"] = shutdownFile;
        if (missing is not null) info.Environment[missing] = " ";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        return (process.ExitCode, await output, await error);
    }
}
