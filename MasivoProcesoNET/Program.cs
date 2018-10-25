using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using System.IO;
using System.Diagnostics;
using System.Timers;
using System.Configuration;
using System.Threading;

namespace MasivoProcesoNET
{
    class Program
    {
        public static string _sftServer = string.Empty;
        public static string _userName = string.Empty;
        static string _password = string.Empty;
        static int _port = 0;
        static string _remoteFolder_IMSS = string.Empty;
        static int _numberOfEntities = 0;
        static string _localStorageFolder_BAZ = string.Empty;
        static string _backupFolder = string.Empty;
        static int _monitoring_Frequency = 0;
        static int _numberFilesdownloaded = 0;
        static string _ftpDirectory = string.Empty;

        private const int minimumFrecuency = 30000;

        private static System.Timers.Timer aTimer;

        static StringBuilder configurationEvent = new StringBuilder();
        static StringBuilder message = new StringBuilder();
        static List<ImssFile> filesDownloaded = new List<ImssFile>();

        public static void Main(string[] args)
        {
            try
            {
                ReadConfigurationFile();
                OnTimedEvent(null, null);
                SetTimer();
                Console.ReadLine();
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("Error:[{0}]", ex.Message);
                Logger.WriteEventViewer(sb.ToString(), EventLogEntryType.Error);
            }

        }

        private static void ReadConfigurationFile()
        {
            try
            {
                if (ConfigurationManager.AppSettings.Count == 0)
                {
                    throw new FileNotFoundException("Archivo de configuracion \"MasivoProcesoNET.exe.config\" no encontrado.");
                }

                _sftServer = (string)ReadConfiguration("sftpServer", false);
                _userName = (string)ReadConfiguration("userName", false);
                _password = (string)ReadConfiguration("password", false);
                _port = (Int32)ReadConfiguration("port", true);
                _remoteFolder_IMSS = (string)ReadConfiguration("remoteFolder_IMSS", false);
                _numberOfEntities = (Int32)ReadConfiguration("numberOfEntities", true);
                _localStorageFolder_BAZ = (string)ReadConfiguration("localStorageFolder_BAZ", false);
                _backupFolder = (string)ReadConfiguration("backupFolder", false);
                _monitoring_Frequency = (Int32)ReadConfiguration("monitoring_Frequency", true);
            }
            catch(Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("Error ReadConfigurationFile:[{0}]\n[{1}]", configurationEvent.ToString(), ex.Message);
                Logger.WriteEventViewer(sb.ToString(), EventLogEntryType.Error);
            }
        }

        private static Object ReadConfiguration(string key, bool isInteger)
        {
            string temp = ConfigurationManager.AppSettings[key];
            int intTemp;
            Object obj = null;
            if (isInteger)
            {
                if (!int.TryParse(temp, out intTemp))
                {
                    configurationEvent.AppendFormat("ERROR: El valor de la propiedad [{0}]en el archivo de configuración del servicio es incorreccto", key);
                }
                else
                    obj = intTemp;
            }
            else
            {
                if (string.IsNullOrEmpty(temp))
                {
                    configurationEvent.AppendFormat("ERROR: El valor de [{0}]en el archivo de configuración del servicio, es nulo o esta vacio\n", key);
                }
                else
                    obj = temp;

            }
            return obj;
        }

        private static void SetTimer()
        {
            if (_monitoring_Frequency < minimumFrecuency)
                _monitoring_Frequency = minimumFrecuency;
            aTimer = new System.Timers.Timer(_monitoring_Frequency);
            aTimer.Elapsed += OnTimedEvent;
            aTimer.AutoReset = true;
            aTimer.Enabled = true;
        }

        private static void OnTimedEvent(Object source, ElapsedEventArgs e)
        {
            Console.Write("\n\ninicia validación de archivos en los servidores SFTP del IMSS\n");
            message.AppendLine("inicia validación de archivos en los servidores SFTP del IMSS");
            Console.WriteLine("Servidor:[{0}]", _sftServer);
            message.AppendLine("Servidor:[" + _sftServer + "]");
            Console.WriteLine("Usuario:[{0}]", _userName);
            message.AppendLine("Usuario:[" + _userName + "]");
            Console.WriteLine("Puerto:[{0}]", _port);
            message.AppendLine("Puerto:[" + _port + "]");
            Console.WriteLine("Direccion remota:[{0}]", _remoteFolder_IMSS);
            message.AppendLine("Direccion remota:[" + _remoteFolder_IMSS + "]");
            Console.WriteLine("Direccion local:[{0}]", _localStorageFolder_BAZ);
            message.AppendLine("Direccion local:[" + _localStorageFolder_BAZ + "]");
            Console.WriteLine("Buscando archivos...");
            message.AppendLine("Buscando archivos...");
            List<Thread> workerThreads = new List<Thread>();
            List<Thread> workerThreadsToDelete = new List<Thread>();

            try
            {
                for (int j = 1; j <= _numberOfEntities; j++)
                {
                    string currentRemoteDirectory = _remoteFolder_IMSS.Replace("##", j.ToString("D2"));
                    Thread thread = new Thread(() => downloadFile(currentRemoteDirectory));
                    workerThreads.Add(thread);
                    thread.Start();
                }

                foreach (Thread thread in workerThreads)
                {
                    thread.Join();
                }

                if (_numberFilesdownloaded != 0)
                {
                    Console.WriteLine("---Eliminacion de archivos remotos---");
                    message.AppendLine("---Eliminacion de archivos remotos---");
                    foreach (ImssFile ifile in filesDownloaded)
                    {
                        Thread thread = new Thread(() => deleteFile(ifile.Fullname));
                        workerThreadsToDelete.Add(thread);
                        thread.Start();
                    }

                    foreach (Thread thread in workerThreadsToDelete)
                    {
                        thread.Join();
                    }
                    Console.WriteLine("---Respaldo de archivos descargados---");
                    message.AppendLine("---Respaldo de archivos descargados---");
                    copyFiles();

                    Console.WriteLine("---Renombrado de archivos descargados---");
                    message.AppendLine("---Renombrado de archivos descargados---");
                    foreach (ImssFile currentFile in filesDownloaded)
                    {
                        string tmp = Path.GetFileNameWithoutExtension(currentFile.Name);
                        File.Move(_localStorageFolder_BAZ + currentFile.Name, _localStorageFolder_BAZ + tmp + ".cif");
                    }
                    Console.Write("\tSe completo el renombrado de archivos.\n");
                    message.AppendLine("\tSe completo el renombrado de archivos.");

                }
                else
                {
                    Console.WriteLine("<<<< No hay archivos por descargar.>>>>");
                    message.AppendLine("<<<< No hay archivos por descargar.>>>>");
                }
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("Error OnTimedEvent:[{0}]", ex.Message);
                Logger.WriteEventViewer(sb.ToString(), EventLogEntryType.Error);
            }
            finally
            {
                Console.WriteLine("¡¡Termino validacion!!");
                message.AppendLine("¡¡Termino validacion!!");
                Logger.WriteEventViewer(message.ToString(), EventLogEntryType.Information);
                filesDownloaded.Clear();
                message.Clear();
                workerThreads = null;
                workerThreadsToDelete = null;
                _numberFilesdownloaded = 0;
            }
        }

