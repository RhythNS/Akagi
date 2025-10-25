namespace Akagi.Web.Services.Circuits;

public interface ICircuitIdAccessor
{
    string CircuitId { get; }
}

public class CircuitIdAccessor : ICircuitIdAccessor
{
    public string CircuitId { get; private set; } = string.Empty;

    public void SetCircuitId(string circuitId)
    {
        CircuitId = circuitId;
    }
}
