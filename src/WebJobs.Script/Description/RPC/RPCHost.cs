using Google.Protobuf;
using Microsoft.Azure.Functions.Messages;
using NetMQ;
using NetMQ.Sockets;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Description.RPC
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", MessageId = "RPC")]
    [CLSCompliant(false)]
    public class RPCHost
    {
        RequestSocket reqSocket;


        public RPCHost(string requestAddress)
        {
            reqSocket = new RequestSocket(requestAddress);
        }

        public async Task<FunctionExecution> SendInvocation(FunctionExecution execution)
        {
            // Send Message
            await Task.Factory.StartNew(() => reqSocket.SendFrame(execution.ToByteArray()));
            // Wait for reply
            byte[] response = await Task.Factory.StartNew(() => reqSocket.ReceiveFrameBytes());
            // Return message
            return await Task.Factory.StartNew(() => FunctionExecution.Parser.ParseFrom(response));
        }
    }
}
