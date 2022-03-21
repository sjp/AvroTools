namespace SJP.Avro.Tools;

/// <summary>
/// Convenience extension methods for working with <see cref="char"/> objects.
/// </summary>
public static class CharExtensions
{
    /// <summary>
    /// Indicates whether the specified Unicode character is categorized as a decimal digit.
    /// </summary>
    /// <param name="c">The Unicode character to evaluate.</param>
    /// <returns><c>true</c> if the specified <paramref name="c"/> is a decimal digit; otherwise, <c>false</c>.</returns>
    public static bool IsDigit(this char c) => char.IsDigit(c);

    /// <summary>
    /// Indicates whether a Unicode character is categorized as a Unicode letter.
    /// </summary>
    /// <param name="c">The Unicode character to evaluate.</param>
    /// <returns><c>true</c> if the specified <paramref name="c"/> is a letter; otherwise, <c>false</c>.</returns>
    public static bool IsLetter(this char c) => char.IsLetter(c);

    /// <summary>
    /// Indicates whether a Unicode character is categorized as a letter or a decimal digit.
    /// </summary>
    /// <param name="c">The Unicode character to evaluate.</param>
    /// <returns><c>true</c> if <paramref name="c"/> is a letter or a decimal digit; otherwise, <c>false</c>.</returns>
    public static bool IsLetterOrDigit(this char c) => char.IsLetterOrDigit(c);
}