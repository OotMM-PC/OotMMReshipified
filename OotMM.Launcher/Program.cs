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
        Qt.Application.SetWindowIcon("qrc:/assemblies/OotMM.Launcher/Assets/Icons/App.png");
        Qml.LoadFromRootModule("Main");
        Qml.WaitForExit();
    }
}
