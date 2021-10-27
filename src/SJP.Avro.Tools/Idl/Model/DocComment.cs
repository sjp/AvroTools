using System;

namespace SJP.Avro.Tools.Idl.Model
{
    public record DocComment
    {
        private const string CommentPrefix = "/**";
        private const string CommentSuffix = "*/";

        public DocComment(string comment)
        {
            if (comment.IsNullOrWhiteSpace())
                throw new ArgumentNullException(nameof(comment));
            if (!comment.StartsWith(CommentPrefix) || !comment.EndsWith(CommentSuffix))
                throw new ArgumentException($"A doccomment must start with '{ CommentPrefix }' and end with '{ CommentSuffix }', given: { comment }", nameof(comment));

            Value = TrimCommentSyntax(comment);
        }

        public string Value { get; }

        private static string TrimCommentSyntax(string comment)
            => comment[CommentPrefix.Length..^CommentSuffix.Length].Trim();
    }
}
