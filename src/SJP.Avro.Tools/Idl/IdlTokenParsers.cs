using System;
using System.Collections.Generic;
using System.Linq;
using SJP.Avro.Tools.Idl.Model;
using Superpower;
using Superpower.Model;
using Superpower.Parsers;

namespace SJP.Avro.Tools.Idl;

/// <summary>
/// A set of parser combinators used to construct Avro IDL definitions.
/// </summary>
public static class IdlTokenParsers
{
    private static TokenListParser<IdlToken, int> IntNumber =>
        Token.EqualTo(IdlToken.Number)
            .Where(t => int.TryParse(t.ToStringValue(), out _))
            .Select(t => int.Parse(t.ToStringValue()));

    private static TokenListParser<IdlToken, AvroType> DecimalType =>
        Property.Many()
            .Then(p =>
                 Token.Sequence(IdlToken.Decimal, IdlToken.LParen)
                    .IgnoreThen(IntNumber
                    .Then(precision =>
                        Token.EqualTo(IdlToken.Comma)
                            .IgnoreThen(IntNumber)
                            .Select(scale => (precision, scale)))
                    .Then(parameters =>
                        Token.EqualTo(IdlToken.RParen).Select(_ => new DecimalType(parameters.precision, parameters.scale, p) as AvroType))));

    private static TokenListParser<IdlToken, AvroType> SimpleLogicalType =>
        Property.Many()
            .Then(p => Token.EqualTo(IdlToken.Date)
            .Or(Token.EqualTo(IdlToken.Duration))
            .Or(Token.EqualTo(IdlToken.TimeMs))
            .Or(Token.EqualTo(IdlToken.TimeMicros))
            .Or(Token.EqualTo(IdlToken.TimestampMs))
            .Or(Token.EqualTo(IdlToken.TimestampMicros))
            .Or(Token.EqualTo(IdlToken.LocalTimestampMs))
            .Or(Token.EqualTo(IdlToken.LocalTimestampMicros))
            .Or(Token.EqualTo(IdlToken.Uuid))
            .Select(_ => new LogicalType(_.ToStringValue(), p) as AvroType));

    private static TokenListParser<IdlToken, AvroType> LogicalType =>
        DecimalType.Or(SimpleLogicalType);

    private static TokenListParser<IdlToken, AvroType> PrimitiveType =>
        Property.Many()
            .Then(p => Token.EqualTo(IdlToken.Boolean)
            .Or(Token.EqualTo(IdlToken.Bytes))
            .Or(Token.EqualTo(IdlToken.Int))
            .Or(Token.EqualTo(IdlToken.String))
            .Or(Token.EqualTo(IdlToken.Float))
            .Or(Token.EqualTo(IdlToken.Double))
            .Or(Token.EqualTo(IdlToken.Long))
            .Or(Token.EqualTo(IdlToken.Null))
            .Or(Token.EqualTo(IdlToken.Void))
            .Select(_ => new PrimitiveType(_.ToStringValue(), p) as AvroType));

    private static TokenListParser<IdlToken, AvroType> ReferenceType =>
        Property.Many()
            .Then(p => Identifier
                .Select(name => new ReferenceType(name, p) as AvroType));

    private static TokenListParser<IdlToken, AvroType> ArrayType =>
        Property.Many()
            .Then(p =>
                Token.Sequence(IdlToken.Array, IdlToken.LessThan)
                    .IgnoreThen(Parse.Ref(() => AvroType))
                    .Then(t =>
                        Token.EqualTo(IdlToken.GreaterThan).Select(_ => new ArrayType(t, p) as AvroType)));

    private static TokenListParser<IdlToken, AvroType> MapType =>
        Property.Many()
            .Then(p =>
                Token.Sequence(IdlToken.Map, IdlToken.LessThan)
                    .IgnoreThen(Parse.Ref(() => AvroType))
                    .Then(t =>
                        Token.EqualTo(IdlToken.GreaterThan).Select(_ => new MapType(t, p) as AvroType)));

    private static TokenListParser<IdlToken, AvroType> UnionType =>
        Property.Many()
            .Then(p =>
                 Token.Sequence(IdlToken.Union, IdlToken.LBrace)
                    .IgnoreThen(Parse.Ref(() => AvroType).AtLeastOnceDelimitedBy(Token.EqualTo(IdlToken.Comma)))
                    .Then(t => Token.EqualTo(IdlToken.RBrace).Select(_ => new UnionDefinition(t, p) as AvroType)));

