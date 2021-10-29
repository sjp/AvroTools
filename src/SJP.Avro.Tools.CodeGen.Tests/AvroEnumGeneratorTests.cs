using System.Linq;
using Avro;
using NUnit.Framework;

namespace SJP.Avro.Tools.CodeGen.Tests
{
    [TestFixture]
    internal static class AvroEnumGeneratorTests
    {
        [Test]
        public static void Generate_GivenValidEnumSchema_GeneratesExpectedCode()
        {
            var enumGenerator = new AvroEnumGenerator();

            var schema = @"{""type"":""enum"", ""name"": ""Position"", ""doc"": ""wolololo"", ""namespace"": ""avro.examples.baseball"", ""default"":""CF"",
    ""symbols"": [""P"", ""C"", ""B1"", ""B2"", ""B3"", ""SS"", ""LF"", ""CF"", ""RF"", ""DH""]
}";

            var result = enumGenerator.Generate(schema);

            Assert.Pass(result);
        }

        [Test]
        public static void Generate_GivenValidRecordSchema_GeneratesExpectedCode()
        {
            var recordGenerator = new AvroRecordGenerator();

            var schema = @"{
  ""protocol"" : ""Baseball"",
  ""namespace"" : ""avro.examples.baseball"",
  ""doc"" : ""* Licensed to the Apache Software Foundation (ASF) under one\r\n * or more contributor license agreements.  See the NOTICE file\r\n * distributed with this work for additional information\r\n * regarding copyright ownership.  The ASF licenses this file\r\n * to you under the Apache License, Version 2.0 (the\r\n * \""License\""); you may not use this file except in compliance\r\n * with the License.  You may obtain a copy of the License at\r\n *\r\n *     https://www.apache.org/licenses/LICENSE-2.0\r\n *\r\n * Unless required by applicable law or agreed to in writing, software\r\n * distributed under the License is distributed on an \""AS IS\"" BASIS,\r\n * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.\r\n * See the License for the specific language governing permissions and\r\n * limitations under the License."",
  ""types"" : [ {
    ""type"" : ""enum"",
    ""name"" : ""Position"",
    ""symbols"" : [ ""P"", ""C"", ""B1"", ""B2"", ""B3"", ""SS"", ""LF"", ""CF"", ""RF"", ""DH"" ]
  }, {
    ""type"" : ""error"",
    ""name"" : ""Player"",
    ""fields"" : [ {
      ""name"" : ""number"",
      ""type"" : ""int""
    }, {
      ""name"" : ""first_name"",
      ""type"" : ""string""
    }, {
      ""name"" : ""middle_name"",
      ""doc"": ""wololololo"",
      ""type"": [ ""null"", ""string"" ]
    }, {
      ""name"" : ""last_name"",
      ""type"" : ""string""
    }, {
      ""name"" : ""test_num"", ""type"": {
      ""type"" : ""bytes"",
      ""logicalType"": ""decimal"",
      ""precision"": 18,
      ""scale"": 5
    }}, {
      ""name"" : ""position"",
      ""type"" : {
        ""type"" : ""array"",
        ""items"" : ""Position""
      }}, {
      ""name"" : ""positionLookup"",
      ""type"" : {
        ""type"" : ""map"",
        ""values"" : ""Position""
      }}
    ]
  } ],
  ""messages"" : {
  }
}";

            var result = recordGenerator.Generate(schema);