        private static void downloadFile(string currentRemoteDirectory)
        {
            try
            {
                using (SftpClient sftpclient = new SftpClient(
                        _sftServer, _port, _userName, _password))
                {
                    sftpclient.Connect();
                    Console.WriteLine("Directorio actual [{0}]", currentRemoteDirectory);
                    var files = sftpclient.ListDirectory(currentRemoteDirectory);
                    foreach (var file in files)
                    {
                        Console.WriteLine("Archivo actual [{0}]", file.Name);
                        if (!file.Name.StartsWith("."))
                        {
                            string remoteFileName = file.Name;
                            if (!file.Name.StartsWith("."))

                                using (Stream file1 = File.OpenWrite(_localStorageFolder_BAZ + remoteFileName))
                                {
                                    sftpclient.DownloadFile(currentRemoteDirectory + remoteFileName, file1);
                                    filesDownloaded.Add(new ImssFile(remoteFileName, file.FullName, file.Length));
                                    _numberFilesdownloaded++;
                                    Console.WriteLine("Archivo descargado: [{0}]", remoteFileName);
                                    message.AppendFormat("Archivo descargado: [{0}]\n", remoteFileName);
                                }
                        }
                    }
                    sftpclient.Disconnect();
                }
            }
            catch (Exception e)
            {
                StringBuilder sb = new StringBuilder();
                Console.WriteLine("Error downloadFile:[{0}]", e.Message);
                sb.AppendFormat("Error downloadFile:[{0}]", e.Message);
                Logger.WriteEventViewer(sb.ToString(), EventLogEntryType.Error);
            }
        }

        private static void deleteFile(string pathRemoteFileToDelete)
        {
            try
            {
                using (SftpClient sftpclient = new SftpClient(
                        _sftServer, _port, _userName, _password))
                {
                    sftpclient.Connect();
                    if (sftpclient.Exists(pathRemoteFileToDelete))
                    {
                        sftpclient.DeleteFile(pathRemoteFileToDelete);
                        Console.WriteLine("\tArchivo eliminado: [{0}]", pathRemoteFileToDelete);
                        message.AppendFormat("\tArchivo eliminado: [{0}]\n", pathRemoteFileToDelete);
                    }
                    sftpclient.Disconnect();
                }
            }
            catch (Exception e)
            {
                StringBuilder sb = new StringBuilder();
                Console.WriteLine("Error deleteFile: [{0}]", pathRemoteFileToDelete);
                sb.AppendFormat("Error deleteFile:[{0}]", e.Message);
                Logger.WriteEventViewer(sb.ToString(), EventLogEntryType.Error);
            }
        }

        private static void copyFiles()
        {
            string fileName = string.Empty;
            string destFile = string.Empty;
            try
            {
                string[] files = System.IO.Directory.GetFiles(_localStorageFolder_BAZ);

                foreach (string s in files)
                {
                    fileName = System.IO.Path.GetFileName(s);
                    destFile = System.IO.Path.Combine(_backupFolder, fileName);
                    File.Copy(s, destFile, true);
                    Console.WriteLine("\tArchivo respaldado: [{0}]", destFile);
                    message.AppendFormat("\tArchivo respaldado: [{0}]\n", destFile);
                }
            }
            catch (DirectoryNotFoundException ex)
            {
                Console.WriteLine("\t\nOcurrio un error al respaldar Archivo.\t\nError copyFiles: Directorio no existe[{0}]", ex.Message);
                Logger.WriteEventViewer("\t\nOcurrio un error al respaldar Archivo.\t\nError copyFiles: Directorio no existe [" + ex.Message + "]", EventLogEntryType.Error);
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                Console.WriteLine("\t\nError copyFiles:[{0}]", ex.Message);
                sb.AppendFormat("Error copyFiles:[{0}]", ex.Message);
                Logger.WriteEventViewer(sb.ToString(), EventLogEntryType.Error);
            }
        }
    }
}
