using System;
using System.Collections.Generic;
using System.Text;

namespace BackupUtility.Debug
{
    /// <summary>  Interface for classes that output log
    /// statements 
    /// 
    /// </summary>
    public interface Appender
    {
        /// <summary> Close this appender</summary>
        void Close();

        /// <summary> Log a message
        /// 
        /// </summary>
        /// <param name="msg"> message to log
        /// </param>
        void Log(string msg);

        /// <summary> 
        /// Log a stack trace
        /// </summary>
        /// <param name="t"> throwable object
        /// </param>
        void Log(Exception t);
    }

    /// <summary>
    /// Extends <see cref="Appender"/> by allowing an appender to have its own log-level.
    /// </summary>
    /// <remarks>
    /// Appenders implementing this interface have their own log-level, which overrides the global
    /// logging level, <see cref="Logger.CurrentLevel"/>.
    /// </remarks>
    public interface CustomLogLevelAppender : Appender
    {
        /// <summary>
        /// Logging level for this appender.  This does not affect the global logging level.
        /// </summary>
        Level CurrentLevel { get; set; }
    }
}
