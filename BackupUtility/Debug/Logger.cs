using System;
using System.Globalization;
using System.Collections;
using System.Collections.Specialized;
using System.Threading;
using System.Configuration;
using System.Reflection;
using System.Text;
using System.ComponentModel;

namespace BackupUtility.Debug
{
    /// <summary>
    /// An instance of this class is supplied to the LogMessageReceived event
    /// </summary>
    public class LogMessageEventArgs : EventArgs
    {
        private string loggerName;
        private Level level;
        private string text;
        private Exception e;
        private object[] args;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="loggerName">name of logger</param>
        /// <param name="level">log level of message</param>
        /// <param name="text">message text</param>
        /// <param name="args">any additional args supplied</param>
        internal LogMessageEventArgs(
            string loggerName, Level level, string text, params object[] args)
        {
            this.loggerName = loggerName;
            this.level = level;
            this.text = text;
            this.args = args;
            if (args != null && args.Length == 1 && args[0] is Exception)
                this.e = (Exception)args[0];
        }

        /// <summary>
        /// Returns the level of this message
        /// </summary>
        public Level LogLevel
        {
            get { return level; }
        }

        /// <summary>
        /// Returns the name of the logger for this message
        /// </summary>
        public string LoggerName
        {
            get { return loggerName; }
        }

        /// <summary>
        /// The message text.
        /// </summary>
        /// <remarks>Normally this is a log message, but if additional arguments
        /// are supplied, this will be a Format string so the extra arguments can
        /// be displayed correctly.</remarks>
        public string Text
        {
            get
            {
                return text;
            }
        }

        /// <summary>
        /// The formatted message text, constructed from the arguments and using
        /// the Text as a formatting string.
        /// </summary>
        public string FormattedText
        {
            get
            {
                if (args != null)
                    return string.Format(text, args);
                return text;
            }
        }

        /// <summary>
        /// An exception if it exists (passed in as the first in the
        /// argument list).
        /// </summary>
        public Exception Exception
        {
            get { return e; }
        }

        /// <summary>
        /// The array of variable arguments.
        /// </summary>
        public object[] Arguments
        {
            get { return args; }
        }
    }

    /// <summary>
    /// Delegate used for LogMessageReceived event
    /// </summary>
    public delegate void LogMessageHandler(object sender, LogMessageEventArgs e);

    /// <summary>  
    /// Logger class that mimics log4net Logger class
    /// </summary>
    public class Logger
    {
        #region Tag Methods

        public static void SetTag(string tag)
        {
            lock (threadTags)
            {
                threadTags[Thread.CurrentThread] = tag;
            }
        }

        public static void ClearTag()
        {
            lock (threadTags)
            {
                threadTags.Remove(Thread.CurrentThread);
            }
        }

        private static string GetTag()
        {
            lock (threadTags)
            {
                if (threadTags.ContainsKey(Thread.CurrentThread))
                    return threadTags[Thread.CurrentThread] + " ";
                else
                    return "";
            }
        }

        #endregion

        /// <summary> 
        /// Set all loggers to this level
        /// </summary>
        public static Level CurrentLevel
        {
            set
            {
                globalLevel = value;
            }
            get
            {
                return globalLevel;
            }
        }

        /// <summary>If true then class-names will be shown in log.</summary>
        public static bool ShowClassNames
        {
            get
            {
                return showClassNames;
            }
            set
            {
                showClassNames = value;
            }
        }

        /// <summary>
        /// If true then timestamps/class-name/level-text will be shown in log.
        /// </summary>
        virtual public bool ShowAllTag
        {
            get
            {
                return showAllTag;
            }
            set
            {
                showAllTag = value;
            }
        }

        /// <summary>If true then timestamps will be shown in log.</summary>
        public static bool ShowTimestamp
        {
            get
            {
                return showTimestamp;
            }
            set
            {
                showTimestamp = value;
            }
        }

