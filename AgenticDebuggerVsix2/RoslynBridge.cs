using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;

namespace AgenticDebuggerVsix
{
    internal sealed class RoslynBridge
    {
        private readonly VisualStudioWorkspace _workspace;

        public RoslynBridge(VisualStudioWorkspace workspace)
        {
            _workspace = workspace;
        }

        /// <summary>
        /// Search for symbols across the solution
        /// </summary>
        public async Task<SymbolSearchResponse> SearchSymbolsAsync(SymbolSearchRequest request)
        {
            var response = new SymbolSearchResponse();

            try
            {
                var solution = _workspace.CurrentSolution;
                var results = new List<SymbolInfo>();

                foreach (var project in solution.Projects)
                {
                    var compilation = await project.GetCompilationAsync();
                    if (compilation == null) continue;

                    // Search in all symbols
                    var symbols = compilation.GetSymbolsWithName(
                        name => name.IndexOf(request.Query, StringComparison.OrdinalIgnoreCase) >= 0,
                        SymbolFilter.TypeAndMember
                    );

                    foreach (var symbol in symbols)
                    {
                        // Filter by kind if specified
                        if (!string.IsNullOrEmpty(request.Kind))
                        {
                            var symbolKind = GetSymbolKind(symbol);
                            if (!symbolKind.Equals(request.Kind, StringComparison.OrdinalIgnoreCase))
                                continue;
                        }

                        var symbolInfo = await CreateSymbolInfoAsync(symbol);
                        if (symbolInfo != null)
                        {
                            results.Add(symbolInfo);
                            if (results.Count >= request.MaxResults)
                                break;
                        }
                    }

                    if (results.Count >= request.MaxResults)
                        break;
                }

                response.Ok = true;
                response.Results = results;
                response.TotalFound = results.Count;
            }
            catch (Exception ex)
            {
                response.Ok = false;
                response.Results = new List<SymbolInfo>();
            }

            return response;
        }

        /// <summary>
        /// Go to definition of symbol at position
        /// </summary>
        public async Task<DefinitionResponse> GoToDefinitionAsync(DefinitionRequest request)
        {
            var response = new DefinitionResponse();

            try
            {
                var document = GetDocument(request.File);
                if (document == null)
                {
                    response.Message = "Document not found in workspace";
                    return response;
                }

                var semanticModel = await document.GetSemanticModelAsync();
                var syntaxRoot = await document.GetSyntaxRootAsync();
                if (semanticModel == null || syntaxRoot == null)
                {
                    response.Message = "Could not get semantic model";
                    return response;
                }

                // Convert line/column to position
                var text = await document.GetTextAsync();
                var position = GetPosition(text, request.Line, request.Column);

                // Get symbol at position
                var symbol = await SymbolFinder.FindSymbolAtPositionAsync(semanticModel, position, _workspace);
                if (symbol == null)
                {
                    response.Message = "No symbol found at position";
                    return response;
                }

                // Get definition location
                var location = symbol.Locations.FirstOrDefault();
                if (location == null || !location.IsInSource)
                {
                    response.Message = "Symbol has no source location (might be from metadata)";
                    return response;
                }

                response.Ok = true;
                response.Symbol = await CreateSymbolInfoAsync(symbol);
                response.Location = CreateCodeLocation(location);
            }
            catch (Exception ex)
            {
                response.Ok = false;
                response.Message = ex.Message;
            }

            return response;
        }

