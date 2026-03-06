using System;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;

namespace CopilotInput
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Ensure we use the application's directory for config, not the current working directory
            // (which might be System32 if started via Auto-Start)
            string appBaseDir = AppDomain.CurrentDomain.BaseDirectory;

            var builder = new ConfigurationBuilder()
                .SetBasePath(appBaseDir)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            var config = builder.Build();

            // Initialize the main IME wrapper
            var imeWrapper = new CopilotInputWrapper(config);
            imeWrapper.Start();

            // Run message loop
            Application.Run();

            imeWrapper.Stop();
        }
    }
}