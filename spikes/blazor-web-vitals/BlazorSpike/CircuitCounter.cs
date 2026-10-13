using Microsoft.AspNetCore.Components.Server.Circuits;

namespace BlazorSpike;

/// <summary>Counts open circuits and connected circuits, for the memory and resilience scripts.</summary>
public sealed class CircuitCounter : CircuitHandler
{
    private static int open;
    private static int connected;

    public static int Open => Volatile.Read(ref open);
    public static int Connected => Volatile.Read(ref connected);

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken ct)
    {
        Interlocked.Increment(ref open);
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken ct)
    {
        Interlocked.Decrement(ref open);
        return Task.CompletedTask;
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken ct)
    {
        Interlocked.Increment(ref connected);
        return Task.CompletedTask;
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken ct)
    {
        Interlocked.Decrement(ref connected);
        return Task.CompletedTask;
    }
}
