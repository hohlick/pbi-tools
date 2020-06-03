﻿using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using PbiTools.FileSystem;
using PbiTools.PowerBI;
using PbiTools.ProjectSystem;
using PbiTools.Serialization;
using PbiTools.Utils;
using Serilog;

namespace PbiTools.Model
{
    /// <summary>
    /// Represents the contents of a PBIX file, held in various storage formats.
    /// </summary>
    public interface IPbixModel
    {
        JObject Connections { get; }
        JObject DataModel { get; }
        JObject Report { get; }
        JObject DiagramViewState { get; }
        JObject DiagramLayout { get; }
        JObject LinguisticSchema { get; }
        JObject ReportMetadata { get; }
        JObject ReportSettings { get; }
        string Version { get; }

        IDictionary<string, byte[]> CustomVisuals { get; }
        IDictionary<string, byte[]> StaticResources { get; }

        PbixProject PbixProj { get; }
        PbixModelSource Type { get; }
    }

    /* Workflow: Extract PBIX
     * 1 Create PbixModel from .pbix
     * 2 Serialize PbixModel to folder
     * 2.1 Read existing PbixProject from folder
     * 2.2 Replace query ids accordingly
     */

    public enum PbixModelSource
    {
        PowerBIPackage,
        PbixProjFolder,
        LiveSession
    }

    public class PbixModel : IPbixModel, IDisposable
    {
        private static readonly ILogger Log = Serilog.Log.ForContext<PbixModel>();

        public PbixModelSource Type { get; }
        public string SourcePath { get; }

        private PbixModel(string path, PbixModelSource type)
        {
            this.SourcePath = path;
            this.Type = type;
        }

        public static PbixModel FromFile(string path, IDependenciesResolver dependenciesResolver = null)
        {
            using (var reader = new PbixReader(path ?? throw new ArgumentNullException(nameof(path)), dependenciesResolver ?? DependenciesResolver.Default))
            {
                return FromReader(reader);
            }
        }

        public static PbixModel FromReader(PbixReader reader)
        {
            if (reader is null) throw new ArgumentNullException(nameof(reader));

            var pbixModel = new PbixModel(reader.Path, PbixModelSource.PowerBIPackage)
            { 
                Version = reader.ReadVersion()
            };

            if (!PbixReader.IsV3Version(pbixModel.Version))
            {
                throw new NotSupportedException("The PBIX file does not contain a V3 model. This API only supports V3 PBIX files.");
            }

            pbixModel.Connections = reader.ReadConnections();
            pbixModel.Report = reader.ReadReport();
            pbixModel.DiagramLayout = reader.ReadDiagramLayout();
            pbixModel.DiagramViewState = reader.ReadDiagramViewState();
            pbixModel.LinguisticSchema = reader.ReadLinguisticSchemaV3();
            pbixModel.ReportMetadata = reader.ReadReportMetadataV3();
            pbixModel.ReportSettings = reader.ReadReportSettingsV3();
            pbixModel.CustomVisuals = reader.ReadCustomVisuals();
            pbixModel.StaticResources = reader.ReadStaticResources();

            pbixModel.DataModel = reader.ReadDataModel();  // will fire up SSAS instance if PBIX has embedded model

            using (var projectFolder = new ProjectRootFolder(PbixProject.GetProjectFolderForFile(pbixModel.SourcePath)))
            {
                pbixModel.PbixProj = PbixProject.FromFolder(projectFolder);
            }

            return pbixModel;
        }

        public static PbixModel FromFolder(string path)
        {
            // PBIXPROJ(folder) <==> Serializer <=|PbixModel|=> PbixReader|Writer[Converter] <==> PBIX(file)
            //                       ##########                 ############################

            using (var projectFolder = new ProjectRootFolder(path))
            {
                var serializers = new PowerBIPartSerializers(projectFolder);
                
                var pbixModel = new PbixModel(path, PbixModelSource.PbixProjFolder);

                pbixModel.Version = serializers.Version.DeserializeSafe();
                pbixModel.Connections = serializers.Connections.DeserializeSafe();
                pbixModel.Report = serializers.ReportDocument.DeserializeSafe();
                pbixModel.DiagramLayout = serializers.DiagramLayout.DeserializeSafe();
                pbixModel.DiagramViewState = serializers.DiagramViewState.DeserializeSafe();
                pbixModel.LinguisticSchema = serializers.LinguisticSchema.DeserializeSafe();
                pbixModel.ReportMetadata = serializers.ReportMetadata.DeserializeSafe();
                pbixModel.ReportSettings = serializers.ReportSettings.DeserializeSafe();
                pbixModel.CustomVisuals = serializers.CustomVisuals.DeserializeSafe();
                pbixModel.StaticResources = serializers.StaticResources.DeserializeSafe();                
                pbixModel.DataModel = serializers.DataModel.DeserializeSafe();

                pbixModel.PbixProj = PbixProject.FromFolder(projectFolder);

                return pbixModel;
            }
        }

