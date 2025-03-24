using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.Assertions;

namespace SigmaTau.Unity.ProjectGeneration
{
    public class SigmaTauProjectGeneration
    {
        private readonly SigmaTauProjectGenerationOptions _options;

        // File size generally seems to be 70kB - 90kB on Windows.
        private readonly MemoryStream _memoryStream = new(100 * 1024);

        private readonly List<ProjectInfo> _projects = new();

        private readonly string _unityProjectPath;

        private readonly string _unityProjectName;

        public SigmaTauProjectGeneration(SigmaTauProjectGenerationOptions options)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            _options = options;

            _unityProjectPath = PathUtils.GetParentFolder(Application.dataPath);
            _unityProjectName = Path.GetFileName(_unityProjectPath);
        }

        public void GenerateProjectFiles()
        {
            GetProjectsToInclude();

            foreach (ProjectInfo project in _projects)
            {
                CreateProject(project);
            }

            CreateSolution();
        }

        private void GetProjectsToInclude()
        {
            var assemblies = CompilationPipeline.GetAssemblies();

            foreach (Assembly assembly in assemblies)
            {
                string asmDefPath = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(assembly.name);

                Assert.IsTrue(asmDefPath == null || !string.IsNullOrWhiteSpace(asmDefPath));

                string rootSourcePath = asmDefPath == null
                    ? PathUtils.TryFindRootPathOfAllFiles(assembly.sourceFiles)
                    : PathUtils.GetParentFolder(asmDefPath);

                if (PathUtils.IsInAssetsFolder(rootSourcePath) || (asmDefPath != null && _options.IncludePackages))
                {
                    _projects.Add(new ProjectInfo
                    {
                        AsmDefPath = asmDefPath,
                        Assembly = assembly,
                        CsProjFilename = $"{assembly.name}.csproj",
                        ProjectGuid = GetProjectGuid(assembly),
                        ProjectTypeGuid = _options.ProjectTypeGuid,
                        RootSourcePath = rootSourcePath,
                    });
                }
            }
        }

        private void TryWriteFile(string filename)
        {
            string path = Path.Combine(_unityProjectPath, filename);
            using var fileStream = File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);

            int outputLength = (int)_memoryStream.Length;
            byte[] outputBytes = _memoryStream.GetBuffer();

            if (fileStream.Length == outputLength)
            {
                ReadOnlySpan<byte> outputSpan = outputBytes.AsSpan(0, outputLength);
                Span<byte> buffer = stackalloc byte[1024];

                bool matches = true;
                while (outputSpan.Length > 0 && matches)
                {
                    int bytesRead = fileStream.Read(buffer);
                    matches = buffer[..bytesRead] == outputSpan[..bytesRead];
                    outputSpan = outputSpan[bytesRead..];
                }

                if (matches)
                {
                    return;
                }
            }

