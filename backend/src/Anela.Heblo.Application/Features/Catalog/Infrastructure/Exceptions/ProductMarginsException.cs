namespace Anela.Heblo.Application.Features.Catalog.Infrastructure.Exceptions;

public class ProductMarginsException : Exception
{
    public ProductMarginsException(string message) : base(message) { }
    public ProductMarginsException(string message, Exception innerException) : base(message, innerException) { }
}

public class DataAccessException : ProductMarginsException
{
    public DataAccessException(string message) : base(message) { }
    public DataAccessException(string message, Exception innerException) : base(message, innerException) { }
}

public class MarginCalculationException : ProductMarginsException
{
    public MarginCalculationException(string message) : base(message) { }
    public MarginCalculationException(string message, Exception innerException) : base(message, innerException) { }
}