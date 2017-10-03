using System.Diagnostics;
using System.Data;
using System.Collections;
using Microsoft.VisualBasic;
using System.Collections.Generic;
using System;
using System.Net;
using System.IO;
using System.Text.RegularExpressions;

namespace FTP
{


    #region "FTP client class"
    /// <summary>
    /// A wrapper class for .NET 2.0 FTP protocol
    /// </summary>
    /// <remarks>
    /// This class does not hold open an FTP connection but
    /// instead is stateless: for each FTP request it
    /// connects, performs the request and disconnects.
    /// 
    /// v1.0 - original version
    /// 
    /// v1.1 - added support for EnableSSL, UsePassive and Proxy connections
    /// 
    /// v1.2 - added support for downloading correct date/time from FTP server for
    ///        each file
    ///        Added FtpDirectoryExists function as FtpFileExists does not work as directory
    ///        exists check.
    ///        Amended all URI encoding to ensure special characters are encoded 
    /// </remarks>
    public class FTPclient
    {

        #region "CONSTRUCTORS"
        /// <summary>
        /// Blank constructor
        /// </summary>
        /// <remarks>Hostname, username and password must be set manually</remarks>
        public FTPclient()
        {
        }

        /// <summary>
        /// Constructor just taking the hostname
        /// </summary>
        /// <param name="Hostname">in either ftp://ftp.host.com or ftp.host.com form</param>
        /// <remarks></remarks>
        public FTPclient(string Hostname)
        {
            _hostname = Hostname;
        }

        /// <summary>
        /// Constructor taking hostname, username and password
        /// </summary>
        /// <param name="Hostname">in either ftp://ftp.host.com or ftp.host.com form</param>
        /// <param name="Username">Leave blank to use 'anonymous' but set password to your email</param>
        /// <param name="Password"></param>
        /// <remarks></remarks>
        public FTPclient(string Hostname, string Username, string Password)
        {
            _hostname = Hostname;
            _username = Username;
            _password = Password;
        }

        /// <summary>
        /// Constructor taking hostname, username, password and KeepAlive property
        /// </summary>
        /// <param name="Hostname">in either ftp://ftp.host.com or ftp.host.com form</param>
        /// <param name="Username">Leave blank to use 'anonymous' but set password to your email</param>
        /// <param name="Password">Password</param>
        /// <param name="KeepAlive">Set True to keep connection alive after each request</param>
        /// <remarks></remarks>
        public FTPclient(string Hostname, string Username, string Password, bool KeepAlive)
        {
            _hostname = Hostname;
            _username = Username;
            _password = Password;
            _keepAlive = KeepAlive;
        }

        #endregion

        #region "Upload: File transfer TO ftp server"

        /// <summary>
        /// Copy a local file to the FTP server from local filename string
        /// </summary>
        /// <param name="localFilename">Full path of the local file</param>
        /// <param name="targetFilename">Target filename, if required</param>
        /// <returns></returns>
        /// <remarks>If the target filename is blank, the source filename is used
        /// (assumes current directory). Otherwise use a filename to specify a name
        /// or a full path and filename if required.</remarks>
        public bool Upload(string localFilename, string targetFilename)
        {
            //1. check source
            if (!File.Exists(localFilename))
            {
                throw (new ApplicationException("File " + localFilename + " not found"));
            }
            //copy to FI
            FileInfo fi = new FileInfo(localFilename);
            return Upload(fi, targetFilename);
        }

        /// <summary>
        /// Upload a local file to the FTP server
        /// </summary>
        /// <param name="fi">Source file</param>
        /// <param name="targetFilename">Target filename (optional)</param>
        /// <returns>
        /// 1.2 [HR] simplified checks on target
        /// </returns>
        public bool Upload(FileInfo fi, string targetFilename)
        {
            //copy the file specified to target file: target file can be full path or just filename (uses current dir)

            //1. check target
            string target;
            if (String.IsNullOrEmpty(targetFilename))
            {
                //Blank target: use source filename & current dir
                target = this.CurrentDirectory + fi.Name;
            }
            else
            {
                //otherwise use original
                target = targetFilename;
            }
            using (FileStream fs = fi.OpenRead())
            {
                try
                {
                    return Upload(fs, target);
                }
                catch
                {
                    throw;
                }
                finally
                {
                    //ensure file closed
                    fs.Close();
                }
            }
            return false;
        }