            fileStream.Position = 0;
            fileStream.SetLength(outputLength);
            fileStream.Write(outputBytes, 0, outputLength);
            fileStream.Flush();
            fileStream.Close();
        }

        private void CreateSolution()
        {
            _memoryStream.Position = 0;
            _memoryStream.SetLength(0);

            using var writer = new StreamWriter(_memoryStream, Encoding.UTF8, 512, true);

            writer.WriteLine("Microsoft Visual Studio Solution File, Format Version 12.00");
            writer.WriteLine("# Visual Studio 15");

            foreach (ProjectInfo project in _projects)
            {
                writer.WriteLine(
                    "Project(\"{0}\") = \"{1}\", \"{2}\", \"{3}\"",
                    project.ProjectTypeGuid,
                    project.Assembly.name,
                    project.CsProjFilename,
                    project.ProjectGuid
                );
                writer.WriteLine("EndProject");
            }

            writer.WriteLine("Global");
            writer.WriteLine("    GlobalSection(SolutionConfigurationPlatforms) = preSolution");
            writer.WriteLine("        Debug|Any CPU = Debug|Any CPU");
            writer.WriteLine("        Release|Any CPU = Release|Any CPU");
            writer.WriteLine("    EndGlobalSection");
            writer.WriteLine("    GlobalSection(ProjectConfigurationPlatforms) = postSolution");

            foreach (ProjectInfo project in _projects)
            {
                writer.WriteLine($"        {project.ProjectGuid}.Debug|Any CPU.ActiveCfg = Debug|Any CPU");
                writer.WriteLine($"        {project.ProjectGuid}.Debug|Any CPU.Build.0 = Debug|Any CPU");
                writer.WriteLine($"        {project.ProjectGuid}.Release|Any CPU.ActiveCfg = Release|Any CPU");
                writer.WriteLine($"        {project.ProjectGuid}.Release|Any CPU.Build.0 = Release|Any CPU");
            }

            writer.WriteLine("    EndGlobalSection");
            writer.WriteLine("    GlobalSection(SolutionProperties) = preSolution");
            writer.WriteLine("        HideSolutionNode = FALSE");
            writer.WriteLine("    EndGlobalSection");
            writer.WriteLine("EndGlobal");
            writer.Flush();

            TryWriteFile($"{_unityProjectName}.sln");
        }

        private void CreateProject(ProjectInfo project)
        {
            _memoryStream.Position = 0;
            _memoryStream.SetLength(0);

            using var xml = XmlWriter.Create(
                _memoryStream,
                new XmlWriterSettings
                {
                    Encoding = Encoding.UTF8,
                    Indent = true,
                    IndentChars = "    ",
                    OmitXmlDeclaration = true,
                }
            );

            xml.WriteStartElement("Project");
            xml.WriteAttributeString("ToolsVersion", "Current");

            xml.WriteStartElement("PropertyGroup");
            xml.WriteElementString("BaseIntermediateOutputPath", "$(SolutionDir)/Temp/obj/$(Configuration)/$(MSBuildProjectName)");
            xml.WriteElementString("IntermediateOutputPath", "$(BaseIntermediateOutputPath)");
            xml.WriteEndElement();

            xml.WriteStartElement("Import");
            xml.WriteAttributeString("Project", "Sdk.props");
            xml.WriteAttributeString("Sdk", "Microsoft.NET.Sdk");
            xml.WriteEndElement();

            xml.WriteStartElement("PropertyGroup");
            xml.WriteElementString("GenerateAssemblyInfo", "false");
            xml.WriteElementString("EnableDefaultItems", "false");
            xml.WriteElementString("AppendTargetFrameworkToOutputPath", "false");
            xml.WriteElementString("LangVersion", project.Assembly.compilerOptions.LanguageVersion);
            xml.WriteElementString("Configurations", "Debug;Release");
            xml.WriteStartElement("Configuration");
            xml.WriteAttributeString("Condition", "'$(Configuration)' == ''");
            xml.WriteString("Debug");
            xml.WriteEndElement();
            xml.WriteStartElement("Platform");
            xml.WriteAttributeString("Condition", "'$(Platform)' == ''");
            xml.WriteString("AnyCPU");
            xml.WriteEndElement();
            xml.WriteElementString("RootNamespace", project.Assembly.rootNamespace);
            xml.WriteElementString("OutputType", "Library");
            xml.WriteElementString("AssemblyName", project.Assembly.name);
            xml.WriteElementString("TargetFramework", "netstandard2.1");
            xml.WriteElementString("WarningLevel", "4");
            xml.WriteElementString("NoWarn", "0169;USG0001");
            xml.WriteElementString("AllowUnsafeBlocks", project.Assembly.compilerOptions.AllowUnsafeCode.ToString());
            xml.WriteElementString("OutputPath", "Temp/bin/$(Configuration)/");
            xml.WriteEndElement();

            xml.WriteStartElement("PropertyGroup");
            xml.WriteAttributeString("Condition", "'$(Configuration)|$(Platform)' == 'Debug|AnyCPU'");
            xml.WriteElementString("DebugSymbols", "true");
            xml.WriteElementString("DebugType", "full");
            xml.WriteElementString("Optimize", "false");
            xml.WriteElementString("DefineConstants", string.Join(";", project.Assembly.defines));
            xml.WriteEndElement();

            xml.WriteStartElement("PropertyGroup");
            xml.WriteAttributeString("Condition", "'$(Configuration)|$(Platform)' == 'Release|AnyCPU'");
            xml.WriteElementString("DebugType", "pdbonly");
            xml.WriteElementString("Optimize", "true");
            xml.WriteEndElement();

            xml.WriteStartElement("PropertyGroup");
            xml.WriteElementString("NoStandardLibraries", "true");
            xml.WriteElementString("NoStdLib", "true");
            xml.WriteElementString("NoConfig", "true");
            xml.WriteElementString("DisableImplicitFrameworkReferences", "true");
            xml.WriteElementString("MSBuildWarningsAsMessages", "MSB3277");
            xml.WriteEndElement();

            xml.WriteStartElement("ItemGroup");
            var analyzers = project.Assembly.compilerOptions.RoslynAnalyzerDllPaths
                .Concat(_options.Analyzers ?? Enumerable.Empty<string>());
            foreach (string analyzer in analyzers)
            {
                xml.WriteStartElement("Analyzer");
                xml.WriteAttributeString("Include", analyzer);
                xml.WriteEndElement();
            }
            xml.WriteEndElement();

            xml.WriteStartElement("Import");
            xml.WriteAttributeString("Project", "Sdk.targets");
            xml.WriteAttributeString("Sdk", "Microsoft.NET.Sdk");
            xml.WriteEndElement();

            if (_options.CapabilitiesToRemove is { Length: > 0 })
            {
                xml.WriteStartElement("ItemGroup");
                foreach (string capabilityToRemove in _options.CapabilitiesToRemove)
                {
                    xml.WriteStartElement("ProjectCapability");
                    xml.WriteAttributeString("Remove", capabilityToRemove);
                    xml.WriteEndElement();
                }
                xml.WriteEndElement();
            }

            xml.WriteStartElement("ItemGroup");
            xml.WriteStartElement("Compile");
            xml.WriteAttributeString("Include", project.RootSourcePath + "/**/*.cs");
            xml.WriteEndElement();

            foreach (ProjectInfo nestedProject in _projects)
            {
                if (
                    nestedProject == project
                    || string.IsNullOrWhiteSpace(nestedProject.RootSourcePath)
                    || !PathUtils.IsSubfolder(project.RootSourcePath, nestedProject.RootSourcePath)
                )
                {
                    continue;
                }

                xml.WriteStartElement("Compile");
                xml.WriteAttributeString(
                    "Remove",
                    nestedProject.RootSourcePath[(project.RootSourcePath.Length + 1)..] + "/**/*.cs"
                );
                xml.WriteEndElement();
            }
            xml.WriteEndElement();

            var projectReferences = new List<string>();

            xml.WriteStartElement("ItemGroup");
            foreach (string referencePath in project.Assembly.allReferences)
            {
                string name = Path.GetFileNameWithoutExtension(referencePath);
                ProjectInfo otherProject = _projects.FirstOrDefault((p) => p.Assembly.name == name);
                if (otherProject is not null)
                {
                    projectReferences.Add(otherProject.CsProjFilename);
                    continue;
                }
                xml.WriteStartElement("Reference");
                xml.WriteAttributeString("Include", name);
                xml.WriteElementString("HintPath", referencePath);
                xml.WriteElementString("Private", "false");
                xml.WriteEndElement();
            }
            xml.WriteEndElement();

            xml.WriteStartElement("ItemGroup");
            foreach (string projectReference in projectReferences)
            {
                xml.WriteStartElement("ProjectReference");
                xml.WriteAttributeString("Include", "$(SolutionDir)/" + projectReference);
                xml.WriteEndElement();
            }
            xml.WriteEndElement();

            xml.WriteEndElement();

            xml.Flush();

            TryWriteFile(project.CsProjFilename);
        }

        private static string GetProjectGuid(Assembly assembly)
        {
            static void WriteHex(Span<char> guidSpan, ReadOnlySpan<byte> hashSpan)
            {
                const string hexDigits = "0123456789ABCDEF";
                Assert.AreEqual(guidSpan.Length, hashSpan.Length * 2);
                for (int index = 0; index < hashSpan.Length; index++)
                {
                    byte hashByte = hashSpan[index];
                    int guidIndex = 2 * index;
                    guidSpan[guidIndex] = hexDigits[hashByte >> 4];
                    guidSpan[guidIndex + 1] = hexDigits[hashByte & 0xf];
                }
            }

            int byteCount = Encoding.UTF8.GetByteCount(assembly.name);
            Span<byte> nameEncoded = stackalloc byte[byteCount];
            int bytesWritten = Encoding.UTF8.GetBytes(assembly.name, nameEncoded);

            using MD5 md5 = MD5.Create();
            Span<byte> hashSpan = stackalloc byte[16];
            if (!md5.TryComputeHash(nameEncoded[..bytesWritten], hashSpan, out bytesWritten))
            {
                throw new InvalidOperationException("Failed to compute project GUID");
            }
            Assert.AreEqual(16, bytesWritten);

            Span<char> guidSpan = stackalloc char[38];
            guidSpan[0] = '{';
            WriteHex(guidSpan[1..9], hashSpan[0..4]);
            guidSpan[9] = '-';
            WriteHex(guidSpan[10..14], hashSpan[4..6]);
            guidSpan[14] = '-';
            WriteHex(guidSpan[15..19], hashSpan[6..8]);
            guidSpan[19] = '-';
            WriteHex(guidSpan[20..24], hashSpan[8..10]);
            guidSpan[24] = '-';
            WriteHex(guidSpan[25..37], hashSpan[10..16]);
            guidSpan[37] = '}';

            return guidSpan.ToString();
        }

        private class ProjectInfo
        {
            public Assembly Assembly { get; set; }

            public string ProjectTypeGuid { get; set; }

            public string ProjectGuid { get; set; }

            public string CsProjFilename { get; set; }

            public string RootSourcePath { get; set; }

            public string AsmDefPath { get; set; }
        }
    }
}