        /// <summary> 
        /// Is error logging enabled?
        /// </summary>
        /// <returns> true if enabled
        /// </returns>
        virtual public bool ErrorEnabled
        {
            get
            {
                return IsEnabledFor(Level.ERROR);
            }

        }

        /// <summary> 
        /// Is debug logging enabled?
        /// </summary>
        /// <returns> true if enabled
        /// </returns>
        virtual public bool DebugEnabled
        {
            get
            {
                return IsEnabledFor(Level.DEBUG);
            }

        }
        /// <summary> Is info logging enabled for the supplied level?
        /// 
        /// </summary>
        /// <returns> true if enabled
        /// </returns>
        virtual public bool InfoEnabled
        {
            get
            {
                return IsEnabledFor(Level.INFO);
            }

        }

        /// <summary>
        /// The primary log file is simply the first file appender
        /// that has been added to the logger.
        /// </summary>
        public static string PrimaryLogFile
        {
            get
            {
                return mainFileAppender != null ? mainFileAppender.FileName : null;
            }
            set
            {
                string mainFileAppenderName = (mainFileAppender != null ? mainFileAppender.FileName : null);

                if (mainFileAppenderName != value)
                {
                    if (mainFileAppender != null)
                        RemoveAppender(mainFileAppender);
                    if (value != null)
                        AddAppender(new FileAppender(value));
                }
            }
        }

        /// <summary>
        /// If this property is <c>true</c> then logs will be written to the
        /// console.
        /// </summary>
        public static bool LogToConsole
        {
            get
            {
                return mainConsoleAppender != null;
            }
            set
            {
                if (value == true)
                {
                    if (mainConsoleAppender == null)
                        AddAppender(new StandardOutputAppender());
                }
                else
                {
                    if (mainConsoleAppender != null)
                        RemoveAppender(mainConsoleAppender);
                }
            }
        }

        /// <summary>
        /// If this property is <c>true</c> then logs will be written using
        /// <see cref="System.Diagnostics.Trace"/>.
        /// </summary>
        public static bool LogToTrace
        {
            get
            {
                return mainTraceAppender != null;
            }
            set
            {
                if (value == true)
                {
                    if (mainTraceAppender == null)
                        AddAppender(new TraceAppender());
                }
                else
                {
                    if (mainTraceAppender != null)
                        RemoveAppender(mainTraceAppender);
                }
            }
        }

        /// <summary>
        /// If this event is set then all logging events are directed to the
        /// event as well as the loggers.
        /// </summary>
        /// <remarks>If it is desired to only send logging to the log system subscribing
        /// to this event, the <see cref="CurrentLevel"/> should be set to <see cref="Level.OFF"/>.</remarks>
        public static event LogMessageHandler LogMessageReceived;

        /// <summary> Level of all loggers</summary>
        private static Level globalLevel;

        /// <summary>Date format</summary>
        private static readonly string format = "d MMM yyyy HH:mm:ss.fff";

        private static readonly string LEVEL_PARAM = "MicroD.log.level";

        /// <summary> Hash of all loggers that exist</summary>
        private static Hashtable loggers = Hashtable.Synchronized(new Hashtable(10));

        /// <summary> Vector of all appenders</summary>
        private static ArrayList appenders = ArrayList.Synchronized(new ArrayList(2));

        /// <summary> Timestamp</summary>
        private DateTime ts;

        /// <summary> Class name for this logger</summary>
        private string clazz;

        /// <summary>If true then class-names will be shown in log.</summary>
        private static bool showClassNames = true;

        /// <summary>If true then timestamps will be shown in log.</summary>
        private static bool showTimestamp = true;

        /// <summary>If true then timestamps/class-name/level-text will be shown in log.</summary>
        private static bool showAllTag = true;

        private static Hashtable threadTags = new Hashtable();

        /// <summary>Main file appender</summary>
        private static FileAppender mainFileAppender = null;

        /// <summary>Main file appender</summary>
        private static StandardOutputAppender mainConsoleAppender = null;