        /// <summary>
        /// Serializes the entire model to a file system folder.
        /// </summary>
        /// <param name="path"></param>
        public void ToFolder(string path = null)
        {
            var rootFolderPath = path ?? this.GetProjectFolder();
            using (var projectFolder = new ProjectRootFolder(rootFolderPath))
            {
                var serializers = new PowerBIPartSerializers(projectFolder);
                
                // **** Parts ****
                if (serializers.Version.Serialize(this.Version))
                    Log.Information("Version [{Version}] extracted to: {Path}", this.Version, serializers.Version.BasePath);

                if (serializers.Connections.Serialize(this.Connections))
                    Log.Information("Connections extracted to: {Path}", serializers.Connections.BasePath);

                if (serializers.ReportDocument.Serialize(this.Report))
                    Log.Information("Report extracted to: {Path}", serializers.ReportDocument.BasePath);

                if (serializers.ReportMetadata.Serialize(this.ReportMetadata))
                    Log.Information("Metadata extracted to: {Path}", serializers.ReportMetadata.BasePath);

                if (serializers.ReportSettings.Serialize(this.ReportSettings))
                    Log.Information("Settings extracted to: {Path}", serializers.ReportSettings.BasePath);

                if (serializers.DiagramLayout.Serialize(this.DiagramLayout))
                    Log.Information("DiagramLayout extracted to: {Path}", serializers.DiagramLayout.BasePath);

                if (serializers.DiagramViewState.Serialize(this.DiagramViewState))
                    Log.Information("DiagramViewState extracted to: {Path}", serializers.DiagramViewState.BasePath);

                if (serializers.LinguisticSchema.Serialize(this.LinguisticSchema))
                    Log.Information("LinguisticSchema extracted to: {Path}", serializers.LinguisticSchema.BasePath);

                if (serializers.DataModel.Serialize(this.DataModel))
                    Log.Information("DataModel extracted to: {Path}", serializers.DataModel.BasePath);

                if (serializers.CustomVisuals.Serialize(this.CustomVisuals))
                    Log.Information("CustomVisuals extracted to: {Path}", serializers.CustomVisuals.BasePath);

                if (serializers.StaticResources.Serialize(this.StaticResources))
                    Log.Information("StaticResources extracted to: {Path}", serializers.StaticResources.BasePath);


                // **** Metadata ****
                this.PbixProj.Version = PbixProject.CurrentVersion; // always set latest version on new pbixproj file
                if (this.PbixProj.Created == default) this.PbixProj.Created = DateTimeOffset.Now;
                this.PbixProj.LastModified = DateTimeOffset.Now;
                this.PbixProj.Save(projectFolder);

                projectFolder.Commit();
            }
        }

        public static void ToFile(string path)
        {
            throw new NotImplementedException();
        }

        #region IPbixModel

        public JObject Connections { get; private set; }
        public JObject DataModel { get; private set; }
        public JObject Report { get; private set; }
        public JObject DiagramViewState { get; private set; }
        public JObject DiagramLayout { get; private set; }
        public JObject LinguisticSchema { get; private set; }
        public JObject ReportMetadata { get; private set; }
        public JObject ReportSettings { get; private set; }
        public string Version { get; private set; }
        public IDictionary<string, byte[]> CustomVisuals { get; private set; }
        public IDictionary<string, byte[]> StaticResources { get; private set; }

        public PbixProject PbixProj { get; private set; }

        #endregion

        /// <summary>
        /// Returns the default folder location for this instance.
        /// </summary>
        public string GetProjectFolder()
        {
            switch (this.Type) 
            {
                case PbixModelSource.PbixProjFolder:
                    return this.SourcePath;
                case PbixModelSource.PowerBIPackage:
                case PbixModelSource.LiveSession:
                    return PbixProject.GetProjectFolderForFile(this.SourcePath);
                default:
                    throw new NotSupportedException();
            }
        }

        public void Dispose()
        {
            // TODO Possibly only needed for live session models?
        }
    }
}