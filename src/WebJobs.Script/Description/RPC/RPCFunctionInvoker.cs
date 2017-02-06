using Google.Protobuf;
using Microsoft.Azure.Functions.Messages;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Description.RPC;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    class RPCFunctionInvoker : ScriptFunctionInvokerBase
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        private readonly ScriptHost _host;
        private readonly RPCHost _rpcHost;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        private readonly string _scriptFilePath;
        private readonly string _functionName;

        private readonly Collection<FunctionBinding> _inputBindings;
        private readonly Collection<FunctionBinding> _outputBindings;
        private readonly BindingMetadata _trigger;


        internal RPCFunctionInvoker(ScriptHost host, BindingMetadata trigger, FunctionMetadata functionMetadata,
            Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings, ITraceWriterFactory traceWriterFactory = null)
            : base(host, functionMetadata, traceWriterFactory)
        {
            _host = host;
            _trigger = trigger;
            _scriptFilePath = functionMetadata.ScriptFile;
            _functionName = functionMetadata.Name;
            _inputBindings = inputBindings;
            _outputBindings = outputBindings;

            _rpcHost = new RPCHost("tcp://127.0.0.1:5559", ">tcp://127.0.0.1:5557");
        }
        
        protected override async Task InvokeCore(object[] parameters, FunctionInvocationContext context)
        {
            object input = parameters[0];
            Guid invocationId = context.ExecutionContext.InvocationId;
            DataType dataType = _trigger.DataType ?? DataType.String;

            object convertedInput = ConvertInput(input);
            Utility.ApplyBindingData(convertedInput, context.Binder.BindingData);
            Dictionary<string, object> exeuctionContext = CreateScriptExecutionContext(input, dataType, context);
            var bindingData = (Dictionary<string, object>)exeuctionContext["bindingData"];
            bindingData["InvocationId"] = invocationId;

            Dictionary<string, string> environmentVariables = new Dictionary<string, string>();

            await ProcessInputBindingsAsync(context.Binder, exeuctionContext, bindingData);

            FunctionExecution request = new FunctionExecution();
            request.InvocationId = context.ExecutionContext.InvocationId.ToString();
            request.FunctionName = _functionName;
            foreach(var item in (Dictionary<string, object>)exeuctionContext["bindings"])
            {
                if (item.GetType() == typeof(String))
                {
                    request.Input.Add(item.Key, ByteString.CopyFromUtf8(item.Value.ToString()));
                } else if (item.Value.GetType().FullName.Contains("Generic.Dictionary"))
                {
                    JObject jobject = JObject.FromObject(item.Value);
                    request.Input.Add(item.Key, ByteString.CopyFromUtf8(jobject.ToString()));
                }
                
            }

            FunctionExecution results = await _rpcHost.SendInvocation(request, context.TraceWriter);

            await ProcessOutputBindingsAsync(_outputBindings, input, context.Binder, bindingData, exeuctionContext, results);

        }

        private async Task ProcessInputBindingsAsync(Binder binder, Dictionary<string, object> executionContext, Dictionary<string, object> bindingData)
        {
            var bindings = (Dictionary<string, object>)executionContext["bindings"];

            // create an ordered array of all inputs and add to
            // the execution context. These will be promoted to
            // positional parameters
            List<object> inputs = new List<object>();
            inputs.Add(bindings[_trigger.Name]);

            var nonTriggerInputBindings = _inputBindings.Where(p => !p.Metadata.IsTrigger);
            foreach (var inputBinding in nonTriggerInputBindings)
            {
                BindingContext bindingContext = new BindingContext
                {
                    Binder = binder,
                    BindingData = bindingData,
                    DataType = inputBinding.Metadata.DataType ?? DataType.String,
                    Cardinality = inputBinding.Metadata.Cardinality ?? Cardinality.One
                };
                await inputBinding.BindAsync(bindingContext);

                // Perform any JSON to object conversions if the
                // value is JSON or a JToken
                object value = bindingContext.Value;
                object converted;
                if (TryConvertJson(bindingContext.Value, out converted))
                {
                    value = converted;
                }

                bindings.Add(inputBinding.Metadata.Name, value);
                inputs.Add(value);
            }

            executionContext["_inputs"] = inputs;
        }

        private static async Task ProcessOutputBindingsAsync(Collection<FunctionBinding> outputBindings, object input, Binder binder,
            Dictionary<string, object> bindingData, Dictionary<string, object> scriptExecutionContext, FunctionExecution functionResult)
        {
            if (outputBindings == null)
            {
                return;
            }

            var bindings = (Dictionary<string, object>)scriptExecutionContext["bindings"];
            var returnValueBinding = outputBindings.SingleOrDefault(p => p.Metadata.IsReturn);
            ByteString result = null;
            if (returnValueBinding != null &&
                functionResult.Output.TryGetValue("ScriptConstants.SystemReturnParameterBindingName", out result))
            {
                // if there is a $return binding, bind the entire function return to it
                // if additional output bindings need to be bound, they'll have to use the explicit
                // context.bindings mechanism to set values, not return value.

                bindings[ScriptConstants.SystemReturnParameterBindingName] = result.ToStringUtf8();
            }
            else
            {
                // if the function returned binding values via the function result,
                // apply them to context.bindings
                IDictionary<string, object> functionOutputs = new Dictionary<string, object>();
                foreach(var item in functionResult.Output)
                {
                    functionOutputs.Add(item.Key, item.Value.ToStringUtf8());
                }

                if (functionOutputs != null)
                {
                    foreach (var output in functionOutputs)
                    {
                        bindings[output.Key] = output.Value;
                    }
                }
            }

            foreach (FunctionBinding binding in outputBindings)
            {
                // get the output value from the script
                object value = null;
                bool haveValue = bindings.TryGetValue(binding.Metadata.Name, out value);
                //if (!haveValue && string.Compare(binding.Metadata.Type, "http", StringComparison.OrdinalIgnoreCase) == 0)
                //{
                //    // http bindings support a special context.req/context.res programming
                //    // model, so we must map that back to the actual binding name if a value
                //    // wasn't provided using the binding name itself
                //    haveValue = bindings.TryGetValue("res", out value);
                //}

                // apply the value to the binding
                if (haveValue && value != null)
                {
                    BindingContext bindingContext = new BindingContext
                    {
                        TriggerValue = input,
                        Binder = binder,
                        BindingData = bindingData,
                        Value = value
                    };
                    await binding.BindAsync(bindingContext);
                }
            }
        }

        private Dictionary<string, object> CreateScriptExecutionContext(object input, DataType dataType, FunctionInvocationContext invocationContext)
        {

            var bindings = new Dictionary<string, object>();
            var bind = (Func<object, Task<object>>)(p =>
            {
                IDictionary<string, object> bindValues = (IDictionary<string, object>)p;
                foreach (var bindValue in bindValues)
                {
                    bindings[bindValue.Key] = bindValue.Value;
                }
                return Task.FromResult<object>(null);
            });

            var context = new Dictionary<string, object>()
            {
                { "invocationId", invocationContext.ExecutionContext.InvocationId },
                { "bindings", bindings },
                { "bind", bind }
            };

            //if (!string.IsNullOrEmpty(_entryPoint))
            //{
            //    context["_entryPoint"] = _entryPoint;
            //}

            if (input is HttpRequestMessage)
            {
                // convert the request to a json object
                HttpRequestMessage request = (HttpRequestMessage)input;
                string rawBody = null;
                var requestObject = CreateRequestObject(request, out rawBody);
                input = requestObject;

                if (rawBody != null)
                {
                    requestObject["rawBody"] = rawBody;
                }

                // If this is a WebHook function, the input should be the
                // request body
                HttpTriggerBindingMetadata httpBinding = _trigger as HttpTriggerBindingMetadata;
                if (httpBinding != null &&
                    !string.IsNullOrEmpty(httpBinding.WebHookType))
                {
                    requestObject.TryGetValue("body", out input);
                }

                // make the entire request object available as well
                // this is symmetric with context.res which we also support
                context["req"] = requestObject;
            }
            else if (input is TimerInfo)
            {
                // TODO: Need to generalize this model rather than hardcode
                // so other extensions can also express their Node.js object model
                TimerInfo timerInfo = (TimerInfo)input;
                var inputValues = new Dictionary<string, object>()
                {
                    { "isPastDue", timerInfo.IsPastDue }
                };
                if (timerInfo.ScheduleStatus != null)
                {
                    inputValues["last"] = timerInfo.ScheduleStatus.Last.ToString("s", CultureInfo.InvariantCulture);
                    inputValues["next"] = timerInfo.ScheduleStatus.Next.ToString("s", CultureInfo.InvariantCulture);
                }
                input = inputValues;
            }
            else if (input is Stream)
            {
                FunctionBinding.ConvertStreamToValue((Stream)input, dataType, ref input);
            }

            Utility.ApplyBindingData(input, invocationContext.Binder.BindingData);
            var bindingData = NormalizeBindingData(invocationContext.Binder.BindingData);
            bindingData["invocationId"] = invocationContext.ExecutionContext.InvocationId.ToString();
            context["bindingData"] = bindingData;

            // if the input is json, try converting to an object or array
            object converted;
            if (TryConvertJson(input, out converted))
            {
                input = converted;
            }

            bindings.Add(_trigger.Name, input);

            context.Add("_triggerType", _trigger.Type);

            return context;
        }

        private static Dictionary<string, object> NormalizeBindingData(Dictionary<string, object> bindingData)
        {
            Dictionary<string, object> normalizedBindingData = new Dictionary<string, object>();

            foreach (var pair in bindingData)
            {
                var name = pair.Key;
                var value = pair.Value;
                if (value != null)
                {
                    // we must convert values to types supported by Edge
                    // marshalling as needed
                    value = value.ToString();
                }

                // "camel case" the normally Pascal cased properties by
                // converting the first letter to lower if needed
                // While for binding purposes case doesn't matter,
                // we want to normalize the case to something Node
                // users would expect to reference in code (e.g. "dequeueCount" not "DequeueCount")
                name = Utility.ToLowerFirstCharacter(name);

                normalizedBindingData[name] = value;
            }

            return normalizedBindingData;
        }

        private static Dictionary<string, object> CreateRequestObject(HttpRequestMessage request, out string rawBody)
        {
            rawBody = null;

            // TODO: need to provide access to remaining request properties
            Dictionary<string, object> requestObject = new Dictionary<string, object>();
            requestObject["originalUrl"] = request.RequestUri.ToString();
            requestObject["method"] = request.Method.ToString().ToUpperInvariant();
            requestObject["query"] = request.GetQueryParameterDictionary();

            // since HTTP headers are case insensitive, we lower-case the keys
            // as does Node.js request object
            var headers = request.GetRawHeaders().ToDictionary(p => p.Key.ToLowerInvariant(), p => p.Value);
            requestObject["headers"] = headers;

            // if the request includes a body, add it to the request object 
            if (request.Content != null && request.Content.Headers.ContentLength > 0)
            {
                MediaTypeHeaderValue contentType = request.Content.Headers.ContentType;
                object jsonObject;
                object body = null;
                if (contentType != null)
                {
                    if (contentType.MediaType == "application/json")
                    {
                        body = request.Content.ReadAsStringAsync().Result;
                        if (TryConvertJson((string)body, out jsonObject))
                        {
                            // if the content - type of the request is json, deserialize into an object or array
                            rawBody = (string)body;
                            body = jsonObject;
                        }
                    }
                    else if (contentType.MediaType == "application/octet-stream")
                    {
                        body = request.Content.ReadAsByteArrayAsync().Result;
                    }
                }

                if (body == null)
                {
                    // if we don't have a content type, default to reading as string
                    body = rawBody = request.Content.ReadAsStringAsync().Result;
                }

                requestObject["body"] = body;
            }

            // Apply any captured route parameters to the params collection
            object value = null;
            if (request.Properties.TryGetValue(ScriptConstants.AzureFunctionsHttpRouteDataKey, out value))
            {
                Dictionary<string, object> routeData = (Dictionary<string, object>)value;
                requestObject["params"] = routeData;
            }

            return requestObject;
        }


        /// <summary>
        /// If the specified input is a JSON string, an array of JSON strings, or JToken, attempt to deserialize it into
        /// an object or array.
        /// </summary>
        internal static bool TryConvertJson(object input, out object result)
        {
            if (input is JToken)
            {
                input = input.ToString();
            }

            result = null;
            string inputString = input as string;
            string[] inputStrings = input as string[];
            if (inputString == null && inputStrings == null)
            {
                return false;
            }

            if (Utility.IsJson(inputString))
            {
                // if the input is json, try converting to an object or array
                if (TryDeserializeJsonObjectOrArray(inputString, out result))
                {
                    return true;
                }
            }
            else if (inputStrings != null && inputStrings.All(p => Utility.IsJson(p)))
            {
                // if the input is an array of json strings, try converting to
                // an array
                object[] results = new object[inputStrings.Length];
                for (int i = 0; i < inputStrings.Length; i++)
                {
                    if (TryDeserializeJsonObjectOrArray(inputStrings[i], out result))
                    {
                        results[i] = result;
                    }
                    else
                    {
                        return false;
                    }
                }
                result = results;
                return true;
            }

            return false;
        }

        private static bool TryDeserializeJsonObjectOrArray(string json, out object result)
        {
            result = null;

            // if the input is json, try converting to an object or array
            ExpandoObject obj;
            ExpandoObject[] objArray;
            if (TryDeserializeJson(json, out obj))
            {
                result = obj;
                return true;
            }
            else if (TryDeserializeJson(json, out objArray))
            {
                result = objArray;
                return true;
            }

            return false;
        }

        private static bool TryDeserializeJson<TResult>(string json, out TResult result)
        {
            result = default(TResult);

            try
            {
                result = JsonConvert.DeserializeObject<TResult>(json);
                return true;
            }
            catch
            {
                return false;
            }
        }



    }
}
