namespace Anela.Heblo.Xcc.Domain;

public interface IEntity<T>
{
    /// <summary>
    /// Gets or sets the unique identifier for the entity
    /// </summary>
    T Id { get; }
}