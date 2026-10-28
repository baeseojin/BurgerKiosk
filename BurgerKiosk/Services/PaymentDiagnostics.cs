using System;
using System.IO;

namespace BurgerKiosk.Services
{
    internal static class PaymentDiagnostics
    {
        private static readonly object Sync = new();
         
        public static void Write(string message)
        {
            try
            {
                lock (Sync)
                {
                    File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "payment-diagnostics.log"),
                        $"{DateTimeOffset.Now:O} pid={Environment.ProcessId} {message}{Environment.NewLine}");
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
