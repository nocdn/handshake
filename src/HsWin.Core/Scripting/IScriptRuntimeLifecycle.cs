namespace HsWin.Core.Scripting;

/// <summary>
/// The lifecycle surface the host drives: reload, emergency stop, and disposal. Lets the host's
/// reload and emergency-stop coordination be tested without a real script engine.
/// </summary>
public interface IScriptRuntimeLifecycle : IDisposable
{
    void ReloadFromFile(string filePath);

    /// <summary>
    /// Interrupts a runaway callback without disposing resources. Safe from any thread and never
    /// waits for a reload in progress.
    /// </summary>
    void InterruptEngineOnly();

    /// <summary>Stops all script automation and disposes everything the config created.</summary>
    void EmergencyStop();
}