        /// <summary>Main file appender</summary>
        private static TraceAppender mainTraceAppender = null;

        /// <summary> 
        /// Constructor
        /// </summary>
        /// <param name="clazz">    
        /// class this logger is for
        /// </param>
        private Logger(string clazz)
        {
            this.clazz = clazz;
        }


        /// <summary> Get a logger for the supplied class
        /// 
        /// </summary>
        /// <param name="clazz">   full class name
        /// </param>
        /// <returns>  logger for class
        /// </returns>
        public static Logger GetLogger(System.Type clazz)
        {
            return GetLogger(clazz.FullName);
        }

        /// <summary> 
        /// Get a logger for the supplied class
        /// </summary>
        /// <param name="clazz">   full class name
        /// </param>
        /// <returns>  logger for class
        /// </returns>
        public static Logger GetLogger(string clazz)
        {
            Logger logger = (Logger)loggers[clazz];
            if (logger == null)
            {
                logger = new Logger(clazz);
                loggers[clazz] = logger;
            }
            return logger;
        }

        /// <summary> 
        /// Add an appender to our list
        /// </summary>
        /// <param name="newAppender">
        /// new appender to add
        /// </param>
        public static void AddAppender(Appender newAppender)
        {
            appenders.Add(newAppender);
            if (newAppender is FileAppender && mainFileAppender == null)
                mainFileAppender = (FileAppender)newAppender;
            if (newAppender is StandardOutputAppender && mainConsoleAppender == null)
                mainConsoleAppender = (StandardOutputAppender)newAppender;
            if (newAppender is TraceAppender && mainTraceAppender == null)
                mainTraceAppender = (TraceAppender)newAppender;
        }

        /// <summary> 
        /// Remove an appender from our list
        /// </summary>
        /// <param name="appender">appender to remove</param>
        public static void RemoveAppender(Appender appender)
        {
            appenders.Remove(appender);
            if (appender == mainFileAppender)
                mainFileAppender = null;
            if (appender == mainConsoleAppender)
                mainConsoleAppender = null;
            if (appender == mainTraceAppender)
                mainTraceAppender = null;
        }

        /// <summary> Close and remove all appenders and loggers</summary>
        public static void Shutdown()
        {
            ClearAppenders();
            loggers.Clear();
        }

        /// <summary> Close and remove all appenders</summary>
        public static void ClearAppenders()
        {
            lock (appenders.SyncRoot)
            {
                for (int i = 0; i < appenders.Count; i++)
                {
                    Appender a = (Appender)appenders[i];
                    try
                    {
                        a.Close();
                    }
                    catch (Exception) { }
                }
            }
            appenders.Clear();
        }

        public virtual void Log(Level level, string message, params object[] args)
        {
            Log(level, message, true, args);
        }

        /// <summary>
        /// Log a message using the given level.
        /// </summary>
        /// <param name="level">Log level.</param>
        /// <param name="message">Message to log.</param>
        /// <param name="args">Either an Exception or arguments substituted into message.</param>
        public virtual void Log(Level level, string message, bool _showAllTag, params object[] args)
        {
            // direct logging 
            if (LogMessageReceived != null)
            {
                LogMessageReceived(this, new LogMessageEventArgs(clazz, level, message, args));
            }

            if (IsEnabledFor(level))
            {
                if (args != null && args.Length == 1 && args[0] is Exception)
                    OurLog(level, message, (Exception)args[0], _showAllTag);
                else if (args != null)
                    OurLog(level, string.Format(message, args), null, _showAllTag);
                else
                    OurLog(level, message, null, _showAllTag);
            }
        }

        /// <summary> 
        /// Log a message to our logging system
        /// </summary>
        /// <param name="level">log level</param>
        /// <param name="message">message to log</param>
        /// <param name="t">throwable object</param>
        private void OurLog(Level level, string message, Exception t)
        {
            OurLog(level, message, t, true);
        }

