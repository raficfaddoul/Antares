using System;
using System.Collections.Generic;
using System.Linq;
using System.IO.Ports;

namespace AliantWebS.Pages.MonitoringPages
{

    public enum AccelerometerRange
    {
        /// <summary>
        /// Accelerometer range enumeration
        /// </summary>
        PM_2G = 0,
        PM_4G = 1,
        PM_8G = 2
    }

    public enum AccelerometerHighPassFilter
    {
        /// <summary>
        /// Accelerometer high pass filter enumeration
        /// </summary>
        NO_FILTER = 0,
        FC_24_7_ODR = 1,
        FC_6_2084_ODR = 2,
        FC_1_5545_ODR = 3,
        FC_0_3862_ODR = 4,
        FC_0_0964_ODR = 5,
        FC_0_0238_ODR = 6
    }

    public enum AccelerometerOutputDataRate
    {
        /// <summary>
        /// Accelerometer output data rate enumeration
        /// </summary>
        HZ_4000 = 0,
        HZ_2000 = 1,
        HZ_1000 = 2,
        HZ_500 = 3,
        HZ_250 = 4,
        HZ_125 = 5,
        HZ_62_5 = 6,
        HZ_31_25 = 7,
        HZ_15_625 = 8,
        HZ_7_813 = 9,
        HZ_3_906 = 10
    }

    public enum CommandType
    {
        PING = 0,
        RESET = 1,
        RANGE = 2,
        FILTER = 3,
        RATE = 4,
        SYNC = 5,
        START = 6,
        STOP = 7,
        ID = 8
    }

    public class Command
    {
        public CommandType Type { get; private set; }
        public int? Param { get; private set; }

        private const byte CommandFlagBits = 0b1110_0000;
        private const byte CommandSetFlag = 0b0001_0000;

        public Command(CommandType type, int? param = null)
        {
            Type = type;
            if (type == CommandType.SYNC && param == null)
            {
                throw new ArgumentException("The command SYNC requires a parameter");
            }
            Param = param;
        }

        public byte[] GetBytes()
        {
            byte header = (byte)Type;
            header |= CommandFlagBits;

            if (Param == null)
            {
                return new byte[] { header };
            }
            else
            {
                header |= CommandSetFlag;
            }

            byte[] payload = null;
            if (Type == CommandType.RATE || Type == CommandType.RANGE || Type == CommandType.FILTER)
            {
                payload = new byte[] { (byte)Param.Value };
            }
            else if (Type == CommandType.SYNC)
            {
                payload = BitConverter.GetBytes((long)Param.Value);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(payload);
                }
            }

            byte[] result = new byte[1 + (payload?.Length ?? 0)];
            result[0] = header;
            if (payload != null)
            {
                Array.Copy(payload, 0, result, 1, payload.Length);
            }

            return result;
        }

