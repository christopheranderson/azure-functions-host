// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Configuration;

namespace Microsoft.Azure.WebJobs.Script
{
    // Default policy will pull from AppSettings and then from Env var. 
    class DefaultAppSettingResolver : INameResolver
    {
        public string Resolve(string name)
        {
            string value = ConfigurationManager.AppSettings[name];
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }

            // Check env var
            value = Environment.GetEnvironmentVariable(name);
            if (value != null)
            {
                return value;
            }
            return null; // caller will detect and fail. 
        }
    }
}
