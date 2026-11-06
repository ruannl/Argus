using System.Runtime.InteropServices;

namespace argus.Server.Imports;

/// <summary>
/// Linux process-group helpers for the PDF parser child. The child joins its
/// own process group so the parent can terminate the whole group even after the
/// root process exits and a descendant still holds an inherited pipe.
/// </summary>
internal static class ParserProcessIsolation
{
    private const int SigKill = 9;

    [DllImport("libc", EntryPoint = "setpgid", SetLastError = true)]
    private static extern int SetProcessGroup(int pid, int pgid);

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int SendSignal(int pid, int signal);

    public static void JoinOwnProcessGroup()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            _ = SetProcessGroup(0, 0);
        }
        catch (DllNotFoundException)
        {
            // The OS resolves libc for every supported deployment; a missing
            // symbol only weakens cleanup, never parsing.
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    public static void KillProcessGroup(int leaderPid)
    {
        if (!OperatingSystem.IsLinux() || leaderPid <= 0)
        {
            return;
        }

        try
        {
            _ = SendSignal(-leaderPid, SigKill);
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }
}
