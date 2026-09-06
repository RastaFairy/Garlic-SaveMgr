namespace GarlicSaveMgr.Infrastructure;

/// <summary>
/// Regla de arranque para el aviso de consola no válida: mientras la conexión/detección
/// inicial sigue en curso, "No hay una consola válida conectada" no puede mostrarse.
/// El aviso solo está disponible cuando el intento de conexión ha terminado y sigue
/// sin haber consola utilizable. Las comprobaciones previas a una operación durante
/// esa ventana simplemente no proceden, en silencio.
/// </summary>
public sealed class InitialConnectionGate
{
    private int _suppress = 1;

    public InitialConnectionGate() { }

    /// <summary>Arranca ya suprimido: la ventana aún no ha completado su conexión inicial.</summary>
    public bool SuppressConsoleWarning => Volatile.Read(ref _suppress) != 0;

    /// <summary>
    /// Ámbito de un intento de conexión/detección. Mientras viva, el aviso queda
    /// suprimido; al liberarlo (incluso con excepción) el aviso vuelve a estar disponible.
    /// </summary>
    public IDisposable Begin()
    {
        Interlocked.Exchange(ref _suppress, 1);
        return new Scope(this);
    }

    private void End() => Interlocked.Exchange(ref _suppress, 0);

    private sealed class Scope(InitialConnectionGate gate) : IDisposable
    {
        private InitialConnectionGate? _gate = gate;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _gate, null);
            owner?.End();
        }
    }
}
