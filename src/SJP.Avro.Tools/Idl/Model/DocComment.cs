using System;

namespace SJP.Avro.Tools.Idl.Model;

/// <summary>
/// Describes a comment that should be used to document an associated type, field, message, or message parameter.
/// </summary>
public record DocComment
{
    private const string CommentPrefix = "/**";
    private const string CommentSuffix = "*/";

    /// <summary>
    /// Constructs a comment used for documentation purposes.
    /// </summary>
    /// <param name="comment">A documentation-level comment.</param>
    /// <exception cref="ArgumentNullException"><paramref name="comment"/> is <c>null</c>, empty or whitespace.</exception>
    /// <exception cref="ArgumentException"><paramref name="comment"/> does not start with '/**' or end with '*/'.</exception>
    public DocComment(string comment)
    {
        if (comment.IsNullOrWhiteSpace())
            throw new ArgumentNullException(nameof(comment));
        if (!comment.StartsWith(CommentPrefix) || !comment.EndsWith(CommentSuffix))
            throw new ArgumentException($"A doccomment must start with '{ CommentPrefix }' and end with '{ CommentSuffix }', given: { comment }", nameof(comment));

        Value = TrimCommentSyntax(comment);
    }

    /// <summary>
    /// The value of the comment with the comment prefix and suffixes removed.
    /// </summary>
    public string Value { get; }

    private static string TrimCommentSyntax(string comment)
        => comment[CommentPrefix.Length..^CommentSuffix.Length].Trim();
}
