using System;
using System.IO;

namespace BackupUtility.Debug
{
    /// <summary>  
    /// Appends log statements to standard output
    /// </summary>
    public class StandardOutputAppender : Appender
    {
        /// <summary> 
        /// Destination
        /// </summary>
        private StreamWriter log;

        /// <summary> 
        /// Constructor
        /// </summary>
        public StandardOutputAppender()
        {
            log = new StreamWriter(System.Console.OpenStandardOutput());
        }

        /// <summary> 
        /// Log a message
        /// </summary>
        /// <param name="msg"> message to log
        /// </param>
        public virtual void Log(string msg)
        {
            log.WriteLine(msg);
            log.Flush();
        }

        /// <summary> 
        /// Log a stack trace
        /// </summary>
        /// <param name="t"> throwable object
        /// </param>		
        public virtual void Log(Exception t)
        {
            log.WriteLine(t.GetType().FullName + ": " + t.Message);
            log.WriteLine(t.StackTrace.ToString());
            log.Flush();
        }

        /// <summary> 
        /// Close this appender
        /// </summary>
        public virtual void Close()
        {
            log.Flush();
        }
    }
}
