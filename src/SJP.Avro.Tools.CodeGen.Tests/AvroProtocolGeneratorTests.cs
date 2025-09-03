using Avro;
using NUnit.Framework;

namespace SJP.Avro.Tools.CodeGen.Tests;

[TestFixture]
internal static class AvroProtocolGeneratorTests
{
    private const string TestNamespace = "Test.Avro.Namespace";

    [Test]
    public static void Generate_GivenNullSchema_ThrowsArgumentNullException()
    {
        var protocolGenerator = new AvroProtocolGenerator();

        Assert.That(() => protocolGenerator.Generate(default!, TestNamespace), Throws.ArgumentNullException);
    }

    [TestCase((string)null)]
    [TestCase("")]
    [TestCase("    ")]
    public static void Generate_GivenNullOrWhitespaceBaseNamespace_ThrowsArgumentNullException(string baseNamespace)
    {
        var protocolGenerator = new AvroProtocolGenerator();

        var protocol = Protocol.Parse(@"{
  ""protocol"" : ""Baseball"",
  ""namespace"" : ""avro.examples.baseball"",
  ""doc"" : ""* Licensed to the Apache Software Foundation (ASF) under one\r\n * or more contributor license agreements.  See the NOTICE file\r\n * distributed with this work for additional information\r\n * regarding copyright ownership.  The ASF licenses this file\r\n * to you under the Apache License, Version 2.0 (the\r\n * \""License\""); you may not use this file except in compliance\r\n * with the License.  You may obtain a copy of the License at\r\n *\r\n *     https://www.apache.org/licenses/LICENSE-2.0\r\n *\r\n * Unless required by applicable law or agreed to in writing, software\r\n * distributed under the License is distributed on an \""AS IS\"" BASIS,\r\n * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.\r\n * See the License for the specific language governing permissions and\r\n * limitations under the License."",
  ""types"" : [ {
    ""type"" : ""enum"",
    ""name"" : ""Position"",
    ""symbols"" : [ ""P"", ""C"", ""B1"", ""B2"", ""B3"", ""SS"", ""LF"", ""CF"", ""RF"", ""DH"" ]
  }, {
    ""type"" : ""record"",
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
}");

        Assert.That(() => protocolGenerator.Generate(protocol, baseNamespace), Throws.ArgumentNullException);
    }

    [Test]
    public static void Generate_GivenNoMessages_ReturnsEmptyString()
    {
        var protocolGenerator = new AvroProtocolGenerator();

        var protocol = Protocol.Parse(@"{
  ""protocol"" : ""Baseball"",
  ""namespace"" : ""avro.examples.baseball"",
  ""doc"" : ""* Licensed to the Apache Software Foundation (ASF) under one\r\n * or more contributor license agreements.  See the NOTICE file\r\n * distributed with this work for additional information\r\n * regarding copyright ownership.  The ASF licenses this file\r\n * to you under the Apache License, Version 2.0 (the\r\n * \""License\""); you may not use this file except in compliance\r\n * with the License.  You may obtain a copy of the License at\r\n *\r\n *     https://www.apache.org/licenses/LICENSE-2.0\r\n *\r\n * Unless required by applicable law or agreed to in writing, software\r\n * distributed under the License is distributed on an \""AS IS\"" BASIS,\r\n * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.\r\n * See the License for the specific language governing permissions and\r\n * limitations under the License."",
  ""types"" : [ {
    ""type"" : ""enum"",
    ""name"" : ""Position"",
    ""symbols"" : [ ""P"", ""C"", ""B1"", ""B2"", ""B3"", ""SS"", ""LF"", ""CF"", ""RF"", ""DH"" ]
  }, {
    ""type"" : ""record"",
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
}");

        var result = protocolGenerator.Generate(protocol, TestNamespace);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public static void Generate_GivenValidProtocol_GeneratesExpectedCode()
    {
        var protocolGenerator = new AvroProtocolGenerator();

        var protocol = Protocol.Parse(@"{
    ""protocol"": ""Simple"",
    ""namespace"": ""org.apache.avro.test"",
    ""doc"": ""* A simple test case."",
    ""types"": [
        {
            ""type"": ""enum"",
            ""name"": ""Kind"",
            ""doc"": ""A kind of record."",
            ""namespace"": ""org.apache.avro.test"",
            ""aliases"": [
                ""org.foo.KindOf""
            ],
            ""symbols"": [
                ""FOO"",
                ""BAR"",
                ""BAZ""
            ]
        },
        {
            ""type"": ""enum"",
            ""name"": ""Status"",
            ""namespace"": ""org.apache.avro.test"",
            ""symbols"": [
                ""A"",
                ""B"",
                ""C""
            ],
            ""default"": ""C""
        },
        {
            ""type"": ""fixed"",
            ""name"": ""MD5"",
            ""doc"": ""An MD5 hash."",
            ""namespace"": ""org.apache.avro.test"",
            ""size"": 16,
            ""foo"": ""bar""
        },
        {
            ""type"": ""record"",
            ""name"": ""TestRecord"",
            ""doc"": ""A TestRecord."",
            ""namespace"": ""org.apache.avro.test"",
            ""fields"": [
                {
                    ""name"": ""name"",
                    ""default"": ""foo"",
                    ""type"": ""string""
                },
                {
                    ""name"": ""kind"",
                    ""doc"": ""The kind of record."",
                    ""type"": ""Kind""
                },
                {
                    ""name"": ""status"",
                    ""doc"": ""The status of the record."",
                    ""default"": ""A"",
                    ""type"": ""Status""
                },
                {
                    ""name"": ""hash"",
                    ""default"": ""0000000000000000"",
                    ""type"": ""MD5""
                },
                {
                    ""name"": ""nullableHash"",
                    ""default"": null,
                    ""type"": [
                        ""null"",
                        ""MD5""
                    ],
                    ""aliases"": [
                        ""hh"",
                        ""hsh""
                    ]
                },
                {
                    ""name"": ""value"",
                    ""default"": ""NaN"",
                    ""type"": ""double""
                },
                {
                    ""name"": ""average"",
                    ""default"": ""-Infinity"",
                    ""type"": ""float""
                },
                {
                    ""name"": ""d"",
                    ""default"": 0,
                    ""type"": {
                        ""type"": ""int"",
                        ""logicalType"": ""date""
                    }
                },
                {
                    ""name"": ""t"",
                    ""default"": 0,
                    ""type"": {
                        ""type"": ""int"",
                        ""logicalType"": ""time-millis""
                    }
                },
                {
                    ""name"": ""l"",
                    ""default"": 0,
                    ""type"": ""long""
                },
                {
                    ""name"": ""prop"",
                    ""default"": null,
                    ""type"": [
                        ""null"",
                        ""string""
                    ]
                }
            ],
            ""my-property"": {
                ""key"": 3
            }
        },
        {
            ""type"": ""error"",
            ""name"": ""TestError"",
            ""namespace"": ""org.apache.avro.test"",
            ""fields"": [
                {
                    ""name"": ""message"",
                    ""type"": ""string""
                }
            ]
        }
    ],
    ""messages"": {
        ""hello"": {
            ""doc"": ""method 'hello' takes @parameter 'greeting'"",
            ""request"": [
                {
                    ""name"": ""greeting"",
                    ""type"": ""string""
                }
            ],
            ""response"": ""string""
        },
        ""echo"": {
            ""request"": [
                {
                    ""name"": ""record"",
                    ""default"": {
                        ""name"": ""bar"",
                        ""kind"": ""BAR""
                    },
                    ""type"": ""TestRecord""
                }
            ],
            ""response"": ""TestRecord""
        },
        ""add"": {
            ""doc"": ""method 'add' takes @parameter 'arg1' @parameter 'arg2'"",
            ""request"": [
                {
                    ""name"": ""arg1"",
                    ""type"": ""int""
                },
                {
                    ""name"": ""arg2"",
                    ""default"": 0,
                    ""type"": ""int""
                }
            ],
            ""response"": ""int""
        },
        ""echoBytes"": {
            ""request"": [
                {
                    ""name"": ""data"",
                    ""type"": ""bytes""
                }
            ],
            ""response"": ""bytes""
        },
        ""error"": {
            ""request"": [],
            ""response"": ""null"",
            ""errors"": [
                ""TestError""
            ]
        },
        ""ping"": {
            ""request"": [],
            ""response"": ""null"",
            ""one-way"": true
        }
    }
}");

        var result = protocolGenerator.Generate(protocol, TestNamespace);

        const string expected = @"using System;
using System.Collections.Generic;
using Avro;
using Avro.IO;
using Avro.Specific;

namespace org.apache.avro.test
{
    /// <summary>
    /// A simple test case.
    /// </summary>
    public abstract record Simple : ISpecificProtocol
    {
        private static readonly Protocol _protocol = Protocol.Parse(""{\""protocol\"":\""Simple\"",\""namespace\"":\""org.apache.avro.test\"",\""doc\"":\""* A simple test case.\"",\""types\"":[{\""type\"":\""enum\"",\""name\"":\""Kind\"",\""doc\"":\""A kind of record.\"",\""namespace\"":\""org.apache.avro.test\"",\""aliases\"":[\""org.foo.KindOf\""],\""symbols\"":[\""FOO\"",\""BAR\"",\""BAZ\""]},{\""type\"":\""enum\"",\""name\"":\""Status\"",\""namespace\"":\""org.apache.avro.test\"",\""symbols\"":[\""A\"",\""B\"",\""C\""],\""default\"":\""C\""},{\""type\"":\""fixed\"",\""name\"":\""MD5\"",\""doc\"":\""An MD5 hash.\"",\""namespace\"":\""org.apache.avro.test\"",\""size\"":16,\""foo\"":\""bar\""},{\""type\"":\""record\"",\""name\"":\""TestRecord\"",\""doc\"":\""A TestRecord.\"",\""namespace\"":\""org.apache.avro.test\"",\""fields\"":[{\""name\"":\""name\"",\""default\"":\""foo\"",\""type\"":\""string\""},{\""name\"":\""kind\"",\""doc\"":\""The kind of record.\"",\""type\"":\""Kind\""},{\""name\"":\""status\"",\""doc\"":\""The status of the record.\"",\""default\"":\""A\"",\""type\"":\""Status\""},{\""name\"":\""hash\"",\""default\"":\""0000000000000000\"",\""type\"":\""MD5\""},{\""name\"":\""nullableHash\"",\""default\"":null,\""type\"":[\""null\"",\""MD5\""],\""aliases\"":[\""hh\"",\""hsh\""]},{\""name\"":\""value\"",\""default\"":\""NaN\"",\""type\"":\""double\""},{\""name\"":\""average\"",\""default\"":\""-Infinity\"",\""type\"":\""float\""},{\""name\"":\""d\"",\""default\"":0,\""type\"":{\""type\"":\""int\"",\""logicalType\"":\""date\""}},{\""name\"":\""t\"",\""default\"":0,\""type\"":{\""type\"":\""int\"",\""logicalType\"":\""time-millis\""}},{\""name\"":\""l\"",\""default\"":0,\""type\"":\""long\""},{\""name\"":\""prop\"",\""default\"":null,\""type\"":[\""null\"",\""string\""]}],\""my-property\"":{\""key\"":3}},{\""type\"":\""error\"",\""name\"":\""TestError\"",\""namespace\"":\""org.apache.avro.test\"",\""fields\"":[{\""name\"":\""message\"",\""type\"":\""string\""}]}],\""messages\"":{\""hello\"":{\""doc\"":\""method 'hello' takes @parameter 'greeting'\"",\""request\"":[{\""name\"":\""greeting\"",\""type\"":\""string\""}],\""response\"":\""string\""},\""echo\"":{\""request\"":[{\""name\"":\""record\"",\""default\"":{\""name\"":\""bar\"",\""kind\"":\""BAR\""},\""type\"":\""TestRecord\""}],\""response\"":\""TestRecord\""},\""add\"":{\""doc\"":\""method 'add' takes @parameter 'arg1' @parameter 'arg2'\"",\""request\"":[{\""name\"":\""arg1\"",\""type\"":\""int\""},{\""name\"":\""arg2\"",\""default\"":0,\""type\"":\""int\""}],\""response\"":\""int\""},\""echoBytes\"":{\""request\"":[{\""name\"":\""data\"",\""type\"":\""bytes\""}],\""response\"":\""bytes\""},\""error\"":{\""request\"":[],\""response\"":\""null\"",\""errors\"":[\""TestError\""]},\""ping\"":{\""request\"":[],\""response\"":\""null\"",\""one-way\"":true}}}"");

        public Protocol Protocol { get; } = _protocol;

        public void Request(ICallbackRequestor requestor, string messageName, object[] args, object callback)
        {
            switch (messageName)
            {
                case ""hello"":
                    requestor.Request<string>(messageName, args, callback);
                    break;
                case ""echo"":
                    requestor.Request<TestRecord>(messageName, args, callback);
                    break;
                case ""add"":
                    requestor.Request<int>(messageName, args, callback);
                    break;
                case ""echoBytes"":
                    requestor.Request<byte[]>(messageName, args, callback);
                    break;
                case ""error"":
                    requestor.Request<object>(messageName, args, callback);
                    break;
                case ""ping"":
                    requestor.Request<object>(messageName, args, callback);
                    break;
            }
        }

        /// <summary>
        /// method 'hello' takes @parameter 'greeting'
        /// </summary>
        public abstract string hello(string greeting);

        public abstract TestRecord echo(TestRecord record);

        /// <summary>
        /// method 'add' takes @parameter 'arg1' @parameter 'arg2'
        /// </summary>
        public abstract int add(int arg1, int arg2);

        public abstract byte[] echoBytes(byte[] data);

        public abstract void error();

        public abstract void ping();
    }
}";

        Assert.That(result, Is.EqualTo(expected).IgnoreLineEndingFormat);
    }

    [Test]
    public static void Generate_GivenValidProtocolWithComplexDocumentation_GeneratesExpectedCode()
    {
        var protocolGenerator = new AvroProtocolGenerator();

        var protocol = Protocol.Parse(@"{
    ""protocol"": ""Simple"",
    ""namespace"": ""org.apache.avro.test"",
    ""doc"" : ""* Licensed to the Apache Software Foundation (ASF) under one\r\n * or more contributor license agreements.  See the NOTICE file\r\n * distributed with this work for additional information\r\n * regarding copyright ownership.  The ASF licenses this file\r\n * to you under the Apache License, Version 2.0 (the\r\n * \""License\""); you may not use this file except in compliance\r\n * with the License.  You may obtain a copy of the License at\r\n *\r\n *     https://www.apache.org/licenses/LICENSE-2.0\r\n *\r\n * Unless required by applicable law or agreed to in writing, software\r\n * distributed under the License is distributed on an \""AS IS\"" BASIS,\r\n * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.\r\n * See the License for the specific language governing permissions and\r\n * limitations under the License."",
    ""types"": [
        {
            ""type"": ""enum"",
            ""name"": ""Kind"",
            ""doc"": ""A kind of record."",
            ""namespace"": ""org.apache.avro.test"",
            ""aliases"": [
                ""org.foo.KindOf""
            ],
            ""symbols"": [
                ""FOO"",
                ""BAR"",
                ""BAZ""
            ]
        },
        {
            ""type"": ""enum"",
            ""name"": ""Status"",
            ""namespace"": ""org.apache.avro.test"",
            ""symbols"": [
                ""A"",
                ""B"",
                ""C""
            ],
            ""default"": ""C""
        },
        {
            ""type"": ""fixed"",
            ""name"": ""MD5"",
            ""doc"": ""An MD5 hash."",
            ""namespace"": ""org.apache.avro.test"",
            ""size"": 16,
            ""foo"": ""bar""
        },
        {
            ""type"": ""record"",
            ""name"": ""TestRecord"",
            ""doc"": ""A TestRecord."",
            ""namespace"": ""org.apache.avro.test"",
            ""fields"": [
                {
                    ""name"": ""name"",
                    ""default"": ""foo"",
                    ""type"": ""string""
                },
                {
                    ""name"": ""kind"",
                    ""doc"": ""The kind of record."",
                    ""type"": ""Kind""
                },
                {
                    ""name"": ""status"",
                    ""doc"": ""The status of the record."",
                    ""default"": ""A"",
                    ""type"": ""Status""
                },
                {
                    ""name"": ""hash"",
                    ""default"": ""0000000000000000"",
                    ""type"": ""MD5""
                },
                {
                    ""name"": ""nullableHash"",
                    ""default"": null,
                    ""type"": [
                        ""null"",
                        ""MD5""
                    ],
                    ""aliases"": [
                        ""hh"",
                        ""hsh""
                    ]
                },
                {
                    ""name"": ""value"",
                    ""default"": ""NaN"",
                    ""type"": ""double""
                },
                {
                    ""name"": ""average"",
                    ""default"": ""-Infinity"",
                    ""type"": ""float""
                },
                {
                    ""name"": ""d"",
                    ""default"": 0,
                    ""type"": {
                        ""type"": ""int"",
                        ""logicalType"": ""date""
                    }
                },
                {
                    ""name"": ""t"",
                    ""default"": 0,
                    ""type"": {
                        ""type"": ""int"",
                        ""logicalType"": ""time-millis""
                    }
                },
                {
                    ""name"": ""l"",
                    ""default"": 0,
                    ""type"": ""long""
                },
                {
                    ""name"": ""prop"",
                    ""default"": null,
                    ""type"": [
                        ""null"",
                        ""string""
                    ]
                }
            ],
            ""my-property"": {
                ""key"": 3
            }
        },
        {
            ""type"": ""error"",
            ""name"": ""TestError"",
            ""namespace"": ""org.apache.avro.test"",
            ""fields"": [
                {
                    ""name"": ""message"",
                    ""type"": ""string""
                }
            ]
        }
    ],
    ""messages"": {
        ""hello"": {
            ""doc"": ""method 'hello' takes @parameter 'greeting'"",
            ""request"": [
                {
                    ""name"": ""greeting"",
                    ""type"": ""string""
                }
            ],
            ""response"": ""string""
        },
        ""echo"": {
            ""request"": [
                {
                    ""name"": ""record"",
                    ""default"": {
                        ""name"": ""bar"",
                        ""kind"": ""BAR""
                    },
                    ""type"": ""TestRecord""
                }
            ],
            ""response"": ""TestRecord""
        },
        ""add"": {
            ""doc"": ""method 'add' takes @parameter 'arg1' @parameter 'arg2'"",
            ""request"": [
                {
                    ""name"": ""arg1"",
                    ""type"": ""int""
                },
                {
                    ""name"": ""arg2"",
                    ""default"": 0,
                    ""type"": ""int""
                }
            ],
            ""response"": ""int""
        },
        ""echoBytes"": {
            ""request"": [
                {
                    ""name"": ""data"",
                    ""type"": ""bytes""
                }
            ],
            ""response"": ""bytes""
        },
        ""error"": {
            ""request"": [],
            ""response"": ""null"",
            ""errors"": [
                ""TestError""
            ]
        },
        ""ping"": {
            ""request"": [],
            ""response"": ""null"",
            ""one-way"": true
        }
    }
}");

        var result = protocolGenerator.Generate(protocol, TestNamespace);

        const string expected = @"using System;
using System.Collections.Generic;
using Avro;
using Avro.IO;
using Avro.Specific;

namespace org.apache.avro.test
{
    /// <summary>
    /// <para>Licensed to the Apache Software Foundation (ASF) under one or more contributor license agreements.  See the NOTICE file distributed with this work for additional information regarding copyright ownership.  The ASF licenses this file to you under the Apache License, Version 2.0 (the ""License""); you may not use this file except in compliance with the License.  You may obtain a copy of the License at</para>
    /// <para>https://www.apache.org/licenses/LICENSE-2.0</para>
    /// <para>Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an ""AS IS"" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.</para>
    /// </summary>
    public abstract record Simple : ISpecificProtocol
    {
        private static readonly Protocol _protocol = Protocol.Parse(""{\""protocol\"":\""Simple\"",\""namespace\"":\""org.apache.avro.test\"",\""doc\"":\""* Licensed to the Apache Software Foundation (ASF) under one\\r\\n * or more contributor license agreements.  See the NOTICE file\\r\\n * distributed with this work for additional information\\r\\n * regarding copyright ownership.  The ASF licenses this file\\r\\n * to you under the Apache License, Version 2.0 (the\\r\\n * \\\""License\\\""); you may not use this file except in compliance\\r\\n * with the License.  You may obtain a copy of the License at\\r\\n *\\r\\n *     https://www.apache.org/licenses/LICENSE-2.0\\r\\n *\\r\\n * Unless required by applicable law or agreed to in writing, software\\r\\n * distributed under the License is distributed on an \\\""AS IS\\\"" BASIS,\\r\\n * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.\\r\\n * See the License for the specific language governing permissions and\\r\\n * limitations under the License.\"",\""types\"":[{\""type\"":\""enum\"",\""name\"":\""Kind\"",\""doc\"":\""A kind of record.\"",\""namespace\"":\""org.apache.avro.test\"",\""aliases\"":[\""org.foo.KindOf\""],\""symbols\"":[\""FOO\"",\""BAR\"",\""BAZ\""]},{\""type\"":\""enum\"",\""name\"":\""Status\"",\""namespace\"":\""org.apache.avro.test\"",\""symbols\"":[\""A\"",\""B\"",\""C\""],\""default\"":\""C\""},{\""type\"":\""fixed\"",\""name\"":\""MD5\"",\""doc\"":\""An MD5 hash.\"",\""namespace\"":\""org.apache.avro.test\"",\""size\"":16,\""foo\"":\""bar\""},{\""type\"":\""record\"",\""name\"":\""TestRecord\"",\""doc\"":\""A TestRecord.\"",\""namespace\"":\""org.apache.avro.test\"",\""fields\"":[{\""name\"":\""name\"",\""default\"":\""foo\"",\""type\"":\""string\""},{\""name\"":\""kind\"",\""doc\"":\""The kind of record.\"",\""type\"":\""Kind\""},{\""name\"":\""status\"",\""doc\"":\""The status of the record.\"",\""default\"":\""A\"",\""type\"":\""Status\""},{\""name\"":\""hash\"",\""default\"":\""0000000000000000\"",\""type\"":\""MD5\""},{\""name\"":\""nullableHash\"",\""default\"":null,\""type\"":[\""null\"",\""MD5\""],\""aliases\"":[\""hh\"",\""hsh\""]},{\""name\"":\""value\"",\""default\"":\""NaN\"",\""type\"":\""double\""},{\""name\"":\""average\"",\""default\"":\""-Infinity\"",\""type\"":\""float\""},{\""name\"":\""d\"",\""default\"":0,\""type\"":{\""type\"":\""int\"",\""logicalType\"":\""date\""}},{\""name\"":\""t\"",\""default\"":0,\""type\"":{\""type\"":\""int\"",\""logicalType\"":\""time-millis\""}},{\""name\"":\""l\"",\""default\"":0,\""type\"":\""long\""},{\""name\"":\""prop\"",\""default\"":null,\""type\"":[\""null\"",\""string\""]}],\""my-property\"":{\""key\"":3}},{\""type\"":\""error\"",\""name\"":\""TestError\"",\""namespace\"":\""org.apache.avro.test\"",\""fields\"":[{\""name\"":\""message\"",\""type\"":\""string\""}]}],\""messages\"":{\""hello\"":{\""doc\"":\""method 'hello' takes @parameter 'greeting'\"",\""request\"":[{\""name\"":\""greeting\"",\""type\"":\""string\""}],\""response\"":\""string\""},\""echo\"":{\""request\"":[{\""name\"":\""record\"",\""default\"":{\""name\"":\""bar\"",\""kind\"":\""BAR\""},\""type\"":\""TestRecord\""}],\""response\"":\""TestRecord\""},\""add\"":{\""doc\"":\""method 'add' takes @parameter 'arg1' @parameter 'arg2'\"",\""request\"":[{\""name\"":\""arg1\"",\""type\"":\""int\""},{\""name\"":\""arg2\"",\""default\"":0,\""type\"":\""int\""}],\""response\"":\""int\""},\""echoBytes\"":{\""request\"":[{\""name\"":\""data\"",\""type\"":\""bytes\""}],\""response\"":\""bytes\""},\""error\"":{\""request\"":[],\""response\"":\""null\"",\""errors\"":[\""TestError\""]},\""ping\"":{\""request\"":[],\""response\"":\""null\"",\""one-way\"":true}}}"");

        public Protocol Protocol { get; } = _protocol;

        public void Request(ICallbackRequestor requestor, string messageName, object[] args, object callback)
        {
            switch (messageName)
            {
                case ""hello"":
                    requestor.Request<string>(messageName, args, callback);
                    break;
                case ""echo"":
                    requestor.Request<TestRecord>(messageName, args, callback);
                    break;
                case ""add"":
                    requestor.Request<int>(messageName, args, callback);
                    break;
                case ""echoBytes"":
                    requestor.Request<byte[]>(messageName, args, callback);
                    break;
                case ""error"":
                    requestor.Request<object>(messageName, args, callback);
                    break;
                case ""ping"":
                    requestor.Request<object>(messageName, args, callback);
                    break;
            }
        }

        /// <summary>
        /// method 'hello' takes @parameter 'greeting'
        /// </summary>
        public abstract string hello(string greeting);

        public abstract TestRecord echo(TestRecord record);

        /// <summary>
        /// method 'add' takes @parameter 'arg1' @parameter 'arg2'
        /// </summary>
        public abstract int add(int arg1, int arg2);

        public abstract byte[] echoBytes(byte[] data);

        public abstract void error();

        public abstract void ping();
    }
}";

        Assert.That(result, Is.EqualTo(expected).IgnoreLineEndingFormat);
    }
}