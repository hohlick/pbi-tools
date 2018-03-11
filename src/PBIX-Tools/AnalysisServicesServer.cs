﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using TOM = Microsoft.AnalysisServices.Tabular;
using Microsoft.PowerBI.Packaging;
using Polly;
using Serilog;

namespace PbixTools
{
    /// <summary>
    /// TODO - Remove these references (replace with Serilog calls)
    /// </summary>
    public static class DumpHelper
    {
        public static T Dump<T>(this T o, string message = null)
        {
            switch (o)
            {
                case string s when message != null:
                    Console.WriteLine($"[{message}]: {s}");
                    break;
                case string s:
                    Console.WriteLine(s);
                    break;
                case int i when message != null:
                    Console.WriteLine($"[{message}]: {i}");
                    break;
                case int i:
                    Console.WriteLine(i);
                    break;
            }

            return o;
        }
    }

    public class AnalysisServicesServer : IDisposable
    {
        private readonly IDependenciesResolver _dependenciesResolver;
        private static readonly ILogger Log = Serilog.Log.ForContext<AnalysisServicesServer>();


        private readonly string _tempPath;
        private string _asToolPath;

        public AnalysisServicesServer(ASInstanceConfig config, IDependenciesResolver dependenciesResolver)
        {
            _dependenciesResolver = dependenciesResolver ?? throw new ArgumentNullException(nameof(dependenciesResolver));
            _tempPath = Path.GetTempFileName(); // TODO Use TempFolder
            File.Delete(_tempPath);
            Directory.CreateDirectory(_tempPath);

            if (dependenciesResolver.TryFindMsmdsrv(out var path))
                _asToolPath = path;
            else
                throw new Exception("'msmdsrv.exe' not found");

            Log.Information("MSMDSRV.EXE found at {ASTOOLPATH}", _asToolPath);
            Log.Information("Working directory: {WorkingDirectory}", _tempPath);

            CreateConfig(config);
        }

        private void CreateConfig(ASInstanceConfig config)
        {
            // TODO Reconsider generation of default ini

            //// 1 - Generate default .ini file
            //var procStartInfo = new ProcessStartInfo(_asToolPath, "-f -s .")
            //{
            //    CreateNoWindow = true,
            //    WorkingDirectory = _tempPath,
            //    WindowStyle = ProcessWindowStyle.Hidden,
            //    UseShellExecute = false,
            //};
            //using (var proc = Process.Start(procStartInfo))
            //{
            //    proc?.WaitForExit();
            //}

            // 2 - Modify .ini file
            //config.BackupDir = config.BackupDir ?? _tempPath;
            //config.DataDir = config.DataDir ?? _tempPath;
            //config.LogDir = config.LogDir ?? _tempPath;
            //config.TempDir = config.TempDir ?? _tempPath;

            config.SetWorkingDir(_tempPath);

            var iniFilePath = Path.Combine(_tempPath, "msmdsrv.ini");
            var iniTemplate = GetEmbeddedResource("msmdsrv.ini.xml", XDocument.Load);

            ASIniFile.WriteConfig(config, iniTemplate, iniFilePath);
        }

        private static T GetEmbeddedResource<T>(string name, Func<Stream, T> transform)
        {
            var asm = Assembly.GetExecutingAssembly();
            var resourceNames = asm.GetManifestResourceNames();
            var match = resourceNames.FirstOrDefault(n => n.EndsWith(name));
            if (match == null) throw new ArgumentException($"Embedded resource '{name}' not found.", nameof(name));

            using (var stream = asm.GetManifestResourceStream(match))
            {
                return transform(stream);
            }
        }

        public int Port { get; private set; }
        public string ConnectionString => $"localhost:{Port}";
        public bool HideWindow { get; set; }

        private Process _proc;