    private static TokenListParser<IdlToken, Token<IdlToken>> ExpressionContent =>
        new[] { IdlToken.LParen, IdlToken.RParen }.NotEqualTo();

    private static TokenListParser<IdlToken, Token<IdlToken>> FieldDefaultValueContent =>
        new[] { IdlToken.LParen, IdlToken.RParen, IdlToken.Semicolon }.NotEqualTo();

    private static TokenListParser<IdlToken, Model.Identifier> Identifier =>
        Token.EqualTo(IdlToken.Identifier).Select(name => new Model.Identifier(name.ToStringValue()));

    private static TokenListParser<IdlToken, Property> Property =>
        Token.EqualTo(IdlToken.PropertyName)
            .Then(name => Token.EqualTo(IdlToken.LParen).Select(_ => name.ToStringValue()))
            .Then(name => ExpressionContent.AtLeastOnce().Select(content => (name, content)))
            .Then(p => Token.EqualTo(IdlToken.RParen).Select(_ => new Property(p.name, p.content)));

    private static TokenListParser<IdlToken, (DocComment? comment, AvroType type, IEnumerable<Property> props, Model.Identifier name)> ParameterTypeHeader =>
        DocComment
            .Then(c => AvroType.Select(t => (c, t)))
            .Then(prefix => Property.Many().Select(p => (prefix.c, prefix.t, p)))
            .Then(prefix =>
                Identifier
                    .Select(name => (comment: prefix.c, type: prefix.t, props: prefix.p as IEnumerable<Property>, name)));

    private static TokenListParser<IdlToken, FormalParameter> ParameterWithDefault =>
        ParameterTypeHeader
            .Then(header => Token.EqualTo(IdlToken.Equals).Select(_ => header))
            .Then(header => ExpressionContent.Many()
                .Select(dv => new FormalParameter(
                    header.type,
                    header.name,
                    dv
                )));

    private static TokenListParser<IdlToken, FormalParameter> Parameter =>
        ParameterWithDefault.Try()
            .Or(ParameterTypeHeader.Select(p => new FormalParameter(p.type, p.name, Array.Empty<Token<IdlToken>>())));

    private static TokenListParser<IdlToken, Model.Identifier[]> ErrorList =>
        Token.EqualTo(IdlToken.Throws)
            .IgnoreThen(
                Identifier
                    .ManyDelimitedBy(Token.EqualTo(IdlToken.Comma)));

    private static TokenListParser<IdlToken, bool> OneWay =>
        Token.EqualTo(IdlToken.Oneway)
            .Select(_ => true)
            .OptionalOrDefault(false);

    private static TokenListParser<IdlToken, (bool oneway, Model.Identifier[] errors)> MessageSuffix =>
        OneWay
            .Then(ow =>
                ErrorList.OptionalOrDefault(Array.Empty<Model.Identifier>())
                    .Select(err => (ow, err)));

    private static TokenListParser<IdlToken, EnumDeclaration> SimpleEnumDeclaration =>
        DocComment
            .Then(c => Property.Many().Select(p => (c, p)))
            .Then(prefix =>
                Token.EqualTo(IdlToken.Enum)
                    .IgnoreThen(Identifier)
                    .Then(name =>
                        Token.EqualTo(IdlToken.LBrace)
                            .IgnoreThen(
                                Identifier
                                    .ManyDelimitedBy(Token.EqualTo(IdlToken.Comma))
                                    .Select(members => (name, members))))
                    .Then(result =>
                        Token.EqualTo(IdlToken.RBrace)
                            .Select(_ => new EnumDeclaration(
                                prefix.c,
                                prefix.p,
                                _.Span.Position.Absolute,
                                result.name,
                                result.members
                    ))));

    private static TokenListParser<IdlToken, Model.Identifier> EnumDeclarationDefault =>
        Token.EqualTo(IdlToken.Equals)
            .IgnoreThen(Identifier)
            .Then(def => Token.EqualTo(IdlToken.Semicolon).Select(_ => def));

    private static TokenListParser<IdlToken, EnumDeclaration> EnumDeclarationWithDefault =>
        SimpleEnumDeclaration
            .Then(dec => EnumDeclarationDefault
                .Select(def => new EnumDeclaration(
                    dec.Comment,
                    dec.Properties,
                    dec.Position,
                    dec.Name,
                    dec.Members,
                    def
                )));