        /// <summary>
        /// Find all references to symbol at position
        /// </summary>
        public async Task<ReferencesResponse> FindReferencesAsync(ReferencesRequest request)
        {
            var response = new ReferencesResponse();

            try
            {
                var document = GetDocument(request.File);
                if (document == null)
                {
                    response.Message = "Document not found in workspace";
                    return response;
                }

                var semanticModel = await document.GetSemanticModelAsync();
                var text = await document.GetTextAsync();
                var position = GetPosition(text, request.Line, request.Column);

                // Find symbol at position
                var symbol = await SymbolFinder.FindSymbolAtPositionAsync(semanticModel, position, _workspace);
                if (symbol == null)
                {
                    response.Message = "No symbol found at position";
                    return response;
                }

                // Find all references
                var references = await SymbolFinder.FindReferencesAsync(symbol, _workspace.CurrentSolution);
                var locations = new List<CodeLocation>();

                foreach (var reference in references)
                {
                    foreach (var refLocation in reference.Locations)
                    {
                        if (refLocation.Location.IsInSource)
                        {
                            locations.Add(CreateCodeLocation(refLocation.Location));
                        }
                    }
                }

                // Optionally include declaration
                if (request.IncludeDeclaration)
                {
                    foreach (var declLocation in symbol.Locations)
                    {
                        if (declLocation.IsInSource)
                        {
                            locations.Insert(0, CreateCodeLocation(declLocation));
                        }
                    }
                }

                response.Ok = true;
                response.Symbol = await CreateSymbolInfoAsync(symbol);
                response.References = locations;
                response.TotalCount = locations.Count;
            }
            catch (Exception ex)
            {
                response.Ok = false;
                response.Message = ex.Message;
            }

            return response;
        }

        /// <summary>
        /// Get document outline (symbols hierarchy)
        /// </summary>
        public async Task<DocumentOutlineResponse> GetDocumentOutlineAsync(string filePath)
        {
            var response = new DocumentOutlineResponse { File = filePath };

            try
            {
                var document = GetDocument(filePath);
                if (document == null)
                {
                    response.Ok = false;
                    return response;
                }

                var semanticModel = await document.GetSemanticModelAsync();
                var syntaxRoot = await document.GetSyntaxRootAsync();
                if (semanticModel == null || syntaxRoot == null)
                {
                    response.Ok = false;
                    return response;
                }

                // Get all type declarations
                var symbols = new List<OutlineSymbol>();
                var compilation = await document.Project.GetCompilationAsync();
                if (compilation != null)
                {
                    var tree = await document.GetSyntaxTreeAsync();
                    var root = await tree.GetRootAsync();

                    foreach (var node in root.DescendantNodes())
                    {
                        var symbol = semanticModel.GetDeclaredSymbol(node);
                        if (symbol != null && IsTopLevelSymbol(symbol))
                        {
                            var outlineSymbol = await CreateOutlineSymbolAsync(symbol, semanticModel);
                            if (outlineSymbol != null)
                                symbols.Add(outlineSymbol);
                        }
                    }
                }

                response.Ok = true;
                response.Symbols = symbols;
            }
            catch (Exception ex)
            {
                response.Ok = false;
            }

            return response;
        }

        /// <summary>
        /// Get semantic information at position
        /// </summary>
        public async Task<SemanticInfoResponse> GetSemanticInfoAsync(SemanticInfoRequest request)
        {
            var response = new SemanticInfoResponse();

            try
            {
                var document = GetDocument(request.File);
                if (document == null)
                {
                    response.Message = "Document not found";
                    return response;
                }

                var semanticModel = await document.GetSemanticModelAsync();
                var text = await document.GetTextAsync();
                var position = GetPosition(text, request.Line, request.Column);

                // Find symbol at position
                var symbol = await SymbolFinder.FindSymbolAtPositionAsync(semanticModel, position, _workspace);
                if (symbol == null)
                {
                    response.Message = "No symbol at position";
                    return response;
                }

                response.Ok = true;
                response.Symbol = await CreateSymbolInfoAsync(symbol);
                response.Type = symbol is ITypeSymbol typeSymbol ? typeSymbol.ToDisplayString() :
                                (symbol as IMethodSymbol)?.ReturnType?.ToDisplayString() ??
                                (symbol as IPropertySymbol)?.Type?.ToDisplayString() ??
                                (symbol as IFieldSymbol)?.Type?.ToDisplayString() ??
                                (symbol as ILocalSymbol)?.Type?.ToDisplayString() ??
                                (symbol as IParameterSymbol)?.Type?.ToDisplayString();

                response.Documentation = symbol.GetDocumentationCommentXml();
                response.IsLocal = symbol.Kind == SymbolKind.Local;
                response.IsParameter = symbol.Kind == SymbolKind.Parameter;
            }
            catch (Exception ex)
            {
                response.Ok = false;
                response.Message = ex.Message;
            }

            return response;
        }

        // Helper methods

