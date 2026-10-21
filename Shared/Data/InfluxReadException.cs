using System;

namespace AliantWebS.Data
{
    /// <summary>
    /// InfluxDB could not be read: unreachable, refusing the token, without the organisation, timed out, or the query
    /// failed. InfluxService.GetData throws it instead of returning an empty result, and the pages that read
    /// measurements catch exactly this type, so a failure elsewhere (SQL, a bug) is never shown as unreadable data.
    /// </summary>
    public class InfluxReadException : Exception
    {
        public InfluxReadException(string message, Exception? inner = null) : base(message, inner) { }
    }
}
