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
                _keepLastXCopyOfBackup = Convert.ToInt32(GetValueFromConfigOrArgument(args, "keepLastXCopyOfBackup"));
                _sendEmailPath = GetValueFromConfigOrArgument(args, "sendEmailExePath");
                mailSubject = "";
                _mailFrom = GetValueFromConfigOrArgument(args, "mailFrom");
                _mailTo = GetValueFromConfigOrArgument(args, "mailTo");
                _mailServer = GetValueFromConfigOrArgument(args, "mailServer");
                sourceDir = GetValueFromConfigOrArgument(args, "sourceDir");
                destinationDir = GetValueFromConfigOrArgument(args, "destinationDir");
                _deleteOldCatalogs = Convert.ToBoolean(GetValueFromConfigOrArgument(args, "deleteOldCatalogs"));
                _lowDiskSpaceAlertIfDiskSpaceLessThanThresholdInPercentage = Convert.ToInt32(GetValueFromConfigOrArgument(args, "lowDiskSpaceAlertIfDiskSpaceLessThanThresholdInPercentage"));

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
                    string catalogMessage = string.Format("Catalogs needing backup - {0}", catalogTobackupQueue.Count);
                    log.Info(catalogMessage, false); Console.WriteLine(catalogMessage);
                    #endregion

                    if (catalogTobackupQueue.Count < 1)
                    {
                        string msg = "\nJob is over... tada!!";
                        Console.WriteLine(msg); log.Info(msg, false);

                        mailSubject = "Daily catalog backup was Successful";
                        SendMail(mailSubject, mailBody, logFile);
                        Environment.Exit(0);
                    }

                    log.Info(string.Format("\n\nBackup starting at \"{0}\"", DateTime.Now.ToShortTimeString()), false);
                    
                    //iterate through each catalog for backup process
                    foreach (string catalog in catalogTobackupQueue)
                    {
                        log.Info("\nCatalog - " + catalog + " @ " + DateTime.Now.ToShortTimeString() + "'", false);
                        string sourceDirPath = sourceDir + catalog + "/";

                        //Check if source directory exists.. 
                        if (Directory.Exists(sourceDirPath))
                        {
                            bool backupSuccess = false;
                            try
                            {
                                //Collect disk space needed to backup catalog
                                long diskSpaceReqForCatalog = GetDirectorySize(sourceDirPath);
                                log.Info("\tcatalog " + catalog + " need " + Math.Round(ConvertBytesToMegabytes(diskSpaceReqForCatalog), 2) + " MBytes disk space.", false);

                                //Check if disk has more than threshold disk space after backing up catalog
                                DetermineBackupSpaceAvailability(sourceDir, diskSpaceReqForCatalog, catalog);

                                //now take backup of catalog
                                string destDirPath = destinationDir + string.Format("{0}_{1}.old", catalog, DateTime.Now.ToString("MM-dd-yyyy_hhmmss")) + "/";
                                _totalFiles = new DirectoryInfo(sourceDirPath).GetFiles("*.*", SearchOption.AllDirectories).Length; _processedFiles = 0;
                                _backingUpProgressMessage = string.Format("\nBacking-up: {0},", catalog, _totalFiles);
                                Console.Write(_backingUpProgressMessage);

                                DirectoryCopy(sourceDirPath, destDirPath, true);
                                BackupValidator(sourceDirPath, destDirPath);
                                backupSuccess = true;
                                log.Info(string.Format("\tSuccessfully backed up @ '{1}'", catalog, DateTime.Now.ToShortTimeString()), false);

                                //delete old catalog backup copy and backup file from parente folder
                                if (backupSuccess)
                                {
                                    //after backup collect catalog dirs from backup root
                                    Dictionary<string, List<DirectoryInfo>> dictCtlgWithItsDirs = CollectBackedupCatalogDirsFromRoot(destinationDir);

                                    //delete extra backup copy
                                    DeleteExtraCatalogBackups(dictCtlgWithItsDirs[catalog].ToArray(), catalog);
                                }
                            }
                            catch (Exception ex)
                            {
                                log.Error(string.Format("Catalog backup failed for {0}\nFailure Reason : {1}\nStacktrace : {2}", catalog, ex.Message, ex.StackTrace));
                                AnyErrorOccuredInBackup = true;
                            }
                        }
                        else
                        {
                            log.Info("\tCatalog source directory doesn't exists. So backup didn't got created.", false);
                        }

                        //delete catalog file
                        if (File.Exists(backupFileParentDir + "/" + catalog + ".txt"))
                        {
                            File.Delete(backupFileParentDir + "/" + catalog + ".txt");
                        }
                    }

                    string finalMsg = "\nAll catalogs have been backed-up!! Job is over!! tada!!";
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
                mailSubject = string.Format("CRITICAL ALERT!!! Daily {0} Catalog Backup compelted with failures.", GetValueFromConfigOrArgument(args, "clientName"));
                SendMail(mailSubject, mailBody, logFile);
            }
            else
            {
                mailSubject = "Daily catalog backup was Successful";
                SendMail(mailSubject, mailBody, logFile);
            }
        }

        /// <summary>
        /// Collect all backed up catalog dirs from backup root
        /// </summary>
        /// <param name="destinationDir"></param>
        /// <returns></returns>
        private static Dictionary<string, List<DirectoryInfo>> CollectBackedupCatalogDirsFromRoot(string destinationDir)
        {
            //collect catalog dirs from catalog dir root
            DirectoryInfo[] ctlgDirs = new DirectoryInfo(destinationDir).GetDirectories();
            
            //separate catalog dirs by catalog name
            Dictionary<string, List<DirectoryInfo>> dictCtlgWithItsDirs = new Dictionary<string,List<DirectoryInfo>>();
            foreach (DirectoryInfo ctlgDir in ctlgDirs)
            {
                //format of catalog backup folder = "catalog" + "P4_" + datetime.log
                //collect catalog directories and separate it by catalog name...
                string catalogName = string.Empty;
                
                if (ctlgDir.Name.Contains("P4_"))
                    catalogName = ctlgDir.Name.Substring(0, ctlgDir.Name.IndexOf("P4_") + 2);
                if (ctlgDir.Name.Contains("p4_"))
                    catalogName = ctlgDir.Name.Substring(0, ctlgDir.Name.IndexOf("p4_") + 2);

                if (!catalogName.Equals(string.Empty))
                {
                    if (!dictCtlgWithItsDirs.ContainsKey(catalogName))
                    {
                        dictCtlgWithItsDirs.Add(catalogName, new List<DirectoryInfo>());
                        dictCtlgWithItsDirs[catalogName].Add(ctlgDir);
                    }
                    else
                        dictCtlgWithItsDirs[catalogName].Add(ctlgDir);
                }
            }
            return dictCtlgWithItsDirs;
        }

        /// <summary> Will help to identify backup space availability </summary>
        /// <param name="currentCatalogNeedSpace"> space needed for current catalog to get backed up. </param>
        /// <returns></returns>
        private static void DetermineBackupSpaceAvailability(string sourceDir, long diskSpaceReqForCatalog, string catalog)
        {
            //get available disk space
            string driveName = sourceDir.Substring(0, 1) + ":\\"; long availableDiskSpace = 0; long totalDiskSpace = 0;
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady && drive.Name == driveName)
                {
                    availableDiskSpace = drive.TotalFreeSpace;
                    totalDiskSpace = drive.TotalSize;
                    break;
                }
            }

            //available disk space after catalog backup 
            availableDiskSpace = availableDiskSpace - diskSpaceReqForCatalog;
            double afterBackupRemainingDiskSpacePercentage = (availableDiskSpace * 100) / totalDiskSpace;

            log.Debug(string.Format("After backing up still have {0}% disk space available.", afterBackupRemainingDiskSpacePercentage));
            
            if (afterBackupRemainingDiskSpacePercentage < _lowDiskSpaceAlertIfDiskSpaceLessThanThresholdInPercentage)
            {
                log.Error("catalog " + catalog + " backup faild as do not have enough space on drive.");
                string mailSubject = string.Format("CRITICAL ALERT! Daily Catalog Backup Failed! - Low Disk Space");
                string mailBody = string.Format("Catalog backup cannot be completed as availeble disk space on drive is less than threshold {0}%\n"
                        + "Total Disk Space : {1:00.00}, \nDisk Space required for Catalog backup : {2:00.00}\nAvailable diskspace after backup : {3:00.00}({0})",
                            _lowDiskSpaceAlertIfDiskSpaceLessThanThresholdInPercentage, ConvertBytesToMegabytes(totalDiskSpace),
                            ConvertBytesToMegabytes(diskSpaceReqForCatalog), ConvertBytesToMegabytes(availableDiskSpace));
                SendMail(mailSubject, mailBody, "");
                Environment.Exit(1);
            }
        }

        /// <summary>
        /// Delete extra copies of catalog backups
        /// leave last 3 copies of catalog backups
        /// </summary>
        /// <param name="catalogDirs"></param>
        private static void DeleteExtraCatalogBackups(DirectoryInfo[] catalogDirs, string catalog)
        {
            catalogDirs = catalogDirs.OrderBy(p => p.CreationTime).ToArray();
            string msg = string.Format("\tTotal backup copy '{1}', latest catalog copy date '{2}'.", catalog, catalogDirs.Length, catalogDirs[catalogDirs.Length - 1].CreationTime.ToShortDateString());
            log.Info("\t" + msg, false); Console.WriteLine("\n" + msg);
                        
            if (catalogDirs.Length > 3)
            {
                string latestCatalogMessage = "\t\tCurrent catalogs have the dates ";
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
                                log.Info("\t\tDeleted : backup copy : " + dir.Name + " created @ '" + dir.CreationTime + "'", false);
                            }
                            else
                                log.Info("\t\tCatalog dir " + dir.Name + " created @ " + dir.CreationTime + ", To delete.", false);
                        }
                        else
                        {
                            latestCatalogMessage = latestCatalogMessage + "\"" + dir.CreationTime + "\",";
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                        log.Error("Catalog : {0}\nException : {1}\nStacktrace : {2}", false);
                    }
                    counter++;
                }
                latestCatalogMessage = latestCatalogMessage.Substring(0, latestCatalogMessage.Length - 1) + ".";
                log.Info(latestCatalogMessage, false);
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
        private static String GetValueFromConfigOrArgument(string[] args, String ArgumentName)
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

            return CurrentValue;
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
    }
}