        public void Start()
        {
            if (IsRunning) throw new InvalidOperationException("Server is already running.");

            // Start new instance
            // '-n' arg is REQUIRED for dynamic port assignment! -- will only use default port 2383 otherwise (and fail if that port is in use)
            var procStartInfo = new ProcessStartInfo(_asToolPath, $"-c -n {Guid.NewGuid()} -s \"{_tempPath}\"")
            {
                WorkingDirectory = _tempPath,
            };

            if (HideWindow)
            {
                procStartInfo.CreateNoWindow = true;
                procStartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                procStartInfo.RedirectStandardOutput = true;
                procStartInfo.RedirectStandardError = true;
                procStartInfo.UseShellExecute = false; // required for stdout/stderr redirection
            }

            _proc = new Process();

            if (HideWindow)
            {
                // TODO Could declare receivers on instance, otherwise fwd output to logger
                _proc.OutputDataReceived += (sender, e) => Log.Verbose("MSMDSRV INF: {Data}", e.Data);
                _proc.ErrorDataReceived += (sender, e) => Log.Verbose("MSMDSRV ERR: {Data}", e.Data);
            }

            _proc.StartInfo = procStartInfo;
            Policy
                .Handle<Win32Exception>(ex => ex.NativeErrorCode == 5 /* ERROR_ACCESS_DENIED */)
                .Retry((ex, i) =>
                {
                    Log.Information("Shadow-copying msmdsrv...");
                    _asToolPath = _dependenciesResolver.ShadowCopyMsmdsrv(_asToolPath);
                    _proc.StartInfo.FileName = _asToolPath;
                    Log.Information("Using msmdsrv from shadow-copied location: {Path}", _asToolPath);
                })
                .Execute(_proc.Start);

            Log.Verbose("Started MSMDSRV, PID: {PID}", _proc.Id);

            if (HideWindow)
            {
                _proc.BeginErrorReadLine();
                _proc.BeginOutputReadLine();
            }

            // Wait for cration of Port file
            var portFilePath = Path.Combine(_tempPath, "msmdsrv.port.txt");
            var attempts = 10;
            while (!File.Exists(portFilePath) && attempts-- > 0)
            {
                Thread.Sleep(1000);
            }

            if (File.Exists(portFilePath))
            {
                Port = Int32.Parse(File.ReadAllText(portFilePath, Encoding.Unicode /* IMPORTANT */));
                Log.Information("Tabular Server process launched successfully. Port: {Port}", Port);
            }
            else if (_proc.HasExited) // happens when process could not start, for instance because of misconfiguration or missing capabilities
                throw new Exception("Process has terminated");
            else
                Console.Error.WriteLine("Port Detection Timeout"); // Throw instead?
        }

        public void LoadPbixModel(string path, string id, string name)
        {
            if (!IsRunning) throw new Exception("Server not running");

            using (var pbix = File.OpenRead(path))
            using (var package = PowerBIPackager.Open(pbix))
            using (var server = new TOM.Server())
            {
                if (package.DataModel == null)
                    throw new Exception("PBIX file does not contain a model.");

                server.Connect(ConnectionString); // must be PowerPivot/SharePoint (mode 1) instance

                server.ImageLoad(name, id, package.DataModel.GetStream());
                server.Refresh();

                Log.Information("Model image from PBIX loaded successfully.");
            }
        }

        public bool IsRunning => Port != 0 && _proc != null && !_proc.HasExited;

        public void Dispose()
        {
            if (_proc == null) return;

            using (_proc)
            {
                try
                {
                    if (!_proc.HasExited)
                        _proc.Kill();
                }
                catch (Exception e)
                {
                    Log.Error(e, "Could not terminate msmdsrv.exe.");
                }
            }

            Directory.EnumerateFiles(_tempPath, "*", SearchOption.AllDirectories).Dump("Deleting...");
            var retry = 0;
            var success = false;
            while (!success && retry++ < 5)
            {
                try
                {
                    Directory.Delete(_tempPath, recursive: true);
                    success = true;
                    _tempPath.Dump("Deleted");
                }
                catch (IOException) // Could use Polly instead
                {
                    Thread.Sleep(500);
                }
            }

            _proc = null;
        }
    }


    public enum DeploymentMode
    {
        MultiDimensional = 0,
        SharePoint = 1, // This is needed for Power BI .pbix files
        Tabular = 2
    }

