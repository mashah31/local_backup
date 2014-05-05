using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Linq;

using BackupUtility.Debug;

namespace BackupUtility
{
    class Program
    {
        private static Logger log = Logger.GetLogger("Backup Utility");
        private static string _sendEmailPath = string.Empty, _mailFrom = string.Empty, _mailTo = string.Empty, _mailServer = string.Empty;
        private static int _totalFiles = 0, _processedFiles = 0, _keepLastXCopyOfBackup = 3;
        private static string _backingUpProgressMessage = string.Empty;
        private static bool _deleteOldCatalogs = true;
        private static int _lowDiskSpaceAlertIfDiskSpaceLessThanThresholdInPercentage = 10;
        private static int _lowDiskSpaceWarningIfDiskSpaceLessThanThresholdInPercentage = 20;

        static void Main(string[] args)
        {
            #region argument handling 
            string logFile = string.Empty, sourceDir = string.Empty, destinationDir = string.Empty, backupFileParentDir = string.Empty, clientName = string.Empty;
            DateTime dtBackupFileDate = DateTime.Now;
            string mailSubject = string.Empty, mailBody = string.Empty;

            //Argument handling ...
            try
            {
                backupFileParentDir = GetValueFromConfigOrArgument(args, "backupFileParentDir");
                logFile = GetValueFromConfigOrArgument(args, "backupLogFile") + string.Format("BackupLog_{0}-{1}-{2}-{3}{4}{5}.txt", dtBackupFileDate.Month.ToString("d2"), dtBackupFileDate.Day.ToString("d2"), dtBackupFileDate.Year.ToString("d4"), dtBackupFileDate.Hour.ToString("d2"), dtBackupFileDate.Minute.ToString("d2"), dtBackupFileDate.Second.ToString("d2"));
                
                clientName = GetValueFromConfigOrArgument(args, "clientName");
                mailSubject = string.Empty;
                sourceDir = GetValueFromConfigOrArgument(args, "sourceDir");
                destinationDir = GetValueFromConfigOrArgument(args, "destinationDir");

                _sendEmailPath = GetValueFromConfigOrArgument(args, "sendEmailExePath");
                _mailFrom = GetValueFromConfigOrArgument(args, "mailFrom");
                _mailTo = GetValueFromConfigOrArgument(args, "mailTo");
                _mailServer = GetValueFromConfigOrArgument(args, "mailServer");
                
                _deleteOldCatalogs = Convert.ToBoolean(GetValueFromConfigOrArgument(args, "deleteOldCatalogs"));
                
                int value = Convert.ToInt32(GetValueFromConfigOrArgument(args, "lowDiskSpaceAlertIfDiskSpaceLessThanThresholdInPercentage", "int"));
                _lowDiskSpaceAlertIfDiskSpaceLessThanThresholdInPercentage = value.Equals(0) ? _lowDiskSpaceAlertIfDiskSpaceLessThanThresholdInPercentage : value;

                value = Convert.ToInt32(GetValueFromConfigOrArgument(args, "lowDiskSpaceWarningIfDiskSpaceLessThanThresholdInPercentage", "int"));
                _lowDiskSpaceWarningIfDiskSpaceLessThanThresholdInPercentage = value.Equals(0) ? _lowDiskSpaceWarningIfDiskSpaceLessThanThresholdInPercentage : value;

                value = Convert.ToInt32(GetValueFromConfigOrArgument(args, "keepLastXCopyOfBackup", "int"));
                _keepLastXCopyOfBackup = value.Equals(0) ? _keepLastXCopyOfBackup : value;

                Logger.LogToConsole = false;
                Logger.PrimaryLogFile = logFile;
                Logger.ShowClassNames = false;
                Logger.ShowTimestamp = false;
                Logger.CurrentLevel = Level.INFO;
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("Error : {0}\nException Message : {1}", ex.Message, ex.StackTrace));
                Environment.Exit(1);
            }
            #endregion
            
            #region backup utility
            bool AnyErrorOccuredInBackup = false;     

