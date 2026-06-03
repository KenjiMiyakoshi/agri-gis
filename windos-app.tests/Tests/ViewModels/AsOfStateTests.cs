using AgriGis.Desktop.ViewModels;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.ViewModels;

// E'204 (WE'2): AsOfState の振る舞いを unit test 化。
public sealed class AsOfStateTests
{
    [Fact]
    public void Initial_State_IsCurrentMode()
    {
        var s = new AsOfState();
        Assert.Null(s.Current);
        Assert.False(s.IsReadOnly);
    }

    [Fact]
    public void SetEnabled_True_SetsDefaultValue_And_FiresChanged()
    {
        var s = new AsOfState();
        DateOnly? received = null;
        int callCount = 0;
        s.Changed += (_, v) => { received = v; callCount++; };

        var today = new DateOnly(2026, 6, 3);
        s.SetEnabled(true, today);

        Assert.Equal(today, s.Current);
        Assert.True(s.IsReadOnly);
        Assert.Equal(today, received);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void SetEnabled_False_FromEnabledState_GoesBackToCurrentMode()
    {
        var s = new AsOfState();
        s.SetEnabled(true, new DateOnly(2026, 6, 3));
        int callCount = 0;
        s.Changed += (_, _) => callCount++;

        s.SetEnabled(false, new DateOnly(2026, 6, 3));

        Assert.Null(s.Current);
        Assert.False(s.IsReadOnly);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void SetValue_UpdatesAndFires()
    {
        var s = new AsOfState();
        s.SetEnabled(true, new DateOnly(2026, 6, 3));
        int callCount = 0;
        DateOnly? received = null;
        s.Changed += (_, v) => { received = v; callCount++; };

        s.SetValue(new DateOnly(2025, 1, 1));

        Assert.Equal(new DateOnly(2025, 1, 1), s.Current);
        Assert.Equal(new DateOnly(2025, 1, 1), received);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void SetValue_SameValue_DoesNotFire()
    {
        var s = new AsOfState();
        var d = new DateOnly(2026, 6, 3);
        s.SetEnabled(true, d);
        int callCount = 0;
        s.Changed += (_, _) => callCount++;

        s.SetValue(d);

        Assert.Equal(0, callCount);
    }
}
