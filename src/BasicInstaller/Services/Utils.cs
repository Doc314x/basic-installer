using System.Security.Principal;

namespace BasicInstaller.Services
{
    public static class Utils
    {
        /// <summary>
        /// Die exe laeuft dank app.manifest (requestedExecutionLevel=requireAdministrator)
        /// bereits elevated, sobald UAC bestaetigt wurde - diese Pruefung ist die
        /// Verteidigungslinie fuer den Fall, dass das Manifest kuenftig mal
        /// entfernt/umgangen wird. Fail loud statt stiller Fehlinstallationen in
        /// %ProgramFiles% ohne Schreibrechte.
        /// </summary>
        public static bool IsRunningElevated()
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
    }
}
