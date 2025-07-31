namespace Anela.Heblo.Xcc;

public static class StringExtensions
{
  /// <summary>
  /// Gets a substring of a string from beginning of the string.
  /// </summary>
  /// <exception cref="T:System.ArgumentNullException">Thrown if <paramref name="str" /> is null</exception>
  /// <exception cref="T:System.ArgumentException">Thrown if <paramref name="len" /> is bigger that string's length</exception>
  public static string Left(this string str, int len)
  {
    Check.NotNull<string>(str, nameof (str));
    if (str.Length < len)
      throw new ArgumentException("len argument can not be bigger than given string's length!");
    return str.Substring(0, len);
  }


  /// <summary>Gets a substring of a string from end of the string.</summary>
  /// <exception cref="T:System.ArgumentNullException">Thrown if <paramref name="str" /> is null</exception>
  /// <exception cref="T:System.ArgumentException">Thrown if <paramref name="len" /> is bigger that string's length</exception>
  public static string Right(this string str, int len)
  {
    Check.NotNull<string>(str, nameof (str));
    if (str.Length < len)
      throw new ArgumentException("len argument can not be bigger than given string's length!");
    return str.Substring(str.Length - len, len);
  }
}