        public override string ToString()
        {
            return $"{Type} ({Param})";
        }
    }

    public enum EventType
    {
        OK = 0,
        RANGE = 1,
        FILTER = 2,
        RATE = 3,
        ID = 4,
        DATA = 5,
        READY = 6,

        ERR_SERIAL_OVERFLOW = 16,
        ERR_SERIAL_TIMEOUT = 17,
        ERR_BAD_FORMAT = 18,
        ERR_UNKNOWN_COMMAND = 19,
        ERR_CANT_RESET = 20,
        ERR_UNSUPPORTED_RANGE = 21,
        ERR_CANT_SET_RANGE = 22,
        ERR_UNSUPPORTED_FILTER = 23,
        ERR_CANT_SET_FILTER = 24,
        ERR_UNSUPPORTED_RATE = 25,
        ERR_CANT_SET_RATE = 26,
        ERR_CANT_START = 27,
        ERR_CANT_STOP = 28,
        ERR_FIFO_OVERRUN = 29
    }

    public static class EventTypeExtensions
    {
        public static bool IsError(this EventType eventType)
        {
            return Enum.GetValues(typeof(EventType)).Cast<EventType>()
                       .Where(e => e.ToString().StartsWith("ERR_"))
                       .Contains(eventType);
        }

        public static bool IsRuntimeError(this EventType eventType)
        {
            return eventType == EventType.ERR_FIFO_OVERRUN || eventType == EventType.ERR_BAD_FORMAT;
        }

        public static IEnumerable<EventType> ErrorRange()
        {
            return Enum.GetValues(typeof(EventType)).Cast<EventType>()
                       .Where(e => e.ToString().StartsWith("ERR_"));
        }

        public static IEnumerable<EventType> EventRange()
        {
            return Enum.GetValues(typeof(EventType)).Cast<EventType>()
                       .Where(e => !e.ToString().StartsWith("ERR_"));
        }
    }

    public class MalformedException : Exception
    {
        public MalformedException() : base() { }

        public MalformedException(string message) : base(message) { }

        public MalformedException(string message, Exception innerException)
            : base(message, innerException) { }
    }

    public class EmbeddedRuntimeError : Exception
    {
        public EmbeddedRuntimeError() : base() { }

        public EmbeddedRuntimeError(string message) : base(message) { }

        public EmbeddedRuntimeError(string message, Exception innerException)
            : base(message, innerException) { }
    }

    public class Event
    {
        public EventType Type { get; set; }
        public byte[] Payload { get; set; }

        public const byte EventFlag = 0b1010_0000;

        public const byte EventTypeMask = 0b0001_1111;
        public const byte EventFlagMask = 0b1110_0000;

        public static readonly Dictionary<EventType, int> PayloadSizeLookup = new Dictionary<EventType, int>
        {
            { EventType.OK, 0 },
            { EventType.RANGE, 1 },
            { EventType.FILTER, 1 },
            { EventType.RATE, 1 },
            { EventType.ID, 4 },
            { EventType.DATA, 20 },
            { EventType.READY, 0 },
            { EventType.ERR_SERIAL_OVERFLOW, 0 },
            { EventType.ERR_SERIAL_TIMEOUT, 0 },
            { EventType.ERR_BAD_FORMAT, 0 },
            { EventType.ERR_UNKNOWN_COMMAND, 0 },
            { EventType.ERR_CANT_RESET, 4 },
            { EventType.ERR_UNSUPPORTED_RANGE, 0 },
            { EventType.ERR_CANT_SET_RANGE, 4 },
            { EventType.ERR_UNSUPPORTED_FILTER, 0 },
            { EventType.ERR_CANT_SET_FILTER, 4 },
            { EventType.ERR_UNSUPPORTED_RATE, 0 },
            { EventType.ERR_CANT_SET_RATE, 4 },
            { EventType.ERR_CANT_START, 4 },
            { EventType.ERR_CANT_STOP, 4 },
            { EventType.ERR_FIFO_OVERRUN, 0 },
        };

        public static readonly Dictionary<EventType, string> PayloadTypeLookup = new Dictionary<EventType, string>
        {
            { EventType.RANGE, "unsigned" },
            { EventType.FILTER, "unsigned" },
            { EventType.RATE, "unsigned" },
            { EventType.ID, "unsigned" },
            { EventType.DATA, "data" },
            { EventType.ERR_CANT_RESET, "signed" },
            { EventType.ERR_CANT_SET_RANGE, "signed" },
            { EventType.ERR_CANT_SET_FILTER, "signed" },
            { EventType.ERR_CANT_SET_RATE, "signed" },
            { EventType.ERR_CANT_START, "signed" },
            { EventType.ERR_CANT_STOP, "signed" },
        };

        public Event(byte header, byte[] payload)
        {
            ValidateHeader(header);
            Type = (EventType)(header & EventFlagMask);
            Payload = payload;
        }

        public static int ParseUIntPayload(byte[] payload)
        {
            return BitConverter.ToInt32(payload.Reverse().ToArray(), 0);
        }

        public static int ParseIntPayload(byte[] payload)
        {
            return BitConverter.ToInt32(payload.Reverse().ToArray(), 0);
        }

        public static (int, int, int, int) ParseDataPayload(byte[] payload)
        {
            int ts = BitConverter.ToInt32(payload.Take(4).Reverse().ToArray(), 0);
            int x = BitConverter.ToInt32(payload.Skip(4).Take(4).Reverse().ToArray(), 0);
            int y = BitConverter.ToInt32(payload.Skip(8).Take(4).Reverse().ToArray(), 0);
            int z = BitConverter.ToInt32(payload.Skip(12).Take(4).Reverse().ToArray(), 0);

            return (ts, x, y, z);
        }

        public object ParsePayload()
        {
            string payloadType = PayloadTypeLookup[Type];
            switch (payloadType)
            {
                case "unsigned":
                    return ParseUIntPayload(Payload);
                case "signed":
                    return ParseIntPayload(Payload);
                case "data":
                    return ParseDataPayload(Payload);
                default:
                    return null;
            }
        }

        public static int PayloadSize(byte header)
        {
            EventType type = (EventType)(header & EventFlagMask);
            return PayloadSizeLookup[type];
        }

        public static void ValidateHeader(byte header)
        {
            if ((header & EventFlagMask) != EventFlag)
                throw new MalformedException("Malformed header: start flag bits are wrong");

            EventType type = (EventType)(header & EventFlagMask);
            if (!EventTypeExtensions.EventRange().Contains(type) && !EventTypeExtensions.ErrorRange().Contains(type))
                throw new MalformedException("Malformed header: Unknown event type");
        }

        public override string ToString()
        {
            string typeName = Type.ToString().Replace("ERR_", "");
            return $"{typeName}({ParsePayload()})";
        }
    }

    public class Accelerometer
    {
        private SerialPort _port;
        private HashSet<Action<long, int, int, int>> _listeners;
        private bool _ready;
        private int _range;
        private int _hpf;
        private int _odr;
        private int _id;

        public Accelerometer(string serialPort)
        {
            _port = new SerialPort(serialPort, 115200);
            _port.ReadTimeout = 5000;
            _listeners = new HashSet<Action<long, int, int, int>>();
            _ready = false;
            _range = -1;
            _hpf = -1;
            _odr = -1;
            _id = -1;
        }

        public void AddListener(Action<long, int, int, int> listener)
        {
            _listeners.Add(listener);
        }

        public void RemoveListener(Action<long, int, int, int> listener)
        {
            _listeners.Remove(listener);
        }

        public void Ping()
        {
            SendCommand(new Command(CommandType.PING));
        }

        public void Reset()
        {
            SendCommand(new Command(CommandType.RESET));
        }

        //  private AccelerometerRange _range;
        //  private AccelerometerHighPassFilter _hpf;

        public AccelerometerRange Range
        {
            get
            {
                SendCommand(new Command(CommandType.RANGE));
                // Assuming the command updates the _range field
                return (AccelerometerRange)_range;
            }
            set
            {
                int rangeValue = (int)value;
                SendCommand(new Command(CommandType.RANGE, rangeValue));
                if ((AccelerometerRange)_range != value)
                {
                    throw new InvalidOperationException($"Was expecting RANGE={rangeValue}, got RANGE={(int)_range}");
                }
            }
        }

        public AccelerometerHighPassFilter HPF
        {
            get
            {
                SendCommand(new Command(CommandType.FILTER));
                // Assuming the command updates the _hpf field
                return (AccelerometerHighPassFilter)_hpf;
            }
            set
            {
                int hpfValue = (int)value;
                SendCommand(new Command(CommandType.FILTER, hpfValue));
                if ((AccelerometerHighPassFilter)_hpf != value)
                {
                    throw new InvalidOperationException($"Was expecting HPF={hpfValue}, got HPF={(int)_hpf}");
                }
            }
        }

        public AccelerometerOutputDataRate ODR
        {
            get
            {
                SendCommand(new Command(CommandType.RATE));
                // Assuming the command updates the _odr field
                return (AccelerometerOutputDataRate)_odr;
            }
            set
            {
                int odrValue = (int)value;
                SendCommand(new Command(CommandType.RATE, odrValue));
                if ((AccelerometerOutputDataRate)_odr != value)
                {
                    throw new InvalidOperationException($"Was expecting ODR={odrValue}, got ODR={(int)_odr}");
                }
            }
        }



        public void StartMeasurements()
        {
            SendCommand(new Command(CommandType.START));
        }

        public void StopMeasurements()
        {
            SendCommand(new Command(CommandType.STOP));
        }

        public void SyncClock()
        {
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            SendCommand(new Command(CommandType.SYNC, (int)timestamp));
        }

        public int Address
        {
            get
            {
                SendCommand(new Command(CommandType.ID));
                // Assuming the command updates the _id field
                return _id;
            }
        }


        private Event ReadEvent()
        {
            int header = _port.ReadByte();
            if (header == -1)
            {
                throw new TimeoutException("read_event timeout");
            }

            Event.ValidateHeader((byte)header);

            int payloadSize = Event.PayloadSize((byte)header);
            byte[] payload = new byte[payloadSize];
            if (payloadSize > 0)
            {
                int bytesRead = _port.Read(payload, 0, payloadSize);
                if (bytesRead != payloadSize)
                {
                    throw new InvalidOperationException("Incomplete payload read");
                }
            }

            return new Event((byte)header, payload);
        }


        private void HandleSample(Event eventt)
        {
            var (t, x, y, z) = Event.ParseDataPayload(eventt.Payload);
            foreach (var listener in _listeners)
            {
                listener(t, x, y, z);
            }
        }

        private void HandleEvent(Event evt)
        {
            switch (evt.Type)
            {
                case EventType.RANGE:
                    _range = Event.ParseUIntPayload(evt.Payload);
                    break;
                case EventType.FILTER:
                    _hpf = Event.ParseUIntPayload(evt.Payload);
                    break;
                case EventType.RATE:
                    _odr = Event.ParseUIntPayload(evt.Payload);
                    break;
                case EventType.ID:
                    _id = Event.ParseUIntPayload(evt.Payload);
                    break;
                case EventType.DATA:
                    HandleSample(evt);
                    break;
                case EventType.READY:
                    _ready = true;
                    break;
                case EventType.OK:
                    Console.WriteLine("Missed OKAY line: " + evt);
                    break;
                default:
                    if (EventTypeExtensions.IsError(evt.Type))
                    {
                        if (EventTypeExtensions.IsRuntimeError(evt.Type))
                            throw new EmbeddedRuntimeError("Accelerometer Runtime Error: " + evt);
                        else
                            Console.WriteLine("Missed ERROR line: " + evt);
                    }
                    else
                    {
                        Console.WriteLine("Unknown line syntax: " + evt);
                    }
                    break;
            }
        }

        private void SendCommand(Command command)
        {
            _port.Write(command.GetBytes(), 0, command.GetBytes().Length);

            while (true)
            {
                Event evt = ReadEvent();
                if (evt.Type == EventType.OK)
                    return;

                if (EventTypeExtensions.IsError(evt.Type))
                    throw new EmbeddedRuntimeError($"Command {command} failed to execute, error {evt}");

                HandleEvent(evt);
            }
        }

        public void ResilientReset()
        {
            _port.Write(new Command(CommandType.RESET).GetBytes(), 0, new Command(CommandType.RESET).GetBytes().Length);

            while (true)
            {
                try
                {
                    Event evt = ReadEvent();
                    if (evt.Type == EventType.OK)
                        return;

                    if (EventTypeExtensions.IsError(evt.Type))
                        throw new InvalidOperationException($"Reset failed to execute, error {evt}");

                    HandleEvent(evt);
                }
                catch (MalformedException)
                {
                    continue;
                }
            }
        }

        public void HandleEvents()
        {
            Event evt = ReadEvent();
            HandleEvent(evt);
        }

        public void Close()
        {
            if (_port.IsOpen)
            {
                _port.Close();
            }
        }



    }




}