            try
            {
                string startUpMessage = "Backup Date : " + DateTime.Now.ToShortDateString();
                Console.WriteLine(startUpMessage);                
                
                //if backup file parent directory exists ....
                if(Directory.Exists(backupFileParentDir))
                {
                    //collect catalog backup files
                    #region Collect catalog backup files
                    string[] backupCatalogFiles = Directory.GetFiles(backupFileParentDir);
                    List<string> catalogTobackupQueue = new List<string>();
                    
                    //iterate through each file and collect catalog name to backup
                    foreach(string file in backupCatalogFiles)
                    {
                        string fileNameWithoutExt = file.Substring(0, file.IndexOf('.')).Replace(backupFileParentDir, "");
                        if (!catalogTobackupQueue.Contains(fileNameWithoutExt))
                            catalogTobackupQueue.Add(fileNameWithoutExt);
                    }
                    string catalogMessage = string.Format("Catalogs needing backup: {0}", catalogTobackupQueue.Count);
                    log.Info(catalogMessage, false); Console.WriteLine(catalogMessage);
                    #endregion

                    if (catalogTobackupQueue.Count < 1)
                    {
                        SendSuccessMail(sourceDir, mailBody, logFile);
                        Environment.Exit(0);
                    }

                    log.Info(string.Format("\nBackup starting at {0}", DateTime.Now.ToShortTimeString()), false);
                    
                    //iterate through each catalog for backup process
                    foreach (string catalog in catalogTobackupQueue)
                    {
                        log.Info("\nCatalog: " + catalog + " @ " + DateTime.Now.ToShortTimeString(), false);
                        string sourceDirPath = sourceDir + catalog + "/";

                        //Check if source directory exists.. 
                        if (Directory.Exists(sourceDirPath))
                        {
                            bool backupSuccess = false;
                            try
                            {
                                //Collect disk space needed to backup catalog
                                long diskSpaceReqForCatalog = GetDirectorySize(sourceDirPath);
                                log.Info("needs " + Math.Round(ConvertBytesToMegabytes(diskSpaceReqForCatalog), 2) + " MBytes of disk space.", false);

                                //Check if disk has more than threshold disk space after Successfully backed backing up catalog
                                DetermineBackupSpaceAvailability(sourceDir, diskSpaceReqForCatalog, catalog);

                                //now take backup of catalog
                                string destDirPath = destinationDir + string.Format("{0}_{1}.old", catalog, DateTime.Now.ToString("MM-dd-yyyy_hhmmss")) + "/";
                                _totalFiles = new DirectoryInfo(sourceDirPath).GetFiles("*.*", SearchOption.AllDirectories).Length; _processedFiles = 0;
                                _backingUpProgressMessage = string.Format("\nBacking-up: {0},", catalog, _totalFiles);
                                Console.Write(_backingUpProgressMessage);

                                DirectoryCopy(sourceDirPath, destDirPath, true);
                                BackupValidator(sourceDirPath, destDirPath);
                                backupSuccess = true;

                                //delete old catalog backup copy and backup file from parente folder
                                if (backupSuccess)
                                {
                                    //after backup collect catalog dirs from backup root
                                    Dictionary<string, List<DirectoryInfo>> dictCtlgWithItsDirs = CollectBackedupCatalogDirsFromRoot(catalog, destinationDir);

                                    //delete extra backup copy
                                    DeleteExtraCatalogBackups(dictCtlgWithItsDirs, catalog);
                                }
                            }
                            catch (Exception ex)
                            {
                                log.Error(string.Format("Catalog backup failed for {0}\nFailure Reason : {1}\nStacktrace : {2}", catalog, ex.Message, ex.InnerException));
                                AnyErrorOccuredInBackup = true;
                            }
                        }
                        else
                        {
                            log.Info("Catalog source directory doesn't exists. So backup didn't got created.", false);
                        }

                        //delete catalog file
                        if (File.Exists(backupFileParentDir + "/" + catalog + ".txt"))
                        {
                            File.Delete(backupFileParentDir + "/" + catalog + ".txt");
                        }
                    }

                    string finalMsg = string.Format("\nBackup task completed at {0}.", DateTime.Now.ToShortTimeString());
                    log.Info(finalMsg, false); Console.WriteLine(finalMsg);
                }
                else
                {
                    string error = "Directory does not exists.. : " + backupFileParentDir;
                    log.Error(error); Console.WriteLine(error);
                    throw new DirectoryNotFoundException(string.Format("Backup file {0} does not exists.", backupFileParentDir));
                }
            }
            catch (Exception ex)
            {
                mailSubject = string.Format("CRITICAL ALERT!!! Daily {0} Catalog Backup Failed.", GetValueFromConfigOrArgument(args, "clientName"));
                mailBody = string.Format("Backup on {0} failed.\n\nException : {1}\nInner Exception:{2}", GetValueFromConfigOrArgument(args, "clientName"), ex.Message, ex.StackTrace);
                log.Error(string.Format("Exception : \n{0}\n{1}", mailSubject, mailBody));
                Console.WriteLine(string.Format("Exception : \n{0}\n{1}", mailSubject, mailBody));
                SendMail(mailSubject, mailBody, "");
            }
            #endregion 

