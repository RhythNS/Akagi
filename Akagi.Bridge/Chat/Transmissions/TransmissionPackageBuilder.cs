using System.Collections.Concurrent;

namespace Akagi.Bridge.Chat.Transmissions;

public class TransmissionPackageBuilder : IDisposable
{
    private static readonly int headerSize = 12;
    private static readonly int footerSize = 2;
    private static readonly int extraBytes = headerSize + footerSize;
    private static readonly byte[] endMarkers = { 39, 42 };

    private readonly ConcurrentDictionary<int, TransmissionReceiver> _receivers = new();
    private readonly Timer _cleanupTimer;
    private readonly TimeSpan _receiverTimeout = TimeSpan.FromMinutes(2);
    private readonly MemoryStream _buffer = new();
    private readonly int _bufferSize;

    private int _sendCounter = 0;

    public TransmissionPackageBuilder(int bufferSize)
    {
        if (bufferSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferSize), "Buffer size must be greater than zero.");
        }
        _bufferSize = bufferSize;
        _cleanupTimer = new Timer(CleanupStaleReceivers, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public IEnumerable<byte[]> Send(byte[] data)
    {
        int packageId = Interlocked.Increment(ref _sendCounter);

        int position = 0;
        while (position < data.Length)
        {
            int bytesLeft = data.Length - position;
            int bytesToCopy = Math.Min(_bufferSize - extraBytes, bytesLeft);

            byte[] package = new byte[bytesToCopy + extraBytes];

            // header
            BitConverter.GetBytes(packageId).CopyTo(package, 0);
            BitConverter.GetBytes(data.Length).CopyTo(package, 4);
            BitConverter.GetBytes(package.Length).CopyTo(package, 8);

            // copy data
            Buffer.BlockCopy(data, position, package, headerSize, bytesToCopy);

            // footer
            package[package.Length - 2] = endMarkers[0];
            package[package.Length - 1] = endMarkers[1];

            position += bytesToCopy;

            yield return package;
        }
    }

    public bool IsEmpty => _receivers.IsEmpty && _buffer.Length == 0;

    public (bool IsComplete, byte[]? Data) Receive(byte[] buffer, long offset, long size)
    {
        if (_buffer.Length != 0)
        {
            _buffer.Write(buffer, (int)offset, (int)size);
            buffer = _buffer.ToArray();
            _buffer.SetLength(0);
            offset = 0;
            size = buffer.Length;
        }

        if (size < extraBytes)
        {
            if (size > 0)
            {
                _buffer.Write(buffer, (int)offset, (int)size);
            }
            return (false, null);
        }

        while (true)
        {
            if (size < 4)
            {
                if (size > 0)
                {
                    _buffer.Write(buffer, (int)offset, (int)size);
                }
                return (false, null);
            }

            int packageId = BitConverter.ToInt32(buffer, (int)offset);

            TransmissionReceiver? receiver = _receivers.GetOrAdd(packageId, _ => new TransmissionReceiver());
            receiver.LastActivity = DateTime.UtcNow;

            int newOffset = (int)offset;
            int newSize = (int)size;
            TransmissionReceiver.RecieveResult result = receiver.Receive(buffer, ref newOffset, ref newSize);

            switch (result)
            {
                case TransmissionReceiver.RecieveResult.NotEnoughDataForPackage:
                    _buffer.Write(buffer, (int)offset, (int)size);
                    return (false, null);

                case TransmissionReceiver.RecieveResult.Incomplete:
                    offset = newOffset;
                    size = newSize;
                    break;

                case TransmissionReceiver.RecieveResult.Complete:
                    {
                        if (newSize > 0)
                        {
                            _buffer.Write(buffer, newOffset, newSize);
                        }

                        if (_receivers.TryRemove(packageId, out receiver))
                        {
                            byte[] data = receiver.GetData();
                            receiver.Dispose();
                            return (true, data);
                        }

                        break;
                    }
                default:
                    throw new InvalidOperationException($"Unexpected receive result: {result}");
            }
        }
    }

    private void CleanupStaleReceivers(object? state)
    {
        DateTime threshold = DateTime.UtcNow - _receiverTimeout;

        foreach (KeyValuePair<int, TransmissionReceiver> kvp in _receivers)
        {
            if (kvp.Value.LastActivity < threshold &&
                _receivers.TryRemove(kvp.Key, out TransmissionReceiver? receiver))
            {
                receiver.Dispose();
            }
        }
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();

        foreach (TransmissionReceiver receiver in _receivers.Values)
        {
            receiver.Dispose();
        }

        _receivers.Clear();
        _buffer.Dispose();

        GC.SuppressFinalize(this);
    }

    public class TransmissionReceiver : IDisposable
    {
        public DateTime LastActivity { get; set; } = DateTime.UtcNow;

        public enum RecieveResult
        {
            NotEnoughDataForPackage,
            Incomplete,
            Complete,
        }

        private readonly MemoryStream _buffer = new();
        private int _expectedLength = -1;

        public RecieveResult Receive(byte[] buffer, ref int offset, ref int count)
        {
            if (count < extraBytes)
            {
                return RecieveResult.NotEnoughDataForPackage;
            }

            _expectedLength = BitConverter.ToInt32(buffer, offset + 4);
            int packageSize = BitConverter.ToInt32(buffer, offset + 8);

            if (packageSize > count)
            {
                return RecieveResult.NotEnoughDataForPackage;
            }

            int gottenBytes = packageSize - extraBytes;
            _buffer.Write(buffer.AsSpan(offset + headerSize, gottenBytes));

            if (buffer[offset + packageSize - 2] != 39 || buffer[offset + packageSize - 1] != 42)
            {
                throw new InvalidOperationException("Invalid package end markers (expected 39 and 42).");
            }

            offset += packageSize;
            count -= packageSize;

            return _buffer.Length >= _expectedLength ? RecieveResult.Complete : RecieveResult.Incomplete;
        }

        public byte[] GetData()
        {
            if (_buffer.Length < _expectedLength)
            {
                throw new InvalidOperationException($"Data not completely received yet: {_buffer.Length}/{_expectedLength} bytes");
            }

            byte[] fullBuffer = _buffer.ToArray();
            if (fullBuffer.Length > _expectedLength)
            {
                byte[] trimmed = new byte[_expectedLength];
                Buffer.BlockCopy(fullBuffer, 0, trimmed, 0, _expectedLength);
                return trimmed;
            }
            return fullBuffer;
        }

        public void Dispose()
        {
            _buffer.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
