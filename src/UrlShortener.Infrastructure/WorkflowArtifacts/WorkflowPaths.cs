namespace UrlShortener.Infrastructure.WorkflowArtifacts;

internal static class WorkflowPaths
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    // Relative paths resolve from the repository root so output does not depend on the launch directory.
    public static string Resolve(string? configured, string fallback)
    {
        var path = string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        return Path.GetFullPath(path, RepositoryRoot);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            if (directory.EnumerateFiles("*.sln").Any() || directory.EnumerateFiles("*.slnx").Any())
            {
                return directory.FullName;
            }
        }

        return Directory.GetCurrentDirectory();
    }
}
