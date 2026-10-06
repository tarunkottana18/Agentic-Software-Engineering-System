using System.Reflection;

namespace UrlShortener.Agents.Runtime;

public interface IAgentPromptCatalog
{
    string GetPrompt(string fileName);
}

public sealed class EmbeddedAgentPromptCatalog : IAgentPromptCatalog
{
    private readonly Assembly _assembly;
    private readonly string[] _resourceNames;

    public EmbeddedAgentPromptCatalog()
    {
        _assembly = typeof(EmbeddedAgentPromptCatalog).Assembly;
        _resourceNames = _assembly.GetManifestResourceNames();
    }

    public string GetPrompt(string fileName)
    {
        var suffix = $".Agents.Prompts.{fileName}";
        var resourceName = _resourceNames.SingleOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
        {
            throw new InvalidOperationException($"Agent prompt '{fileName}' is not embedded in the Infrastructure assembly.");
        }

        using var stream = _assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Agent prompt '{fileName}' could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