        private Document GetDocument(string filePath)
        {
            var documentIds = _workspace.CurrentSolution.GetDocumentIdsWithFilePath(filePath);
            var documentId = documentIds.FirstOrDefault();
            return documentId != null ? _workspace.CurrentSolution.GetDocument(documentId) : null;
        }

        private int GetPosition(SourceText text, int line, int column)
        {
            // Convert 1-based line/column to 0-based position
            var linePos = text.Lines[line - 1];
            return linePos.Start + (column - 1);
        }

        private async Task<SymbolInfo> CreateSymbolInfoAsync(ISymbol symbol)
        {
            if (symbol == null) return null;

            var location = symbol.Locations.FirstOrDefault();
            var lineSpan = location?.GetLineSpan();

            return new SymbolInfo
            {
                Name = symbol.Name,
                Kind = GetSymbolKind(symbol),
                ContainerName = symbol.ContainingType?.ToDisplayString() ?? symbol.ContainingNamespace?.ToDisplayString(),
                File = location?.SourceTree?.FilePath,
                Line = lineSpan?.StartLinePosition.Line + 1,
                Column = lineSpan?.StartLinePosition.Character + 1,
                Summary = GetDocumentationSummary(symbol)
            };
        }

        private async Task<OutlineSymbol> CreateOutlineSymbolAsync(ISymbol symbol, SemanticModel semanticModel)
        {
            var location = symbol.Locations.FirstOrDefault();
            var lineSpan = location?.GetLineSpan();

            var outlineSymbol = new OutlineSymbol
            {
                Name = symbol.Name,
                Kind = GetSymbolKind(symbol),
                Line = lineSpan?.StartLinePosition.Line + 1 ?? 0,
                Column = lineSpan?.StartLinePosition.Character + 1 ?? 0,
                Children = new List<OutlineSymbol>()
            };

            // Add members for types
            if (symbol is INamedTypeSymbol typeSymbol)
            {
                foreach (var member in typeSymbol.GetMembers())
                {
                    if (!member.IsImplicitlyDeclared && member.CanBeReferencedByName)
                    {
                        var childSymbol = await CreateOutlineSymbolAsync(member, semanticModel);
                        if (childSymbol != null)
                            outlineSymbol.Children.Add(childSymbol);
                    }
                }
            }

            return outlineSymbol;
        }

        private CodeLocation CreateCodeLocation(Location location)
        {
            var lineSpan = location.GetLineSpan();
            return new CodeLocation
            {
                File = location.SourceTree?.FilePath ?? "",
                Line = lineSpan.StartLinePosition.Line + 1,
                Column = lineSpan.StartLinePosition.Character + 1,
                EndLine = lineSpan.EndLinePosition.Line + 1,
                EndColumn = lineSpan.EndLinePosition.Character + 1
            };
        }

        private string GetSymbolKind(ISymbol symbol)
        {
            return symbol.Kind switch
            {
                SymbolKind.NamedType => (symbol as INamedTypeSymbol)?.TypeKind.ToString() ?? "Type",
                SymbolKind.Method => "Method",
                SymbolKind.Property => "Property",
                SymbolKind.Field => "Field",
                SymbolKind.Event => "Event",
                SymbolKind.Namespace => "Namespace",
                SymbolKind.Parameter => "Parameter",
                SymbolKind.Local => "Local",
                _ => symbol.Kind.ToString()
            };
        }

        private bool IsTopLevelSymbol(ISymbol symbol)
        {
            return symbol.Kind == SymbolKind.NamedType ||
                   symbol.Kind == SymbolKind.Namespace ||
                   (symbol.ContainingType != null && symbol.CanBeReferencedByName);
        }

        private string GetDocumentationSummary(ISymbol symbol)
        {
            var xml = symbol.GetDocumentationCommentXml();
            if (string.IsNullOrEmpty(xml)) return null;

            try
            {
                // Simple extraction without System.Xml.Linq dependency
                var startTag = "<summary>";
                var endTag = "</summary>";
                var startIdx = xml.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
                var endIdx = xml.IndexOf(endTag, StringComparison.OrdinalIgnoreCase);

                if (startIdx >= 0 && endIdx > startIdx)
                {
                    var content = xml.Substring(startIdx + startTag.Length, endIdx - startIdx - startTag.Length);
                    // Clean up whitespace and newlines
                    return string.Join(" ", content.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
