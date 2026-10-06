using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using UrlShortener.Application.Workflows;

namespace UrlShortener.Infrastructure.WorkflowArtifacts;

public sealed class FileWorkspaceChangeTracker(IConfiguration configuration) : IWorkspaceChangeTracker
{
    private const int MaximumWorkspaceFiles = 10000;
    private readonly string _root = WorkflowPaths.Resolve(
        configuration["WorkflowGovernance:WorkspaceRoot"], WorkflowPaths.RepositoryRoot);
    private static readonly string[] TrackedDirectories = ["src", "tests", Path.Combine("generated", "greenfield")];

    public async Task<IReadOnlyList<WorkspaceFileSnapshot>> CaptureAsync(CancellationToken cancellationToken)
    {
        EnsureSafeRoot();
        var files = EnumerateFiles(cancellationToken);
        var snapshots = new List<WorkspaceFileSnapshot>(files.Count);
        foreach (var (path, absolutePath) in files)
        {
            await using var stream = new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken);
            snapshots.Add(new WorkspaceFileSnapshot(path, Convert.ToHexString(hash)));
        }

        return snapshots;
    }

    public async Task<IReadOnlyList<WorkspaceFileChange>> FindChangesAsync(
        IReadOnlyList<WorkspaceFileSnapshot> baseline,
        CancellationToken cancellationToken)
    {
        var current = await CaptureAsync(cancellationToken);
        var beforeByPath = baseline.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        var afterByPath = current.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        return beforeByPath.Keys.Union(afterByPath.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Where(path => !beforeByPath.TryGetValue(path, out var before)
                || !afterByPath.TryGetValue(path, out var after)
                || !StringComparer.Ordinal.Equals(before.Sha256, after.Sha256))
            .Select(path => new WorkspaceFileChange(
                path,
                beforeByPath.TryGetValue(path, out var before) ? before.Sha256 : null,
                afterByPath.TryGetValue(path, out var after) ? after.Sha256 : null))
            .ToArray();
    }

    private List<(string Path, string AbsolutePath)> EnumerateFiles(CancellationToken cancellationToken)
    {
        var files = new List<(string Path, string AbsolutePath)>();
        foreach (var trackedDirectory in TrackedDirectories)
        {
            var directory = Path.Combine(_root, trackedDirectory);
            if (!Directory.Exists(directory))
            {
                continue;
            }
            if (IsReparsePoint(directory))
            {
                throw new InvalidOperationException($"Workspace verification does not follow symbolic links: '{directory}'.");
            }

            var pending = new Stack<string>();
            pending.Push(directory);
            while (pending.TryPop(out var currentDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var entry in Directory.EnumerateFileSystemEntries(currentDirectory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (IsReparsePoint(entry))
                    {
                        var name = Path.GetFileName(entry);
                        if (name is "bin" or "obj" or ".vs" or "node_modules")
                        {
                            continue;
                        }

                        throw new InvalidOperationException($"Workspace verification does not follow symbolic links: '{entry}'.");
                    }

                    if (Directory.Exists(entry))
                    {
                        var name = Path.GetFileName(entry);
                        if (name is "bin" or "obj" or ".vs" or "node_modules")
                        {
                            continue;
                        }

                        pending.Push(entry);
                        continue;
                    }

                    var relativePath = Path.GetRelativePath(_root, entry).Replace('\\', '/');
                    if (IsDatabaseArtifact(Path.GetFileName(entry)))
                    {
                        continue;
                    }

                    files.Add((relativePath, entry));
                    if (files.Count > MaximumWorkspaceFiles)
                    {
                        throw new InvalidOperationException($"Workspace verification is limited to {MaximumWorkspaceFiles} files.");
                    }
                }
            }
        }

        return files.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static bool IsDatabaseArtifact(string fileName) =>
        fileName.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".db-shm", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".db-wal", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".db-journal", StringComparison.OrdinalIgnoreCase);

    private void EnsureSafeRoot()
    {
        for (var directory = new DirectoryInfo(_root); directory is not null; directory = directory.Parent)
        {
            if (directory.Exists && IsReparsePoint(directory.FullName))
            {
                throw new InvalidOperationException($"Workspace verification root cannot contain a symbolic link: '{directory.FullName}'.");
            }
        }
    }
}