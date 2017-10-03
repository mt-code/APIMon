using System;
using System.Collections.Generic;
using System.IO;
using System.Net.NetworkInformation;
using System.Threading;
using APIMon.Classes;

namespace APIMon
{
    /**
     * CSV Structure:
     * 
     *  1 - Device name
     *  2 - Device IP address
     *  3 - Polling frequency
     *  4 - Time-out limit
     *  5 - Last known state (1 = ONLINE, 0 = OFFLINE)
     *  6 - Current poll count
     *  7 - Current time-out count
     *  8 - Time-out state (1 = Timed-out, 0 = Not Timed-out)
     * 
     * */

    class Program
    {
        private static bool _isVerbose = true, _hasUploadedLogs = true;
        private static int _cycleFrequency, _cycleCount = 1;
        private static string _hourToUpload;
        private static readonly List<string> UpdatedDeviceList = new List<string>(); 

        // Log variables.
        private const string LogDirectory = "logs";
        private const string OverallLog = "overall.log";
        private const string DeviceList = "devices.csv";
        private const string ConfigFile = "config.csv";

        // FTP variables.
        private static string _remoteLogName;
        private const string FtpHost = @"ftp://pialert.lde.co.uk/";
        private const string AlertUser = "apialert";
        private const string AlertPass = "pialerts";
        private const string LogUser = "apilogs";
        private const string LogPass = "pilogs";

        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "verbose")
                _isVerbose = true;

            // Checks for neccesary files and creates and directories needed for execution.
            if (!Initialize())
                return;

