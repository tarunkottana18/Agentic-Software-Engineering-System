namespace UrlShortener.Core.Exceptions
{
    public sealed class WorkflowRevisionConflictException : Exception
    {
        public WorkflowRevisionConflictException(Exception? innerException = null)
            : base("The workflow was updated by another request.", innerException)
        {
        }
    }
}
