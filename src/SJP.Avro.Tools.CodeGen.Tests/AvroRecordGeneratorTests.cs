using System.Linq;
using Avro;
using NUnit.Framework;

namespace SJP.Avro.Tools.CodeGen.Tests;

[TestFixture]
internal static class AvroRecordGeneratorTests
{
    private const string TestNamespace = "Test.Avro.Namespace";

    [Test]
    public static void Generate_GivenNullSchema_ThrowsArgumentNullException()
    {
        var recordGenerator = new AvroRecordGenerator();

        Assert.That(() => recordGenerator.Generate(default!, TestNamespace), Throws.ArgumentNullException);
    }

    [Test]
    public static void Generate_GivenNullBaseNamespace_ThrowsArgumentNullException()
    {
        var recordGenerator = new AvroRecordGenerator();

        var protocol = Protocol.Parse("""
{
  "protocol" : "Baseball",
  "namespace" : "avro.examples.baseball",
  "doc" : "* Licensed to the Apache Software Foundation (ASF) under one\r\n * or more contributor license agreements.  See the NOTICE file\r\n * distributed with this work for additional information\r\n * regarding copyright ownership.  The ASF licenses this file\r\n * to you under the Apache License, Version 2.0 (the\r\n * \"License\"); you may not use this file except in compliance\r\n * with the License.  You may obtain a copy of the License at\r\n *\r\n *     https://www.apache.org/licenses/LICENSE-2.0\r\n *\r\n * Unless required by applicable law or agreed to in writing, software\r\n * distributed under the License is distributed on an \"AS IS\" BASIS,\r\n * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.\r\n * See the License for the specific language governing permissions and\r\n * limitations under the License.",
  "types" : [ {
    "type" : "enum",
    "name" : "Position",
    "symbols" : [ "P", "C", "B1", "B2", "B3", "SS", "LF", "CF", "RF", "DH" ]
  }, {
    "type" : "record",
    "name" : "Player",
    "fields" : [ {
      "name" : "number",
      "type" : "int"
    }, {
      "name" : "first_name",
      "type" : "string"
    }, {
      "name" : "middle_name",
      "doc": "wololololo",
      "type": [ "null", "string" ]
    }, {
      "name" : "last_name",
      "type" : "string"
    }, {
      "name" : "test_num", "type": {
      "type" : "bytes",
      "logicalType": "decimal",
      "precision": 18,
      "scale": 5
    }}, {
      "name" : "position",
      "type" : {
        "type" : "array",
        "items" : "Position"
      }}, {
      "name" : "positionLookup",
      "type" : {
        "type" : "map",
        "values" : "Position"
      }}
    ]
  } ],
  "messages" : {
  }
}
""");

        var schema = protocol.Types.Last() as RecordSchema;

        Assert.That(() => recordGenerator.Generate(schema, null), Throws.ArgumentNullException);
    }

    [TestCase("")]
    [TestCase("    ")]
    public static void Generate_GivenEmptyOrWhitespaceBaseNamespace_ThrowsArgumentException(string baseNamespace)
    {
        var recordGenerator = new AvroRecordGenerator();

        var protocol = Protocol.Parse("""
{
  "protocol" : "Baseball",
  "namespace" : "avro.examples.baseball",
  "doc" : "* Licensed to the Apache Software Foundation (ASF) under one\r\n * or more contributor license agreements.  See the NOTICE file\r\n * distributed with this work for additional information\r\n * regarding copyright ownership.  The ASF licenses this file\r\n * to you under the Apache License, Version 2.0 (the\r\n * \"License\"); you may not use this file except in compliance\r\n * with the License.  You may obtain a copy of the License at\r\n *\r\n *     https://www.apache.org/licenses/LICENSE-2.0\r\n *\r\n * Unless required by applicable law or agreed to in writing, software\r\n * distributed under the License is distributed on an \"AS IS\" BASIS,\r\n * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.\r\n * See the License for the specific language governing permissions and\r\n * limitations under the License.",
  "types" : [ {
    "type" : "enum",
    "name" : "Position",
    "symbols" : [ "P", "C", "B1", "B2", "B3", "SS", "LF", "CF", "RF", "DH" ]
  }, {
    "type" : "record",
    "name" : "Player",
    "fields" : [ {
      "name" : "number",
      "type" : "int"
    }, {
      "name" : "first_name",
      "type" : "string"
    }, {
      "name" : "middle_name",
      "doc": "wololololo",
      "type": [ "null", "string" ]
    }, {
      "name" : "last_name",
      "type" : "string"
    }, {
      "name" : "test_num", "type": {
      "type" : "bytes",
      "logicalType": "decimal",
      "precision": 18,
      "scale": 5
    }}, {
      "name" : "position",
      "type" : {
        "type" : "array",
        "items" : "Position"
      }}, {
      "name" : "positionLookup",
      "type" : {
        "type" : "map",
        "values" : "Position"
      }}
    ]
  } ],
  "messages" : {
  }
}
""");

        var schema = protocol.Types.Last() as RecordSchema;

        Assert.That(() => recordGenerator.Generate(schema, baseNamespace), Throws.ArgumentException);
    }

