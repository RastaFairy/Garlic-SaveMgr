using GarlicSaveMgr.Infrastructure;
using Xunit;

namespace GarlicSaveMgr.Tests;

public sealed class InitialConnectionGateTests
{
    [Fact]
    public void Warning_IsSuppressedFromConstructionUntilInitialConnectionEnds()
    {
        var gate = new InitialConnectionGate();

        // Arranque: la ventana se construye antes de conocer el resultado de la
        // búsqueda de consola, así que el aviso arranca suprimido.
        Assert.True(gate.SuppressConsoleWarning);

        using (gate.Begin())
        {
            Assert.True(gate.SuppressConsoleWarning);
        }

        Assert.False(gate.SuppressConsoleWarning);
    }

    [Fact]
    public void ReconnectionScope_SuppressesWarningAgainUntilItEnds()
    {
        var gate = new InitialConnectionGate();
        using (gate.Begin()) { }
        Assert.False(gate.SuppressConsoleWarning);

        // Cambio de perfil/ajustes: nueva conexión en curso vuelve a suprimir el aviso.
        using (gate.Begin())
        {
            Assert.True(gate.SuppressConsoleWarning);
        }

        Assert.False(gate.SuppressConsoleWarning);
    }

    [Fact]
    public void ScopeDispose_IsIdempotent()
    {
        var gate = new InitialConnectionGate();

        var scope = gate.Begin();
        scope.Dispose();
        scope.Dispose();

        Assert.False(gate.SuppressConsoleWarning);
    }
}
