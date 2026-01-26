using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;

namespace VoiceImeApp
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

            var config = builder.Build();

            // Initialize the main IME wrapper
            var imeWrapper = new MainImeWrapper(config);
            imeWrapper.Start();

            // Run message loop
            Application.Run();

            imeWrapper.Stop();
        }
    }
}