    [Test]
    public static void Generate_GivenValidRecordType_GeneratesExpectedCode()
    {
        var recordGenerator = new AvroRecordGenerator();

        var protocol = Protocol.Parse("""
{
  "protocol" : "Baseball",
  "namespace" : "avro.examples.baseball",
  "doc" : "* Licensed to the Apache Software Foundation (ASF) under one\r\n * or more contributor license agreements.  See the NOTICE file\r\n * distributed with this work for additional information\r\n * regarding copyright ownership.  The ASF licenses this file\r\n * to you under the Apache License, Version 2.0 (the\r\n * \"License\"); you may not use this file except in compliance\r\n * with the License.  You may obtain a copy of the License at\r\n *\r\n *     https://www.apache.org/licenses/LICENSE-2.0\r\n *\r\n * Unless required by applicable law or agreed to in writing, software\r\n * distributed under the License is distributed on an \"AS IS\" BASIS,\r\n * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.\r\n * See the License for the specific language governing permissions and\r\n * limitations under the License.",
  "types" : [ {
    "type" : "enum",
    "name" : "Position",
    "symbols" : [ "P", "C", "B1", "B2", "B3", "SS", "LF", "CF", "RF", "DH" ]
  }, {
    "type" : "record",
    "name" : "Player",
    "fields" : [ {
      "name" : "number",
      "type" : "int"
    }, {
      "name" : "first_name",
      "type" : "string"
    }, {
      "name" : "middle_name",
      "doc": "wololololo",
      "type": [ "null", "string" ]
    }, {
      "name" : "last_name",
      "type" : "string"
    }, {
      "name" : "test_num", "type": {
      "type" : "bytes",
      "logicalType": "decimal",
      "precision": 18,
      "scale": 5
    }}, {
      "name" : "position",
      "type" : {
        "type" : "array",
        "items" : "Position"
      }}, {
      "name" : "positionLookup",
      "type" : {
        "type" : "map",
        "values" : "Position"
      }}
    ]
  } ],
  "messages" : {
  }
}
""");

        var schema = protocol.Types.Last() as RecordSchema;
        var result = recordGenerator.Generate(schema, TestNamespace);

        const string expected = """
using System;
using System.Collections.Generic;
using Avro;
using Avro.Specific;
using AvroSchema = Avro.Schema;

namespace avro.examples.baseball
{
    public record Player : ISpecificRecord
    {
        private static readonly AvroSchema _schema = AvroSchema.Parse("{\"type\":\"record\",\"name\":\"Player\",\"namespace\":\"avro.examples.baseball\",\"fields\":[{\"name\":\"number\",\"type\":\"int\"},{\"name\":\"first_name\",\"type\":\"string\"},{\"name\":\"middle_name\",\"doc\":\"wololololo\",\"type\":[\"null\",\"string\"]},{\"name\":\"last_name\",\"type\":\"string\"},{\"name\":\"test_num\",\"type\":{\"type\":\"bytes\",\"logicalType\":\"decimal\",\"precision\":18,\"scale\":5}},{\"name\":\"position\",\"type\":{\"type\":\"array\",\"items\":{\"type\":\"enum\",\"name\":\"Position\",\"namespace\":\"avro.examples.baseball\",\"symbols\":[\"P\",\"C\",\"B1\",\"B2\",\"B3\",\"SS\",\"LF\",\"CF\",\"RF\",\"DH\"]}}},{\"name\":\"positionLookup\",\"type\":{\"type\":\"map\",\"values\":\"Position\"}}]}");

        public AvroSchema Schema { get; } = _schema;

        public int number { get; set; }

        public string first_name { get; set; } = default!;

        /// <summary>
        /// wololololo
        /// </summary>
        public string? middle_name { get; set; }

        public string last_name { get; set; } = default!;

        public decimal test_num { get; set; }

        public List<Position> position { get; set; } = default!;

        public IDictionary<string, Position> positionLookup { get; set; } = default!;

        public object Get(int fieldPos)
        {
            var playerField = (PlayerField)fieldPos;
            return playerField switch
            {
                PlayerField.number => number,
                PlayerField.first_name => first_name,
                PlayerField.middle_name => middle_name,
                PlayerField.last_name => last_name,
                PlayerField.test_num => new AvroDecimal(Math.Round(test_num, 5, MidpointRounding.AwayFromZero) + new decimal(0, 0, 0, false, 5)),
                PlayerField.position => position,
                PlayerField.positionLookup => positionLookup,
                _ => throw new AvroRuntimeException("Bad index " + fieldPos + " in Get()")
            };
        }

        public void Put(int fieldPos, object fieldValue)
        {
            var playerField = (PlayerField)fieldPos;
            switch (playerField)
            {
                case PlayerField.number:
                    number = (int)fieldValue;
                    break;
                case PlayerField.first_name:
                    first_name = (string)fieldValue;
                    break;
                case PlayerField.middle_name:
                    middle_name = (string?)fieldValue;
                    break;
                case PlayerField.last_name:
                    last_name = (string)fieldValue;
                    break;
                case PlayerField.test_num:
                    test_num = AvroDecimal.ToDecimal((AvroDecimal)fieldValue);
                    break;
                case PlayerField.position:
                    position = (List<Position>)fieldValue;
                    break;
                case PlayerField.positionLookup:
                    positionLookup = (IDictionary<string, Position>)fieldValue;
                    break;
                default:
                    throw new AvroRuntimeException("Bad index " + fieldPos + " in Put()");
            }
        }

        private enum PlayerField
        {
            number,
            first_name,
            middle_name,
            last_name,
            test_num,
            position,
            positionLookup
        }
    }
}
""";

        Assert.That(result, Is.EqualTo(expected).IgnoreLineEndingFormat);
    }