            if (AnyErrorOccuredInBackup)
            {
                mailSubject = string.Format("CRITICAL ALERT!!! Daily {0} Catalog Backup completed with failures.", GetValueFromConfigOrArgument(args, "clientName"));
                SendMail(mailSubject, mailBody, logFile);
            }
            else
            {
                SendSuccessMail(sourceDir, mailBody, logFile);
            }
        }

        /// <summary>
        /// Collect all backed up catalog dirs from backup root
        /// </summary>
        /// <param name="destinationDir"></param>
        /// <returns></returns>
        private static Dictionary<string, List<DirectoryInfo>> CollectBackedupCatalogDirsFromRoot(string catalogName, string destinationDir)
        {
            //collect catalog dirs from catalog dir root
            DirectoryInfo[] ctlgDirs = new DirectoryInfo(destinationDir).GetDirectories(string.Format("{0}*", catalogName), SearchOption.TopDirectoryOnly);
            
            //separate catalog dirs by catalog name
            Dictionary<string, List<DirectoryInfo>> dictCtlgWithItsDirs = new Dictionary<string,List<DirectoryInfo>>();
            foreach (DirectoryInfo ctlgDir in ctlgDirs)
            {
                //initiallize list if its first time. 
                if (!dictCtlgWithItsDirs.ContainsKey(catalogName))
                    dictCtlgWithItsDirs.Add(catalogName, new List<DirectoryInfo>());
                
                dictCtlgWithItsDirs[catalogName].Add(ctlgDir);
            }
            return dictCtlgWithItsDirs;
        }

        /// <summary> Will help to identify backup space availability </summary>
        /// <param name="currentCatalogNeedSpace"> space needed for current catalog to get backed up. </param>
        /// <returns></returns>
        private static void DetermineBackupSpaceAvailability(string sourceDir, long diskSpaceReqForCatalog, string catalog)
        {
            //get available disk space
            long availableDiskSpace = 0; long totalDiskSpace = 0;
            GetDiskSpace(sourceDir, ref availableDiskSpace, ref totalDiskSpace);

            //available disk space after catalog backup 
            availableDiskSpace = availableDiskSpace - diskSpaceReqForCatalog;
            double afterBackupRemainingDiskSpacePercentage = (availableDiskSpace * 100) / totalDiskSpace;

            log.Debug(string.Format("After backing up still have {0}% disk space available.", afterBackupRemainingDiskSpacePercentage));
            
            if (afterBackupRemainingDiskSpacePercentage < _lowDiskSpaceAlertIfDiskSpaceLessThanThresholdInPercentage)
            {
                log.Error("catalog " + catalog + " backup faild as do not have enough space on drive.");
                string mailSubject = string.Format("CRITICAL ALERT!!! Daily Catalog Backup Failed! - Low Disk Space");

                string mailBody = string.Format(
                        "The catalog backups cannot be completed. Available free disk space is less than {0}%.\n" +
                        "Free disk space remaining after backups: {1:00.00}",
                            _lowDiskSpaceAlertIfDiskSpaceLessThanThresholdInPercentage, ConvertBytesToGigabytes(availableDiskSpace));

                SendMail(mailSubject, mailBody, "");
                Environment.Exit(1);
            }
        }

