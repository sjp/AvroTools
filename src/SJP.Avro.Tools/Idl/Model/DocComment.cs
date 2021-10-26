using System;
using System.Diagnostics;

namespace SJP.Avro.Tools.Idl.Model
{
    [DebuggerDisplay("{" + nameof(Value) + ",nq}")]
    public class DocComment
    {
        private readonly string _commentValue;

        public DocComment(string comment)
        {
            if (comment.IsNullOrWhiteSpace())
                throw new ArgumentNullException(nameof(comment));

            _commentValue = comment;
        }

        public string Value => _commentValue[3..^2].Trim(); // trim off starting '/**' and trailing '*/'
    }
}