        /// <summary> 
        /// Log a message to our logging system
        /// </summary>
        /// <param name="level">log level</param>
        /// <param name="message">message to log</param>
        /// <param name="t">throwable object</param>
        /// <param name="_showAllTag">whether to show tags</param>
        private void OurLog(Level level, string message, Exception t, bool _showAllTag)
        {
            ts = DateTime.Now;
            string stamp = ts.ToString(format, CultureInfo.CurrentCulture.DateTimeFormat);
            System.Text.StringBuilder buf;
            if (showAllTag && _showAllTag)
            {
                buf = new System.Text.StringBuilder(level.ToString());
                if (showClassNames)
                {
                    buf.Append(" [");
                    buf.Append(clazz);
                    buf.Append("]");
                }
                if (showTimestamp)
                {
                    buf.Append(" ");
                    buf.Append(stamp);
                }
                buf.Append(" : ");
                buf.Append(GetTag());
            }
            else
            {
                buf = new System.Text.StringBuilder();
            }
            string prefix = buf.ToString();

            if (message != null)
                buf.Append(message);
            if (t != null)
                buf.Append(" : ").Append(t.GetType().FullName).Append(": ").Append(t.Message);
            if (appenders.Count == 0)
            {
                // by default to stdout
                System.Console.Out.WriteLine(buf.ToString());
                if (t != null)
                {
                    if (t.StackTrace != null)
                        foreach (string line in t.StackTrace.Replace("\r", "").Split('\n'))
                            OurLog(level, prefix + line, null);
                    if (t.InnerException != null)
                    {
                        System.Console.Out.WriteLine(
                            string.Format("{0}CAUSED BY - {1}: {2}",
                            prefix,
                            t.InnerException.GetType().FullName,
                            t.InnerException.Message));
                        if (t.InnerException.StackTrace != null)
                            foreach (string line in t.InnerException.StackTrace.Replace("\r", "").Split('\n'))
                                OurLog(level, prefix + line, null);
                    }
                }
            }
            else
            {
                bool appendToAll = globalLevel.IsGreaterOrEqual(level);
                lock (appenders.SyncRoot)
                {
                    for (int i = 0; i < appenders.Count; i++)
                    {
                        Appender a = (Appender)appenders[i];
                        bool appendToCustom = false;
                        if (a is CustomLogLevelAppender)
                        {
                            CustomLogLevelAppender appender = (CustomLogLevelAppender)a;
                            appendToCustom = appender.CurrentLevel.IsGreaterOrEqual(level);
                        }
                        if (appendToAll || appendToCustom)
                        {
                            if (message != null)
                                a.Log(prefix + message);
                            if (t != null)
                            {
                                a.Log(prefix + t.GetType().FullName + ": " + t.Message);
                                if (t.StackTrace != null)
                                    foreach (string line in t.StackTrace.Replace("\r", "").Split('\n'))
                                        a.Log(prefix + line);
                                if (t.InnerException != null)
                                {
                                    a.Log(prefix + "CAUSED BY - " + t.InnerException.GetType().FullName + ": " + t.Message);
                                    if (t.InnerException.StackTrace != null)
                                        foreach (string line in t.InnerException.StackTrace.Replace("\r", "").Split('\n'))
                                            a.Log(prefix + line);
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary> Log an info level message
        /// 
        /// </summary>
        /// <param name="message">  message to log</param>
        /// <param name="ShowTag">  whether to show tag or not, this will disable all the tag in front of log message.</param>
        public virtual void Info(string message, bool _showAllTag)
        {
            Log(Level.INFO, message, _showAllTag, null);
        }

        /// <summary> Log an info level message
        /// 
        /// </summary>
        /// <param name="message">  message to log
        /// </param>
        public virtual void Info(string message)
        {
            Info(message, true);
        }

        /// <summary> Log an info level message
        /// 
        /// </summary>
        /// <param name="message">  message to log
        /// </param>
        /// <param name="t">        throwable object
        /// </param>
        public virtual void Info(string message, Exception t)
        {
            Log(Level.INFO, message, t);
        }

        /// <summary> Log an info level message
        /// 
        /// </summary>
        /// <param name="message">  message to log
        /// </param>
        /// <param name="args">arguments references in the message.
        /// </param>
        public virtual void Info(string message, params object[] args)
        {
            if (IsEnabledFor(Level.INFO))
                Log(Level.INFO, string.Format(message, args), null);
        }

        /// <summary> Log a warning level message
        /// 
        /// </summary>
        /// <param name="message">  message to log
        /// </param>
        public virtual void Warn(string message)
        {
            Log(Level.WARN, message, null);
        }

        /// <summary> Log a warning level message
        /// 
        /// </summary>
        /// <param name="message">  message to log
        /// </param>
        /// <param name="t">        throwable object
        /// </param>
        public virtual void Warn(string message, Exception t)
        {
            Log(Level.WARN, message, t);
        }

        /// <summary>Log an warning level message</summary>
        /// <param name="message">message to log</param>
        /// <param name="t">throwable object</param>
        /// <param name="args">arguments references in the message.</param>
        public virtual void Warn(string message, Exception t, params object[] args)
        {
            Log(Level.WARN, string.Format(message, args), t);
        }

        /// <summary> Log an error level message
        /// 
        /// </summary>
        /// <param name="message">  message to log
        /// </param>
        public virtual void Error(string message)
        {
            Log(Level.ERROR, message, null);
        }

        public virtual void Error(string message, bool showAllTag)
        {
            Log(Level.ERROR, message, showAllTag, null);
        }

        /// <summary> Log an error level message
        /// Argument Error Message
        /// </summary>
        /// <param name="type">  Error type
        /// <param name="Error Message">  Message to log
        /// </param>
        public virtual void Error(string type, string errorMessage)
        {
            Log(Level.ERROR, string.Format("{0} : {1}", type, errorMessage), null);
        }

        /// <summary> Log an error level message
        /// Argument Error Message
        /// </summary>
        /// <param name="type">  Error type
        /// <param name="errorMessage">  Message to log
        /// <param name="ex">  Exception
        /// </param>
        public virtual void Error(string type, string errorMessage, Exception ex)
        {
            Log(Level.ERROR, string.Format("{0} : {1}", type, errorMessage), ex);
        }

        /// <summary> Log an error level message
        /// Argument Error Message
        /// </summary>
        /// <param name="type">  Error type
        /// <param name="argument">  Which is wrong
        /// <param name="Error Message">  Message to log
        /// </param>
        public virtual void Error(string type, string argument, string errorMessage)
        {
            Log(Level.ERROR, string.Format("{0} : {1}/{2}", type, argument, errorMessage), null);
        }

        /// <summary> Log an error level message
        /// 
        /// </summary>
        /// <param name="message">  message to log
        /// </param>
        /// <param name="t">        throwable object
        /// </param>
        public virtual void Error(string message, Exception t)
        {
            Log(Level.ERROR, message, t);
        }


        /// <summary>Log an error level message</summary>
        /// <param name="message">message to log</param>
        /// <param name="t">throwable object</param>
        /// <param name="args">arguments references in the message.</param>
        public virtual void Error(string message, Exception t, params object[] args)
        {
            Log(Level.ERROR, string.Format(message, args), t);
        }

        /// <summary> Log an error level message
        /// 
        /// </summary>
        /// <param name="t">        throwable object
        /// </param>
        public virtual void Error(Exception t)
        {
            Log(Level.ERROR, null, t);
        }

        /// <summary> Log a fatal level message
        /// 
        /// </summary>
        /// <param name="message">  message to log
        /// </param>
        public virtual void Fatal(string message)
        {
            Log(Level.FATAL, message, null);
        }

        /// <summary> Log a fatal level message
        /// 
        /// </summary>
        /// <param name="message">  message to log
        /// </param>
        /// <param name="t">        throwable object
        /// </param>
        public virtual void Fatal(string message, Exception t)
        {
            Log(Level.FATAL, message, t);
        }

        /// <summary> 
        /// Log a debug level message
        /// </summary>
        /// <param name="message">  message to log
        /// </param>
        public virtual void Debug(string message)
        {
            Log(Level.DEBUG, message, null);
        }

        /// <summary>
        /// Log a debug level message
        /// </summary>
        /// <param name="message">message to log</param>
        /// <param name="showAllTags">whether to show all tags in front of debug message or not</param>
        public virtual void Debug(string message, bool showAllTags)
        {
            Log(Level.DEBUG, message, showAllTag, null);
        }

        /// <summary>
        /// Assert will print debug message if condition is true
        /// </summary>
        /// <param name="result"></param>
        /// <param name="message"></param>
        public virtual void Assert(bool condition, string message)
        {
            if (condition)
                Log(Level.DEBUG, message, null);
        }

        /// <summary>Log a debug level message</summary>
        /// <param name="message">message to log</param>
        /// <param name="args">arguments references in the message.</param>
        public virtual void Debug(string message, params object[] args)
        {
            if (IsEnabledFor(Level.DEBUG))
                Log(Level.DEBUG, string.Format(message, args), null);
        }


        /// <summary> Log a debug level message
        /// 
        /// </summary>
        /// <param name="message">  message to log
        /// </param>
        /// <param name="t">        throwable object
        /// </param>
        public virtual void Debug(string message, Exception t)
        {
            Log(Level.DEBUG, message, t);
        }

        /// <summary> Is logging enabled for the supplied level?
        /// 
        /// </summary>
        /// <param name="level">  level to test for
        /// </param>
        /// <returns> true   if enabled
        /// </returns>
        public virtual bool IsEnabledFor(Level level)
        {
            if (globalLevel.IsGreaterOrEqual(level))
                return true;
            lock (appenders.SyncRoot)
            {
                foreach (Appender a in appenders)
                    if (a is CustomLogLevelAppender)
                    {
                        CustomLogLevelAppender appender = (CustomLogLevelAppender)a;
                        if (appender.CurrentLevel.IsGreaterOrEqual(level))
                            return true;
                    }
            }
            return false;
        }

        /// <summary> Determine the logging level</summary>
        static Logger()
        {
            {
                globalLevel = null;
                string level = null;
                try
                {
                    level = ConfigurationManager.AppSettings[LEVEL_PARAM];
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine("WARN: Failure reading configuration file: " + ex.Message);
                }
                if (level != null)
                {
                    // first try with the strings INFO etc
                    globalLevel = Level.GetLevel(level);
                    if (globalLevel == null)
                    {
                        try
                        {
                            // now try from the enum
                            LogLevel logLevel = (LogLevel)Enum.Parse(typeof(LogLevel), level, true);
                            globalLevel = Level.GetLevel(logLevel);
                        }
                        catch (Exception)
                        {
                        }
                    }
                }

                // if no level set, switch off
                if (globalLevel == null)
                {
                    globalLevel = Level.OFF;
                    if (level != null)
                    {
                        System.Console.Out.WriteLine("WARN: '" + LEVEL_PARAM + "' configuration property invalid. Unable to parse '" + level + "' - logging switched off");
                    }
                }
            }
        }

        /// <summary>
        /// Logs the public properties of an object.
        /// </summary>
        /// <param name="level">Logging level to use.</param>
        /// <param name="prefix">Text to prepend to the properties.</param>
        /// <param name="obj">Object whose properties are to be logged.</param>
        public void LogObject(Level level, string prefix, object obj)
        {
            if (IsEnabledFor(level))
            {
                if (obj == null)
                    Log(level, prefix + "(null)", null);
                Type objType = obj.GetType();
                bool useShortFormat = true;
                PropertyInfo[] properties = obj.GetType().GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                foreach (PropertyInfo p in properties)
                {
                    if (RequiresLongFormat(p, obj))
                    {
                        useShortFormat = false;
                        break;
                    }
                }
                StringBuilder s = new StringBuilder();
                foreach (PropertyInfo p in properties)
                {
                    object v = p.GetValue(obj, null);
                    if (s.Length > 0)
                        s.Append(useShortFormat ? ", " : "\n  ");
                    s.Append(p.Name).Append("=");
                    DumpValue(v, s, "    ");
                }
                Log(level, prefix + s, null);
            }
        }

        private ArrayList GetAllProperties(Type t)
        {
            ArrayList fields = new ArrayList();
            while (t != typeof(object))
            {
                fields.AddRange(t.GetProperties(BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public));
                t = t.BaseType;
            }
            return fields;
        }

        private bool RequiresLongFormat(PropertyInfo p, object obj)
        {
            object value = p.GetValue(obj, null);
            if (value == null
                || value is string
                || value.GetType().IsPrimitive)
                return false;
            if (value.GetType().IsArray && value.GetType().GetElementType().IsPrimitive)
                return ((Array)value).Length > 16;
            if (value is StringDictionary)
                return ((StringDictionary)value).Count > 1;
            if (value is ICollection)
                return ((ICollection)value).Count > 1;
            return typeof(IEnumerable).IsAssignableFrom(p.PropertyType);
        }

        private void DumpValue(object value, StringBuilder valueStr, string indent)
        {
            if (value == null)
                valueStr.Append("(null)");
            else if (value.GetType().IsArray && value.GetType().GetElementType().IsPrimitive)
            {
                int count = 0;
                Array arr = (Array)value;
                if (arr.Length > 16)
                    valueStr.Append("(")
                        .Append(arr.Length)
                        .Append(" items)")
                        .Append("\n")
                        .Append(indent)
                        .Append("  ");
                foreach (object o in arr)
                {
                    if (o is byte)
                        valueStr.Append(((byte)o).ToString("X2"));
                    else
                        valueStr.Append(o);
                    count++;
                    if (count % 16 != 0)
                        valueStr.Append(" ");
                    else
                        valueStr.Append("\n").Append(indent).Append("  ");
                }
            }
            else if (value is IDictionary)
            {
                IDictionary dict = (IDictionary)value;
                bool useLongFormat = dict.Count > 1;
                if (useLongFormat)
                    valueStr.Append("(").Append(dict.Count).Append(" items)");
                foreach (object key in dict.Keys)
                {
                    if (useLongFormat)
                        valueStr.Append("\n").Append(indent);
                    valueStr.Append(key).Append("=");
                    DumpValue(dict[key], valueStr, indent + "  ");
                }
            }
            else if (value is StringDictionary)
            {
                StringDictionary dict = (StringDictionary)value;
                bool useLongFormat = dict.Count > 1;
                if (useLongFormat)
                    valueStr.Append("(").Append(dict.Count).Append(" items)");
                foreach (string key in dict.Keys)
                {
                    if (useLongFormat)
                        valueStr.Append("\n").Append(indent);
                    valueStr.Append(key).Append("=");
                    DumpValue(dict[key], valueStr, indent + "  ");
                }
            }
            else if (!(value is string) && value is IEnumerable)
            {
                bool useLongFormat = true;
                if (value is ICollection)
                {
                    ICollection col = (ICollection)value;
                    useLongFormat = col.Count > 1;
                    if (useLongFormat)
                        valueStr.Append("(").Append(col.Count).Append(" items)");
                }
                foreach (object i in (IEnumerable)value)
                {
                    if (useLongFormat)
                        valueStr.Append("\n").Append(indent);
                    DumpValue(i, valueStr, indent + "  ");
                }
            }
            else
                valueStr.Append(value.ToString());
        }
    }
}
