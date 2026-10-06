using System.Text;
using Microsoft.Extensions.Configuration;
using UrlShortener.Application.Workflows;

namespace UrlShortener.Infrastructure.WorkflowArtifacts;

public sealed class FileWorkflowArtifactStore(IConfiguration configuration) : IWorkflowArtifactStore
{
    private readonly string _root = WorkflowPaths.Resolve(
        configuration["WorkflowGovernance:ArtifactRoot"], Path.Combine("artifacts", "workflows"));

    public async Task<string> WriteStageArtifactAsync(
        Guid workflowId,
        int planVersion,
        WorkflowStage stage,
        string output,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(_root, workflowId.ToString("N"), $"plan-{planVersion}");
        Directory.CreateDirectory(directory);
        var fileName = string.Concat(stage.Id.Select(character => char.IsLetterOrDigit(character) ? character : '-')) + ".md";
        var path = Path.Combine(directory, fileName);
        var content = new StringBuilder()
            .AppendLine($"# {stage.Name}")
            .AppendLine()
            .AppendLine($"- Workflow: `{workflowId}`")
            .AppendLine($"- Plan version: `{planVersion}`")
            .AppendLine($"- Category: `{stage.Category}`")
            .AppendLine()
            .AppendLine("## Output")
            .AppendLine()
            .AppendLine(output)
            .ToString();
        await File.WriteAllTextAsync(path, content, Encoding.UTF8, cancellationToken);
        return Path.GetRelativePath(WorkflowPaths.RepositoryRoot, path);
    }

    public Task DeletePlanArtifactsAsync(
        Guid workflowId,
        int planVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.Combine(_root, workflowId.ToString("N"), $"plan-{planVersion}");
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }
}