    private static TokenListParser<IdlToken, EnumDeclaration> EnumDeclaration =>
        EnumDeclarationWithDefault.Try().Or(SimpleEnumDeclaration);

    private static TokenListParser<IdlToken, FixedDeclaration> FixedDeclaration =>
        DocComment
            .Then(c => Property.Many().Select(p => (c, p)))
            .Then(prefix =>
                Token.EqualTo(IdlToken.Fixed)
                    .IgnoreThen(Identifier)
                    .Then(name =>
                        Token.EqualTo(IdlToken.LParen)
                            .IgnoreThen(Token.EqualTo(IdlToken.Number))
                            .Then(size =>
                                Token.EqualTo(IdlToken.RParen)
                                    .Select(_ => new FixedDeclaration(
                                        prefix.c,
                                        prefix.p,
                                        _.Position.Absolute,
                                        name,
                                        int.Parse(size.ToStringValue())))
                                    )))
            .Then(dec => Token.EqualTo(IdlToken.Semicolon).Select(_ => dec));

    private static TokenListParser<IdlToken, (DocComment? comment, IEnumerable<Property> props, AvroType type, Model.Identifier name)> FieldHeader =>
        DocComment
            .Then(c => AvroType.Select(t => (c, t)))
            .Then(prefix => Property.Many().Select(p => (prefix.c, p, prefix.t)))
            .Then(prefix =>
                Identifier
                    .Select(name => (comment: prefix.c, props: prefix.p as IEnumerable<Property>, type: prefix.t, name)));

    private static TokenListParser<IdlToken, IEnumerable<Token<IdlToken>>> FieldDefaultValue =>
        Token.EqualTo(IdlToken.Equals)
            .IgnoreThen(FieldDefaultValueContent.Many().Select(_ => _ as IEnumerable<Token<IdlToken>>))
            .OptionalOrDefault(Enumerable.Empty<Token<IdlToken>>());

    private static TokenListParser<IdlToken, FieldDeclaration> FieldDeclaration =>
        FieldHeader
            .Then(header => FieldDefaultValue.Select(dv => (header, dv)))
            .Select(result => new FieldDeclaration(
                result.header.comment,
                result.header.props,
                result.header.type,
                result.header.name,
                result.dv
                ))
            .Then(f => Token.EqualTo(IdlToken.Semicolon).Select(_ => f));

    private static TokenListParser<IdlToken, RecordDeclaration> RecordDeclaration =>
        DocComment
            .Then(c => Property.Many().Select(p => (c, p)))
            .Then(prefix =>
                Token.EqualTo(IdlToken.Record)
                    .IgnoreThen(Identifier)
                    .Select(name => (comment: prefix.c, props: prefix.p, name)))
            .Then(prefix => Token.EqualTo(IdlToken.LBrace).Select(_ => prefix))
            .Then(prefix =>
                FieldDeclaration
                    .Many()
                    .Select(members => (prefix.comment, prefix.props, prefix.name, members)))
            .Then(result =>
                Token.EqualTo(IdlToken.RBrace)
                    .Select(_ => new RecordDeclaration(
                        result.comment,
                        result.props,
                        _.Position.Absolute,
                        result.name,
                        result.members
                    )));

    private static TokenListParser<IdlToken, ErrorDeclaration> ErrorDeclaration =>
        DocComment
            .Then(c => Property.Many().Select(p => (c, p)))
            .Then(prefix =>
                Token.EqualTo(IdlToken.Error)
                    .IgnoreThen(Identifier)
                    .Select(name => (comment: prefix.c, props: prefix.p, name)))
            .Then(prefix => Token.EqualTo(IdlToken.LBrace).Select(_ => prefix))
            .Then(prefix =>
                FieldDeclaration
                    .Many()
                    .Select(members => (prefix.comment, prefix.props, prefix.name, members)))
            .Then(result =>
                Token.EqualTo(IdlToken.RBrace)
                    .Select(_ => new ErrorDeclaration(
                        result.comment,
                        result.props,
                        _.Position.Absolute,
                        result.name,
                        result.members
                    )));

    private static TokenListParser<IdlToken, AvroType> AvroType =>
        PrimitiveType
            .Try().Or(LogicalType)
            .Try().Or(ReferenceType)
            .Try().Or(MapType)
            .Try().Or(UnionType)
            .Try().Or(ArrayType);

