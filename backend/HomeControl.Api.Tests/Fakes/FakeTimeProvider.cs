namespace HomeControl.Api.Tests.Fakes;

// Uhr für Tests: steht still, bis der Test sie mit Advance vorstellt.
public class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset now;

    public FakeTimeProvider() : this(new DateTimeOffset(2026, 10, 27, 8, 0, 0, TimeSpan.Zero))
    {
    }

    // Startzeit frei wählbar, z. B. für ein Token, das schon in der Vergangenheit abgelaufen ist.
    public FakeTimeProvider(DateTimeOffset start)
    {
        now = start;
    }

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan time) => now += time;
}