        /// <summary> Get disk space  </summary>
        /// <param name="sourceDir">Source directory</param>
        /// <param name="availableDiskSpace">Available disk space</param>
        /// <param name="totalDiskSpace">Total disk space</param>
        public static void GetDiskSpace(string sourceDir, ref long availableDiskSpace, ref long totalDiskSpace)
        {
            string driveName = sourceDir.Substring(0, 1) + ":\\";
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady && drive.Name == driveName)
                {
                    availableDiskSpace = drive.TotalFreeSpace;
                    totalDiskSpace = drive.TotalSize;
                    break;
                }
            }
        }

        /// <summary>
        /// Delete extra copies of catalog backups
        /// leave last 3 copies of catalog backups
        /// </summary>
        /// <param name="catalogDirs"></param>
        private static void DeleteExtraCatalogBackups(Dictionary<string, List<DirectoryInfo>> allCatalogDirs, string catalog)
        {
            //Delete extra backup copy 
            //Only if catalog has more than one backup copy already exists
            //Before deleting check number of existing copy of catalog backup. 
            if (allCatalogDirs.ContainsKey(catalog))
            {
                DirectoryInfo[] catalogDirs = allCatalogDirs[catalog].ToArray();
                catalogDirs = catalogDirs.OrderBy(p => p.CreationTime).ToArray();
                string msg = string.Format("Total number of backup copies: {0}.", catalogDirs.Length);
                log.Info(msg, false); Console.WriteLine("\n" + msg);

                if (catalogDirs.Length > 3)
                {
                    string latestCatalogMessage = "Current catalogs have the dates";
                    int counter = 0;
                    Console.WriteLine(string.Format("\nCatalog dirs to delete : {0}", catalogDirs.Length - 3));
                    foreach (DirectoryInfo dir in catalogDirs)
                    {
                        try
                        {
                            if (counter < catalogDirs.Length - 3)
                            {
                                if (_deleteOldCatalogs)
                                {
                                    Directory.Delete(dir.FullName, true);
                                    Console.WriteLine("Deleted:" + dir.Name);
                                    log.Info("Deleted backup copy " + dir.Name + " created @ " + dir.CreationTime, false);
                                }
                                else
                                {
                                    log.Info("Catalog dir " + dir.Name + " created @ " + dir.CreationTime + ", To delete.", false);
                                }
                            }
                            else
                            {
                                latestCatalogMessage = string.Format("{0} {1},", latestCatalogMessage, dir.CreationTime);
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.Message);
                            log.Error(string.Format("Catalog : {0}\nException : {1}\nStacktrace : {2}", catalog, e.Message, e.InnerException) , false);
                        }
                        counter++;
                    }
                    log.Info(string.Format("{0}.", latestCatalogMessage.Trim().Substring(0, latestCatalogMessage.Length - 1)), false);
                }
            }
        }

        /// <summary>
        /// Copy source directory to destination
        /// </summary>
        /// <param name="sourceDirName"></param>
        /// <param name="destDirName"></param>
        /// <param name="copySubDirs"></param>
        private static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs)
        {
            DirectoryInfo dir = new DirectoryInfo(sourceDirName);
            DirectoryInfo[] dirs = dir.GetDirectories();

            if (!dir.Exists)
                throw new DirectoryNotFoundException("Source directory does not exist or could not be found: "+ sourceDirName);

            if (Directory.Exists(destDirName))
                Directory.Delete(destDirName, true);

            Directory.CreateDirectory(destDirName);

            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string temppath = Path.Combine(destDirName, file.Name);
                file.CopyTo(temppath, false);
                _processedFiles++;
                ConsoleWriteProgress((100 * _processedFiles) / _totalFiles);
            }

            if (copySubDirs)
            {
                foreach (DirectoryInfo subdir in dirs)
                {
                    string temppath = Path.Combine(destDirName, subdir.Name);
                    DirectoryCopy(subdir.FullName, temppath, copySubDirs);
                }
            }
        }

        /// <summary>
        /// Write progress percentage to console
        /// </summary>
        /// <param name="percentageCompleted"></param>
        private static void ConsoleWriteProgress(int percentageCompleted)
        {
            try
            {
                StringBuilder sb;
                sb = new StringBuilder(_totalFiles);
                sb.Append(": ");
                sb.Append(percentageCompleted.ToString());
                sb.Append(" % Complete");

                int indexToGoLeft = _backingUpProgressMessage.Length;
                if (indexToGoLeft > Console.WindowWidth)
                    Console.CursorLeft = indexToGoLeft - Console.WindowWidth;
                else
                    Console.CursorLeft = indexToGoLeft;
                Console.Write(sb.ToString());
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        /// <summary>
        /// It validates if backup have been created successfully or not
        /// </summary>
        /// <param name="sourceDirName"></param>
        /// <param name="destDirName"></param>
        private static void BackupValidator(string sourceDirName, string destDirName)
        {
            if (!Directory.Exists(destDirName))
                throw new DirectoryNotFoundException("Catalog backup failed.. catalog directory doesnot exists.");

            DirectoryInfo sourceDirInfo = new DirectoryInfo(sourceDirName);
            DirectoryInfo destDirInfo = new DirectoryInfo(destDirName);

            if (sourceDirInfo.GetDirectories("*.*", SearchOption.AllDirectories).Length != destDirInfo.GetDirectories("*.*", SearchOption.AllDirectories).Length)
                throw new Exception("Catalog backup failed.. Different number of sub-directories in catalog directories.");

            if (sourceDirInfo.GetFiles("*.*", SearchOption.AllDirectories).Length != destDirInfo.GetFiles("*.*", SearchOption.AllDirectories).Length)
                throw new Exception("Catalog backup failed.. Different number of files in catalog directories.");

            if (GetDirectorySize(sourceDirName) != GetDirectorySize(destDirName))
                throw new Exception("Catalog backup failed.. catalog directory sizes are different.");
        }

        /// <summary>
        /// Get size of directory
        /// </summary>
        /// <param name="parentDirectory"></param>
        /// <returns></returns>
        public static long GetDirectorySize(string parentDirectory)
        {
            return new DirectoryInfo(parentDirectory).GetFiles("*.*", SearchOption.AllDirectories).Sum(file => file.Length);
        }

        /// <summary>
        /// Parse the value from config/argument
        /// </summary>
        /// <param name="args">List of arguments</param>
        /// <param name="ArgumentName">Argument name</param>
        /// <returns></returns>
        private static String GetValueFromConfigOrArgument(string[] args, String ArgumentName, string argsType="string")
        {
            String CurrentValue;
            CurrentValue = System.Configuration.ConfigurationManager.AppSettings[ArgumentName];

            ArgumentName = ArgumentName.ToLower();
            String CurrentArg;

            for (int i = 0; i < args.Length; i++)
            {
                CurrentArg = args[i].ToLower();
                if (CurrentArg.StartsWith("-" + ArgumentName) | CurrentArg.StartsWith("/" + ArgumentName) | CurrentArg.StartsWith("–" + ArgumentName))
                {
                    try
                    {
                        CurrentValue = args[i + 1];
                    }
                    catch (Exception ex)
                    {
                        throw new ArgumentException(string.Format("Error reading command line value.  ArgumentName={0}", ArgumentName, ex));
                    }
                    break;
                }
            }

            //Handle all null values
            if (CurrentValue == null)
                CurrentValue = "";

            if (CurrentValue.Contains("\\"))
                CurrentValue = CurrentValue.Replace("\\", "/");

            if (argsType.Equals("int"))
            {
                int number;
                if (!int.TryParse(CurrentValue, out number))
                    CurrentValue = "0";
            }

            return CurrentValue;
        }

        private static void SendSuccessMail(string sourceDir, string body, string logfile)
        {
            long totalDiskSpace = 0, availableDiskSpace = 0;
            GetDiskSpace(sourceDir, ref availableDiskSpace, ref totalDiskSpace);

            double remainingDiskSpacePercentage = (availableDiskSpace * 100) / totalDiskSpace;
            bool isWarningRequired = remainingDiskSpacePercentage <= _lowDiskSpaceWarningIfDiskSpaceLessThanThresholdInPercentage;
            
            if (isWarningRequired)
            {
                Logger.Shutdown();

                body = string.Format("Catalog backups are approaching the critical low free disk space threshold. Available free disk space is {0}%.\n" +
                        "Free disk space remaining: {1:00.00} GB\n" +
                        "Catalog backups will be suspended when the free disk space falls below {2}%.", remainingDiskSpacePercentage, ConvertBytesToGigabytes(availableDiskSpace),
                        _lowDiskSpaceAlertIfDiskSpaceLessThanThresholdInPercentage);
                body = body + "\n\n----------------\n\n" + File.ReadAllText(Logger.PrimaryLogFile);
                SendMail("CRITICAL ALERT !!! Daily Catalog Backup is Nearing Low Disk Space", body, string.Empty);
                return;
            }

            SendMail("Daily catalog backup was Successful", body, logfile);
        }

        /// <summary>
        /// Send mail
        /// </summary>
        /// <param name="subject"></param>
        /// <param name="body"></param>
        /// <param name="attachment"></param>
        private static void SendMail(string subject, string body, string attachment)
        {
            try
            {
                string sendMailEXEPath = _sendEmailPath + "sendEmail.exe";
                string arguments = string.Empty;
                if (string.IsNullOrEmpty(attachment))
                    arguments = string.Format("-f {0} -t {1} -s {2} -u \"{3}\" -m \"{4}\"", _mailFrom, _mailTo, _mailServer, subject, body);
                else
                    arguments = string.Format("-f {0} -t {1} -s {2} -u \"{3}\" -o message-file=\"{4}\"", _mailFrom, _mailTo, _mailServer, subject, attachment);
                System.Diagnostics.Process.Start(sendMailEXEPath, arguments);
            }
            catch (Exception ex)
            {
                log.Error(string.Format("Exception SendEmail : \n{0}\n{1}", ex.Message, ex.InnerException));
                Console.WriteLine(string.Format("Exception SendEmail : \n{0}\n{1}", ex.Message, ex.InnerException));
                throw ex;
            }
        }

        /// <summary>
        /// Convert bytes to mb
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        static double ConvertBytesToMegabytes(long bytes)
        {
            return (bytes / 1024f) / 1024f;
        }

        /// <summary>
        /// Convert bytes to gb
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        static double ConvertBytesToGigabytes(long bytes)
        {
            return ConvertBytesToMegabytes(bytes) / 1024.0;
        }
    }
}
