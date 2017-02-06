using Google.Protobuf;
using Microsoft.Azure.Functions.Messages;
using Microsoft.Azure.WebJobs.Host;
using NetMQ;
using NetMQ.Sockets;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Description.RPC
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", MessageId = "RPC")]
    [CLSCompliant(false)]
    public class RPCHost
    {
        RequestSocket reqSocket;
        PullSocket pullSocket;
        Dictionary<String, TraceWriter> traceWriters;
        Thread logThread;


        public RPCHost(string requestAddress, string pullAddress)
        {
            reqSocket = new RequestSocket(requestAddress);
            pullSocket = new PullSocket(pullAddress);
            traceWriters = new Dictionary<String, TraceWriter>();
            logThread = new Thread(ProcessLogs);
            logThread.Start();
        }

        public async void ProcessLogs()
        {
            Console.WriteLine("Listening...");
            while (true)
            {
                try
                {
                    String m = await Task.Factory.StartNew(() => pullSocket.ReceiveFrameString());
                    FunctionLog message = await Task.Factory.StartNew(() => JsonConvert.DeserializeObject<FunctionLog>(m));
                    TraceWriter tr;
                    if (message != null && message.invocationId != null && traceWriters.TryGetValue(message.invocationId, out tr))
                    {
                        if (message.message == "$DIEDIEDIE$")
                        {
                            traceWriters.Remove(message.invocationId);
                        } else
                        {
                            tr.Info(message.message);
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                }
            }
        }

        public async Task<FunctionExecution> SendInvocation(FunctionExecution execution, TraceWriter traceWriter)
        {
            // Send Message
            await Task.Factory.StartNew(() => reqSocket.SendFrame(execution.ToByteArray()));
            traceWriters.Add(execution.InvocationId, traceWriter);
            // Wait for reply
            byte[] response = await Task.Factory.StartNew(() => reqSocket.ReceiveFrameBytes());
            // Return message
            return await Task.Factory.StartNew(() => FunctionExecution.Parser.ParseFrom(response));
        }

        internal class FunctionLog
        {
            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", MessageId = "invocation")]
            public String invocationId { get; set; }
            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", MessageId = "function")]
            public String function { get; set; }
            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", MessageId = "message")]
            public String message { get; set; }
            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", MessageId = "level")]
            public String level { get; set; }
        }
    }
}
