namespace StreamingIndicatorForDiscord;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\StreamingIndicatorForDiscord", out var isFirstInstance);
        if (!isFirstInstance)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext());
    }
}
