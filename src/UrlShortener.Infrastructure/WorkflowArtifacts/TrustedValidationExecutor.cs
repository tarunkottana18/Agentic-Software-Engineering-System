using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using UrlShortener.Application.Workflows;

namespace UrlShortener.Infrastructure.WorkflowArtifacts;

public sealed class TrustedValidationExecutor(IConfiguration configuration) : ITrustedValidationExecutor
{
    private readonly string _workspaceRoot = WorkflowPaths.Resolve(
        configuration["WorkflowGovernance:TrustedWorkspaceRoot"], ".");

    public bool IsEnabled => bool.TryParse(configuration["WorkflowGovernance:AllowTrustedCommands"], out var enabled) && enabled;

    public async Task<WorkflowStageExecutionResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return new WorkflowStageExecutionResult(
                true,
                "Source: operator-required-validation\nTrusted dotnet execution is disabled by configuration. Submit verified build/test evidence manually.");
        }

        var build = await RunAsync("build", cancellationToken);
        if (!build.Succeeded)
        {
            return build;
        }

        var test = await RunAsync("test", cancellationToken);
        return new WorkflowStageExecutionResult(
            test.Succeeded,
            $"Source: trusted-local-executor\nBuild:\n{build.Output}\nTests:\n{test.Output}",
            test.FailureReason);
    }

    private async Task<WorkflowStageExecutionResult> RunAsync(string command, CancellationToken cancellationToken)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"{command} --no-restore",
            WorkingDirectory = _workspaceRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Unable to start the dotnet CLI.");

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var combined = string.Join(Environment.NewLine, new[] { output.Trim(), error.Trim() }.Where(value => value.Length > 0));
        return process.ExitCode == 0
            ? new WorkflowStageExecutionResult(true, combined)
            : new WorkflowStageExecutionResult(false, combined, $"dotnet {command} exited with code {process.ExitCode}.");
    }
}