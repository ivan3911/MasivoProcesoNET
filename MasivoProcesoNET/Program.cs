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
using System.Text.RegularExpressions;
using System.Net.Sockets;

namespace MasivoProcesoNET
{
    static class Program
    {
        public static string _sftServer = string.Empty;
        public static string _userName = string.Empty;
        static string _password = string.Empty;
        static int _port = 0;
        static string _remoteFolder_IMSS = string.Empty;
        static int _numberOfEntities = 0;
        static string _localStorageFolder_BAZ = string.Empty;
        static int _numberFilesdownloaded = 0;

        static string _regularExpresion = string.Empty;

        static EventLogEntryType BeforeDownloadEntryType = EventLogEntryType.Information;
        static EventLogEntryType EventLogEntryTypeForMessage = EventLogEntryType.Information;

        static bool errorInThread = false;

        private const int minimumFrecuency = 30000;
        private const string acuse = "ACUSE_";
        private const string extensionAcuse = ".PK7";

        private static System.Timers.Timer aTimer;

        static StringBuilder configurationEvent = new StringBuilder();
        static StringBuilder message = new StringBuilder();
        static StringBuilder logBeforeDownload = new StringBuilder();
        static StringBuilder logBeforeDownload_totalFilesFound = new StringBuilder();
        static List<ImssFile> filesDownloaded = new List<ImssFile>();

        public static int Main(string[] args)
        {
            int result;
            try
            {
                ReadConfigurationFile();
                result = OnTimedEvent();
            }
            catch (Exception exp)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("Error:[{0}]", exp.Message);
                Logger.WriteEventViewer(sb.ToString(), EventLogEntryType.Error);
                result = -1;
            }
            Console.WriteLine($"RESULT[{result}]");
            return result;
        }

