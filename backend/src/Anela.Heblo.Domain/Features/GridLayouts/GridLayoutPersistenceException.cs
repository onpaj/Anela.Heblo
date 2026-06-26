namespace Anela.Heblo.Domain.Features.GridLayouts;

public class GridLayoutPersistenceException : Exception
{
    public GridLayoutPersistenceException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
