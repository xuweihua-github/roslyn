﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Extensions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Editing;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.UseDefaultLiteral
{
    [ExportCodeFixProvider(LanguageNames.CSharp), Shared]
    internal partial class CSharpUseDefaultLiteralCodeFixProvider : SyntaxEditorBasedCodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds { get; }
            = ImmutableArray.Create(IDEDiagnosticIds.UseDefaultLiteralDiagnosticId);

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            context.RegisterCodeFix(new MyCodeAction(
                c => FixAsync(context.Document, context.Diagnostics.First(), c)),
                context.Diagnostics);
            return SpecializedTasks.EmptyTask;
        }

        protected override async Task FixAllAsync(
            Document document, ImmutableArray<Diagnostic> diagnostics,
            SyntaxEditor editor, CancellationToken cancellationToken)
        {
            // Fix-All for this feature is somewhat complicated.  Each time we fix one case, it
            // may make the next case unfixable.  For example:
            //
            //    'var v = x ? default(string) : default(string)'.

            var workspace = document.Project.Solution.Workspace;
            var originalRoot = editor.OriginalRoot;

            var originalNodes = diagnostics.SelectAsArray(
                d => (DefaultExpressionSyntax)originalRoot.FindNode(d.Location.SourceSpan, getInnermostNodeForTie: true));

            // We're going to be continually editing this tree.  Track all the nodes we
            // care about so we can find them across each edit.
            document = document.WithSyntaxRoot(originalRoot.TrackNodes(originalNodes));
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            var currentRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

            foreach (var originalDefaultExpression in originalNodes)
            {
                var defaultExpression = currentRoot.GetCurrentNode(originalDefaultExpression);

                if (defaultExpression.CanReplaceWithDefaultLiteral(semanticModel, cancellationToken))
                {
                    document = FixOne(document, currentRoot, defaultExpression);
                    semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                    currentRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            editor.ReplaceNode(originalRoot, currentRoot);
        }

        private static Document FixOne(
            Document document, SyntaxNode currentRoot, DefaultExpressionSyntax defaultExpression)
        {
            var replacement = SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression)
                                           .WithTriviaFrom(defaultExpression);

            var newRoot = currentRoot.ReplaceNode(defaultExpression, replacement);

            return document.WithSyntaxRoot(newRoot);
        }

        private class MyCodeAction : CodeAction.DocumentChangeAction
        {
            public MyCodeAction(Func<CancellationToken, Task<Document>> createChangedDocument)
                : base(FeaturesResources.Use_default_literal, createChangedDocument)
            {
            }
        }
    }
}