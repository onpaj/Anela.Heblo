namespace Anela.Heblo.Xcc;

public static class Check
{
    public static T NotNull<T>([System.Diagnostics.CodeAnalysis.NotNull] T? value, string parameterName)
    {
        return (object)value != null ? value : throw new ArgumentNullException(parameterName);
    }

    public static T NotNull<T>([System.Diagnostics.CodeAnalysis.NotNull] T? value, string parameterName, string message)
    {
        return (object)value != null ? value : throw new ArgumentNullException(parameterName, message);
    }
}