    public static class ASIniFile
    {
        /// Substitutes PrivateProcess with CurrentProcess.Id if not provided.
        public static void WriteConfig(ASInstanceConfig config, XDocument template, string path)
        {
            config.PrivateProcess = config.PrivateProcess == 0 ? Process.GetCurrentProcess().Id : config.PrivateProcess;

            var xml = new XDocument(template);
            foreach (var p in typeof(ASInstanceConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var map = p.GetCustomAttribute<MapAttribute>();
                var xpath = map?.Path ?? p.Name;

                var element = xml.EnsureElement($"/ConfigurationSettings/{xpath}");

                if (p.PropertyType.IsEnum)
                    element.Value = Convert.ToInt32(p.GetValue(config)).ToString();
                else if (p.PropertyType == typeof(bool))
                    element.Value = ((bool)p.GetValue(config)) ? "1" : "0";
                else
                    element.Value = p.GetValue(config)?.ToString() ?? "";
            }
            using (var writer = XmlWriter.Create(path, new XmlWriterSettings { Indent = true, OmitXmlDeclaration = true }))
            {
                xml.WriteTo(writer);
            }

            // log verbose:
            //File.ReadAllText(path).Dump("msmdsrv.ini");
        }

        public static XElement EnsureElement(this XDocument doc, string xpath)
        {
            var element = doc.XPathSelectElement(xpath);
            if (element == null)
            {
                foreach (var e in xpath.Split('/').Skip(2))
                {
                    element = (element ?? doc.Root).EnsureElement(e);
                }
            }
            return element;
        }

        public static XElement EnsureElement(this XElement parent, string name)
        {
            var element = parent.Element(name);
            if (element == null)
            {
                element = new XElement(name);
                parent.AddFirst(element);
            }
            return element;
        }
    }

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public class ASInstanceConfig  // TODO Add property docs
    {
        public DeploymentMode DeploymentMode { get; set; } = DeploymentMode.Tabular;
        public string DataDir { get; set; }
        public string TempDir { get; set; }
        public string LogDir { get; set; }
        public string BackupDir { get; set; }
        [Map("Log/Exception/CrashReportsFolder")] public string CrashReportsFolder { get; set; }
        public int PrivateProcess { get; set; }
        public int Port { get; set; } = 0;
        public int Language { get; set; } = CultureInfo.CurrentCulture.LCID;
        public string AllowedBrowsingFolders { get; set; }
        [Map("Log/FlightRecorder/Enabled")] public bool FlightRecorderEnabled { get; set; } = true;
        public bool InstanceVisible { get; set; }
        public bool DisklessModeRequested { get; set; }
        [Map("VertiPaq/EnableDisklessTMImageSave")] public bool EnableDisklessTMImageSave { get; set; }
        public int RecoveryModel { get; set; } = 1;
        [Map("DAX/DQ/EnableAllFunctions")] public bool EnableAllDaxFunctions { get; set; } = true;
        [Map("DAX/DQ/EnableVariationNotation")] public bool EnableVariationNotation { get; set; } = true;
        [Map("Network/ListenOnlyOnLocalConnections")] public bool ListenOnlyOnLocalConnections { get; set; } = true;
        public int TMCompatibilitySKU { get; set; } = 0; // Required for MEngine Integration, only compatible with DeploymentMode.Tabular and with SSDT distro
    }

    // ReSharper disable once InconsistentNaming
    public static class ASInstanceConfigExtensions
    {
        public static ASInstanceConfig SetWorkingDir(this ASInstanceConfig config, string folder, bool force = false)
        {
            config.DataDir = force ? folder : (config.DataDir ?? folder);
            config.BackupDir = force ? folder : (config.BackupDir ?? folder);
            config.LogDir = force ? folder : (config.LogDir ?? folder);
            config.TempDir = force ? folder : (config.TempDir ?? folder);
            config.CrashReportsFolder = force ? folder : (config.CrashReportsFolder ?? folder);
            config.AllowedBrowsingFolders = force ? folder : (config.AllowedBrowsingFolders ?? folder);
            return config;
        }

        public static ASInstanceConfig SetCulture(this ASInstanceConfig config, CultureInfo culture)
        {
            config.Language = culture.LCID;
            return config;
        }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class MapAttribute : Attribute
    {
        public MapAttribute(string path)
        {
            this.Path = path;
        }
        public string Path { get; }
    }
}