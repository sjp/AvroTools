using Superpower.Display;
using Superpower.Parsers;

namespace SJP.Avro.Tools.Idl
{
    /// <summary>
    /// A token used to capture information from SQL as known to SQLite.
    /// </summary>
    public enum IdlToken
    {
        /// <summary>
        /// A none/unknown token value.
        /// </summary>
        None,

        /// <summary>
        /// An identifier.
        /// </summary>
        Identifier,

        /// <summary>
        /// A decimal number.
        /// </summary>
        Number,

        /// <summary>
        /// A left parenthesis.
        /// </summary>
        [Token(Example = "(")]
        LParen,

        /// <summary>
        /// A right parenthesis.
        /// </summary>
        [Token(Example = ")")]
        RParen,

        /// <summary>
        /// A left brace symbol.
        /// </summary>
        [Token(Example = "{")]
        LBrace,

        /// <summary>
        /// A right brace symbol.
        /// </summary>
        [Token(Example = "}")]
        RBrace,

        /// <summary>
        /// A left square bracket symbol.
        /// </summary>
        [Token(Example = "[")]
        LBracket,

        /// <summary>
        /// A right square bracket symbol.
        /// </summary>
        [Token(Example = "]")]
        RBracket,

        /// <summary>
        /// The Colon symbol.
        /// </summary>
        [Token(Example = ":")]
        Colon,

        /// <summary>
        /// The semicolon symbol.
        /// </summary>
        [Token(Example = ";")]
        Semicolon,

        /// <summary>
        /// The comma symbol.
        /// </summary>
        [Token(Example = ",")]
        Comma,

        /// <summary>
        /// The @/'at' symbol.
        /// </summary>
        [Token(Example = "@")]
        At,

        /// <summary>
        /// The dot/period symbol.
        /// </summary>
        [Token(Example = ".")]
        Dot,

        /// <summary>
        /// The equals symbol.
        /// </summary>
        [Token(Example = "=")]
        Equals,

        /// <summary>
        /// The dash symbol.
        /// </summary>
        [Token(Example = "-")]
        Dash,

        /// <summary>
        /// The <c>&lt;</c> (less than) operator.
        /// </summary>
        [Token(Category = "operator", Example = "<")]
        LessThan,

        /// <summary>
        /// The <c>&gt;</c> (greater than) operator.
        /// </summary>
        [Token(Category = "operator", Example = ">")]
        GreaterThan,

        /// <summary>
        /// The backtick symbol.
        /// </summary>
        [Token(Example = "`")]
        Backtick,








        /// <summary>
        /// A doc comment.
        /// </summary>
        [Token(Description = "doc comment literal")]
        DocComment,



        /// <summary>
        /// A comment.
        /// </summary>
        [Token(Description = "comment literal")]
        Comment,



        /// <summary>
        /// A string literal.
        /// </summary>
        [Token(Description = "string literal")]
        StringLiteral,


        /// <summary>
        /// A property name.
        /// </summary>
        [Token(Description = "property name")]
        PropertyName,

        /// <summary>
        /// The <c>array</c> keyword.
        /// </summary>
        [Token(Category = "keyword", Example = "array")]
        Array,

        /// <summary>
        /// The <c>boolean</c> keyword.
        /// </summary>
        [Token(Category = "keyword", Example = "boolean")]
        Boolean,

        /// <summary>
        /// The <c>double</c> keyword.
        /// </summary>
        [Token(Category = "keyword", Example = "double")]
        Double,

        /// <summary>
        /// The <c>duration</c> keyword.
        /// </summary>
        [Token(Category = "keyword", Example = "duration")]
        Duration,

        /// <summary>
        /// The <c>enum</c> keyword.
        /// </summary>
        [Token(Category = "keyword", Example = "enum")]
        Enum,

        /// <summary>
        /// The <c>error</c> keyword.
        /// </summary>
        [Token(Category = "keyword", Example = "error")]
        Error,

