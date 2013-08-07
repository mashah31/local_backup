using System;
using System.IO;

namespace BackupUtility.Debug
{
    /// <summary>  
    /// Appends log statements to a file
    /// </summary>
    public class FileAppender : Appender
    {

        /// <summary> Destination</summary>
        protected TextWriter logger;

        protected FileStream fileStream;

        /// <summary> Log file</summary>
        private string fileName;

        /// <summary>
        /// True if closed
        /// </summary>
        protected bool closed = false;

        /// <summary>Constructor</summary>
        /// <param name="fileName">name of file to log to</param>
        /// <throws>  IOException </throws>
        public FileAppender(string fileName)
        {
            this.fileName = new FileInfo(fileName).FullName;
            Open();
        }

        protected void Open()
        {
            fileStream =
                new FileStream(fileName, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            logger = StreamWriter.Synchronized(new StreamWriter(fileStream));
            closed = false;
        }

        /// <summary>
        /// Name of file to log to.
        /// </summary>
        public string FileName
        {
            get
            {
                return fileName;
            }
        }

        /// <summary> 
        /// Log a message
        /// </summary>
        /// <param name="msg"> message to log
        /// </param>
        public virtual void Log(string msg)
        {
            if (!closed)
            {
                logger.WriteLine(msg);
                logger.Flush();
            }
            else
            {
                System.Console.WriteLine(msg);
            }
        }

        /// <summary> 
        /// Log a stack trace
        /// </summary>
        /// <param name="t"> throwable object
        /// </param>
        public virtual void Log(Exception t)
        {
            if (!closed)
            {
                logger.WriteLine(t.GetType().FullName + ": " + t.Message);
                logger.WriteLine(t.StackTrace.ToString());
                logger.Flush();
            }
            else
            {
                System.Console.WriteLine(t.GetType().FullName + ": " + t.Message);
            }
        }

        /// <summary> 
        /// Close this appender
        /// </summary>
        public virtual void Close()
        {
            closed = true;
            logger.Flush();
            logger.Close();
            logger = null;
            fileStream = null;
        }
    }
}
