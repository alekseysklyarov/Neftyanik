namespace Neftyanik.Portal.Application.Associations;

public sealed class AssociationIsolationException : InvalidOperationException
{
    public AssociationIsolationException(string message) : base(message)
    {
    }
}
