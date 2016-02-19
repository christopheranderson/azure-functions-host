// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script;
using System.IO;
using Newtonsoft.Json;
using Microsoft.Azure.WebJobs;
using System.Collections.Generic;

namespace WebJobs.Script.Host

{
    class Program
    {
        static void Main(string[] args)
        {
            string rootPath = Environment.CurrentDirectory;
            if (args.Length > 0)
            {
                rootPath = (string)args[0];
            }

            ScriptHostConfiguration config = new ScriptHostConfiguration()
            {
                RootScriptPath = rootPath,
                FileLoggingEnabled = true
            };

            // For local execution, allow reading secrets from a file. 
            string secretsFile = Path.Combine(rootPath, "secrets.json");
            if (File.Exists(secretsFile))
            {
                string json = File.ReadAllText(secretsFile);
                var dict = JsonConvert.DeserializeObject<IDictionary<string, string>>(json);
                config.AppSettings = new DefaultNameResolver(dict);
            }

            ScriptHostManager scriptHostManager = new ScriptHostManager(config);
            scriptHostManager.RunAndBlock();
        }


        // Adapt from Dictionary to INameResolver
        class DefaultNameResolver : INameResolver
        {
            private readonly IDictionary<string, string> _dict;

            public DefaultNameResolver(IDictionary<string, string> dict)
            {
                _dict = dict;
            }

            public string Resolve(string name)
            {
                return _dict[name];
            }
        }
    }
}
