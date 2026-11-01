using Qt.Quick;

[assembly: Qt.IgnoreType(typeof(OotMM.Core.Seed), Inherited = true)]
[assembly: Qt.IgnoreType(typeof(OotMM.Launcher.Models.ModInfo), Inherited = true)]
[assembly: Qt.IgnoreType(typeof(OotMM.Launcher.Session.SessionCoordinator), Inherited = true)]

namespace OotMM.Launcher;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        Qml.LoadFromRootModule("Main");
        Qml.WaitForExit();
    }
}
