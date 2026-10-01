using System.Security.Principal;

namespace OpenLimiter.Windows.Security;

public static class AdministratorStatus
{
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}

