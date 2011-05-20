//-----------------------------------------------------------------------
// <copyright company="CoApp Project">
//     Copyright (c) 2011 Garrett Serack. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace CoApp.AnyGen {
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text.RegularExpressions;
    using Microsoft.VisualStudio.Shell;
    using Microsoft.Win32;
    using Toolkit.Exceptions;
    using Toolkit.Extensions;
    using Toolkit.Scripting.Languages.PropertySheet;
    using Toolkit.Utility;

    /// <summary>
    ///   This is the generator class. 
    ///   When setting the 'Custom Tool' property of a C#, VB, or J# project item to "AnyGen", 
    ///   the GenerateCode function will get called and will return the contents of the generated file 
    ///   to the project system
    /// </summary>
    [ComVisible(true)]
    [Guid("D1834544-F1E0-4D65-A258-59F403E0C03B")]
    [ProvideObject(typeof (AnyGenerator))]
    public class AnyGenerator : BaseCodeGeneratorWithSite {
        private static Guid CustomToolGuid = new Guid("{D1834544-F1E0-4D65-A258-59F403E0C03B}");
        private static Guid CSharpCategory = new Guid("{FAE04EC1-301F-11D3-BF4B-00C04F79EFBC}");
        private static Guid VBCategory = new Guid("{164B10B9-B200-11D0-8C61-00A0C91E29D5}");

        private const string CustomToolName = "AnyGen";
        private const string CustomToolDescription = "Generate Code for anything.";
        private const string KeyFormat = @"SOFTWARE\Microsoft\VisualStudio\{0}\Generators\{1}\{2}";

        private List<string> tmpFiles= new List<string>();

        private string WriteTempScript(string text) {
            var tmpFilename = Path.GetTempFileName();
            tmpFiles.Add(tmpFilename);
            tmpFilename += ".cmd";
            tmpFiles.Add(tmpFilename);
            File.WriteAllText(tmpFilename, text);
            return tmpFilename;
        }

        private ProcessUtility Exec(string script, string currentDirectory) {
            var cmdexe = new ProcessUtility("cmd.exe");

            if (script.Contains("\r")) {
                script =
@"@echo off
@setlocal 
@cd ""{0}""

".format(currentDirectory) + script;
                var scriptpath = WriteTempScript(script);
                cmdexe.Exec("/c {0}", scriptpath);
            }
            else {
                cmdexe.Exec("/c {0}", script);
            }

            return cmdexe;
        }

        private string Load(string fromWhere, string stdOut, string stdErr ) {
            switch( Environment.ExpandEnvironmentVariables(fromWhere) ) {
                case "stderr":
                    return stdErr;
                    
                case "stdout":
                    return stdOut;
                    
                default:
                    if( File.Exists(fromWhere)) {
                        return File.ReadAllText(fromWhere);
                    }
                    break;
            }
            return string.Empty;
        }

        /// <summary>
        ///   Function that builds the contents of the generated file based on the contents of the input file
        /// </summary>
        /// <param name = "inputFileContent">Content of the input file</param>
        /// <returns>Generated file as a byte array</returns>
        protected override byte[] GenerateCode(string inputFileContent) {
            var projectFilename = string.Empty;
            var anygenPath = string.Empty;
            var ext = string.Empty;

            try {
                projectFilename = GetVSProject().Project.FileName;
                anygenPath = Path.Combine(Path.GetDirectoryName(projectFilename), Path.GetFileNameWithoutExtension(projectFilename) + ".anygen");
                ext = Path.GetExtension(InputFilePath).Replace(".", "");

                if (!File.Exists(anygenPath)) {
                    return GenerateMessage("AnyGen Failed.\r\nUnable to locate AnyGen config file at:\r\n{0}", anygenPath);
                }

                var propertySheet = PropertySheet.Load(anygenPath);
                var rule =
                    (from r in propertySheet.Rules where r.Class.Equals(ext, StringComparison.CurrentCultureIgnoreCase) select r)
                        .FirstOrDefault();

                if( rule == null ) {
                    return GenerateMessage("No matching rule for [.{1}] in AnyGen configuration file {0}\r\n", anygenPath,ext);
                }

                var classname = Path.GetFileNameWithoutExtension(InputFilePath).ToCharArray();
                var okchars = StringExtensions.LettersNumbersUnderscores.ToCharArray();
                for( var i=0;i<classname.Length ;i++) {
                    if( !okchars.Contains(classname[i]))
                        classname[i] = '_';
                }
               

                Environment.SetEnvironmentVariable("AG_PROJECTFILENAME",projectFilename);
                Environment.SetEnvironmentVariable("AG_INPUTFILENAME",InputFilePath);
                Environment.SetEnvironmentVariable("AG_OUTPUTFILETYPE",GetDefaultExtension());
                Environment.SetEnvironmentVariable("AG_NAMESPACE",FileNameSpace);
                Environment.SetEnvironmentVariable("AG_CLASSNAME",new string( classname));

                var command = rule["command"].AsString();

                var resultIn = rule["result"].AsString("stdout").ToLower();
                var errorsIn = rule["errors"].AsString("stdout").ToLower();
                var warningsIn = rule["warnings"].AsString("stdout").ToLower();
                var errorRx= rule["error-rx"].AsString();
                var warningsRx = rule["warnings-rx"].AsString();
                    
                if( string.IsNullOrEmpty("command") ) {
                    return GenerateMessage("Missing command for [.{1}] in AnyGen configuration file {0}\r\n", anygenPath,ext);
                }

                var cmdexe = Exec(command, Path.GetDirectoryName(InputFilePath));
                var stderr = cmdexe.StandardError.Trim();
                var stdout = cmdexe.StandardOut.Trim();

                if( cmdexe.ExitCode != 0 ) {
                    // hmm. errors 
                    var errorText = Load(errorsIn, stdout, stderr);

                    if( !string.IsNullOrEmpty(errorRx)) {
                        var rx = new Regex( errorRx , RegexOptions.IgnoreCase);
                            
                        var matches = rx.Matches(errorText);
                        foreach( var mx in matches ) {
                            var m = (Match)mx;
                            if(m.Success) {
                                uint row =0;
                                uint column =0;
                                uint level = 1;

                                var filename =  m.Groups["filename"].Value.Trim();
                                var code = m.Groups["code"].Value.Trim();
                                var message = m.Groups["message"].Value.Trim();

                                UInt32.TryParse( m.Groups["row"].Value , out row) ;
                                UInt32.TryParse( m.Groups["column"].Value , out column) ;
                                UInt32.TryParse( m.Groups["level"].Value , out level) ;

                                if (row > 0)
                                    row--;
                                if (column > 0)
                                    column--;

                                GeneratorError(level, message, row, column);
                            }
                        }
                        
                    }
                    return GenerateMessage("Tool returned error.\r\nCommand:{0}\r\nStdErr:\r\n{1}\r\nStdOut:\r\n{2}", command, stderr, stdout);
                } 
                // hmm. no errors
                return Load(resultIn, stdout, stderr).Trim().ToByteArray();
                
            } catch( EndUserPropertyException eupe) {
                return GenerateMessage("Error in AnyGen configuration file {0}\r\n{1} in property{2}", anygenPath,eupe.Message, eupe.Property.Name);
            }
            catch (EndUserParseException pspe) {
                return GenerateMessage("Error in AnyGen configuration file {0}.\r\n{1}--found '{2}'", anygenPath,pspe.Message, pspe.Token.Data);
            }
            catch( Exception e ) {
                return GenerateExceptionMessage(e);
            }
            finally {
                foreach (var f in tmpFiles.Where(File.Exists)) {
                    f.TryHardToDeleteFile();
                }
            }
        }

         protected static void Register(Version vsVersion, Guid categoryGuid) {
            var subKey = String.Format(KeyFormat, vsVersion, categoryGuid.ToString("B"), CustomToolName);

            using(var key = Registry.LocalMachine.CreateSubKey(subKey)) {
                key.SetValue("", CustomToolDescription);
                key.SetValue("CLSID", CustomToolGuid.ToString("B"));
                key.SetValue("GeneratesDesignTimeSource", 1);
            }
        }

        protected static void Unregister(Version vsVersion, Guid categoryGuid) {
            var subKey = String.Format(KeyFormat, vsVersion, categoryGuid.ToString("B"), CustomToolName);
            Registry.LocalMachine.DeleteSubKey(subKey, false);
        }
         
        [ComRegisterFunction]
        public static void RegisterClass(Type t) {
            Register(new Version(10, 0), CSharpCategory);
            Register(new Version(10, 0), VBCategory);
            Register(new Version(9, 0), CSharpCategory);
            Register(new Version(9, 0), VBCategory);
        }

        [ComUnregisterFunction]
        public static void UnregisterClass(Type t) {
            Unregister(new Version(10, 0), CSharpCategory);
            Unregister(new Version(10, 0), VBCategory);
            Unregister(new Version(9, 0), CSharpCategory);
            Unregister(new Version(9, 0), VBCategory);
        }

    }
}