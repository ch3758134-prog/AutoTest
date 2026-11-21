using AutoTest.Desktop.Dbc;
using AutoTest.Projects.Dbc;
using Xunit;

namespace AutoTest.Desktop.Tests.Shell;

public sealed class DbcNumericEditorTests
{
    [Fact]
    public void EditableDecimalIsReadableWithoutSilentlyChangingSourcePrecision()
    {
        var exact = double.BitIncrement(2.56);
        var original = new DbcSignal("Signal", 0, 8, true, false,
            exact, 0.01, 0, 255, "V", "ECU");
        var draft = DbcSignalEditState.FromSignal(original);

        Assert.Equal("2.56", draft.Factor);
        Assert.True(draft.Matches(original));
        Assert.Equal(exact,
            DbcSignalEditState.ResolveEditedNumber(draft.Factor, original.Factor, "因子"));
        Assert.NotEqual(exact,
            DbcSignalEditState.ResolveEditedNumber("2.57", original.Factor, "因子"));
    }

    [Fact]
    public void NonFiniteNumbersAreRejected()
    {
        Assert.Throws<FormatException>(() =>
            DbcSignalEditState.ResolveEditedNumber("NaN", 1, "因子"));
        Assert.Throws<FormatException>(() =>
            DbcSignalEditState.ResolveEditedNumber("Infinity", 1, "因子"));
    }
}
