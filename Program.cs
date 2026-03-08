namespace WolfMixer;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Single instance check
        using var mutex = new System.Threading.Mutex(true, "WolfMixer_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("WolfMixer is already running. Check the system tray.", "WolfMixer", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Logger.Log("WolfMixer starting...");
        Application.Run(new TrayApp());
    }
}
