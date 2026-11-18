using System;

namespace DevTools.Tests;

// A clock the test moves by hand.
internal class FakeTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        return Now;
    }
}
