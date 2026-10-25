namespace Akagi.Web.Services.Circuits;

public interface ICircuitIdAccessor
{
    string CircuitId { get; }
    event Action? CircuitOpened;
}

public class CircuitIdAccessor : ICircuitIdAccessor
{
    public string CircuitId { get; private set; } = string.Empty;
    public event Action? CircuitOpened;

    public void SetCircuitId(string circuitId)
    {
        CircuitId = circuitId;
        CircuitOpened?.Invoke();
    }
}