        /// <summary>
        /// Upload a local source strean to the FTP server
        /// </summary>
        /// <param name="sourceStream">Source Stream</param>
        /// <param name="targetFilename">Target filename</param>
        /// <returns>
        /// 1.2 [HR] added CreateURI
        /// </returns>
        public bool Upload(Stream sourceStream, string targetFilename)
        {
            // validate the target file
            if (string.IsNullOrEmpty(targetFilename))
            {
                throw new ApplicationException("Target filename must be specified");
            };

            //perform copy
            string URI = CreateURI(targetFilename);
            System.Net.FtpWebRequest ftp = GetRequest(URI);

            //Set request to upload a file in binary
            ftp.Method = System.Net.WebRequestMethods.Ftp.UploadFile;
            ftp.UseBinary = true;

            //Notify FTP of the expected size
            ftp.ContentLength = sourceStream.Length;

            //create byte array to store: ensure at least 1 byte!
            const int BufferSize = 2048;
            byte[] content = new byte[BufferSize - 1 + 1];
            int dataRead;

            //open file for reading
            using (sourceStream)
            {
                try
                {
                    sourceStream.Position = 0;
                    //open request to send
                    using (Stream rs = ftp.GetRequestStream())
                    {
                        do
                        {
                            dataRead = sourceStream.Read(content, 0, BufferSize);
                            rs.Write(content, 0, dataRead);
                        } while (!(dataRead < BufferSize));
                        rs.Close();
                    }
                    return true;
                }
                catch (Exception)
                {
                    throw;
                }
                finally
                {
                    //ensure file closed
                    sourceStream.Close();
                    ftp = null;
                }
            }
            return false;
        }


        #endregion

        #region "private supporting fns"

        /// <summary>
        /// Ensure the data payload for URI is properly encoded
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        private string CreateURI(string filename)
        {
            string path;
            if (filename.Contains("/"))
            {
                path = AdjustDir(filename);
            }
            else
            {
                path = this.CurrentDirectory + filename;
            }
            // escape the path
            string escapedPath = GetEscapedPath(path);
            return this.Hostname + escapedPath;
        }

        //Get the basic FtpWebRequest object with the
        //common settings and security
        private FtpWebRequest GetRequest(string URI)
        {
            //create request
            FtpWebRequest result = (FtpWebRequest)FtpWebRequest.Create(URI);
            //Set the login details
            result.Credentials = GetCredentials();
            // support for EnableSSL
            result.EnableSsl = EnableSSL;
            //keep alive? (stateless mode)
            result.KeepAlive = KeepAlive;
            // support for passive connections 
            result.UsePassive = UsePassive;
            // support for proxy settings
            result.Proxy = Proxy;

            return result;
        }

        /// <summary>
        /// Ensure chars in path are correctly escaped e.g. #
        /// </summary>
        /// <param name="path">path to escape</param>
        /// <returns></returns>
        private string GetEscapedPath(string path)
        {
            string[] parts;
            parts = path.Split('/');
            string result;
            result = "";
            foreach (string part in parts)
            {
                if (!string.IsNullOrEmpty(part))
                    result += @"/" + Uri.EscapeDataString(part);
            }
            return result;
        }


        /// <summary>
        /// Get the credentials from username/password
        /// </summary>
        /// <remarks>
        /// Amended to store credentials on first use, for re-use
        /// when using KeepAlive=true
        /// </remarks>
        private System.Net.ICredentials GetCredentials()
        {
            if (_credentials == null)
                _credentials = new System.Net.NetworkCredential(Username, Password);
            return _credentials;
        }

        /// <summary>
        /// stored credentials
        /// </summary>
        private System.Net.NetworkCredential _credentials = null;

        /// <summary>
        /// returns a full path using CurrentDirectory for a relative file reference
        /// </summary>
        private string GetFullPath(string file)
        {
            if (file.Contains("/"))
            {
                return AdjustDir(file);
            }
            else
            {
                return this.CurrentDirectory + file;
            }
        }

        /// <summary>
        /// Amend an FTP path so that it always starts with /
        /// </summary>
        /// <param name="path">Path to adjust</param>
        /// <returns></returns>
        /// <remarks></remarks>
        private string AdjustDir(string path)
        {
            return ((path.StartsWith("/")) ? "" : "/").ToString() + path;
        }

        private string GetDirectory(string directory)
        {
            string URI;
            if (directory == "")
            {
                //build from current
                URI = Hostname + this.CurrentDirectory;
                _lastDirectory = this.CurrentDirectory;
            }
            else
            {
                if (!directory.StartsWith("/"))
                {
                    throw (new ApplicationException("Directory should start with /"));
                }
                URI = this.Hostname + directory;
                _lastDirectory = directory;
            }
            return URI;
        }