    [Test]
    public static void Generate_GivenValidErrorType_GeneratesExpectedCode()
    {
        var recordGenerator = new AvroRecordGenerator();

        var protocol = Protocol.Parse("""
{
  "protocol" : "Baseball",
  "namespace" : "avro.examples.baseball",
  "doc" : "* Licensed to the Apache Software Foundation (ASF) under one\r\n * or more contributor license agreements.  See the NOTICE file\r\n * distributed with this work for additional information\r\n * regarding copyright ownership.  The ASF licenses this file\r\n * to you under the Apache License, Version 2.0 (the\r\n * \"License\"); you may not use this file except in compliance\r\n * with the License.  You may obtain a copy of the License at\r\n *\r\n *     https://www.apache.org/licenses/LICENSE-2.0\r\n *\r\n * Unless required by applicable law or agreed to in writing, software\r\n * distributed under the License is distributed on an \"AS IS\" BASIS,\r\n * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.\r\n * See the License for the specific language governing permissions and\r\n * limitations under the License.",
  "types" : [ {
    "type" : "enum",
    "name" : "Position",
    "symbols" : [ "P", "C", "B1", "B2", "B3", "SS", "LF", "CF", "RF", "DH" ]
  }, {
    "type" : "error",
    "name" : "Player",
    "fields" : [ {
      "name" : "number",
      "type" : "int"
    }, {
      "name" : "first_name",
      "type" : "string"
    }, {
      "name" : "middle_name",
      "doc": "wololololo",
      "type": [ "null", "string" ]
    }, {
      "name" : "last_name",
      "type" : "string"
    }, {
      "name" : "test_num", "type": {
      "type" : "bytes",
      "logicalType": "decimal",
      "precision": 18,
      "scale": 5
    }}, {
      "name" : "position",
      "type" : {
        "type" : "array",
        "items" : "Position"
      }}, {
      "name" : "positionLookup",
      "type" : {
        "type" : "map",
        "values" : "Position"
      }}
    ]
  } ],
  "messages" : {
  }
}
""");

        var schema = protocol.Types.Last() as RecordSchema;
        var result = recordGenerator.Generate(schema, TestNamespace);

        const string expected = """
using System;
using System.Collections.Generic;
using Avro;
using Avro.Specific;
using AvroSchema = Avro.Schema;

namespace avro.examples.baseball
{
    public record Player : SpecificException
    {
        private static readonly AvroSchema _schema = AvroSchema.Parse("{\"type\":\"error\",\"name\":\"Player\",\"namespace\":\"avro.examples.baseball\",\"fields\":[{\"name\":\"number\",\"type\":\"int\"},{\"name\":\"first_name\",\"type\":\"string\"},{\"name\":\"middle_name\",\"doc\":\"wololololo\",\"type\":[\"null\",\"string\"]},{\"name\":\"last_name\",\"type\":\"string\"},{\"name\":\"test_num\",\"type\":{\"type\":\"bytes\",\"logicalType\":\"decimal\",\"precision\":18,\"scale\":5}},{\"name\":\"position\",\"type\":{\"type\":\"array\",\"items\":{\"type\":\"enum\",\"name\":\"Position\",\"namespace\":\"avro.examples.baseball\",\"symbols\":[\"P\",\"C\",\"B1\",\"B2\",\"B3\",\"SS\",\"LF\",\"CF\",\"RF\",\"DH\"]}}},{\"name\":\"positionLookup\",\"type\":{\"type\":\"map\",\"values\":\"Position\"}}]}");

        public override AvroSchema Schema { get; } = _schema;

        public int number { get; set; }

        public string first_name { get; set; } = default!;

        /// <summary>
        /// wololololo
        /// </summary>
        public string? middle_name { get; set; }

        public string last_name { get; set; } = default!;

        public decimal test_num { get; set; }

        public List<Position> position { get; set; } = default!;

        public IDictionary<string, Position> positionLookup { get; set; } = default!;

        public override object Get(int fieldPos)
        {
            var playerField = (PlayerField)fieldPos;
            return playerField switch
            {
                PlayerField.number => number,
                PlayerField.first_name => first_name,
                PlayerField.middle_name => middle_name,
                PlayerField.last_name => last_name,
                PlayerField.test_num => new AvroDecimal(Math.Round(test_num, 5, MidpointRounding.AwayFromZero) + new decimal(0, 0, 0, false, 5)),
                PlayerField.position => position,
                PlayerField.positionLookup => positionLookup,
                _ => throw new AvroRuntimeException("Bad index " + fieldPos + " in Get()")
            };
        }

        public override void Put(int fieldPos, object fieldValue)
        {
            var playerField = (PlayerField)fieldPos;
            switch (playerField)
            {
                case PlayerField.number:
                    number = (int)fieldValue;
                    break;
                case PlayerField.first_name:
                    first_name = (string)fieldValue;
                    break;
                case PlayerField.middle_name:
                    middle_name = (string?)fieldValue;
                    break;
                case PlayerField.last_name:
                    last_name = (string)fieldValue;
                    break;
                case PlayerField.test_num:
                    test_num = AvroDecimal.ToDecimal((AvroDecimal)fieldValue);
                    break;
                case PlayerField.position:
                    position = (List<Position>)fieldValue;
                    break;
                case PlayerField.positionLookup:
                    positionLookup = (IDictionary<string, Position>)fieldValue;
                    break;
                default:
                    throw new AvroRuntimeException("Bad index " + fieldPos + " in Put()");
            }
        }

        private enum PlayerField
        {
            number,
            first_name,
            middle_name,
            last_name,
            test_num,
            position,
            positionLookup
        }
    }
}
""";

        Assert.That(result, Is.EqualTo(expected).IgnoreLineEndingFormat);
    }