            // Main loop.
            while (true)
            {
                try
                {
                    CheckTime();

                    if (_isVerbose)
                        Console.WriteLine("Polling Cycle #{0}:", _cycleCount++);

                    UpdatedDeviceList.Clear();
                    PollDevices();

                    if (_isVerbose)
                        Console.WriteLine("");

                    Thread.Sleep(_cycleFrequency * 1000);
                }
                catch (Exception ex)
                {
                    string errorFileName = String.Format("{0:yyyy-MM-dd_hh-mm-ss-tt}-error.log", DateTime.Now);

                    File.WriteAllText(errorFileName, ex.ToString());
                }
            }
        }

        /// <summary>
        /// Checks for neccesary files and creates and directories needed for execution.
        /// </summary>
        /// <returns>True if all neccesary files are located or false if otherwise.</returns>
        private static bool Initialize()
        {
            Console.WriteLine("APIMon v1.2 - Created by Matthew Croston (support@byteguardsoftware.co.uk)");
            Console.WriteLine("-");

            if (!File.Exists(DeviceList))
            {
                Console.WriteLine("Cannot find the specified file: {0}", DeviceList);
                return false;
            }

            if (!File.Exists(ConfigFile))
            {
                Console.WriteLine("Cannot find the specified file: {0}", ConfigFile);
                return false;
            }
            
            // Assign variables from the config files.
            string[] configDetails = File.ReadAllText(ConfigFile).Split(',');
            _remoteLogName = configDetails[0];
            _cycleFrequency = Convert.ToInt32(configDetails[1]);
            _hourToUpload = configDetails[2];

            string individualLogDirectory = Path.Combine(LogDirectory, "individual");

            if (!Directory.Exists(LogDirectory))
            {
                Directory.CreateDirectory(LogDirectory);
                Directory.CreateDirectory(individualLogDirectory);
            }
            else if (!Directory.Exists(individualLogDirectory))
            {
                Directory.CreateDirectory(individualLogDirectory);
            }

            return true;
        }

        private static void CheckTime()
        {
            string currentHour = String.Format("{0:HH}", DateTime.Now);

            if (currentHour == _hourToUpload && !_hasUploadedLogs)
            {
                // Currently between 00:00-01:00 and logs haven't been uploaded.
                Logs log = new Logs(FtpHost, LogUser, LogPass, _isVerbose);
                log.UploadLogs(Path.Combine(LogDirectory, "individual"));

                _hasUploadedLogs = true;
                _cycleCount = 1;
            }
            else if (currentHour != _hourToUpload)
            {
                _hasUploadedLogs = false;
            }
        }

        /// <summary>
        /// Polls all the devices and logs accordingly.
        /// </summary>
        private static void PollDevices()
        {
            bool uploadRequired = false;
            List<string> overallLog = new List<string>();

            foreach (string deviceString in File.ReadAllLines(DeviceList))
            {
                // Prevents errors if any additional lines are added.
                if (deviceString == String.Empty)
                    continue;

                // Gather required information from the device string.
                string[] deviceInformation = deviceString.Split(',');

                string deviceName = deviceInformation[0];
                string deviceAddress = deviceInformation[1];

                int pollingFrequency = Convert.ToInt32(deviceInformation[2]);
                int timeoutLimit = Convert.ToInt32(deviceInformation[3]);
                int currentPollCount = Convert.ToInt32(deviceInformation[5]);
                int currentTimeoutCount = Convert.ToInt32(deviceInformation[6]);

                bool isOnline = false, isTimedOut = false;
                bool wasOnline = Convert.ToInt32(deviceInformation[4]) == 1;
                bool wasTimedOut = Convert.ToInt32(deviceInformation[7]) == 1;

                // Checks whether it is time to poll the current device.
                if (currentPollCount == pollingFrequency)
                {
                    if (_isVerbose)
                        Console.Write(" - Currently polling {0}... ", deviceName);

                    isOnline = PingDevice(deviceAddress);

                    if (currentTimeoutCount >= timeoutLimit && !isOnline)
                        isTimedOut = true;

                    if (wasTimedOut != isTimedOut)
                    {
                        uploadRequired = true;

                        if (_isVerbose)
                            Console.WriteLine(isOnline ? "BACK ONLINE!" : "TIMED OUT! (Limit Reached)");
                    }
                    else
                    {
                        if (_isVerbose)
                            Console.WriteLine(isOnline
                                ? "ONLINE!"
                                : (wasOnline ? "OFFLINE" : String.Format("TIMTED OUT! (Count: {0})", currentTimeoutCount)));
                    }
                        
                    LogIndividualState(deviceName, deviceAddress, isOnline, wasOnline);
                }
                else if (wasOnline)
                {
                    // Prevents isOnline being set to false due to the device not being polled.
                    isOnline = true;
                }

                // Creates a string to be logged in the overall log file.
                string logString = String.Format("{0} - {1} ({2}) is currently {3}.",
                    DateTime.Now,
                    deviceName,
                    deviceAddress,
                    isTimedOut ? "OFFLINE" : "ONLINE");

                if (isTimedOut)
                    logString = String.Format("*** {0} ***", logString);

                overallLog.Add(logString);

                UpdateDeviceList(deviceName, deviceAddress, pollingFrequency, timeoutLimit, currentPollCount, currentTimeoutCount, isOnline, isTimedOut);
            }
            
            File.WriteAllLines(OverallLog, overallLog);
            File.WriteAllLines(DeviceList, UpdatedDeviceList);

            if (uploadRequired)
            {
                Logs log = new Logs(FtpHost, AlertUser, AlertPass, _isVerbose);

                log.UploadLog(OverallLog, _remoteLogName);
            }
        }

        private static void UpdateDeviceList(string deviceName, string deviceAddress, int pollingFrequency,
            int timeoutLimit, int currentPollCount, int currentTimeoutCount, bool isOnline, bool isTimedOut)
        {
            if (currentPollCount == pollingFrequency)
            {
                currentPollCount = 1;
                currentTimeoutCount = (isOnline ? 1 : currentTimeoutCount + 1);
            }
            else
            {
                currentPollCount++;
            }

            string newDeviceLine =
                String.Format("{0},{1},{2},{3},{4},{5},{6},{7}", 
                    deviceName, deviceAddress, pollingFrequency, timeoutLimit, isOnline ? "1" : "0",
                    currentPollCount, currentTimeoutCount, isTimedOut ? "1" : "0");

            UpdatedDeviceList.Add(newDeviceLine);
        }

        /// <summary>
        /// Logs whether the specified device is currently on/offline.
        /// </summary>
        /// <param name="deviceName">The name of the deivce.</param>
        /// <param name="deviceAddress">The address of the device.</param>
        /// <param name="isOnline">A boolean value stating if the device IS online/offline.</param>
        /// <param name="wasOnline">A boolean value stating if the device WAS online/offline.</param>
        private static void LogIndividualState(string deviceName, string deviceAddress, bool isOnline, bool wasOnline)
        {
            if (isOnline == wasOnline) return;

            string logName = Path.Combine(LogDirectory, "individual", String.Format("{0}.log", deviceName));

            File.AppendAllText(logName,
                String.Format("{0} - {1} is now {2}.{3}",
                    DateTime.Now, deviceAddress, isOnline ? "ONLINE" : "OFFLINE", Environment.NewLine));
        }

        /// <summary>
        /// Pings the specified device.
        /// </summary>
        /// <param name="deviceAddress">The device IP address to ping.</param>
        /// <returns>True if device is pinged successfully or false if otherwise.</returns>
        private static bool PingDevice(string deviceAddress)
        {
            bool isPingable = false;

            using (Ping pinger = new Ping())
            {
                try
                {
                    PingReply pReply = pinger.Send(deviceAddress);

                    if (pReply != null) isPingable = (pReply.Status == IPStatus.Success);
                }
                catch (PingException)
                {
                    return isPingable;
                }
            }

            return isPingable;
        }
    }
}