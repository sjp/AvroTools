using System;

namespace SJP.Avro.Tools.Idl
{
    internal struct IdlKeyword
    {
        public IdlKeyword(string keyword, IdlToken token)
        {
            if (keyword.IsNullOrWhiteSpace())
                throw new ArgumentNullException(nameof(keyword));
           // if (!token.IsValid())
             //   throw new ArgumentException($"The { nameof(IdlToken) } provided must be a valid enum.", nameof(token));

            Text = keyword;
            Token = token;
        }

        public string Text { get; }

        public IdlToken Token { get; }
    }
}
