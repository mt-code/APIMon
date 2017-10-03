using System;
using System.IO;
using FTP;

namespace APIMon.Classes
{
    internal class Logs
    {
        private readonly bool _isVerbose;
        private readonly string _ftpHost, _ftpUser, _ftpPass;

        public Logs(string ftpHostname, string ftpUsername, string ftpPassword, bool isVerbose)
        {
            _ftpHost = ftpHostname;
            _ftpUser = ftpUsername;
            _ftpPass = ftpPassword;
            _isVerbose = isVerbose;
        }

        /// <summary>
        /// Uploads the specified file to the FTP server.
        /// </summary>
        /// <param name="localFilename">The filename of the log file you wish to upload.</param>
        /// <param name="remoteFilename">The filename that you wish to save the log file with on the FTP server.</param>
        public void UploadLog(string localFilename, string remoteFilename)
        {
            while (true)
            {
                if (!File.Exists(localFilename))
                    return;

                if (_isVerbose)
                    Console.WriteLine("*** UPLOADING OVERALL LOG FILE ***");

                FTPclient ftp = new FTPclient(_ftpHost, _ftpUser, _ftpPass);

                //if (ftp.Upload(Path.GetFullPath(OverallLog), String.Format("{0:yyyy-MM-dd_hh-mm-ss-tt}.log", DateTime.Now)))
                if (ftp.Upload(Path.GetFullPath(localFilename), String.Format("{0}.log", remoteFilename)))
                {
                    if (_isVerbose)
                        Console.WriteLine("*** UPLOAD SUCCESSFUL ***");
                }
                else
                {
                    if (_isVerbose)
                        Console.WriteLine("*** UPLOAD FAILED - RETRYING ***");

                    continue;
                }
                break;
            }
        }

        /// <summary>
        /// Uploads all files within the specified directory.
        /// </summary>
        /// <param name="directoryPath">All files within this directory will be uploaded to the FTP server.</param>
        public void UploadLogs(string directoryPath)
        {
            if (_isVerbose)
                Console.WriteLine("*** UPLOADING DAILY LOG FILES ({0}) ***", DateTime.Now);

            FTPclient ftp = new FTPclient(_ftpHost, _ftpUser, _ftpPass);
            //string individualLogDirecotry = Path.Combine(LogDirectory, "individual");

            foreach (string logFile in Directory.GetFiles(directoryPath))
            {
                if (_isVerbose)
                    Console.Write(" - Uploading {0}... ", logFile);

                if (ftp.Upload(Path.GetFullPath(logFile), String.Format("{0:yyyy-MM-dd_hh-mm-ss-tt}-{1}", DateTime.Now, Path.GetFileName(logFile))))
                {
                    File.Delete(logFile);

                    if (_isVerbose)
                        Console.WriteLine("done!");
                }
            }

            if (_isVerbose)
                Console.WriteLine("*** UPLOAD COMPLETE ***");
        }
    }
}
