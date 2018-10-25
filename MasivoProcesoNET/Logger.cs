using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MasivoProcesoNET
{
    public static class Logger
    {
        const string _customServiceName = "MasivoProcesoNET";
        const int MaxEventLogEntryLength = 25600;

        public static void WriteEventViewer(string message, EventLogEntryType messageType)
        {
            StringBuilder EventSource = new StringBuilder(_customServiceName);
            string _message = string.Empty;

            _message = EnsureLogMessageLimit(message);

            if (!EventLog.SourceExists(EventSource.ToString()))
            {
                EventLog.CreateEventSource(EventSource.ToString(), "Application");
            }
            EventLog.WriteEntry(EventSource.ToString(), _message, messageType);
        }

        private static string EnsureLogMessageLimit(string logMessage)
        {
            if (logMessage.Length > MaxEventLogEntryLength)
            {
                string truncateWarningText = string.Format(CultureInfo.CurrentCulture, "... | Log Message Truncated [ Limit: {0} ]", MaxEventLogEntryLength);

                logMessage = logMessage.Substring(0, MaxEventLogEntryLength - truncateWarningText.Length);

                logMessage = string.Format(CultureInfo.CurrentCulture, "{0}{1}", logMessage, truncateWarningText);
            }

            return logMessage;
        }
    }
}
