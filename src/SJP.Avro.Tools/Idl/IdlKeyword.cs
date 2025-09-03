using System;

namespace SJP.Avro.Tools.Idl;

internal readonly struct IdlKeyword
{
    public IdlKeyword(string keyword, IdlToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);

        Text = keyword;
        Token = token;
    }

    public string Text { get; }

    public IdlToken Token { get; }
}