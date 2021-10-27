using System;

namespace SJP.Avro.Tools.Idl
{
    internal struct IdlKeyword
    {
        public IdlKeyword(string keyword, IdlToken token)
        {
            if (keyword.IsNullOrWhiteSpace())
                throw new ArgumentNullException(nameof(keyword));

            Text = keyword;
            Token = token;
        }

        public string Text { get; }

        public IdlToken Token { get; }
    }
}
