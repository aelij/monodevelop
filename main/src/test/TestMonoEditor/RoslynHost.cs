/*using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;

namespace TestMonoEditor
{
    internal class RoslynHost
    {
        private readonly SourceTextContainer _container;
        public Workspace Workspace { get; }
        public DocumentId DocumentId { get; }

        public Document GetDocument()
        {
            return Workspace.CurrentSolution.GetDocument(DocumentId);
        }

        public RoslynHost(SourceTextContainer container)
        {
            _container = container;
            var assemblies = new[]
            {
                Assembly.Load("Microsoft.CodeAnalysis"),
                Assembly.Load("Microsoft.CodeAnalysis.CSharp"),
                Assembly.Load("Microsoft.CodeAnalysis.Features"),
                Assembly.Load("Microsoft.CodeAnalysis.CSharp.Features")
            };
            var workspace = new MyWorkspace(MefHostServices.Create(MefHostServices.DefaultAssemblies
                .Concat(assemblies).Distinct()));
            var project = workspace.CurrentSolution.AddProject("P", "P", LanguageNames.CSharp)
                .WithMetadataReferences(new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) })
                .WithParseOptions(new CSharpParseOptions(kind: SourceCodeKind.Script));

            var document = project.AddDocument("D", _container.CurrentText);
            DocumentId = document.Id;

            workspace.SetCurrentSolution(document.Project.Solution);
            workspace.OpenDocument(DocumentId, _container);

            Workspace = workspace;
        }

        class MyWorkspace : Workspace
        {
            public MyWorkspace(HostServices host) : base(host, WorkspaceKind.Host)
            {
            }

            public new void SetCurrentSolution(Solution solution)
            {
                var oldSolution = CurrentSolution;
                var newSolution = base.SetCurrentSolution(solution);
                RaiseWorkspaceChangedEventAsync(WorkspaceChangeKind.SolutionChanged, oldSolution, newSolution);
            }

            public void OpenDocument(DocumentId documentId, SourceTextContainer textContainer)
            {
                OnDocumentOpened(documentId, textContainer);
                OnDocumentContextUpdated(documentId);
            }
        }
    }
}*/