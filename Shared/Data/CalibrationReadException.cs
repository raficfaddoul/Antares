using System;

namespace AliantWebS.Data
{
    /// <summary>
    /// The calibration (C1/C0) of the measurements asked for could not be read, or one of their coefficients is not a
    /// number. The reported values cannot be given then either, so it is an <see cref="InfluxReadException"/>: every
    /// page that shows unreadable measurements shows this the same way, instead of the raw values in their place
    /// (Rafic, 2026-10-22).
    /// </summary>
    public class CalibrationReadException : InfluxReadException
    {
        public CalibrationReadException(string message, Exception? inner = null) : base(message, inner) { }
    }
}
