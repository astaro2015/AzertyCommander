namespace AzertyCommander;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        if (args.Any(arg => string.Equals(arg, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.Exit(SelfTest.Run() ? 0 : 1);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }    
}