    private static TokenListParser<IdlToken, MessageDeclaration> Message =>
        DocComment
            .Then(docComments =>
                Property
                    .Many()
                    .Select(props => (
                        doc: docComments, props
                    ))
            )
            .Then(prefix =>
                AvroType
                    .Select(ret => (prefix.doc, prefix.props, returnType: ret)))
            .Then(prefix =>
                Identifier
                    .Select(name => (prefix.doc, prefix.props, prefix.returnType, name)))
            .Then(res =>
                Token.EqualTo(IdlToken.LParen)
                    .IgnoreThen(Parameter.ManyDelimitedBy(Token.EqualTo(IdlToken.Comma)))
                    .Then(parameters =>
                        Token.EqualTo(IdlToken.RParen)
                            .Select(_ => (
                                res.doc,
                                res.name,
                                res.returnType,
                                res.props,
                                position: _.Position.Absolute,
                                parameters
                            ))))
            .Then(m => MessageSuffix.Select(suffix => new MessageDeclaration(
                m.doc,
                m.name,
                m.returnType,
                m.props,
                m.position,
                m.parameters,
                suffix.oneway,
                suffix.errors
            )))
            .Then(result => Token.EqualTo(IdlToken.Semicolon).Select(_ => result));

    private static TokenListParser<IdlToken, ImportType> ImportType =>
        Token.EqualTo(IdlToken.Idl)
            .Or(Token.EqualTo(IdlToken.Schema))
            .Or(Token.EqualTo(IdlToken.Protocol))
            .Select(t => Enum.TryParse<ImportType>(t.ToStringValue(), true, out var importType)
                ? importType
                : Model.ImportType.Unknown);

    private static TokenListParser<IdlToken, ImportDeclaration> Import =>
        Token.EqualTo(IdlToken.Import)
            .IgnoreThen(ImportType)
            .Then(type =>
                Token.EqualTo(IdlToken.StringLiteral)
                    .Select(name => new ImportDeclaration(type, name.ToStringValue(), name.Position.Absolute)))
            .Then(result => Token.EqualTo(IdlToken.Semicolon).Select(_ => result));

    private static TokenListParser<IdlToken, NamedSchemaDeclaration> Declaration =>
        RecordDeclaration.Select(_ => _ as NamedSchemaDeclaration)
            .Try().Or(FixedDeclaration.Select(_ => _ as NamedSchemaDeclaration))
            .Try().Or(EnumDeclaration.Select(_ => _ as NamedSchemaDeclaration))
            .Try().Or(ErrorDeclaration.Select(_ => _ as NamedSchemaDeclaration))
            .Try().Or(Message.Select(_ => _ as NamedSchemaDeclaration))
            .Try().Or(Import.Select(_ => _ as NamedSchemaDeclaration));

    private static TokenListParser<IdlToken, DocComment?> DocComment =>
        Token.EqualTo(IdlToken.DocComment)
            .Many()
            .Select(docComments => docComments.LastOrDefault().HasValue
                ? new DocComment(docComments.LastOrDefault().ToStringValue())
                : (DocComment?)null);

    /// <summary>
    /// Parses a protocol from a sequence of tokens representing an IDL document.
    /// </summary>
    public static TokenListParser<IdlToken, Protocol> Protocol =>
        DocComment
            .Then(docComment =>
                Property.Many()
                    .Select(props => (
                        doc: docComment, props
                    ))
            )
            .Then(prefix =>
                Token.EqualTo(IdlToken.Protocol)
                    .IgnoreThen(Identifier)
                    .Select(name => (prefix.doc, prefix.props, name)))
            .Then(header =>
                Token.EqualTo(IdlToken.LBrace)
                    .IgnoreThen(Declaration.Many())
                    .Select(d => (header, declarations: d)))
            .Then(res => Token.EqualTo(IdlToken.RBrace).Select(_ =>
                new Protocol(
                    res.header.doc,
                    res.header.name,
                    res.header.props,
                    res.declarations.OfType<RecordDeclaration>().ToList(),
                    res.declarations.OfType<FixedDeclaration>().ToList(),
                    res.declarations.OfType<EnumDeclaration>().ToList(),
                    res.declarations.OfType<ErrorDeclaration>().ToList(),
                    res.declarations.OfType<ImportDeclaration>().ToList(),
                    res.declarations.OfType<MessageDeclaration>()
                        .Select(m => new KeyValuePair<Model.Identifier, MessageDeclaration>(m.Name, m))
                        .ToReadOnlyDictionary())
            ));
}