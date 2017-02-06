using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Binding;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal class RPCFunctionDescriptorProvider : FunctionDescriptorProvider
    {
        public RPCFunctionDescriptorProvider(ScriptHost host, ScriptHostConfiguration config) 
            : base(host, config)
        {
        }

        public override bool TryCreate(FunctionMetadata functionMetadata, out FunctionDescriptor functionDescriptor)
        {
            if(functionMetadata == null)
            {
                throw new ArgumentNullException("functionMetadata");
            }

            functionDescriptor = null;

            if(functionMetadata.ScriptType != ScriptType.RPC)
            {
                return false;
            }

            return base.TryCreate(functionMetadata, out functionDescriptor);

        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        protected override IFunctionInvoker CreateFunctionInvoker(string scriptFilePath, BindingMetadata triggerMetadata, FunctionMetadata functionMetadata, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
        {
            return new RPCFunctionInvoker(Host, triggerMetadata, functionMetadata, inputBindings, outputBindings);
        }
    }
}