        /// <summary>
        /// The <c>false</c> keyword.
        /// </summary>
        [Token(Category = "keyword", Example = "false")]
        False,

        /// <summary>
        /// The <c>fixed</c> keyword.
        /// </summary>
        [Token(Category = "keyword", Example = "fixed")]
        Fixed,

        /// <summary>
        /// The <c>float</c> keyword.
        /// </summary>
        [Token(Category = "keyword", Example = "float")]
        Float,

        /// <summary>
        /// The <c>idl</c> keyword.
        /// </summary>
        [Token(Category = "keyword", Example = "idl")]
        Idl,

        /// <summary>
        /// The <c>import</c> keyword.
        /// </summary>
        [Token(Category = "keyword", Example = "import")]
        Import,

        /// <summary>
        /// The <c>int</c> keyword.
        /// </summary>
        [Token(Category = "keyword", Example = "int")]
        Int,

        /// <summary>
        /// The <c>long</c> keyword.
        /// </summary>
        [Token(Category = "keyword", Example = "long")]
        Long,

        /// <summary>
        /// The <c>map</c> keyword.
        /// </summary>
        [Token(Category = "keyword", Example = "map")]
        Map,

        /// <summary>
        /// The <c>oneway</c> keyword.
        /// </summary>
        [Token(Category = "keyword", Example = "oneway")]
        Oneway,

        /// <summary>
        /// The <c>bytes</c> keyword.
        /// </summary>
        [Token(Category = "keyword", Example = "bytes")]
        Bytes,

        /// <summary>
        /// The <c>schema</c> keyword.
        /// </summary>
        [Token(Category = "keyword", Example = "schema")]
        Schema,

        /// <summary>
        /// The <c>string</c> keyword.
        /// </summary>
        [Token(Category = "keyword", Example = "string")]
        String,

        /// <summary>
        /// The <c>null</c> keyword.
        /// </summary>
        [Token(Category = "keyword", Example = "null")]
        Null,

        /// <summary>
        /// The <c>protocol</c> keyword.
        /// </summary>
        [Token(Category = "keyword", Example = "protocol")]
        Protocol,

        /// <summary>
        /// The <c>record</c> keyword.
        /// </summary>
        [Token(Category = "keyword", Example = "record")]
        Record,

        /// <summary>
        /// The <c>throws</c> keyword.
        /// </summary>
        [Token(Category = "keyword", Example = "throws")]
        Throws,

        /// <summary>
        /// The <c>true</c> keyword.
        /// </summary>
        [Token(Category = "keyword", Example = "true")]
        True,

        /// <summary>
        /// The <c>union</c> keyword.
        /// </summary>
        [Token(Category = "keyword", Example = "union")]
        Union,

        /// <summary>
        /// The <c>void</c> keyword.
        /// </summary>
        [Token(Category = "keyword", Example = "void")]
        Void,

        /// <summary>
        /// The <c>date</c> keyword.
        /// </summary>
        [Token(Category = "keyword", Example = "date")]
        Date,

        /// <summary>
        /// The <c>time_ms</c> keyword.
        /// </summary>
        [Token(Category = "keyword", Example = "time_ms")]
        TimeMs,

        /// <summary>
        /// The <c>timestamp_ms</c> keyword.
        /// </summary>
        [Token(Category = "keyword", Example = "timestamp_ms")]
        TimestampMs,

        /// <summary>
        /// The <c>decimal</c> keyword.
        /// </summary>
        [Token(Category = "keyword", Example = "decimal")]
        Decimal,

        /// <summary>
        /// The <c>local_timestamp_ms</c> keyword.
        /// </summary>
        [Token(Category = "keyword", Example = "local_timestamp_ms")]
        LocalTimestampMs,

        /// <summary>
        /// The <c>uuid</c> keyword.
        /// </summary>
        [Token(Category = "keyword", Example = "uuid")]
        Uuid
    }
}
