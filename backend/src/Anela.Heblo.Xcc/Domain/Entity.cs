namespace Anela.Heblo.Xcc.Domain;

public class Entity<T> : IEntity<T>
{
    public T Id { get; set; }
}