using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Akagi.Web.Services.Circuits;

public class CircuitIdHandler : CircuitHandler
{
    private readonly CircuitIdAccessor _circuitIdAccessor;

    public CircuitIdHandler(ICircuitIdAccessor circuitIdAccessor)
    {
        _circuitIdAccessor = circuitIdAccessor as CircuitIdAccessor ?? throw new ArgumentNullException(nameof(circuitIdAccessor));
    }

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _circuitIdAccessor.SetCircuitId(circuit.Id);

        return base.OnCircuitOpenedAsync(circuit, cancellationToken);
    }
}
