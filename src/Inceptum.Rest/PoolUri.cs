using System;
using System.Diagnostics;

namespace Inceptum.Rest
{
    [DebuggerDisplay("Address = {Uri} LastAttemptStart={LastAttemptStart} LastAttemptFinish={LastAttemptFinish} IsBeingTested={IsBeingTested} IsValid={IsValid}")]
    class PoolUri
    {
        public PoolUri(Uri uri)
        {
            Uri = uri;
            IsValid = false;
            LastAttemptFinish = 0;
        }

        public Uri Uri { get; private set; }
        public long LastAttemptFinish { get; set; }
        public long LastAttemptStart { get; set; }
        public bool IsBeingTested { get; set; }
        public bool IsValid { get; set; }
    }
}