            Assert.Pass(result);
        }

        [Test]
        public static void Generate_GivenValidFixedSchema_GeneratesExpectedCode()
        {
            var fixedGenerator = new AvroFixedGenerator();

            var schema = "{\"type\":\"fixed\",\"name\":\"MD5\",\"doc\":\"An MD5 hash.\",\"namespace\":\"org.apache.avro.te" +
   "st\",\"size\":199,\"foo\":\"bar\"}";

            var result = fixedGenerator.Generate(schema);

            Assert.Pass(result);
        }

        [Test]
        public static void Generate_GivenValidProtocol_GeneratesExpectedCode()
        {
            var protocolGenerator = new AvroProtocolGenerator();

            var schema = "{\"protocol\":\"Simple\",\"namespace\":\"org.apache.avro.test\",\"doc\":\"* A simple test ca" +
                "se.\",\"types\":[{\"type\":\"enum\",\"name\":\"Kind\",\"doc\":\"A kind of record.\",\"namespace\"" +
                ":\"org.apache.avro.test\",\"aliases\":[\"org.foo.KindOf\"],\"symbols\":[\"FOO\",\"BAR\",\"BAZ" +
                "\"]},{\"type\":\"enum\",\"name\":\"Status\",\"namespace\":\"org.apache.avro.test\",\"symbols\":" +
                "[\"A\",\"B\",\"C\"],\"default\":\"C\"},{\"type\":\"fixed\",\"name\":\"MD5\",\"doc\":\"An MD5 hash.\",\"" +
                "namespace\":\"org.apache.avro.test\",\"size\":16,\"foo\":\"bar\"},{\"type\":\"record\",\"name\"" +
                ":\"TestRecord\",\"doc\":\"A TestRecord.\",\"namespace\":\"org.apache.avro.test\",\"fields\":" +
                "[{\"name\":\"name\",\"default\":\"foo\",\"type\":\"string\"},{\"name\":\"kind\",\"doc\":\"The kind " +
                "of record.\",\"type\":\"Kind\"},{\"name\":\"status\",\"doc\":\"The status of the record.\",\"d" +
                "efault\":\"A\",\"type\":\"Status\"},{\"name\":\"hash\",\"default\":\"0000000000000000\",\"type\":" +
                "\"MD5\"},{\"name\":\"nullableHash\",\"default\":null,\"type\":[\"null\",\"MD5\"],\"aliases\":[\"h" +
                "h\",\"hsh\"]},{\"name\":\"value\",\"default\":\"NaN\",\"type\":\"double\"},{\"name\":\"average\",\"d" +
                "efault\":\"-Infinity\",\"type\":\"float\"},{\"name\":\"d\",\"default\":0,\"type\":{\"type\":\"int\"" +
                ",\"logicalType\":\"date\"}},{\"name\":\"t\",\"default\":0,\"type\":{\"type\":\"int\",\"logicalTyp" +
                "e\":\"time-millis\"}},{\"name\":\"l\",\"default\":0,\"type\":\"long\"},{\"name\":\"prop\",\"defaul" +
                "t\":null,\"type\":[\"null\",\"string\"]}],\"my-property\":{\"key\":3}},{\"type\":\"error\",\"nam" +
                "e\":\"TestError\",\"namespace\":\"org.apache.avro.test\",\"fields\":[{\"name\":\"message\",\"t" +
                "ype\":\"string\"}]}],\"messages\":{\"hello\":{\"doc\":\"method \'hello\' takes @parameter \'g" +
                "reeting\'\",\"request\":[{\"name\":\"greeting\",\"type\":\"string\"}],\"response\":\"string\"},\"" +
                "echo\":{\"request\":[{\"name\":\"record\",\"default\":{\"name\":\"bar\",\"kind\":\"BAR\"},\"type\":" +
                "\"TestRecord\"}],\"response\":\"TestRecord\"},\"add\":{\"doc\":\"method \'add\' takes @parame" +
                "ter \'arg1\' @parameter \'arg2\'\",\"request\":[{\"name\":\"arg1\",\"type\":\"int\"},{\"name\":\"a" +
                "rg2\",\"default\":0,\"type\":\"int\"}],\"response\":\"int\"},\"echoBytes\":{\"request\":[{\"name" +
                "\":\"data\",\"type\":\"bytes\"}],\"response\":\"bytes\"},\"error\":{\"request\":[],\"response\":\"" +
                "null\",\"errors\":[\"TestError\"]},\"ping\":{\"request\":[],\"response\":\"null\",\"one-way\":t" +
                "rue}}}";



            var result = protocolGenerator.Generate(schema);

            Assert.Pass(result);
        }

        [Test]
        public static void Generate_GivenValidProtocolWithVariousNamespaces_GeneratesExpectedCode()
        {
            var protocolGenerator = new AvroProtocolGenerator();

            var schema = @"{
  ""protocol"" : ""TestNamespace"",
  ""namespace"" : ""avro.test.protocol"",
  ""doc"" : ""* Licensed to the Apache Software Foundation (ASF) under one\r\n * or more contributor license agreements.  See the NOTICE file\r\n * distributed with this work for additional information\r\n * regarding copyright ownership.  The ASF licenses this file\r\n * to you under the Apache License, Version 2.0 (the\r\n * \""License\""); you may not use this file except in compliance\r\n * with the License.  You may obtain a copy of the License at\r\n *\r\n *     https://www.apache.org/licenses/LICENSE-2.0\r\n *\r\n * Unless required by applicable law or agreed to in writing, software\r\n * distributed under the License is distributed on an \""AS IS\"" BASIS,\r\n * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.\r\n * See the License for the specific language governing permissions and\r\n * limitations under the License."",
  ""types"" : [ {
    ""type"" : ""fixed"",
    ""name"" : ""FixedInOtherNamespace"",
    ""namespace"" : ""avro.test.fixed"",
    ""size"" : 16
  }, {
    ""type"" : ""fixed"",
    ""name"" : ""FixedInThisNamespace"",
    ""size"" : 16
  }, {
    ""type"" : ""record"",
    ""name"" : ""RecordInOtherNamespace"",
    ""namespace"" : ""avro.test.record"",
    ""fields"" : [ ]
  }, {
    ""type"" : ""error"",
    ""name"" : ""ErrorInOtherNamespace"",
    ""namespace"" : ""avro.test.error"",
    ""fields"" : [ ]
  }, {
    ""type"" : ""enum"",
    ""name"" : ""EnumInOtherNamespace"",
    ""namespace"" : ""avro.test.enum"",
    ""symbols"" : [ ""FOO"" ]
  }, {
    ""type"" : ""record"",
    ""name"" : ""RefersToOthers"",
    ""fields"" : [ {
      ""name"" : ""someFixed"",
      ""type"" : ""avro.test.fixed.FixedInOtherNamespace""
    }, {
      ""name"" : ""someRecord"",
      ""type"" : ""avro.test.record.RecordInOtherNamespace""
    }, {
      ""name"" : ""someError"",
      ""type"" : ""avro.test.error.ErrorInOtherNamespace""
    }, {
      ""name"" : ""someEnum"",
      ""type"" : ""avro.test.enum.EnumInOtherNamespace""
    }, {
      ""name"" : ""thisFixed"",
      ""type"" : ""FixedInThisNamespace""
    } ]
  } ],
  ""messages"" : {
  }
}";



            var result = protocolGenerator.Generate(schema);

            var recordGenerator = new AvroRecordGenerator();

            var rresult = recordGenerator.Generate(schema);

            Assert.Pass(result);
            Assert.Pass(rresult);
        }
    }
}
