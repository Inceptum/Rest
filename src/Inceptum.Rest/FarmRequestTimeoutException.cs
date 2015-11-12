using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Inceptum.Rest
{
    public class FarmRequestTimeoutException : Exception
    {
        public NodeRequestResult[] Attempts { get; private set; }

        public FarmRequestTimeoutException()
        {
        }

        public FarmRequestTimeoutException(string message, IEnumerable<NodeRequestResult> attempts = null)
            : base(message)
        {
            Attempts = attempts != null ? attempts.ToArray() : new NodeRequestResult[0];
        }

        public FarmRequestTimeoutException(string message, Exception innerException, IEnumerable<NodeRequestResult> attempts = null)
            : base(message, innerException)
        {
            Attempts = attempts != null ? attempts.ToArray() : new NodeRequestResult[0];
        }

        protected FarmRequestTimeoutException(SerializationInfo info, StreamingContext context, IEnumerable<NodeRequestResult> attempts = null)
            : base(info, context)
        {
            Attempts = attempts != null ? attempts.ToArray() : new NodeRequestResult[0];
        }
    }
}