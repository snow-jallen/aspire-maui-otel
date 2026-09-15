using System.Diagnostics;

var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.GeneratedClassNamePrefix_ApiService>("apiservice")
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.GeneratedClassNamePrefix_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);

// Mobile devices cannot reach the development machine's localhost.
//
// The tunnel ID is pinned with a random suffix chosen once, when this app was generated.
// Left unset, Aspire derives the ID from a hash of the AppHost folder, so moving or
// renaming the folder silently orphans the old tunnel - and orphans count against the
// 10-tunnel-per-account cap for 20-30 days. Pinning also avoids the "already exists, but
// then reported it was not found" inconsistent-state error, whose own recovery advice is
// to supply an explicit tunnel ID.
var mobileApiTunnel = builder.AddDevTunnel("mobile-api", tunnelId: "mobile-api-TUNNELSUFFIX")
    .WithAnonymousAccess()
    .WithReference(apiService.GetEndpoint("https"));

var mobile = builder.AddMauiProject(
    "mobile",
    "../PublicOtel.Mobile/PublicOtel.Mobile.csproj");

// Windows talks to the local endpoint directly.
mobile.AddWindowsDevice()
    .WithReference(apiService);

// Android uses a tunnel for the API and another for OTLP telemetry.
// NOTE: WithOtlpDevTunnel() has no tunnelId parameter, so the OTLP tunnel's ID is still
// derived from the AppHost folder hash and cannot be pinned the way mobile-api is above.
var androidEmulator = mobile.AddAndroidEmulator()
    .WithOtlpDevTunnel()
    .WithReference(apiService, mobileApiTunnel);

// Starting this resource normally fails with NETSDK1085, because Aspire.Hosting.Maui
// 13.5.3-preview.1 launches Android with -p:NoBuild=true and Android's Run target depends
// on Install, which invokes Build. Starting it is still useful: it performs the pre-build
// and writes the environment targets file carrying the OTLP and service discovery values.
// This command then launches the app with the identical command minus that one flag.
// See scripts/run-android.ps1. Remove both once the integration is fixed upstream.
var runAndroidScript = Path.GetFullPath(
    Path.Combine(builder.AppHostDirectory, "..", "scripts", "run-android.ps1"));

androidEmulator.WithCommand(
    name: "run-android",
    displayName: "▶ Run on Android",
    executeCommand: async context =>
    {
        var startInfo = new ProcessStartInfo("powershell")
        {
            // Repo root, not scripts/: the Aspire CLI locates the AppHost from the current
            // directory and does not scan upward.
            WorkingDirectory = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..")),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(runAndroidScript);

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return CommandResults.Failure($"Could not start {runAndroidScript}.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(context.CancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(context.CancellationToken);
        await process.WaitForExitAsync(context.CancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode == 0)
        {
            return CommandResults.Success();
        }

        // The dashboard shows only this string - there is no "build output above" for the
        // user to scroll to. Surface the actual compiler/MSBuild errors from stdout; an
        // earlier version returned stderr alone, which held nothing but PowerShell's
        // Write-Error wrapper and hid the real cause entirely.
        return CommandResults.Failure(SummariseFailure(stdout, stderr, process.ExitCode));

        static string SummariseFailure(string stdout, string stderr, int exitCode)
        {
            var combined = string.Join(
                Environment.NewLine,
                new[] { stdout, stderr }.Where(s => !string.IsNullOrWhiteSpace(s)));

            var lines = combined.Split('\n')
                .Select(l => l.TrimEnd('\r'))
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToArray();

            // Prefer real diagnostics over PowerShell's own error decoration.
            var diagnostics = lines
                .Where(l => l.Contains(": error ", StringComparison.OrdinalIgnoreCase)
                         || l.Contains("error XA", StringComparison.OrdinalIgnoreCase)
                         || l.Contains("error APT", StringComparison.OrdinalIgnoreCase)
                         || l.StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .Take(6)
                .ToArray();

            var detail = diagnostics.Length > 0
                ? string.Join(Environment.NewLine, diagnostics)
                : string.Join(Environment.NewLine, lines.TakeLast(15));

            return $"run-android.ps1 exited with code {exitCode}.{Environment.NewLine}{Environment.NewLine}{detail}"
                 + $"{Environment.NewLine}{Environment.NewLine}Run .\\scripts\\run-android.ps1 in a terminal for the full output.";
        }
    },
    commandOptions: new CommandOptions
    {
        Description = "Builds and launches the MAUI app on the Android emulator. Use this "
                    + "instead of Start: starting this resource directly always fails with "
                    + "NETSDK1085. This does the whole thing in one step.",
        IconName = "Play",
        // The resource is usually in a failed state when you want this, so keep it
        // clickable regardless of resource state.
        UpdateState = _ => ResourceCommandState.Enabled,
    });

builder.Build().Run();
