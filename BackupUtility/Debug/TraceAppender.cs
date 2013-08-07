using System;
using System.Diagnostics;

namespace BackupUtility.Debug
{
    public class TraceAppender : Appender
    {
        /// <summary> 
        /// Log a message
        /// </summary>
        /// <param name="msg"> message to log
        /// </param>
        public virtual void Log(string msg)
        {
            Trace.WriteLine(msg);
        }

        /// <summary> 
        /// Log a stack trace
        /// </summary>
        /// <param name="t"> throwable object
        /// </param>		
        public virtual void Log(Exception t)
        {
            Trace.WriteLine(t.GetType().FullName + ": " + t.Message);
            Trace.WriteLine(t.StackTrace.ToString());
        }

        /// <summary> 
        /// Close this appender
        /// </summary>
        public virtual void Close()
        {
            Trace.Close();
        }
    }
}
