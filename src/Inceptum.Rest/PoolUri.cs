using System;
using System.Diagnostics;

namespace Inceptum.Rest
{
    class PoolUri
    {
        public PoolUri(Uri uri)
        {
            Uri = uri;
            IsValid = true;
            LastAttemptFinish = 0;
        }

        public Uri Uri { get; private set; }
        public long LastAttemptFinish { get; set; }
        public long LastAttemptStart { get; set; }
        public bool IsBeingTested { get; set; }
        public bool IsValid { get; set; }


        public override string ToString()
        {
            return string.Format(@"Address = {0} IsValid={4}",
                Uri,
                LastAttemptStart > 0 ? TimeSpan.FromMilliseconds(LastAttemptStart).ToString() : "never",
                LastAttemptFinish >= LastAttemptStart ? TimeSpan.FromMilliseconds(LastAttemptFinish).ToString() : "not finished",
                IsBeingTested, IsValid
                );
        }
    }
}