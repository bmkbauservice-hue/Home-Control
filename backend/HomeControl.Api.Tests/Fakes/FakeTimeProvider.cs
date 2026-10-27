namespace HomeControl.Api.Tests.Fakes;

// Uhr für Tests: steht still, bis der Test sie mit Advance vorstellt.
public class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan time) => now += time;
}
