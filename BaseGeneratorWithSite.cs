//-----------------------------------------------------------------------
// <copyright company="CoApp Project">
//     Copyright (c) 2011 Garrett Serack. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace CoApp.AnyGen {
    using System;
    using System.CodeDom;
    using System.CodeDom.Compiler;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text;
    using EnvDTE;
    using Microsoft.CSharp;
    using Microsoft.VisualStudio;
    using Microsoft.VisualStudio.Designer.Interfaces;
    using Microsoft.VisualStudio.OLE.Interop;
    using Microsoft.VisualStudio.Shell;
    using Microsoft.VisualStudio.Shell.Interop;
    using VSLangProj;
    using CodeNamespace = System.CodeDom.CodeNamespace;
     
    using IServiceProvider = Microsoft.VisualStudio.OLE.Interop.IServiceProvider;

    /// <summary>
    ///   Base code generator with site implementation
    /// </summary>
    public abstract class BaseCodeGeneratorWithSite : BaseCodeGenerator, IObjectWithSite {
        private CodeDomProvider _codeDomProvider;
        private ServiceProvider _serviceProvider;
        private object _site;

        /// <summary>
        ///   Demand-creates a ServiceProvider
        /// </summary>
        private ServiceProvider SiteServiceProvider {
            get {
                if (_serviceProvider == null) {
                    _serviceProvider = new ServiceProvider(_site as IServiceProvider);
                    Debug.Assert(_serviceProvider != null, "Unable to get ServiceProvider from site object.");
                }
                return _serviceProvider;
            }
        }

        /// <summary>
        ///   GetSite method of IOleObjectWithSite
        /// </summary>
        /// <param name = "riid">interface to get</param>
        /// <param name = "ppvSite">IntPtr in which to stuff return value</param>
        public void GetSite(ref Guid riid, out IntPtr ppvSite) {
            if (_site == null) {
                throw new COMException("object is not sited", VSConstants.E_FAIL);
            }

            var pUnknownPointer = Marshal.GetIUnknownForObject(_site);
            var intPointer = IntPtr.Zero;
            Marshal.QueryInterface(pUnknownPointer, ref riid, out intPointer);

            if (intPointer == IntPtr.Zero) {
                throw new COMException("site does not support requested interface", VSConstants.E_NOINTERFACE);
            }

            ppvSite = intPointer;
        }

        /// <summary>
        ///   SetSite method of IOleObjectWithSite
        /// </summary>
        /// <param name = "pUnkSite">site for this object to use</param>
        void IObjectWithSite.SetSite(object pUnkSite) {
            _site = pUnkSite;
            _codeDomProvider = null;
            _serviceProvider = null;
        }

        /// <summary>
        ///   Method to get a service by its GUID
        /// </summary>
        /// <param name = "serviceGuid">GUID of service to retrieve</param>
        /// <returns>An object that implements the requested service</returns>
        protected object GetService(Guid serviceGuid) {
            return SiteServiceProvider.GetService(serviceGuid);
        }

        /// <summary>
        ///   Method to get a service by its Type
        /// </summary>
        /// <param name = "serviceType">Type of service to retrieve</param>
        /// <returns>An object that implements the requested service</returns>
        protected object GetService(Type serviceType) {
            return SiteServiceProvider.GetService(serviceType);
        }

        /// <summary>
        ///   Returns a CodeDomProvider object for the language of the project containing
        ///   the project item the generator was called on
        /// </summary>
        /// <returns>A CodeDomProvider object</returns>
        protected virtual CodeDomProvider GetCodeProvider() {
            if (_codeDomProvider == null) {
                //Query for IVSMDCodeDomProvider/SVSMDCodeDomProvider for this project type
                var provider = GetService(typeof (SVSMDCodeDomProvider)) as IVSMDCodeDomProvider;
                if (provider != null) {
                    _codeDomProvider = provider.CodeDomProvider as CodeDomProvider;
                }
                else {
                    //In the case where no language specific CodeDom is available, fall back to C#
                    _codeDomProvider = CodeDomProvider.CreateProvider("C#");
                }
            }
            return _codeDomProvider;
        }

        /// <summary>
        ///   Gets the default extension of the output file from the CodeDomProvider
        /// </summary>
        /// <returns></returns>
        protected override string GetDefaultExtension() {
            var codeDom = GetCodeProvider();
            Debug.Assert(codeDom != null, "CodeDomProvider is NULL.");
            var extension = codeDom.FileExtension;
            if (extension != null && extension.Length > 0) {
                extension = "." + extension.TrimStart(".".ToCharArray());
            }
            return extension;
        }

        /// <summary>
        ///   Returns the EnvDTE.ProjectItem object that corresponds to the project item the code 
        ///   generator was called on
        /// </summary>
        /// <returns>The EnvDTE.ProjectItem of the project item the code generator was called on</returns>
        protected ProjectItem GetProjectItem() {
            var p = GetService(typeof (ProjectItem));
            Debug.Assert(p != null, "Unable to get Project Item.");
            return (ProjectItem) p;
        }

        /// <summary>
        ///   Returns the EnvDTE.Project object of the project containing the project item the code 
        ///   generator was called on
        /// </summary>
        /// <returns>
        ///   The EnvDTE.Project object of the project containing the project item the code generator was called on
        /// </returns>
        protected Project GetProject() {
            return GetProjectItem().ContainingProject;
        }

        /// <summary>
        ///   Returns the VSLangProj.VSProjectItem object that corresponds to the project item the code 
        ///   generator was called on
        /// </summary>
        /// <returns>The VSLangProj.VSProjectItem of the project item the code generator was called on</returns>
        protected VSProjectItem GetVSProjectItem() {
            return (VSProjectItem) GetProjectItem().Object;
        }

        /// <summary>
        ///   Returns the VSLangProj.VSProject object of the project containing the project item the code 
        ///   generator was called on
        /// </summary>
        /// <returns>
        ///   The VSLangProj.VSProject object of the project containing the project item 
        ///   the code generator was called on
        /// </returns>
        protected VSProject GetVSProject() {
            return (VSProject) GetProject().Object;
        }

        protected byte[] GenerateExceptionMessage(Exception ex) {
            return GenerateMessage(ex.ToString());
        }

        protected byte[] GenerateMessage(string format, params object[] args ) {
            var code = new CodeCompileUnit();
            var codeNamespace =  new CodeNamespace(FileNameSpace);
            var codeProvider = new CSharpCodeProvider();

            code.Namespaces.Add(codeNamespace);

            codeNamespace.Comments.Add(new CodeCommentStatement(String.Format(format,args)));
            
            var stringcode = WriteCode(codeProvider, code);

            return Encoding.UTF8.GetBytes(stringcode);
        }

        protected static string WriteCode(CodeDomProvider pProvider, CodeCompileUnit code) {
            var stringwriter = new StringWriter();
            pProvider.GenerateCodeFromCompileUnit(code, stringwriter, null);
            var stringcode = stringwriter.ToString();
            stringwriter.Close();

            return stringcode;
        }
    }
}