        //stores last retrieved/set directory
        private string _lastDirectory = "";

        /// <summary>
        /// Obtains a response stream as a string
        /// </summary>
        /// <param name="ftp">current FTP request</param>
        /// <returns>String containing response</returns>
        /// <remarks>
        /// FTP servers typically return strings with CR and
        /// not CRLF. Use respons.Replace(vbCR, vbCRLF) to convert
        /// to an MSDOS string
        /// 1.1: modified to ensure accepts UTF8 encoding
        /// </remarks>
        private string GetStringResponse(FtpWebRequest ftp)
        {
            //Get the result, streaming to a string
            string result = "";
            using (FtpWebResponse response = (FtpWebResponse)ftp.GetResponse())
            {
                long size = response.ContentLength;
                using (Stream datastream = response.GetResponseStream())
                {
                    using (StreamReader sr = new StreamReader(datastream, System.Text.Encoding.UTF8))
                    {
                        result = sr.ReadToEnd();
                        sr.Close();
                    }

                    datastream.Close();
                }

                response.Close();
            }

            return result;
        }

        /// <summary>
        /// Gets the size of an FTP request
        /// </summary>
        /// <param name="ftp"></param>
        /// <returns></returns>
        /// <remarks></remarks>
        private long GetSize(FtpWebRequest ftp)
        {
            long size;
            using (FtpWebResponse response = (FtpWebResponse)ftp.GetResponse())
            {
                size = response.ContentLength;
                response.Close();
            }

            return size;
        }

        /// <summary>
        /// Internal function to get the modified datetime stamp via FTP
        /// </summary>
        /// <param name="ftp">connection to use</param>
        /// <returns>
        /// DateTime of file, or throws exception
        /// </returns>
        private DateTime GetLastModified(FtpWebRequest ftp)
        {
            DateTime lastmodified;
            using (FtpWebResponse response = (FtpWebResponse)ftp.GetResponse())
            {
                lastmodified = response.LastModified;
                response.Close();
            }
            return lastmodified;

        }
        #endregion

        #region Properties

        /// <summary>
        /// Hostname
        /// </summary>
        /// <value></value>
        /// <remarks>Hostname can be in either the full URL format
        /// ftp://ftp.myhost.com or just ftp.myhost.com
        /// </remarks>
        public string Hostname
        {
            get
            {
                if (_hostname.StartsWith("ftp://"))
                {
                    return _hostname;
                }
                else
                {
                    return "ftp://" + _hostname;
                }
            }
            set { _hostname = value; }
        }
        private string _hostname;

        /// <summary>
        /// Username property
        /// </summary>
        /// <value></value>
        /// <remarks>Can be left blank, in which case 'anonymous' is returned</remarks>
        public string Username
        {
            get { return (_username == "" ? "anonymous" : _username); }
            set { _username = value; }
        }
        private string _username;

        /// <summary>
        /// Password for username
        /// </summary>
        public string Password
        {
            get { return _password; }
            set { _password = value; }
        }
        private string _password;

        /// <summary>
        /// The CurrentDirectory value
        /// </summary>
        /// <remarks>Defaults to the root '/'</remarks>
        public string CurrentDirectory
        {
            get
            {
                //return directory, ensure it ends with /
                return _currentDirectory + ((_currentDirectory.EndsWith("/")) ? "" : "/").ToString();
            }
            set
            {
                if (!value.StartsWith("/"))
                {
                    throw (new ApplicationException("Directory should start with /"));
                }
                _currentDirectory = value;
            }
        }
        private string _currentDirectory = "/";

        /// <summary>
        /// Support for EnableSSL flag on FtpWebRequest class
        /// </summary>
        public bool EnableSSL
        {
            get { return _enableSSL; }
            set { _enableSSL = value; }
        }
        private bool _enableSSL = false;

        /// <summary>
        /// KeepAlive property for permanent connections
        /// </summary>
        /// <remarks>
        /// KeepAlive is set False by default (no permanent connection)
        /// </remarks>
        public bool KeepAlive
        {
            get { return _keepAlive; }
            set { _keepAlive = value; }
        }
        private bool _keepAlive = false;

        /// <summary>
        /// Support for Passive mode
        /// </summary>
        public bool UsePassive
        {
            get { return _usePassive; }
            set { _usePassive = value; }
        }
        private bool _usePassive;

        /// <summary>
        /// Support for Proxy settings
        /// </summary>
        public IWebProxy Proxy
        {
            get { return _proxy; }
            set { _proxy = value; }
        }
        private IWebProxy _proxy = null;

        #endregion

    }
    #endregion


}