        private static void ReadConfigurationFile()
        {
            try
            {
                if (ConfigurationManager.AppSettings.Count == 0)
                {
                    throw new FileNotFoundException("Archivo de configuración \"MasivoProcesoNET.exe.config\" no encontrado.");
                }

                _sftServer = (string)ReadConfiguration("sftpServer", false);
                _userName = (string)ReadConfiguration("userName", false);
                _password = (string)ReadConfiguration("password", false);
                _port = (Int32)ReadConfiguration("port", true);
                _remoteFolder_IMSS = (string)ReadConfiguration("remoteFolder_IMSS", false);
                _numberOfEntities = (Int32)ReadConfiguration("numberOfEntities", true);
                _localStorageFolder_BAZ = (string)ReadConfiguration("localStorageFolder_BAZ", false);
                _regularExpresion = (string)ReadConfiguration("RegularExpresion", false);
            }
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendFormat("Error ReadConfigurationFile:[{0}]\n[{1}]", configurationEvent.ToString(), ex.Message);
                Console.WriteLine(sb.ToString());
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
                    configurationEvent.AppendFormat("ERROR: El valor de [{0}]en el archivo de configuración del servicio, es nulo o esta vacío\n", key);
                }
                else
                    obj = temp;

            }
            return obj;
        }

        private static int OnTimedEvent()
        {
            int successful = 0;
            Console.Write("\n\ninicia validación de archivos en los servidores SFTP del IMSS\n");
            message.AppendLine("inicia validación de archivos en los servidores SFTP del IMSS");
            Console.WriteLine("Servidor:[{0}]", _sftServer);
            message.AppendLine("Servidor:[" + _sftServer + "]");
            Console.WriteLine("Usuario:[{0}]", _userName);
            message.AppendLine("Usuario:[" + _userName + "]");
            Console.WriteLine("Puerto:[{0}]", _port);
            message.AppendLine("Puerto:[" + _port + "]");
            Console.WriteLine("Dirección remota:[{0}]", _remoteFolder_IMSS);
            message.AppendLine("Dirección remota:[" + _remoteFolder_IMSS + "]");
            Console.WriteLine("Numero de entidades:[{0}]", _numberOfEntities);
            message.AppendLine("Numero de entidades:[" + _numberOfEntities + "]");
            Console.WriteLine("Direccion local:[{0}]", _localStorageFolder_BAZ);
            message.AppendLine("Direccion local:[" + _localStorageFolder_BAZ + "]");
            Console.WriteLine("Expresión regular:[{0}]", _regularExpresion);
            message.AppendLine("Expresión regular:[" + _regularExpresion + "]");
            Console.WriteLine("Buscando archivos...");
            message.AppendLine("Buscando archivos...");
            List<Thread> workerThreads = new List<Thread>();

            try
            {
                var msj = "#####################################################\nListado de archivos existentes previo a la descarga.";
                Console.WriteLine(msj);
                logBeforeDownload_totalFilesFound.AppendLine(msj);

                for(int j = 1; j <= _numberOfEntities; j++)
                {
                    string currentRemoteDirectory = _remoteFolder_IMSS.Replace("##", j.ToString("D2"));
                    snapshotBeforeDownload(currentRemoteDirectory);
                }

                var detailsFilesFound = $"\nDetalle de archivos:{logBeforeDownload.ToString()}";
                Console.WriteLine(detailsFilesFound);
                logBeforeDownload_totalFilesFound.Append(detailsFilesFound);

                Console.WriteLine("#####################################################");
                Console.WriteLine(">>>>>INICIA EL PROCESO DE DESCARGA<<<<<");

                for (int j = 1; j <= _numberOfEntities; j++)
                {
                    string currentRemoteDirectory = _remoteFolder_IMSS.Replace("##", j.ToString("D2"));
                    Thread thread = new Thread(() => download(currentRemoteDirectory, j));
                    workerThreads.Add(thread);
                    thread.Start();
                }

                foreach (Thread thread in workerThreads)
                {
                    thread.Join();
                }

                //Utilizado para validación si ocurrio en error en alguno de los hilos.
                if (errorInThread)
                {
                    successful = -1;
                }

                if (_numberFilesdownloaded == 0)
                {
                    Console.WriteLine("<<<< No hay archivos por descargar.>>>>");
                    message.AppendLine("<<<< No hay archivos por descargar.>>>>");
                }
            }
            catch (SocketException se)
            {
                logBeforeDownload_totalFilesFound.AppendLine("\t¡Ocurrio un error al obtener el listado de los archivos!");
                message.AppendLine($"\t¡Ocurrio un error al obtener el listado de los archivos!\n[{se.Message}]");
                Console.WriteLine($"\t¡Ocurrio un error al obtener el listado de los archivos!\n[{se.Message}]");
                BeforeDownloadEntryType = EventLogEntryType.Error;
                EventLogEntryTypeForMessage = EventLogEntryType.Error;
                successful = -1;
            }
            catch (Exception ex)
            {
                message.AppendLine($"Error OnTimedEvent:[{ex.Message}]");
                Console.WriteLine($"Error OnTimedEvent:[{ex.Message}]");
                EventLogEntryTypeForMessage = EventLogEntryType.Error;
                successful = -1;
            }
            finally
            {
                Console.WriteLine("¡¡Terminó validación!!");
                message.AppendLine("¡¡Terminó validación!!");
                message.AppendLine($"RESULT[{successful}]");
                Logger.WriteEventViewer(logBeforeDownload_totalFilesFound.ToString(), BeforeDownloadEntryType);
                Logger.WriteEventViewer(message.ToString(), EventLogEntryTypeForMessage);
                filesDownloaded.Clear();
                message.Clear();
                logBeforeDownload.Clear();
                logBeforeDownload_totalFilesFound.Clear();
                workerThreads = null;
                _numberFilesdownloaded = 0;
            }
            return successful;
        }

        private static void snapshotBeforeDownload(string currentRemoteDirectory)
        {
            int FoundFilesCounter = 0;
            try
            {
                using (SftpClient sftpclient = new SftpClient(
                        _sftServer, _port, _userName, _password))
                {
                    sftpclient.Connect();
                    if (sftpclient.Exists(currentRemoteDirectory))
                    {
                        var files = sftpclient.ListDirectory(currentRemoteDirectory);

                        foreach (var file in files)
                        {
                            if (!file.Name.StartsWith("."))
                            {
                                var message = $"\n\t#Directorio:[{currentRemoteDirectory}]->[{Path.GetFileName(file.FullName)}]";
                                logBeforeDownload.AppendFormat(message);
                                FoundFilesCounter++;
                            }
                        }
                        var MessageNumberFilesFound = $"Archivos encontrados en el directorio [{currentRemoteDirectory}]: [{FoundFilesCounter}]";
                        Console.WriteLine(MessageNumberFilesFound);
                        logBeforeDownload_totalFilesFound.AppendLine(MessageNumberFilesFound);
                        sftpclient.Disconnect();
                    }
                    else
                    {
                        Console.Write("\tsnapshotBeforeDownload->El directorio remoto no existe: [{0}].\n", currentRemoteDirectory);
                        message.AppendLine("\tsnapshotBeforeDownload->El directorio remoto no existe:: [" + currentRemoteDirectory + "].");
                    }
                }
            }
            catch (SocketException se)
            {
                if(se.ErrorCode==10060)
                     throw;
            }
            catch (Exception exp)
            {
                Console.WriteLine($"HResult:[{exp.HResult.ToString()}]");
                Console.WriteLine($"GetHashCode:[{exp.GetHashCode().ToString()}]");
                StringBuilder sb = new StringBuilder();
                Console.WriteLine("Error snapshotBeforeDownload:[{0}]", exp.Message);
                sb.AppendFormat("Error snapshotBeforeDownload:[{0}]", exp.Message);
                Logger.WriteEventViewer(sb.ToString(), EventLogEntryType.Error);
            }
        }

        private static void download(string currentRemoteDirectory, int delegation)
        {
            //Regex regex = new Regex(@"MXIMSS[A-Z]{2}\d{2}[A-Z]{3}-\d{7}[A-Z]{1,2}.PK7");
            
            UnicodeEncoding uniEncoding = new UnicodeEncoding();
            Regex regex = new Regex(_regularExpresion);
            try
            {
                using (SftpClient sftpclient = new SftpClient(
                        _sftServer, _port, _userName, _password))
                {
                    sftpclient.Connect();
                    if (sftpclient.Exists(currentRemoteDirectory))
                    {
                        var files = sftpclient.ListDirectory(currentRemoteDirectory);

                        foreach (var file in files)
                        {
                            Match match = regex.Match(file.Name);
                            if (match.Success)
                            {
                                if (file.Length != 0)
                                    downloadFile(sftpclient, file, currentRemoteDirectory);
                                else
                                {
                                    byte[] errorMessage = uniEncoding.GetBytes(
                                        string.Format("Se recibió Archivo [{0}] vacío.\n", file.Name)
                                        );

                                    string nameFileTemporal = string.Format("{0}{1}{2}", acuse, Path.GetFileNameWithoutExtension(file.Name), extensionAcuse);
                                    char[] nameFileInArray = nameFileTemporal.ToCharArray();
                                    nameFileInArray[18] = 'V';
                                    string nameFileWithExtension = new string(nameFileInArray);
                                    var acuseRemoteDirectory = currentRemoteDirectory.Replace("pago", "acuse");

                                    createFileResponse(sftpclient, nameFileWithExtension, acuseRemoteDirectory, errorMessage, ref message);

                                    Console.WriteLine("\tDebido al tamaño del archivo 0 KB, Se generó archivo de respuesta automático.\n\t>>>>[" + acuseRemoteDirectory + nameFileWithExtension + "]");
                                    message.AppendLine("\tDebido al tamaño del archivo 0 KB, Se generó archivo de respuesta automático.\n\t>>>>[" + acuseRemoteDirectory + nameFileWithExtension + "]");
                                }
                            }
                            else
                            {
                                if (!file.Name.StartsWith("."))
                                {
                                    byte[] errorMessage = uniEncoding.GetBytes(
                                        string.Format("El archivo [{0}] no cumple con estructura definida.\n", file.Name)
                                        );

                                    var nameFileWithExtension = string.Format("{0}{1}{2}", acuse, Path.GetFileNameWithoutExtension(file.Name), extensionAcuse);
                                    var acuseRemoteDirectory = currentRemoteDirectory.Replace("pago", "acuse");

                                    createFileResponse(sftpclient, nameFileWithExtension, acuseRemoteDirectory, errorMessage, ref message);

                                    Console.WriteLine("\tSe generó archivo de respuesta automático por no cumplir con la estructura definida.\n\t>>>>[" + acuseRemoteDirectory + nameFileWithExtension + "]");
                                    message.AppendLine("\tSe generó archivo de respuesta automático por no cumplir con la estructura definida.\n\t>>>>[" + acuseRemoteDirectory + nameFileWithExtension + "]");
                                }
                            }
                        }
                        sftpclient.Disconnect();
                    }
                    else
                    {
                        Console.Write("\tEl directorio remoto no existe: [{0}].\n", currentRemoteDirectory);
                        message.AppendLine("\tEl directorio remoto no existe:: [" + currentRemoteDirectory + "].");
                    }
                }
            }
            catch (Exception exp)
            {
                StringBuilder sb = new StringBuilder();
                Console.WriteLine("Error download:[{0}]", exp.Message);
                Console.WriteLine("HResult[{0}]", exp.HResult);
                Console.WriteLine("StackTrace[{0}]", exp.StackTrace);
                sb.AppendFormat("Error download:[{0}]", exp.Message);
                sb.AppendFormat("\nHResult:[{0}]", exp.HResult);
                sb.AppendFormat("\nStackTrace:[{0}]", exp.StackTrace);
                Logger.WriteEventViewer(sb.ToString(), EventLogEntryType.Error);
                errorInThread = true;
            }

        }

        private static void downloadFile(SftpClient sftpclient, SftpFile file, string currentRemoteDirectory)
        {
            try
            {
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
            catch (Exception ex)
            {
                StringBuilder sb = new StringBuilder();
                Console.WriteLine("Error downloadFile:[{0}]", ex.Message);
                sb.AppendFormat("Error downloadFile:[{0}]", ex.Message);
                Logger.WriteEventViewer(sb.ToString(), EventLogEntryType.Error);
                errorInThread = true;
            }
        }

        static bool createFileResponse(SftpClient sftpclient, string nameFileWithExtension, string acuseRemoteDirectory, byte[] message, ref StringBuilder mainMessageInformation)
        {
            bool successful = true;
            try
            {
                sftpclient.ChangeDirectory(acuseRemoteDirectory);

                using (var stream = new MemoryStream())
                {
                    var writer = new StreamWriter(stream);

                    stream.Write(message, 0, message.Length);
                    stream.Position = 0;

                    sftpclient.UploadFile(stream, nameFileWithExtension);
                }
                
            }
            catch (Exception ex)
            {
                Console.WriteLine("createFileResponse Error[{0}]", ex.Message);
                mainMessageInformation.AppendLine("createFileResponse Error[" + ex.Message + "]");
                successful = false;
            }
            return successful;
        }
    }
}
