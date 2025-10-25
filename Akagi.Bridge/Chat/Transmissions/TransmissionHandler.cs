using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions;

public abstract class TransmissionHandler
{
    public abstract string HandlesType { get; }

    public T GetTransmission<T>(TransmissionWrapper transmissionWrapper) where T : Transmission
    {
        if (transmissionWrapper is null || transmissionWrapper.Payload is null)
        {
            throw new ArgumentNullException(nameof(transmissionWrapper));
        }
        if (transmissionWrapper.MessageType != HandlesType)
        {
            throw new InvalidOperationException($"Transmission type mismatch: expected {HandlesType}, got {transmissionWrapper.MessageType}.");
        }

        return MessagePackSerializer.Deserialize<T>(transmissionWrapper.Payload)
               ?? throw new InvalidOperationException($"Failed to deserialize transmission of type {HandlesType}.");
    }
}
