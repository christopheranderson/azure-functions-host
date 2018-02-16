// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Binding;
using Xunit;

namespace Microsoft.WebJobs.Script.Tests
{
    public class HttpTestHelpers
    {
        public static HttpRequest CreateHttpRequest(string method, string uriString, IHeaderDictionary headers = null, object body = null)
        {
            var uri = new Uri(uriString);
            var request = new DefaultHttpContext().Request;
            var requestFeature = request.HttpContext.Features.Get<IHttpRequestFeature>();
            requestFeature.Method = method;
            requestFeature.Scheme = uri.Scheme;
            requestFeature.Path = uri.GetComponents(UriComponents.KeepDelimiter | UriComponents.Path, UriFormat.Unescaped);
            requestFeature.PathBase = string.Empty;
            requestFeature.QueryString = uri.GetComponents(UriComponents.KeepDelimiter | UriComponents.Query, UriFormat.Unescaped);

            headers = headers ?? new HeaderDictionary();

            if (!string.IsNullOrEmpty(uri.Host))
            {
                headers.Add("Host", uri.Host);
            }

            if (body != null)
            {
                byte[] bytes = null;
                if (body is string bodyString)
                {
                    bytes = Encoding.UTF8.GetBytes(bodyString);
                }
                else if (body is byte[] bodyBytes)
                {
                    bytes = bodyBytes;
                }

                requestFeature.Body = new MemoryStream(bytes);
                request.ContentLength = request.Body.Length;
                headers.Add("Content-Length", request.Body.Length.ToString());
            }

            requestFeature.Headers = headers;

            return request;
        }

        public static Task<string> Response<Req>(ScriptHost host, Req content, string contentType, string expectedContentType = null)
        {
            return CreateTest(host, content, contentType, false, false, expectedContentType);
        }

        public static Task<string> Return<Req>(ScriptHost host, Req content, string contentType, string expectedContentType = null)
        {
            return CreateTest(host, content, contentType, false, true, expectedContentType);
        }

        public static Task<string> Raw<Req>(ScriptHost host, Req content, string contentType, string expectedContentType = null)
        {
            return CreateTest(host, content, contentType, true, false, expectedContentType);
        }

        public static async Task<string> CreateTest<Req>(ScriptHost host, Req content, string contentType, bool isRaw, bool isReturn, string expectedContentType = null)
        {
            IHeaderDictionary headers = new HeaderDictionary();

            if (!String.IsNullOrEmpty(expectedContentType)) {
                headers.Add("accept", expectedContentType);
            }
            headers.Add("type", contentType);
            headers.Add("scenario", "content");
            if (isRaw)
            {
                headers.Add("raw", "true");
            }
            if (isReturn)
            {
                headers.Add("return", "true");
            }

            HttpRequest request = HttpTestHelpers.CreateHttpRequest("POST", "http://localhost/api/httptrigger", headers, content);

            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { "req", request }
            };
            await host.CallAsync("HttpTrigger-Scenarios", arguments);

            var result = (IActionResult)request.HttpContext.Items[ScriptConstants.AzureFunctionsHttpResponseKey];

            switch (result)
            {
                case RawScriptResult rawResult:
                    Assert.NotNull(rawResult);
                    Assert.Equal(contentType, rawResult.Headers["content-type"].ToString());
                    Assert.Equal(200, rawResult.StatusCode);
                    return rawResult.Content.ToString();
                case ObjectResult objResult:
                    Assert.NotNull(objResult);
                    Assert.Equal(contentType, objResult.ContentTypes[0]);
                    Assert.Equal(200, objResult.StatusCode);
                    if (content is byte[])
                    {
                        Assert.Equal(System.Text.Encoding.UTF8.GetString(content as byte[]), objResult.Value);
                    }
                    else
                    {
                        Assert.Equal(content.ToString(), objResult.Value);
                    }
                    return objResult.Value.ToString();
                default:
                    throw new NotImplementedException("Unknown implementation of IActionResult");
            }
        }
    }
}
