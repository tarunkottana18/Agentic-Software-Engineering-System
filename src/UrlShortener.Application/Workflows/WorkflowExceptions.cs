namespace UrlShortener.Application.Workflows;

public sealed class WorkflowNotFoundException(Guid id)
    : Exception($"Workflow '{id}' was not found.");

public sealed class WorkflowConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class WorkflowValidationException(string message)
    : Exception(message);