    [Test]
    public static void Generate_GivenFieldsWithVariousNamespaces_GeneratesExpectedCode()
    {
        var recordGenerator = new AvroRecordGenerator();

        var protocol = Protocol.Parse("""
{
  "protocol" : "TestNamespace",
  "namespace" : "avro.test.protocol",
  "doc" : "* Licensed to the Apache Software Foundation (ASF) under one\r\n * or more contributor license agreements.  See the NOTICE file\r\n * distributed with this work for additional information\r\n * regarding copyright ownership.  The ASF licenses this file\r\n * to you under the Apache License, Version 2.0 (the\r\n * \"License\"); you may not use this file except in compliance\r\n * with the License.  You may obtain a copy of the License at\r\n *\r\n *     https://www.apache.org/licenses/LICENSE-2.0\r\n *\r\n * Unless required by applicable law or agreed to in writing, software\r\n * distributed under the License is distributed on an \"AS IS\" BASIS,\r\n * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.\r\n * See the License for the specific language governing permissions and\r\n * limitations under the License.",
  "types" : [ {
    "type" : "fixed",
    "name" : "FixedInOtherNamespace",
    "namespace" : "avro.test.fixed",
    "size" : 16
  }, {
    "type" : "fixed",
    "name" : "FixedInThisNamespace",
    "size" : 16
  }, {
    "type" : "record",
    "name" : "RecordInOtherNamespace",
    "namespace" : "avro.test.record",
    "fields" : [ ]
  }, {
    "type" : "error",
    "name" : "ErrorInOtherNamespace",
    "namespace" : "avro.test.error",
    "fields" : [ ]
  }, {
    "type" : "enum",
    "name" : "EnumInOtherNamespace",
    "namespace" : "avro.test.enum",
    "symbols" : [ "FOO" ]
  }, {
    "type" : "record",
    "name" : "RefersToOthers",
    "fields" : [ {
      "name" : "someFixed",
      "type" : "avro.test.fixed.FixedInOtherNamespace"
    }, {
      "name" : "someRecord",
      "type" : "avro.test.record.RecordInOtherNamespace"
    }, {
      "name" : "someError",
      "type" : "avro.test.error.ErrorInOtherNamespace"
    }, {
      "name" : "someEnum",
      "type" : "avro.test.enum.EnumInOtherNamespace"
    }, {
      "name" : "thisFixed",
      "type" : "FixedInThisNamespace"
    } ]
  } ],
  "messages" : {
  }
}
""");

        var schema = protocol.Types.Last() as RecordSchema;
        var result = recordGenerator.Generate(schema, TestNamespace);

        const string expected = """
using System;
using System.Collections.Generic;
using Avro;
using Avro.Specific;
using avro.test.enum;
using avro.test.error;
using avro.test.fixed;
using avro.test.record;
using AvroSchema = Avro.Schema;

namespace avro.test.protocol
{
    public record RefersToOthers : ISpecificRecord
    {
        private static readonly AvroSchema _schema = AvroSchema.Parse("{\"type\":\"record\",\"name\":\"RefersToOthers\",\"namespace\":\"avro.test.protocol\",\"fields\":[{\"name\":\"someFixed\",\"type\":{\"type\":\"fixed\",\"name\":\"FixedInOtherNamespace\",\"namespace\":\"avro.test.fixed\",\"size\":16}},{\"name\":\"someRecord\",\"type\":{\"type\":\"record\",\"name\":\"RecordInOtherNamespace\",\"namespace\":\"avro.test.record\",\"fields\":[]}},{\"name\":\"someError\",\"type\":{\"type\":\"error\",\"name\":\"ErrorInOtherNamespace\",\"namespace\":\"avro.test.error\",\"fields\":[]}},{\"name\":\"someEnum\",\"type\":{\"type\":\"enum\",\"name\":\"EnumInOtherNamespace\",\"namespace\":\"avro.test.enum\",\"symbols\":[\"FOO\"]}},{\"name\":\"thisFixed\",\"type\":{\"type\":\"fixed\",\"name\":\"FixedInThisNamespace\",\"namespace\":\"avro.test.protocol\",\"size\":16}}]}");

        public AvroSchema Schema { get; } = _schema;

        public FixedInOtherNamespace someFixed { get; set; } = default!;

        public RecordInOtherNamespace someRecord { get; set; } = default!;

        public ErrorInOtherNamespace someError { get; set; } = default!;

        public EnumInOtherNamespace someEnum { get; set; }

        public FixedInThisNamespace thisFixed { get; set; } = default!;

        public object Get(int fieldPos)
        {
            var refersToOthersField = (RefersToOthersField)fieldPos;
            return refersToOthersField switch
            {
                RefersToOthersField.someFixed => someFixed,
                RefersToOthersField.someRecord => someRecord,
                RefersToOthersField.someError => someError,
                RefersToOthersField.someEnum => someEnum,
                RefersToOthersField.thisFixed => thisFixed,
                _ => throw new AvroRuntimeException("Bad index " + fieldPos + " in Get()")
            };
        }

        public void Put(int fieldPos, object fieldValue)
        {
            var refersToOthersField = (RefersToOthersField)fieldPos;
            switch (refersToOthersField)
            {
                case RefersToOthersField.someFixed:
                    someFixed = (FixedInOtherNamespace)fieldValue;
                    break;
                case RefersToOthersField.someRecord:
                    someRecord = (RecordInOtherNamespace)fieldValue;
                    break;
                case RefersToOthersField.someError:
                    someError = (ErrorInOtherNamespace)fieldValue;
                    break;
                case RefersToOthersField.someEnum:
                    someEnum = (EnumInOtherNamespace)fieldValue;
                    break;
                case RefersToOthersField.thisFixed:
                    thisFixed = (FixedInThisNamespace)fieldValue;
                    break;
                default:
                    throw new AvroRuntimeException("Bad index " + fieldPos + " in Put()");
            }
        }

        private enum RefersToOthersField
        {
            someFixed,
            someRecord,
            someError,
            someEnum,
            thisFixed
        }
    }
}
""";

        Assert.That(result, Is.EqualTo(expected).IgnoreLineEndingFormat);
    }
}